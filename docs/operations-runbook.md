# Runbook operacional do Hiram Core

## 1. Topologia suportada

O runtime padrão contém uma imagem `hiram` e PostgreSQL 17. API e workers executam no mesmo
processo. SMTP ou Resend fazem a última milha; um coletor OTLP é opcional.

Copie `.env.hiram.example`, troque todos os valores `CHANGE_ME` e mantenha o arquivo fora do
repositório. O diretório indicado por `DataProtection__KeysPath` deve ser persistente e acompanhado
pelo mesmo ciclo de backup do banco.

Antes de cada rollout, execute a migration com a mesma imagem que será publicada:

```bash
docker run --rm --env-file .env ghcr.io/michel-az-de/hiram:<sha> --migrate-only
docker compose up -d
curl --fail http://localhost:3357/health/live
curl --fail http://localhost:3357/health/ready
```

`live` verifica o processo. `ready` verifica PostgreSQL e a configuração mínima. Não habilite
`Hiram__MigrateOnStartup` em produção.

## 2. Onboarding de tenant

Comece em `shadow` para provar autenticação, persistência e resolução do provider sem enviar:

```bash
curl --fail-with-body -X POST https://hiram.example/v1/admin/tenants \
  -H "X-Admin-Key: $HIRAM_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Projeto","deliveryMode":"shadow"}'
```

Guarde o `id` retornado e emita uma chave. A chave em claro aparece uma única vez:

```bash
curl --fail-with-body -X POST https://hiram.example/v1/admin/api-keys \
  -H "X-Admin-Key: $HIRAM_ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"<tenant-id>","name":"producao"}'
```

Armazene a chave no cofre do projeto consumidor. Configure o provider do próprio tenant, por
exemplo SMTP:

```bash
curl --fail-with-body -X PUT https://hiram.example/v1/providers/email \
  -H "X-Api-Key: $HIRAM_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"provider":"smtp","settings":{"host":"smtp.example.com","port":"587","from":"notifications@example.com","security":"starttls","username":"user"},"secret":"<password>"}'
```

Envie uma notificação com `Idempotency-Key` e confirme estado e tentativas em
`GET /v1/notifications/{id}`. A API atual não altera o modo de um tenant existente. Depois de provar
o fluxo em shadow, crie o tenant de produção com `deliveryMode: live`, configure seu provider e emita
uma chave própria. Não reutilize a chave de homologação.

## 3. Onboarding de credencial Twilio por tenant

O Twilio é o provider de última milha de SMS e WhatsApp, e opcionalmente de e-mail (ADR-028). A
credencial é por tenant: vive em `tenant_provider_configs`, nunca em variável de ambiente do host, para
que um tenant não decida pelos outros.

### 3.1 O que colher no console

No console da Twilio, com a conta já criada:

| Valor | Onde nasce | Formato | Onde vai |
|---|---|---|---|
| Account SID | painel inicial da conta | `AC` + 32 hex | `settings.account_sid`, em claro |
| API Key SID | Account, API keys & tokens, criar uma Standard key | `SK` + 32 hex | `settings.api_key_sid`, em claro |
| API Key Secret | exibido **uma única vez** na criação da key | string opaca | campo `secret`, cifrado |
| Remetente | Messaging, número do trial ou comprado | E.164 (`+15550000000`) | `settings.from`, em claro |

Use uma API Key por ambiente, com nome que diga qual é. Não use o Auth Token da conta: ele não é
revogável isoladamente e vale para tudo.

O secret aparece uma única vez. Se não foi copiado, crie outra key e apague a anterior; não há como
relê-lo.

### 3.2 Gravar a configuração

O tenant configura o próprio canal com a sua api key do Hiram:

```bash
curl --fail-with-body -X PUT https://hiram.example/v1/providers/sms \
  -H "X-Api-Key: $HIRAM_API_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @- <<'JSON'
{"provider":"twilio-sms",
 "settings":{"account_sid":"AC...","from":"+15550000000","api_key_sid":"SK..."},
 "secret":"<api key secret>"}
JSON
```

