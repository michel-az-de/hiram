# Plano: Hiram como plataforma de notificações do EasyStok (absorção total, email primeiro)

> Plano executável no estilo das fases anteriores. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Test-first: vermelho antes de verde. Cerca additive-only e DoD uniforme valem. Em nenhum texto use travessão (em dash). Decisão estrutural exige ADR antes do código.

## Contexto

O EasyStok (ERP de estoque, .NET 9, Clean Architecture, Postgres) hoje tem um subsistema de
notificações próprio e maduro: 37 tipos de evento, 5 canais (Email SMTP/SendGrid, SMS
Twilio/Zenvia, WhatsApp Meta/Twilio, InApp), templates versionados com aprovação, rotinas cron e por
evento, consentimento LGPD por usuário, bloqueios/kill-switch, outbox em Postgres com sharding e
advisory locks. Funciona, mas a confiabilidade depende de polling em banco, não há fila dedicada nem
replay de DLQ, a observabilidade de entrega é parcial e o status "enviado" mente (não ingere
bounce/complaint do provider).

O Hiram (plataforma multi-tenant de notificações, .NET 10) já resolve a parte difícil de entrega:
ingestão transacional com outbox, relay para RabbitMQ, retry com Polly, DLQ com replay, idempotência
(Redis + índice único), metering por credit ledger, shadow mode para auditoria de paridade e
observabilidade OpenTelemetry ponta a ponta. Mas só entrega Email e Push, e não tem nenhuma das
camadas de gestão (eventos, rotinas, consentimento, templates como motor, bloqueios).

O objetivo é o Hiram passar a gerenciar e entregar todas as notificações do EasyStok, rodando toda a
sua infraestrutura no ambiente do EasyStok, com email como prioridade, sob exigência de que tudo seja
medido objetivamente e apresentado com justificativa e probabilidade.

Decisão tomada com o usuário:

1. Modelo: absorção total. Hiram assume eventos, rotinas, consentimento, templates, fallback e
   bloqueios. EasyStok passa a só emitir eventos crus.
2. Canais nesta fase: email primeiro. SMS, WhatsApp e InApp ficam para ondas seguintes, cada um com
   ADR próprio.
3. Deploy: k3s + KEDA (ADR-010) na VM do EasyStok, single-node co-residente.
4. Banco: mesmo servidor Postgres do EasyStok, com database `hiram` dedicado.

Consequência honesta: o subsistema de notificações do EasyStok será progressivamente desativado,
canal por canal. Não é integração lado a lado permanente, é substituição medida.

## O que já existe, medido (o "code-review" pedido)

Estado verificado por leitura de código. Caminhos citados.

| Componente Hiram | Estado | Evidência |
|---|---|---|
| Ingestão `POST /v1/notifications` (pré-renderizado) | PRONTO | `src/Hiram.Api/Notifications/NotificationEndpoints.cs` |
| Persistência transacional (request + outbox + ledger na mesma transação) | PRONTO | `src/Hiram.Infrastructure/Persistence/NotificationStore.cs` |
| Outbox relay (FOR UPDATE SKIP LOCKED, poll 1s, publisher confirms) | PRONTO | `src/Hiram.Infrastructure/Messaging/OutboxRelay.cs` |
| RabbitMQ topology + DLX | PRONTO | `src/Hiram.Infrastructure/Messaging/HiramTopology.cs` |
| Email SMTP (MailKit) e Resend (HTTP), resolver por tenant | PRONTO | `EmailProviderResolver.cs`, `Delivery/*` |
| Retry Polly + DLQ + DeadLetterMessage + replay | PRONTO | `EmailDeliveryPipeline.cs` |
| Idempotência (Redis fast-path + índice único) | PRONTO mas só nível evento direto | `RedisIdempotencyKeys.cs` |
| Shadow mode (grava PayloadHash, não chama provider) | PRONTO | `EmailNotificationProcessor.RecordShadowAttemptAsync` |
| Templates (Scriban) CRUD | PRONTO mas básico | `ScribanTemplateRenderer.cs`; sem evento, versão, aprovação, checksum |
| Migrations em produção | AUSENTE (bug confirmado) | `src/Hiram.Api/Program.cs:38` só migra em Development |
| Data Protection key ring compartilhado | AUSENTE (bug confirmado) | `DependencyInjection.cs:29` sem PersistKeys nem SetApplicationName |
| Health/readiness | AUSENTE | `Program.cs` não mapeia `/health` |
| Graceful shutdown com draining | PARCIAL | consumers cancelam, não drenam in-flight |
| Dockerfile, manifests k8s/KEDA | AUSENTE | só `docker-compose.dev.yml` |
| Callback de provider (bounce/complaint/delivery) | AUSENTE | `ResendEmailProvider` descarta o message-id; status para em "sent" |
| Camada de gestão (eventos crus, rotinas, consentimento, bloqueios, fallback, adiamento) | AUSENTE | não existe no domínio |

Ambiente de deploy do EasyStok: VM Azure única via `EasyStok/docker-compose.azure.yml` (Postgres 17
em container com volume, Redis 7 efêmero, Caddy TLS, secrets em `.env`, sem RabbitMQ). Dockerfiles
maduros como referência (`EasyStok/Dockerfile`, `EasyStock.Worker/Dockerfile`). Data Protection já em
volume (`DataProtection__KeysPath`). `EasyStok/k8s/` vestigial com placeholders.

## Fronteira da absorção total (o limite irredutível)

Absorção total não deixa o EasyStok sem nada. A camada que precisa permanecer nele, por depender de
dados do ERP que o Hiram não tem nem deve ter:

- Emissão de eventos crus de negócio (criar pedido, baixar estoque). É emissão de evento de domínio,
  não gestão de notificação.
- Detectores cron que varrem dados do ERP (produto vencendo em 7 dias, conta a pagar vencendo). A
  varredura roda no EasyStok com acesso ao schema; o detector vira emissor de eventos crus. O Hiram é
  dono do agendamento de rotinas que não dependem de dados do ERP e da decisão de notificação a partir
  do evento.
