# Provisionamento do tenant Jornada do Candidato

A Jornada do Candidato emite eventos para o Hiram em `POST /v1/events`. Para que esses eventos virem
mensagem, o tenant precisa existir com api key, provider configurado no canal, templates aprovados e uma
rotina por eventType. O `provision.sh` faz esse caminho inteiro de forma idempotente, no lugar da
sequencia de curl feita a mao.

Os tres canais do ADR-028 estao no ar: e-mail, SMS e WhatsApp. `JORNADA_CHANNELS` escolhe o mix.

## Pre-requisitos

- `bash` e `curl` (sem `jq`).
- Um Hiram no ar e respondendo em `GET /health/ready`. Local: `docker compose -f docker-compose.dev.yml up -d`
  na raiz do repo, que sobe a API em `http://localhost:3357` com Mailpit em `http://localhost:8025`.
- A admin key do ambiente (`Hiram__AdminKey`, `admin-dev-local` no compose de desenvolvimento).
- Para SMS e WhatsApp, uma credencial Twilio. Sem ela o script avisa e segue: o canal fica sem provider.

## Uso

```bash
cd deploy/jornada
cp .env.jornada.example .env   # ajuste a URL, a admin key e o mix de canais do ambiente alvo
./provision.sh all
```

Rodar de novo e seguro: o `all` reaproveita tenant e api key dos arquivos de estado, o `PUT` do provider
e um upsert, o template ja criado e reaproveitado e a rotina existente volta como 200 em vez de virar
duplicata.

## Subcomandos

| Subcomando | O que faz |
|---|---|
| `tenant` | Cria o tenant live e emite a api key do emissor. Grava `.jornada-tenant` e `.jornada-key` com permissao 600. |
| `providers` | Configura o provider de cada canal servido em `PUT /v1/providers/{channel}`. |
| `templates` | Cria e aprova os templates da jornada em cada canal pedido. Template ja existente tem o conteudo atualizado, quando ele mudou, e e aprovado de novo. |
| `routines` | Liga cada eventType da jornada ao seu template, na categoria `transactional`. |
| `consent` | Registra opt-in para os guids em `JORNADA_TEST_USER_IDS`, em cada canal servido, nas categorias transactional e operational. |
| `all` | Executa os cinco na ordem. |

### `providers`

Canal servido e canal do mix mais o `email`, porque a rotina de verificacao mantem o e-mail em jogo em
qualquer mix.

O `PUT /v1/providers/{channel}` grava a configuracao daquele canal para o tenant autenticado. O SID da
conta e o SID da api key ficam em `settings`, em claro; o secret vai para a coluna protegida por Data
Protection e nunca volta em leitura. O script nunca ecoa o secret, nem em log nem em mensagem de erro, e
manda o payload por stdin justamente para que o valor nao apareca na linha de comando do processo.

| Canal | Provider | O que o script envia |
|---|---|---|
| `sms` | `twilio-sms` | `account_sid`, `from`, `api_key_sid` e o secret. Com `TWILIO_TRIAL_MODE=true` acrescenta `trial_mode` e `trial_template`. |
| `whatsapp` | `twilio-whatsapp` | `account_sid`, `from`, `api_key_sid` e o secret. Sem modo trial: o adapter de WhatsApp nao tem um. |
| `email` | `twilio-email` | Nada, por padrao. So configura quando `USE_TWILIO_EMAIL=true`; fora disso o e-mail continua saindo pelo provider da plataforma. |

Regras de borda:

- Variavel Twilio faltando no canal: aviso e o canal e pulado, com saida 0. Um mix so de e-mail nao
  precisa de Twilio nenhum. Valor ainda com o `CHANGE_ME` do exemplo conta como faltando, para que um
  `.env` copiado e nao editado avise no provisionamento em vez de falhar um dead letter depois.
- `TWILIO_TRIAL_MODE=true` sem a chave do conteudo aprovado do canal: aviso e o canal e pulado. Gravar o
  provider assim escreveria uma configuracao que so poderia falhar no envio.
- `TWILIO_WHATSAPP_FROM` com o prefixo `whatsapp:` ou fora do E.164: erro imediato. O prefixo pertence ao
  adapter, e o numero duplicado seria recusado em toda mensagem.
- Canal em `JORNADA_CHANNELS` sem provider configurado nao falha no provisionamento: falha na entrega,
  como dead letter `provider_not_configured`. E por isso que `providers` roda antes dos templates.

### Mix de canais e conteudo

`JORNADA_CHANNELS` aceita `email`, `sms` e `whatsapp`, separados por virgula, em qualquer ordem. Nome
desconhecido vira aviso e e ignorado.