WhatsApp é a mesma chamada em `/v1/providers/whatsapp` com `"provider":"twilio-whatsapp"`. A resposta é
`204`. O `from` do WhatsApp é o número puro em E.164: o prefixo `whatsapp:` é montado pelo adapter no
envio, e gravá-lo aqui produziria `whatsapp:whatsapp:+1...` e recusa em toda mensagem.

O payload vai por stdin de propósito. A linha de comando de um processo é legível por outros processos
do host, e o secret está nela quando se usa `-d`.

O que está em `settings` volta em leitura; o `secret` não volta nunca. Ele é cifrado por Data Protection
antes de ser gravado e só é decifrado no momento do envio. Nada disso aparece em log: a auditoria da
troca registra tenant, canal e nome do provider, jamais o valor.

Para o tenant da Jornada do Candidato, `deploy/jornada/provision.sh providers` faz exatamente estas
chamadas a partir do `.env` do ambiente.

### 3.3 O key ring é parte da credencial

`DataProtection__KeysPath` (volume `hiram-keyring` no compose) guarda as chaves que cifram esses
segredos. **Descartar o volume torna todo secret de provider indecifrável, mesmo com o banco intacto.**
Um `docker compose down -v`, uma recriação da máquina sem restore ou um deploy que troque o caminho
produzem o mesmo efeito: as linhas de `tenant_provider_configs` continuam lá e nenhuma delas serve mais.

O sintoma é falha de entrega em todos os canais que dependem de secret, ao mesmo tempo, sem mudança de
credencial do lado do provider. O remédio é reconfigurar: repetir o `PUT` de cada canal, ou
`./provision.sh providers` no caso da Jornada. Por isso o key ring entra no backup junto do banco
(seção 9) e o `deploy/dr/verify-backup.sh` prova os dois.

### 3.4 Limitações da conta trial

Enquanto a conta for trial, o caminho de entrega é comprovável e o conteúdo não:

- **Destino precisa estar verificado.** SMS para número não verificado no console é recusado com
  **21608**. Isso classifica como falha permanente, então vai direto a dead letter, com o código no
  motivo. Não é retentável: verificar o número e fazer replay é o caminho.
- **O corpo enviado não é o do template.** Com `trial_mode` em `settings`, o adapter envia a chave do
  conteúdo pré-aprovado no lugar do corpo renderizado. O corpo real continua persistido em
  `notification_requests`, e o `DeliveryAttempt` marca `trial_content` para que o histórico não afirme
  ter entregue um texto que nunca saiu. Sair do trial é remover `trial_mode` da configuração, sem deploy.
- **Não há consulta de status.** Consultar a mensagem aceita responde `403` e a listagem da conta não a
  exibe. Logo não existe polling de estado de entrega no trial, e o que o Hiram sabe é que o provider
  aceitou. O callback de status continua pendente (itens 5 e 6 do ADR-028).

### 3.5 Sandbox do WhatsApp

Sem WABA próprio, o canal roda no sandbox da Twilio, que tem duas regras que não existem no SMS:

1. **Adesão explícita.** O destinatário precisa enviar `join <frase>` para o número do sandbox, uma vez
   por número. A frase está no console, em Messaging, Try it out, WhatsApp sandbox.
2. **Janela de 24 horas.** Cada mensagem do destinatário abre ou renova uma janela de 24h. Fora dela a
   Twilio recusa com **63016**, que também é permanente e vira dead letter nomeada. Depois que o
   destinatário responder de novo, `POST /v1/notifications/{id}/replay` reenvia sem duplicar.

Isso é independente do consentimento do Hiram, que é anterior e mais estrito: a `ConsentPolicy` nega
WhatsApp em qualquer categoria sem opt-in registrado, inclusive transacional, e nesse caso nem chega a
existir notificação para dead-letar.

### 3.6 Rotação de credencial