- Fonte de verdade do contato do destinatário (email, telefone). Ver decisão em "Decisões transversais".

Tudo o mais (resolução de rotina, template, consentimento, canais permitidos, ordem de fallback,
adiamento por janela, bloqueio, limite diário, renderização, entrega, retry, DLQ, metering, status)
passa a viver no Hiram.

## Arquitetura alvo

Novo caminho de ingestão no Hiram, ao lado do `POST /v1/notifications` (que continua para envios
diretos pré-renderizados, sob cerca additive-only):

```
EasyStok emite evento cru  ->  POST /v1/events (Hiram)
  -> persiste Event + OutboxMessage na mesma transação (chave de idempotência de evento)
  -> motor de rotina resolve N rotinas: template(versão) + canais + categoria + janela/fuso + dedupe
  -> consentimento + bloqueio + limite diário filtram canais permitidos
  -> para cada (canal, destinatário): gera mensagem com chave determinística de mensagem
  -> renderiza assunto/corpo (Scriban) por canal
  -> adiamento por janela/fuso decide dispatch_at (envia já, ou agenda para abertura da janela)
  -> relay publica na fila do canal quando dispatch_at <= agora
  -> consumer entrega via provider (retry, DLQ, shadow), checando chave de mensagem antes do provider
  -> registra DeliveryAttempt + credit ledger
  -> callback de provider atualiza estado (delivered/bounced/complained), idempotente e por precedência
  -> status webhook de volta ao EasyStok (HMAC, já existe)
```

Mapeamento: `Empresa` vira Tenant (um por empresa). Canais mapeados por nome no wire (email, sms,
whatsapp), nunca por ordinal (os enums divergem entre os dois sistemas).

## Contratos congelados antes do código (artefato único, referenciado pelos dois repos)

Antes de qualquer código de onda, congelar um contrato versionado em `Hiram.Contracts`. O cliente do
EasyStok é gerado por codegen a partir do OpenAPI do Hiram, nunca um espelho mantido à mão (espelho
manual diverge em silêncio). Produtor e consumidor referenciam a mesma fonte. Inclui:

- Schema do `POST /v1/events`: tenant, tipo de evento (string), `event_id` (idempotência),
  `emission_seq` (sequência monotônica atribuída pelo banco do EasyStok, é o atributo de watermark do
  cutover), contato do destinatário, `logical_alert_id` (dedupe), dados para template, timezone.
- Vocabulário dos 37 tipos de evento como strings canônicas (mapa explícito do enum do EasyStok para a
  string). Nunca trafegar ordinal.
- Mapa de nome de canal (email/sms/whatsapp/inapp/push) para o enum de cada lado.
- Labels de métrica OTel padronizados (event_type, channel, outcome), idênticos nos dois sistemas,
  senão o dashboard de paridade não junta as séries. O label `tenant` NÃO entra nas séries de alto
  volume: tenant x event_type x channel x outcome com muitas empresas explode a cardinalidade no LGTM
  single-container, que roda na mesma VM protegida de OOM. Tenant fica restrito a um subconjunto
  curado ou como exemplar, nunca em tudo.
- Payload do status webhook (estados, timestamps, provider, motivo).
- Regra de tipo de evento desconhecido: se o EasyStok deployar um tipo novo antes de o Hiram conhecer,
  o Hiram faz dead-letter mais alerta, nunca accept-and-drop silencioso. O tipo desconhecido é visível
  e recuperável por replay, não perdido.

Critério: contrato revisado e versionado em commit próprio antes do passo 1.1. Mudança de contrato
depois disso é breaking e exige bump de versão mais nota de migração.

## Decisões transversais (os gaps críticos resolvidos, não só sinalizados)

### Idempotência de dois níveis no fan-out (gap 1)

Hoje o índice único protege apenas o caminho direto: `NotificationRequest (tenant_id,
idempotency_key)` para o `POST /v1/notifications`. Um evento, porém, vira N mensagens renderizadas, e
replay de DLQ ou redelivery do RabbitMQ pode refazer o fan-out e reenviar email. Dois níveis:

- Nível evento (ingestão): chave = `event_id` do EasyStok (o OutboxId atual). Índice único `events
  (tenant_id, event_id)`. Reentrega do mesmo evento retorna o mesmo resultado, não refaz fan-out.
- Nível mensagem (envio): chave determinística = `hash(event_id, channel, recipient,
  template_version, dispatch_slot)`. Índice único na tabela de mensagens/entregas.

A chave vem da decisão persistida no momento do fan-out, congelada com a mensagem, nunca recalculada
depois. Bump de template ou recálculo de janela mudaria a chave e causaria double-send se fosse
recomputada na entrega. Por isso `template_version` é o da decisão e `dispatch_slot` é atado ao
schedule lógico (a data do alerta, por exemplo), não ao wall-clock `dispatch_at`. O slot permite
reenvio legítimo recorrente (alerta diário) sem colidir com o dedupe de redelivery do mesmo slot.
Para evento transacional ("pedido criado") não há data de schedule: o slot colapsa para um valor fixo
e a chave degenera para `hash(event_id, channel, recipient, template_version)`. O slot só varia em
alertas recorrentes ou cron.

O claim antes do provider é durável no Postgres, na mesma transação do DeliveryAttempt, e o Postgres é
a única autoridade de "já enviado". Redis só acelera o caminho feliz; um miss segue para o claim no
Postgres, nunca decide sozinho. Redis nunca é autoritativo, porque um hit em Redis com o claim no
Postgres ainda não commitado (crash entre os dois) seria falso positivo e o email se perderia.

### Recuperação outbox versus RabbitMQ efêmero (gap 11)