O nome do template e o mesmo em todos os canais, porque o indice e por (tenant, canal, nome). O que muda
e o conteudo: e-mail leva assunto e corpo longo, SMS e WhatsApp levam uma linha e nenhum assunto. Mandar
`subject` nesses dois canais e 400 no endpoint, entao o script omite o campo.

| eventType | template | e-mail | sms e whatsapp |
|---|---|---|---|
| `VerificacaoDeEmailSolicitada` | `verificacao-de-email` | sim | **nao** |
| `CandidatoEncaminhado` | `candidato-encaminhado` | sim | sim |
| `CandidatoAprovadoPelaLoja` | `candidato-aprovado` | sim | sim |
| `CandidatoStatusAlterado` | `candidato-recebido-pela-loja` | sim | sim |

**Excecao da verificacao de e-mail.** `VerificacaoDeEmailSolicitada` tem rotina apenas no canal `email`,
mesmo quando `JORNADA_CHANNELS` inclui os outros, e o template `verificacao-de-email` so existe em
e-mail. Um link de confirmacao de endereco de e-mail nao tem para onde levar em SMS ou WhatsApp: o
destinatario nao esta provando posse do telefone, esta provando posse da caixa. Por isso o template de
e-mail e criado em qualquer mix, ate quando o mix nao pede o canal `email`.

O eventType e comparado por igualdade exata, entao o PascalCase acima e obrigatorio. As variaveis do
corpo tambem sao PascalCase: o renderer roda com `StrictVariables`, e uma chave com casing diferente
derruba a renderizacao e a mensagem nao sai. E-mail usa `Protocolo`, `Nome`, `LinkVerificacao` e
`ExpiraEm`; SMS e WhatsApp usam apenas `Protocolo`, o que mantem a mensagem em um segmento e pede menos
do emissor.

### `consent` e o opt-in obrigatorio do WhatsApp

O subcomando registra `optIn: true` para cada guid de `JORNADA_TEST_USER_IDS`, em cada canal do mix mais
o `email`, nas categorias `transactional` e `operational`.

Em e-mail e SMS isso e explicitacao, nao requisito: a `ConsentPolicy` do Hiram e fall-open nessas duas
categorias, e so `marketing` exige registro. **Em WhatsApp e requisito.** A politica e fail-closed para
esse canal em toda categoria, transacional inclusive: sem registro de opt-in a mensagem e suprimida no
fan-out, nenhuma `notification_request` e escrita e o log diz `suppressed on channel WhatsApp by consent`.
Um destinatario sem opt-in gera e-mail e SMS e nao gera WhatsApp, o que costuma ser lido como bug e nao e.

O opt-in do WhatsApp em producao vem do consentimento real do candidato, registrado em `POST /v1/consent`
pelo sistema da Jornada. `JORNADA_TEST_USER_IDS` existe para os usuarios de teste do ambiente, nao para
suprir o consentimento de quem nao deu.

## Variaveis

Ordem de precedencia: ambiente, depois `.env` ao lado do script, depois o padrao.

| Variavel | Padrao | Para que serve |
|---|---|---|
| `HIRAM_BASE_URL` | `http://localhost:3357` | Hiram alvo. |
| `HIRAM_ADMIN_KEY` | `admin-dev-local` | Header `X-Admin-Key` das chamadas de admin. |
| `HIRAM_JORNADA_API_KEY` | vazio | Api key ja emitida, para reprovisionar sem gerar outra. |
| `JORNADA_TENANT_NAME` | `jornada-do-candidato` | Nome do tenant. |
| `JORNADA_CHANNELS` | `email` | Mix de canais: `email`, `sms`, `whatsapp`. |
| `JORNADA_TEST_USER_IDS` | vazio | Guids que recebem opt-in explicito no subcomando `consent`. |
| `TWILIO_ACCOUNT_SID` | vazio | SID da conta (`AC...`), vai para `settings`. |
| `TWILIO_API_KEY_SID` | vazio | SID da api key (`SK...`), vai para `settings`. |
| `TWILIO_API_KEY_SECRET` | vazio | Secret da api key, vai para o campo protegido. Nunca ecoado. |
| `TWILIO_SMS_FROM` | vazio | Remetente de SMS em E.164. |
| `TWILIO_WHATSAPP_FROM` | vazio | Remetente de WhatsApp em E.164, **sem** o prefixo `whatsapp:`. |
| `TWILIO_TRIAL_MODE` | `false` | Liga o conteudo pre-aprovado em sms e email. |
| `TWILIO_SMS_TRIAL_TEMPLATE` | vazio | Chave da mensagem aprovada de SMS, exigida quando o modo trial esta ligado. |
| `USE_TWILIO_EMAIL` | `false` | Configura `twilio-email` no canal de e-mail em vez de manter o provider da plataforma. |
| `TWILIO_EMAIL_FROM`, `TWILIO_EMAIL_FROM_NAME` | vazio | Remetente do canal de e-mail pela Twilio. |
| `TWILIO_EMAIL_TRIAL_SUBJECT`, `TWILIO_EMAIL_TRIAL_HTML` | vazio | Conteudo aprovado de e-mail, exigido quando o modo trial esta ligado. |