Rotacionar é repetir o `PUT` do canal com o novo `api_key_sid` e o novo secret: a escrita é upsert e a
próxima entrega já usa o valor novo. Só depois de ver entrega bem-sucedida com a key nova é que a antiga
deve ser apagada no console. Um canal por vez, para que uma key errada não derrube os três.

### 3.7 Roteiro de smoke manual

**Este roteiro nunca entra no gate de CI.** O CI não tem rede nem credencial e prova os adapters com
stub de `HttpMessageHandler` (item 12 do ADR-028). Rodar o smoke é ato manual, contra a conta real, com
a credencial vinda de user-secrets ou do cofre do ambiente.

1. Confirme `GET /health/ready`.
2. Grave a configuração dos canais (3.2) e confira que `tenant_provider_configs` tem uma linha por canal
   e que nenhum secret aparece em log.
3. Verifique o número de destino no console e, no WhatsApp, faça o `join` a partir dele.
4. Emita um evento real com `recipient.userId` que tenha opt-in e `recipient.phone` em E.164.
5. Confira em `GET /v1/notifications/{id}`: estado, tentativa, `provider`, e `trialContent` quando o
   modo trial estiver ligado.
6. Erro esperado sem conta verificada: `21608` no SMS e `63016` no WhatsApp fora da janela, ambos
   nomeados no motivo do dead letter. `20003` significa credencial inválida, não limitação de trial.
7. Não faça replay antes de entender o motivo (seções 7 e 8).

### 3.8 Simulador de providers, para provar sem gastar

O roteiro de 3.7 exige conta, crédito e número verificado. O `tools/Hiram.Simulator` (ADR-029) prova o
mesmo caminho sem nada disso: ele sobe um duplo HTTP da Twilio, aponta o Hiram para ele por configuração
e conduz três atos, uma entrega aceita, uma recusada e uma pelo fan-out de eventos.

**O endereço de cada provider é configuração.** Os padrões são os de produção, então quem não configura
nada continua falando com a Twilio de verdade:

| Chave | Padrão |
|---|---|
| `Hiram:Providers:Endpoints:TwilioApi` | `https://api.twilio.com/` |
| `Hiram:Providers:Endpoints:TwilioEmail` | `https://comms.twilio.com/v1/` |
| `Hiram:Providers:Endpoints:Resend` | `https://api.resend.com/` |

O valor precisa ser uma URL absoluta em `http` ou `https`. Qualquer outra coisa é recusada no startup, com
o nome da chave na mensagem. Falhar ali é melhor do que falhar na entrega, onde o sintoma apareceria como
erro de transporte e mandaria quem está de plantão olhar o provider em vez da configuração.

O esquema faz parte da checagem por um motivo concreto: no Linux o parser de URI aceita um caminho puro
como URI absoluto de arquivo, então `/twilio/` passaria em uma verificação que só exige "absoluto".

**Rodar.** Com um PostgreSQL disponível, suba o Hiram apontado para o duplo e execute o roteiro:

```bash
ASPNETCORE_URLS=http://localhost:3357 \
ConnectionStrings__Hiram="Host=localhost;Port=5433;Database=hiram;Username=hiram;Password=hiram" \
Hiram__AdminKey=admin-dev-local \
Hiram__MigrateOnStartup=true \
Hiram__Workers__Enabled=true \
Hiram__Providers__Endpoints__TwilioApi=http://localhost:4010/ \
Hiram__Providers__Endpoints__TwilioEmail=http://localhost:4010/v1/ \
dotnet run --project src/Hiram.Api
```

```bash
dotnet run --project tools/Hiram.Simulator -- walkthrough
```

O roteiro devolve `0` quando a entrega aceita termina em `sent`, a recusada termina em `dead_lettered` e o
fan-out termina em `sent`. Qualquer outro desfecho é falha, e o console mostra a tentativa, o motivo e o
que o duplo respondeu a cada chamada.

**Se o Hiram roda em contêiner e o duplo no host**, o endereço não é `localhost` e sim
`http://host.docker.internal:4010/`, senão o contêiner procura o duplo dentro de si mesmo.