Afirmar e verificar a semântica: o outbox é fonte de recuperação apenas até a publicação. O relay
marca `processed` ao confirmar a publicação (publisher confirms ativos). Depois disso, a durabilidade
é responsabilidade do RabbitMQ (filas e mensagens persistentes, volume obrigatório em produção). Se o
RabbitMQ perder uma mensagem já confirmada, o outbox não republica, porque já está `processed`. Logo:
exchange/filas duráveis, `Persistent = true`, volume persistente no broker, e DLQ como rede. Teste
nomeado verifica que matar o broker antes do consumo, com mensagem confirmada, é coberto pela
durabilidade do broker, e que matar antes da confirmação mantém a linha pendente para republicação.

Limite declarado, não coberto: RabbitMQ single-node com publisher confirms sobrevive a restart de
processo (estado no disco), mas não a perda de disco. Uma mensagem confirmada e ainda não consumida,
nesse cenário de perda de disco, some, e o outbox já está `processed`. É aceitável para esta
arquitetura single-node, mas fica registrado como risco residual, não como caso coberto. Mitigação
futura se o risco não for tolerável: quórum/cluster de broker ou marcar o outbox como entregue só após
ack do consumer (muda a semântica de recuperação).

### Adiamento por janela e fuso (gap 2, ADR-020)

Decisão: adiar, não suprimir. Suprimir mudaria o comportamento do EasyStok (cujo cron envia na
abertura da janela) e quebraria a paridade. O adiamento mora no Postgres, não no RabbitMQ: a mensagem
recebe `dispatch_at` calculado a partir da janela e do fuso do tenant; o relay só publica linhas com
`dispatch_at <= agora`. Isso reusa o poll de outbox que já existe e evita o plugin delayed-message do
RabbitMQ (que seria dependência nova, ADR à parte). Evento às 2h com janela 8h-20h vira mensagem com
`dispatch_at` = 8h do fuso do tenant.

O due-check (`dispatch_at <= agora`) usa o relógio do banco (`now()` do Postgres) como autoridade, não
o relógio do app, para não sofrer skew entre pods.

Para evitar thundering herd na abertura da janela (um batch sincronizado às 8h dispara pico de publish,
KEDA e rate limit do provider), o `dispatch_at` recebe jitter dentro da janela, e o relay tem
rate-limit de publicação. A telemetria separa duas grandezas: backlog agendado (mensagens com
`dispatch_at` no futuro, esperado) de backlog atrasado (overdue, `dispatch_at` vencido e ainda não
publicado, o que de fato é problema). São SLIs distintos.

### Relay compartilhado e a cerca additive-only (gap 2)

O adiamento reusa o relay existente, que o caminho direto fenceado também usa. Decisão consciente:
reusar o relay, não forká-lo, porque duplicar a lógica de confiabilidade já madura (FOR UPDATE SKIP
LOCKED, publisher confirms, marcação processed) é pior que o acoplamento. O preço: a query do relay
passa de "pega pendentes" para `WHERE dispatch_at IS NULL OR dispatch_at <= now()`, e `dispatch_at`
é coluna nova nullable onde NULL preserva o comportamento imediato do caminho direto. Como isso toca
infra que a cerca protege, o gate de regressão ganha uma asserção explícita: `DirectNotification_
StillPublishesImmediately_WithDeferralQuery`. A alternativa (tabela e relay próprios para o caminho de
eventos) fica registrada e rejeitada por duplicação.

### Localização do contato do destinatário (gap 8)

Decisão: o contato (email, telefone) viaja no payload do evento, no instante da emissão. O EasyStok é
a fonte de verdade do contato e ele está fresco no momento do evento de negócio. O consentimento e as
preferências moram no store do Hiram. Trade-off aceito: se o usuário trocar o email entre emissão e
envio, usamos o email da emissão; aceitável para transacional e evita sync de contato por usuário.
Afeta os passos 1.4 e 1.8.

### Consentimento durante a transição (gaps 5 e cross-channel)

O consentimento é por usuário e cruza canais (por usuário, ou por usuário mais categoria), não por
canal. Logo, a autoridade de escrita do consentimento NÃO move canal por canal. O email-first cria uma
restrição de sequência que o ADR-018 precisa nomear: quando o consentimento migra para o Hiram, os
canais que o EasyStok ainda serve localmente (SMS, WhatsApp, InApp) precisam do mesmo consentimento
por usuário. Sem tratar isso, vira split-brain de consentimento.

Decisão, em três partes:

- Durante o shadow: dual-write de consentimento. A UI do EasyStok grava no store local e, na mesma
  operação, chama a API de consentimento do Hiram. Falha do Hiram não bloqueia o EasyStok (best
  effort com reconciliação periódica), mas o dual-write reduz drift a quase zero. A auditoria de
  paridade ignora eventos cujo consentimento mudou dentro de uma janela de carência configurável.
- No cutover de consentimento (evento único, cross-channel, independente do cutover de entrega): a
  autoridade de LEITURA migra para o Hiram (UI e worker do EasyStok passam a ler via API do Hiram). O
  dual-write PERMANECE ligado através do cutover, com soak antes de desligar a escrita local. Ou seja,
  durante o soak as escritas continuam indo para os dois stores, mantendo o local sincronizado. Isso
  torna o rollback do 2.0 uma flag (volta a leitura ao store local, que já está atual), em vez de
  reverter release de código e ressincronizar consentimento mudado em live. Só após o soak sem
  incidente a escrita local é desligada e o store entra em somente leitura, depois desativado. Não há
  janela com dois stores de escrita divergentes, porque o dual-write os mantém iguais.
- Enquanto o EasyStok ainda servir qualquer canal, UI e worker leem consentimento via API do Hiram
  (não só a UI). Cache curto com fail-safe conservador (na dúvida, não enviar) para não acoplar a
  disponibilidade do worker à do Hiram. O cutover de consentimento acontece no, ou antes do, primeiro
  cutover de entrega.

### Callbacks de provider idempotentes e independentes de ordem (gap 9)

Provider reenvia webhooks e pode entregar fora de ordem (delivered depois de bounced). A máquina de
estados pós-envio é um lattice, não uma sobrescrita por chegada:

- Dedupe por índice único `(provider, provider_event_id)`.
- Hard bounce versus soft bounce são distintos. Hard bounce é terminal e domina delivered atrasado.
  Soft bounce não é terminal: uma entrega posterior pode sucedê-lo, então soft seguido de delivery
  resolve em entregue, não trava em bounced.
- Complained não é um estado único que apaga delivered. Um email pode ter sido entregue E reclamado:
  os dois coexistem (delivered mais flag de complaint), porque ambos são fatos verdadeiros.
- Resolver por posição no lattice mais o timestamp do evento do provider, nunca pela ordem de chegada.
- Correlação por `provider_message_id` persistido no DeliveryAttempt no envio (hoje descartado).

Esse é o motivo real de o passo 2.3 ficar em 0.60, e está escrito aqui.

### Emissão durável e fronteira determinística por sequência (gaps 1, 4 e 10, o keystone)

Não existe transação compartilhada entre o Postgres do EasyStok e o do Hiram, então o flip não é
atômico: são duas escritas em dois sistemas. Chamar de "atômico" é impreciso. O que torna o flip não
atômico seguro é uma fronteira determinística carregada por evento. Uma peça fecha três gaps.

Verificação na fonte (pré-condição que o review exigiu): os outboxes do EasyStok têm `ShardKey =
hash[0] % 4` de um SHA-256 (shard por hash, não por empresa) e nenhuma sequência monotônica (`Id` é
GUID aleatório, `CriadoEm` é wall-clock do app). Logo não há sequência existente para reusar como
watermark, e wall-clock não é monotônico na fronteira (skew, múltiplas instâncias, NTP). O keystone,
então, precisa adicionar a sequência, não herdá-la.

Decisão, em três partes que se sustentam mutuamente:

- Emissão durável (gap 4): a emissão EasyStok para Hiram anda numa outbox do EasyStok, gravada na
  mesma transação do mutação de negócio, com retry. Para ficar totalmente aditivo e não perturbar a
  semântica da `outbox_evento_integracao` existente, usar uma tabela dedicada de emissão. O relay dessa
  emissão é flag-based (marca cada linha pendente/processada, igual ao outbox relay), nunca cursor
  (`WHERE emission_seq > last_sent`): um cursor pularia para sempre um late-committer com seq abaixo do
  cursor. A emissão é durável desde o shadow, sempre ligada. Falha ao emitir ao Hiram nunca é perda: a
  linha fica pendente e é reenviada. Best-effort fire-and-forget seria perda de dados pós-cutover,
  quando o local já não envia (ver semântica shadow versus live abaixo).
- Atributo de watermark (gap 1): a tabela de emissão tem uma sequência `emission_seq` atribuída pelo
  banco (`bigserial`, monotônica, não wall-clock). É o atributo carregado por evento no contrato
  congelado. Comparação por tenant: um subconjunto de uma sequência global monotônica é monotônico, e
  o tenant é a granularidade do corte. GUID não serve para isso; a sequência sim.
- Fronteira determinística (gap 10): no flip de uma empresa, capturar W = maior `emission_seq`
  atribuída até T0. A regra é só de valor, não de horário de visibilidade. O gate é dos DOIS lados: o
  EasyStok filtra a origem por `emission_seq` menor ou igual a W (o local entrega) e o Hiram persiste W
  por tenant e gateia a entrega em `emission_seq` maior que W, independente. Mesmo que a origem erre, o
  Hiram não entrega menor ou igual a W. Como a sequência só cresce, nenhum evento iniciado após T0 cai
  em menor ou igual a W. Sem janela cega, porque a emissão ao Hiram nunca para; o flip só muda quem
  entrega. Sem duplicar nem perder. Rollback move W de volta, religa o local e volta o tenant a shadow.
- Predicado de drain completo (gap 3, sutileza do bigserial): `bigserial` atribui na ordem de request
  mas commita fora de ordem, então uma seq menor ou igual a W pode estar aberta em T0 e commitar
  depois. Por isso drain completo NÃO é "não há pendente menor ou igual a W". Drain completo =
  (nenhum pendente menor ou igual a W) E (nenhuma transação iniciada antes de T0 ainda aberta). A
  segunda condição garante que todo seq menor ou igual a W já commitou e foi drenado, ou deu rollback.
  Só então o local pode declarar a empresa drenada.

### Semântica de emissão shadow versus live (gap 4)

A emissão é sempre durável (outbox-backed do lado do EasyStok). O que muda entre shadow e live não é a
durabilidade da emissão, é quem entrega, governado pelo watermark. Em shadow o Hiram registra e não
envia; em live o Hiram envia para `emission_seq` maior que W. Por isso o teste de que "falha do Hiram
não afeta o local" vale para a entrega local, não para a emissão: a emissão é retried, não
fire-and-forget, senão pós-cutover um erro de emissão viraria evento perdido.

### Isolamento multi-tenant no Postgres compartilhado (gap médio, segurança)

O Hiram usa filtros de tenant na aplicação (EF), não RLS, coerente com o desenho atual. No database
compartilhado isso exige postura de menor privilégio explícita, para não repetir a classe do P0 de
bypass de RLS do EasyStok (role com `rolsuper`/`rolbypassrls`): o role do Hiram no Postgres é dono
apenas do database `hiram`, sem superuser, sem `BYPASSRLS`, sem acesso aos schemas do EasyStok.
Database dedicado mais role de menor privilégio fecham o blast radius cruzado. Declarado no ADR-016.

### Não objetivo declarado: ordenação causal (gap médio)

Ordenação causal estrita entre eventos (um cancela chegar antes do cria correspondente) é não objetivo
deliberado para email nesta fase. Justificativa: email é assíncrono e tolerante a eventual; o custo de
ordenação total não se paga para transacional. Declarado, não omitido. Será revisitado se um canal
futuro exigir.

### Metering em shadow e enforcement de quota (gap D)