## Estado local

`.jornada-tenant` e `.jornada-key` ficam ao lado do script e estao no `.gitignore` deste diretorio,
junto do `.env`. O script aplica `chmod 600` nos dois; em sistema de arquivos que ignora o modo, como
NTFS pelo Git Bash, a chamada nao tem efeito e a protecao passa a ser a do diretorio.

A api key aparece em texto claro uma unica vez, na execucao que a emite: o Hiram guarda apenas o hash.
Se o arquivo for perdido, o caminho e emitir outra em `POST /v1/admin/api-keys` e revogar a anterior.

Apagar `.jornada-tenant` nao e reversivel do lado do Hiram: nao existe busca de tenant por nome, entao
a proxima execucao cria um tenant novo em vez de reencontrar o antigo. Guarde os dois arquivos junto do
backup do ambiente.

## Conta trial da Twilio

Enquanto a conta for trial, o que sai pelo provider nao e necessariamente o que o Hiram renderizou:

- **Destino verificado.** SMS so chega a numero verificado no console. Fora disso a Twilio recusa com
  **21608**, que e falha permanente e vira dead letter com o codigo no motivo.
- **Conteudo pre-aprovado.** Com `trial_mode` ligado, o corpo enviado e o conteudo aprovado, nao o do
  template. O corpo renderizado continua persistido em `notification_requests`, e o `DeliveryAttempt`
  marca `trial_content` para que o historico nao afirme ter entregue um texto que nunca saiu.
- **Sem consulta de status.** Consultar a mensagem enviada responde 403 no trial, entao nao ha polling
  de estado de entrega. O que o Hiram sabe e que o provider aceitou.
- **Sandbox do WhatsApp.** O destinatario precisa mandar `join <frase>` para o numero do sandbox. Isso
  abre uma janela de 24h, renovada a cada mensagem dele. Fora da janela a Twilio recusa com **63016**,
  que vira dead letter permanente e e recuperavel por replay depois do rejoin.

O procedimento completo de onboarding da credencial, incluindo rotacao e o roteiro de smoke manual, esta
em `docs/operations-runbook.md`, secao "Onboarding de credencial Twilio por tenant".

## Avisos

- **O volume `hiram-keyring` precisa persistir.** Ele guarda as chaves de Data Protection que cifram os
  segredos de provider por tenant. Se o volume for descartado (`docker compose down -v`, recriacao da
  maquina sem restore), esses segredos deixam de descriptografar e precisam ser reconfigurados, mesmo
  com o banco intacto. O remedio e rodar `./provision.sh providers` de novo. O procedimento de backup e
  restore esta em `deploy/dr/` e no `docs/operations-runbook.md`.
- **Os corpos sao texto puro, nao HTML.** Nenhum provider de e-mail do Hiram entrega HTML hoje: o
  `SmtpEmailProvider` monta `TextPart("plain")` e o `ResendEmailProvider` envia o corpo no campo
  `text`. Um corpo com marcacao chegaria ao candidato com as tags a mostra. A parte `text/html` nos
  adapters esta proposta na issue #114; quando ela existir, basta trocar o conteudo aqui.
- Mudar assunto ou corpo no script e o caminho normal de correcao: uma reexecucao atualiza o template
  que ja existe e o aprova de novo. A atualizacao so acontece quando o conteudo muda de fato, porque
  ela incrementa a versao do template, e a versao compoe a chave de mensagem do fan-out.
- **Mudar `JORNADA_CHANNELS` depois da primeira execucao nao converge a rotina.**
  `POST /v1/admin/routines` devolve a rotina que esta la, sem alterar canais ou categoria, e nao existe
  endpoint de atualizacao. O script compara o que voltou com o que pediu e avisa quando os dois divergem,
  mas trocar o vinculo hoje exige desativar a linha antiga direto no banco. Decida o mix antes da
  primeira execucao no ambiente.