**Provocar o caminho ruim.** `--scenario` aceita o nome ou o código do provider, que é o que aparece no
dead letter e portanto o que se tem em mãos ao reproduzir um incidente:

| Nome | Twilio | Meta | O que representa |
|---|---|---|---|
| `accept` | `201 queued` | `200` com `wamid` | entrega aceita |
| `geo` | `21408` | não tem | região fora das Geo Permissions |
| `10dlc` | `30034` | não tem | número dos EUA sem campanha 10DLC |
| `optout` | `21610` | não tem | destinatário respondeu STOP |
| `filtered` | `30007` | não tem | aceito e depois filtrado pela operadora |
| `unreachable` | `30003` | não tem | aparelho inalcançável, transitório |
| `unknown` | `30005` | `131026` | o destinatário não existe ou não está no WhatsApp |
| `window` | `63016` | `131047` | texto livre fora da janela de 24h |
| `template` | `21654` | `132001` | exige template, e ele não serve ou não existe |
| `parameters` | não tem | `132000` | o template espera outra quantidade de valores |
| `token` | não tem | `190` | token de acesso expirado |
| `restricted` | não tem | `131031` | conta restrita por violação de política |
| `ratelimited` | `429` | `130429` | rate limit, transitório |
| `servererror` | `500` | `131000` | erro do lado do provider, transitório |

O mesmo nome vale nos dois providers, e o código de qualquer um dos dois também: `--scenario 63016` e
`--scenario 131047` chegam ao mesmo cenário, e cada duplo responde no seu dialeto. Um cenário que o
provider escolhido não tem **falha na linha de comando**, nomeando os que existem. Um duplo que inventasse
um erro que a API real nunca devolve seria pior que nenhum duplo.

No roteiro, `--scenario` escolhe **como o ato 2 recusa**, porque os atos 1 e 3 precisam ter sucesso para a
execução provar alguma coisa. Sem o argumento, a recusa é o `optout` na Twilio e o `window` na Meta, que é
o que ela tem de mais próximo.

`serve` sobe só o duplo, sem roteiro, para exercitar o Hiram por outro caminho. `POST /_control/scenario`
troca a resposta sem reiniciar o processo.

**`--live` gasta dinheiro.** Ele não sobe duplo: o Hiram mantém os endereços de produção e o roteiro dispara
contra a conta real. Use apenas com a credencial do ambiente e sabendo que a mensagem sai.

O duplo não entra no gate de CI. O que o CI cobre é a paridade entre o que ele responde e o que os adapters
classificam (`ProviderDoubleParityTests` e `MetaDoubleParityTests`), sem abrir porta e sem rede.

### 3.8.1 Escolher o provider

`--provider twilio` é o padrão e não mudou. `--provider meta` sobe o duplo da Cloud API em vez do da
Twilio:

```bash
dotnet run --project tools/Hiram.Simulator -- serve --provider meta
```

O Hiram aponta para ele por uma variável só, porque a Cloud API é um host só:

```bash
Hiram__Providers__Endpoints__MetaGraph=http://localhost:4010/ dotnet run --project src/Hiram.Api
```

Duas diferenças de comportamento, ambas por limite real do provider e não por escolha:

1. **O roteiro da Meta corre em WhatsApp, não em SMS.** A Cloud API não tem SMS, então os três atos usam o
   canal que existe. O da Twilio continua em SMS, como sempre foi.
2. **A Meta configura um canal só.** O tenant recebe `meta-whatsapp` em `whatsapp` e nada mais.

O tenant do roteiro recebe `phone_number_id` de teste, e a versão da Graph API vem do padrão do host, para
que a execução exercite o valor que produção usaria. Um tenant pode fixar a sua em
`settings.graph_version`.

## 4. Rotação de API key

1. Emita uma segunda chave com nome identificável.
2. Atualize o segredo do consumidor e confirme tráfego autenticado com a chave nova.
3. Revogue a antiga pelo `id`, nunca pelo valor em claro:

```bash
curl --fail-with-body -X DELETE https://hiram.example/v1/admin/api-keys/<api-key-id> \
  -H "X-Admin-Key: $HIRAM_ADMIN_KEY"
```

Não revogue antes de observar o consumidor novo. Resposta `404` exige conferir o `id`; não repita a
operação contra outra chave.

## 5. Provider indisponível

Confirme primeiro PostgreSQL e `/health/ready`. Depois filtre logs por `notification_id`, provider e
tenant, sem registrar recipient, body ou segredo. Falhas transitórias permanecem no outbox até
`available_at`; falhas esgotadas viram dead letter.

Se o provider estiver indisponível:

1. preserve os workers ligados para que a política de retry controle a pressão;
2. não reduza `available_at` manualmente;
3. acompanhe backlog, item elegível mais antigo e dead letters;
4. corrija credencial, rede ou provider;
5. observe a drenagem e a taxa de `sent`;
6. faça replay somente de dead letters cujo motivo foi entendido.

## 6. Fila crescente e leases

As consultas abaixo são operacionais e não devem virar endpoints públicos. Execute com uma role de
leitura, sempre em conexão segura.

```sql
SELECT count(*) AS backlog
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL;

SELECT min(available_at) AS oldest_eligible_at
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
  AND available_at <= now()
  AND (lease_until IS NULL OR lease_until <= now());

SELECT count(*) AS expired_leases
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
  AND lease_until <= now();

SELECT type, attempt_count, last_error, available_at, lease_until
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
ORDER BY created_at_utc
LIMIT 50;
```

Backlog com `available_at` futuro pode ser retry normal. Backlog elegível crescendo, sem
`hiram.outbox.dispatched`, indica worker parado ou incapaz de reivindicar. Lease vencido volta a ser
elegível automaticamente; não altere a linha manualmente.

## 7. Dead letter e replay

```sql
SELECT id, tenant_id, notification_id, channel, reason, attempt_count, created_at_utc
FROM notifications.dead_letter_messages
WHERE replayed_at_utc IS NULL
ORDER BY created_at_utc;
```

Depois de corrigir a causa, use a API autenticada do tenant:

```bash
curl --fail-with-body -X POST \
  https://hiram.example/v1/notifications/<notification-id>/replay \
  -H "X-Api-Key: $HIRAM_API_KEY"
```

O replay cria trabalho novo de forma auditável. Não edite status, outbox ou dead letter diretamente.

## 8. Resultado incerto pós-provider

Timeout ou queda depois da chamada pode esconder um aceite do provider. Antes de qualquer replay,
consulte `GET /v1/notifications/{id}` e correlacione `delivery_attempts.provider_message_id` com o
painel ou API do provider. Se houver aceite, reconcilie o estado sem reenviar. Se não houver evidência
conclusiva, escale e preserve a linha, os logs e o trace. Reenvio cego pode duplicar comunicação e é
proibido.

## 9. Backup e restore

Execute `deploy/dr/hiram-backup.sh` diariamente com `PGHOST`, `PGUSER`, `BACKUP_DIR` e
`KEYRING_DIR`. O destino deve sair do host. A ausência do key ring torna o backup incompleto e faz o
script falhar.

Restaure em ambiente isolado:

```bash
createdb hiram_restored
pg_restore --exit-on-error --dbname=hiram_restored hiram-<stamp>.dump
tar -xzf hiram-keyring-<stamp>.tar.gz -C /var/hiram/dataprotection-keys
```

Valide contagens, um tenant, uma notificação e a leitura de configuração cifrada antes de promover.
`deploy/dr/verify-backup.sh` automatiza uma prova descartável de banco e key ring e roda no CI.

## 10. Observabilidade e escalonamento

Monitore as métricas documentadas em `deploy/observability/slos.md`, `/health/ready`, backlog e
dead letters. Registre em todo incidente: início, tenant afetado, provider, IDs de notificação,
primeiro sintoma, ação tomada e critério de encerramento. Nunca inclua body, recipient, API key ou
segredo do provider.