O débito do credit ledger acontece na ingestão (atômico com request mais outbox), não na entrega.
Logo, em shadow o evento é aceito e o ledger debita igual, o que inflaria metering e quota antes de o
tenant ir live. Duas decisões para fechar a incompletude silenciosa:

- Entradas de ledger geradas em shadow carregam flag de shadow (ou escopo de ledger separado) e ficam
  fora de qualquer cômputo de quota. Shadow não bilha consumo real.
- Enforcement de quota é não objetivo declarado desta fase. O ledger é observabilidade de custo, não
  porta de bloqueio. Quando a enforcement entrar, será passo com ADR próprio (revisão do ADR-007),
  não efeito colateral. Sem isso, a enforcement bloquearia envio real por consumo fantasma de shadow.

## Orçamento de conexões e recursos (gaps 3 e 4, dentro do ADR-016)

KEDA escala o Dispatcher por profundidade de fila. Sem teto, cada réplica abre pool no mesmo Postgres
do qual o ERP depende, e a contenção derruba o ERP. Regras duras, com números no ADR-016:

- Pool por réplica com `Maximum Pool Size` baixo e explícito por host.
- PgBouncer dedicado ao Hiram (instância e porta próprias), só na frente das conexões do Hiram. O
  caminho de dados do EasyStok NÃO passa pelo pooler: pôr PgBouncer na frente do ERP é mudança em
  produção do EasyStok e fura a cerca additive-only. Além disso, transaction pooling é incompatível
  com advisory locks de sessão, LISTEN/NOTIFY, GUCs de sessão e prepared statements server-side
  (Npgsql auto-prepare); o outbox do EasyStok usa advisory locks. As conexões do EasyStok entram no
  orçamento como alocação fixa medida, com o caminho intocado. Se algum dia o EasyStok for posto atrás
  de pooler, confirmar antes que seus advisory locks são xact-scoped (`pg_advisory_xact_lock`), não de
  sessão.
- Teto de réplica do Dispatcher (`maxReplicaCount` do KEDA) amarrado a um orçamento de conexões:
  `replicas_max x pool_por_replica (via PgBouncer do Hiram) + alocação_fixa_do_ERP + folga` menor que
  `max_connections`. O orçamento é calculado e versionado, não estimado em runtime.
- Requests e limits de memória e CPU em todo pod do Hiram. Orçamento de memória explícito da VM, com
  a soma dos limites do compose do EasyStok mais os limites do k3s deixando folga para o kernel.
- Postgres fora do conjunto que escala: ele é compartilhado e não roda no k3s. Limites em tudo que
  escala não bastam, porque o OOM killer pontua por RSS e o Postgres é o maior alvo. Reservar memória
  e proteger o processo do Postgres com `oom_score_adj` negativo, para que o ERP nunca seja a vítima.
  Cadeia de falha documentada no ADR.

Se o orçamento de conexões ou de memória não puder ser garantido na VM atual, o ADR-016 deve registrar
isso como gatilho para Postgres dedicado ou nó separado, em vez de prosseguir e arriscar o ERP.

## ADRs obrigatórios antes do código

- ADR-016: Deploy k3s + KEDA single-node na VM, Postgres compartilhado, orçamento de conexões e
  recursos, PgBouncer, isolamento por role de menor privilégio. Coexistência com o docker-compose do
  EasyStok.
- ADR-017: Ingestão de eventos crus e motor de notificação. Idempotência de dois níveis. Semântica de
  recuperação outbox/RabbitMQ. Semântica de matching de rotina. Emissão durável do EasyStok com
  sequência de watermark (emission_seq) e fronteira determinística de cutover. Metering em shadow e
  enforcement de quota como não objetivo desta fase.
- ADR-018: Destinatário e contato, consentimento LGPD no Hiram, transição (dual-write em shadow,
  cutover de consentimento único e cross-channel, não por canal), EasyStok (UI e worker) lendo
  consentimento via API do Hiram para os canais que ainda serve, localização do contato no payload.
- ADR-019: Callbacks de provider, máquina de estados idempotente e por precedência.
- ADR-020: Adiamento por janela e fuso via dispatch_at no Postgres. Reuso do relay com `dispatch_at`
  nullable (NULL preserva o caminho direto) mais asserção de regressão, jitter contra thundering herd,
  due-check por `now()` do banco.
- ADR-007 revisão e ADR-021 (SMS), ADR-022 (WhatsApp): ondas seguintes.

## Plano por ondas (WIP=1)

Cada passo tem esforço (P até meio dia, M de um a dois dias, G de três ou mais), probabilidade pela
fórmula da seção de medição, e o nome do teste que prova (test-first, escrito como asserção antes do
código).

### Onda 0: Fundação de produção

| # | Passo | Teste nomeado | Esforço | Prob. |
|---|---|---|---|---|
| 0.0 | ADR-016 | aceite do ADR | P | 0.90 |
| 0.1 | Dockerfiles Api e Dispatcher (.NET 10, multi-stage, non-root) | `DockerImage_Builds_AndApiAnswersHealth` | M | 0.85 |
| 0.2 | Secrets via env + `.env.hiram.example` | `Host_Boots_FromEnvOnly` | P | 0.90 |
| 0.3 | Data Protection key ring compartilhado (corrige bug) | `ApiEncrypts_DispatcherDecrypts_AcrossProcesses` (cross-process real) | P | 0.80 |
| 0.4 | Migrations em produção via Job `--migrate-only` (corrige bug) | `Migrate_OnEmptyDb_CreatesSchema; Migrate_OnCurrentDb_IsNoop; Migrate_DryRun_WritesNothing` | M | 0.85 |
| 0.5 | Health/readiness (Api live/ready, Dispatcher liveness) | `Ready_Returns503_WhenDependencyDown` | P | 0.75 |
| 0.6 | Graceful shutdown com draining | `Shutdown_DrainsInflight_NoLoss_BoundedDuplicates` | M | 0.80 |
| 0.7 | Database `hiram` (role menor privilégio, backup lógico) + backup do volume do keyring de Data Protection no DR | `HiramRole_CannotReadEasyStokSchemas; PgDump_Hiram_Succeeds; KeyringVolume_IsBackedUp_AndRestores` | P | 0.85 |
| 0.8 | PgBouncer dedicado ao Hiram + orçamento de conexões (ERP intocado) | `ConnectionBudget_HoldsUnderMaxReplicas; ErpPath_DoesNotTraversePooler` | M | 0.70 |
| 0.9 | Manifests k3s + RabbitMQ e Redis no cluster com persistência, requests/limits em todo pod | `Stack_BootsOnK3s_SmokePasses` | G | 0.65 |
| 0.10 | KEDA ScaledObject (Dispatcher por profundidade de fila, maxReplicaCount do orçamento) | `Keda_ScalesWithinReplicaCeiling_DrainsQueue` | M | 0.70 |
| 0.11 | Observabilidade LGTM single-container + dashboards + SLOs | `Otel_FromBothHosts_AppearsInGrafana` | M | 0.85 |

### Onda 1: Email como fatia vertical da absorção (em shadow)

Camada de gestão construída agnóstica a canal; só email é ligado e cortado.

| # | Passo | Teste nomeado | Esforço | Prob. |
|---|---|---|---|---|
| 1.0 | ADR-017, ADR-018, ADR-020 + contratos congelados | aceite + contrato versionado | M | 0.80 |
| 1.1 | `POST /v1/events` + persistência transacional (event + outbox) + idempotência de evento | `EventIngestion_PersistsEventAndOutbox_InSameTransaction; DuplicateEventId_DoesNotRefanout` | M | 0.80 |
| 1.2 | Motor de rotinas por evento (N matches, zero match, template não aprovado) | `Routine_MatchesAllActive; NoRoutine_RecordsNoRoute_NoSend; UnapprovedTemplate_Suppressed_WithReason` | G | 0.70 |
| 1.3 | Template estendido (evento, categoria, idioma, aprovação, versão, checksum) + migração de dados com dry-run | `MigratedTemplate_Renders_EqualUnderSharedCanonicalization` (mesma função de canonicalização do passo 2.1, definida uma vez no contrato) | G | 0.70 |
| 1.4 | Destinatário/consentimento/preferências + contato no payload + dual-write em shadow | `Consent_OptOut_FiltersChannel; ConsentDualWrite_ReconcilesDrift` | G | 0.65 |
| 1.5 | Bloqueio/kill-switch (global/canal/tenant, expiração) | `ActiveBlock_SuppressesEvent` | M | 0.80 |
| 1.6 | Resolver de canais com fallback (consentimento + config + bloqueio + janela) | `ChannelResolver_OrdersFallback_AppliesAllFilters` | M | 0.75 |
| 1.7 | Adiamento por janela/fuso (dispatch_at com jitter, due-check por now() do banco) + limite diário | `OffWindowEvent_DefersToWindowOpen_InTenantTz; WindowOpen_SpreadsByJitter_NoSpike; DailyLimit_BlocksExcess` | M | 0.70 |
| 1.8 | Idempotência de mensagem no fan-out + claim antes do provider | `Fanout_Replay_DoesNotResend; Redelivery_DoesNotResend` | M | 0.70 |
| 1.9 | EasyStok emite eventos crus ao Hiram via outbox dedicado durável (emission_seq bigserial, retry), tee/shadow para email | `EmailEvent_SendsLocal_AndEmitsToHiram; Emission_IsDurable_RetriesOnHiramFailure; LocalDelivery_Unaffected_OnHiramFailure` | M | 0.70 |
| 1.10 | Provisionar tenants, templates e consentimento migrados; tenant em Shadow | shadow real em staging com N emails | P | 0.85 |
| 1.11 | Coleta e instrumentação das 3 séries de paridade (contagem, decisão, conteúdo canonicalizado), pré-condição do soak | `ParitySeries_Collected_ForCount_Decision_Content` | M | 0.80 |

### Onda 2: Auditoria de paridade e cutover de email (live)

| # | Passo | Teste nomeado | Esforço | Prob. |
|---|---|---|---|---|
| 2.0 | Cutover de leitura de consentimento (cross-channel; UI e worker do EasyStok leem via API do Hiram; dual-write permanece ligado com soak antes de desligar escrita local). Rollback é flag (volta leitura ao local já sincronizado) | `ConsentReadAuthority_MovesToHiram_DualWriteStaysOn; EasyStokWorker_ReadsConsentViaApi; ConsentRollback_IsFlag_LocalStillSynced` | G | 0.65 |
| 2.1 | Dashboard de DECISÃO de paridade (consome as 3 séries coletadas em 1.11) + critérios de corte | `Parity_TwoSided_AlertsOnOverAndUnder` | M | 0.80 |
| 2.2 | Cutover de entrega por canary (fronteira por sequência emission_seq, drain do backlog menor ou igual a W), rollback por watermark | `BoundaryEvent_NoDuplicate_NoLoss_AcrossWatermark; Rollback_ReenablesLocal_NoLoss` | P (código) | 0.70 |
| 2.3 | ADR-019 + callbacks de provider (provider_message_id, endpoint, estados idempotentes por precedência) | `Callback_Idempotent_OnDuplicate; LateDelivered_DoesNotOverwriteBounce; InvalidSignature_Rejected` | G | 0.60 |

### Ondas seguintes (listadas)

SMS (ADR-021, Twilio/Zenvia em HTTP puro no molde do ResendEmailProvider), WhatsApp (ADR-022, Meta
Cloud/Twilio, verificação Meta Business e janela de 24h), InApp (callback ao EasyStok). Cada canal
repete: ADR, adapter, fila, consumer, shadow, paridade, canary, cutover. Revisar ADR-007 para custo
por mensagem. Dedupe de evento de cron recorrente: o `logical_alert_id` no payload e uma janela de
dedupe por rotina evitam renotificar o mesmo alerta lógico ao longo de dias (distinto do limite
diário, que é volume por usuário).

## Definition of Done uniforme (trava o commit de qualquer passo)

Um passo só está pronto quando todos abaixo são verdade:

- ADR aceito quando o passo é estrutural ou introduz biblioteca.
- Teste nomeado do passo verde, escrito antes do código (test-first).
- Migration aditiva (nunca altera migration aplicada), reversível, com re-run no-op verificado e
  `--dry-run` que não escreve.
- Atributos e métricas OTel com os labels do contrato congelado.
- Zero biblioteca nova sem ADR.
- Suíte do caminho crítico existente continua verde (gate de regressão).
- Commit por pathspec, mensagem conventional, nenhum arquivo fora do escopo do passo.
- Ledger de calibração atualizado (ver medição).

## Cerca additive-only (não tocar)

Marcados como intocáveis, com gate de regressão que falha o build se mudarem comportamento:

- `POST /v1/notifications` e seu contrato atual.
- A idempotência atual do caminho direto.
- A entrega local de email do EasyStok enquanto em shadow (só o tee é adicionado, o caminho local não
  muda).
- O relay compartilhado: com a coluna `dispatch_at` nullable, o caminho direto (NULL) continua
  publicando imediatamente. Asserção de regressão obrigatória `DirectNotification_
  StillPublishesImmediately_WithDeferralQuery`.

Qualquer mudança nesses pontos exige decisão explícita, não pode ser efeito colateral de refactor.

## Gates go/no-go por onda (artefato)

Probabilidade por passo não decide onda. Cada onda só começa com o gate da anterior marcado:

- Gate da Onda 0 para iniciar a Onda 1: stack em staging k3s, `/health/ready` verde nos dois hosts,
  KEDA escalando dentro do teto de réplica, orçamento de conexões validado sob carga. O ponta a ponta
  de `POST /v1/events` é da Onda 1 (passo 1.1), não da Onda 0.
- Gate da Onda 1 para iniciar a Onda 2: um evento ponta a ponta em shadow do `POST /v1/events` até o
  DeliveryAttempt shadow; as três séries de paridade (contagem, decisão, conteúdo canonicalizado) já
  COLETADAS e instrumentadas no fim da Onda 1 (a coleta é pré-condição do soak; o dashboard de decisão
  fica em 2.1); shadow contínuo por 7 dias com os três critérios atingidos, zero erro de ingestão,
  dual-write de consentimento reconciliando.
- Gate dentro da Onda 2: o cutover de leitura de consentimento (2.0) precede o cutover de entrega
  (2.2). Não flipar entrega de nenhuma empresa antes de a leitura de consentimento estar no Hiram e o
  worker do EasyStok lendo via API. Os dois rollbacks são flags e ambos testados antes do canary: o de
  entrega move o watermark; o de consentimento volta a leitura ao store local, que segue sincronizado
  porque o dual-write fica ligado durante o soak. Desligar a escrita local só depois do soak.
- Gate da Onda 2 para expandir o canary: taxa de entrega maior ou igual ao baseline local e bounce
  menor ou igual ao baseline na primeira empresa, ambos os rollbacks testados.

## Estratégia de migração medida

1. Fase 0: provisionamento e contrato congelado. Tenant em shadow, provider config igual ao do
   EasyStok, feature flag por canal desligada. Critério: smoke do `POST /v1/events` verde em staging.
2. Fase 1: shadow de email. EasyStok entrega pelo canal local e em paralelo emite o evento ao Hiram
   (que só registra shadow). Critérios de corte na seção de paridade abaixo.
3. Fase 2: cutover de consentimento (cross-channel, primeiro), depois cutover de entrega por canary
   com fronteira por sequência (emission_seq): o local drena o backlog menor ou igual a W, o Hiram
   entrega maior que W, a emissão ao Hiram nunca para. Rollback de entrega move o watermark e religa o
   local; rollback de consentimento reverte a release. Sem perda de estado.
4. Fase 3: fechar o loop de status (callbacks de provider, webhook ao EasyStok), depois desativar o
   email do EasyStok.

## Framework de medição e probabilidade

### SLIs e SLOs (fontes já existem como instrumentos OTel)

| SLI | Definição | SLO |
|---|---|---|
| Aceitação para envio | sent / accepted | maior ou igual a 99.9% em 5 min (email transacional) |
| Latência accepted->sent | p95 send.duration + lag de outbox | p95 menor ou igual a 60s, p99 menor ou igual a 5 min |
| Lag de outbox (overdue) | idade da pendente mais antiga com dispatch_at vencido | p95 menor ou igual a 30s |
| Backlog agendado | volume com dispatch_at no futuro | informativo, não viola SLO (esperado pelo adiamento) |
| Entrega real | delivered / sent | maior ou igual a 98% (só observável após 2.3) |
| Bounce | bounced / sent | menor ou igual a 2% alerta, 5% crítico |
| Dead-letter | dead_lettered / accepted | menor ou igual a 0.1% |
| Orçamento de conexões | conexões físicas / max_connections | menor ou igual a 80%, alerta antes do teto |

Enquanto o passo 2.3 não estiver pronto, "entrega real" é não observável e "sent" significa "aceito
pelo provider", declarado explicitamente.

### Paridade do shadow (gaps 6 e 7)

A paridade tem três dimensões, e a meta de conteúdo só fecha com canonicalização escrita:

- Contagem por tipo de evento, dois lados: alerta separado para sobra (Hiram maior que EasyStok, risco
  de spam e LGPD) e falta (Hiram menor, risco de não enviar). Critério: `|hiram - easystok| / easystok`
  menor ou igual a 0.5% por tipo, 3 dias consecutivos, com over-send tratado como mais grave.
- Decisão por evento: conjunto de canais escolhidos e conjunto de destinatários iguais entre os dois.
  Contagem igual com destinatário diferente é falha de decisão, não de volume.
- Conteúdo por hash canonicalizado: antes do hash, normalizar e remover os campos voláteis: token de
  unsubscribe, query params de tracking, pixel de tracking, datas e horas renderizadas, ids únicos por
  envio. A regra de canonicalização é parte do contrato congelado, idêntica nos dois lados, uma única
  função usada tanto pela migração de templates (1.3) quanto pela auditoria de paridade (2.1), para que
  "byte-identical" e "divergência menor ou igual a 0.1%" sejam o mesmo padrão de mesmo conteúdo, não
  dois. Critério: divergência menor ou igual a 0.1%, cada caso explicável. Sem essa regra a meta é
  fantasia.
- Envelope, não só corpo: From, Return-Path e domínio de envio. Um corpo byte-idêntico com From ou
  Return-Path diferente muda deliverability e a experiência do destinatário. O Hiram envia do mesmo
  domínio, From e Return-Path que o EasyStok usa hoje, e a paridade verifica o envelope além do corpo.

### Probabilidade calibrada

`P(passo) = M x C x R`, cada fator em [0,1]:

- M, maturidade do componente: PRONTO 0.95, PARCIAL 0.75, AUSENTE 0.55.
- C, cobertura de teste do caminho: alta (e2e + unit) 1.0, média (só unit) 0.85, baixa 0.7.
- R, precedente copiável (solução no EasyStok ou ADR fechado): sim 1.0, parcial 0.9, não 0.8.

Ledger de calibração em `docs/calibration.md`, um registro por passo: P estimado, fechou de primeira
(sim/não), nota. Atualizar é parte do DoD. Após cada passo, comparar estimado com real e ajustar pesos
para o próximo, transformando estimativa em previsão calibrada.

Cutover de email (2.2) depende do produto dos elos críticos: Data Protection cross-host (0.80),
migrations em produção (0.85), manifests k3s (0.65), orçamento de conexões (0.70), cutover de
consentimento (0.55, pré-requisito do de entrega), paridade em shadow (0.95) e idealmente callbacks
(0.60). Produto na casa de 0.25 na primeira tentativa sem retrabalho, subindo para cerca de 0.75 após
uma iteração em cada elo. O corte é provável, mas claramente não num único passe. Elos mais fracos: o
cutover de consentimento (legalmente sensível, atômico com release, rollback pesado), o deploy k3s sem
precedente, o orçamento de conexões, os callbacks e o Data Protection cross-host (silencioso até o
live).

### Provas cross-process obrigatórias

Para o passo 0.3 (Data Protection) e para o shadow, a prova roda em processos/pods separados, nunca
in-proc. Api cifra num processo, Dispatcher decifra noutro. Prova in-proc não prova nada para o bug de
Data Protection, que é silencioso até o live.

## Migração de dados (1.3 e 1.4)

Todo script de migração de dados (templates, consentimento) exige modo `--dry-run` que lê, conta e
emite diff sem escrever, mais rollback documentado, antes de qualquer escrita real. É o ponto de dano
irreversível; sem dry-run aprovado, não escreve.

## Verificação ponta a ponta

1. Local: `docker compose -f docker-compose.dev.yml up -d`, migrar, subir Api e Dispatcher, emitir um
   evento em `POST /v1/events`, confirmar trace único do POST ao sent e o email no Mailpit.
2. Testes: `dotnet test` com Testcontainers (Postgres + RabbitMQ + Redis + Mailpit), cobrindo evento
   -> rotina -> consentimento -> adiamento -> render -> entrega -> sent, mais shadow, replay,
   idempotência de mensagem e callbacks idempotentes. Cobertura do caminho crítico maior ou igual a 90%.
3. Staging k3s: subir pelos manifests, carga sintética, validar KEDA dentro do teto, orçamento de
   conexões sob carga, e LGTM recebendo dos dois hosts.
4. Shadow em staging: 7 dias espelhando tráfego real, validar as três dimensões de paridade.
5. Canary em produção: cutover por fronteira de sequência em uma empresa (local drena menor ou igual a
   W, Hiram entrega maior que W), comparar entrega e bounce com o baseline, testar os dois rollbacks
   (watermark e consentimento).

## Riscos

- Escopo: absorção total é programa de meses. Email-first como fatia vertical de ponta a ponta é a
  forma de de-riscar; só expandir canais após o email estável.
- Derrubar o ERP pela infra de notificação: tempestade de conexões e contenção de memória no
  single-node são as ameaças mais sérias. Orçamento de conexões com PgBouncer, teto de réplica e
  requests/limits em tudo são pré-condição, não otimização. Gatilho de Postgres dedicado ou nó
  separado se o orçamento não fechar.
- LGPD e split-brain de consentimento: consentimento é por usuário cross-channel e não move por canal.
  Dual-write em shadow, cutover de consentimento único, e EasyStok (UI e worker) lendo via API do
  Hiram para os canais que ainda serve evitam dois stores de escrita vivos e a divergência cross-channel.
- Perda de disco do broker single-node: mensagem confirmada e não consumida pode sumir, com o outbox
  já marcado processed. Registrado como risco residual aceito nesta fase, ver semântica de recuperação.
- Perda do volume do keyring de Data Protection: credenciais de provider e secrets de tenant viram
  indecifráveis, ou seja outage, justo o cenário que o passo 0.3 cria. Mitigado pelo backup do volume
  do keyring no DR (passo 0.7), não só pg_dump.
- Paridade inatingível: sem a regra de canonicalização e a paridade de decisão, o critério de corte
  nunca fecha ou fecha errado.
- Callbacks fora de ordem: a máquina de estados precisa ser idempotente e por precedência desde o
  desenho, senão um delivered atrasado apaga um bounce.
- Deliverability: SPF/DKIM/DMARC do domínio no provider do Hiram corretos antes do cutover.
- Coexistência k3s + compose na mesma VM: dois orquestradores elevam a carga operacional, coberto pelo
  ADR-016.
