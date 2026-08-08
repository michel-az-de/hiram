# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em Keep a Changelog (https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Added
- Provisionamento do tenant Jornada do Candidato em `deploy/jornada/`: script idempotente que cria o
  tenant live, emite a api key do emissor, configura o provider de cada canal, aprova os templates da
  jornada e liga cada eventType ao seu template. `JORNADA_CHANNELS` escolhe entre e-mail, SMS e
  WhatsApp; o e-mail de verificacao permanece so no canal de e-mail. Reexecucao reaproveita o que ja
  existe em vez de duplicar.
- Onboarding de credencial Twilio por tenant no runbook operacional: o que vai em `settings` e o que vai
  no segredo protegido, a dependencia do key ring, os limites da conta trial, o sandbox do WhatsApp,
  rotacao e o roteiro de smoke manual, que fica fora do gate de CI.
  tenant live, emite a api key do emissor, aprova os templates de e-mail da jornada e liga cada
  eventType ao seu template. Reexecucao reaproveita o que ja existe em vez de duplicar.
- Registro da substituicao de conteudo em modo trial na tentativa de entrega. Quando o adapter
  `twilio-sms` ou `twilio-email` envia o conteudo pre-aprovado no lugar do corpo da notificacao, a
  coluna nova `delivery_attempts.trial_content` marca a tentativa e o detalhe da notificacao passa a
  expor o campo. Sem isso o historico afirmava ter entregue um texto que nunca saiu. Fecha o item de
  acao 2.1 do ADR-028.
- Canal WhatsApp entregue pelo sandbox da Twilio: porta `IWhatsAppProvider`, adapter
  `twilio-whatsapp` sobre o mesmo recurso Messages do SMS e `PUT /v1/providers/whatsapp`. O prefixo
  `whatsapp:` do `To` e do `From` e montado somente no adapter, entao a notificacao guarda o numero em
  E.164 puro. Texto livre fora da janela de 24 h e recusado com 63016, que vira falha permanente
  carregando a razao do provider: a notificacao fica em dead-letter nomeada e pode ser reenviada por
  replay depois que o destinatario refizer o join.
- Superficies abertas ao canal `whatsapp`: `POST /v1/notifications` (com a mesma validacao E.164 do
  SMS), `POST /v1/templates`, o campo `channels` de `POST /v1/admin/routines` e `POST /v1/consent`.
  O consentimento e o mais importante deles: a politica e fail-closed para WhatsApp em toda
  categoria, entao sem registro de opt-in o canal nunca envia.
- Fan-out de eventos para WhatsApp, no mesmo formato do SMS e reaproveitando a persistencia comum.
- Fan-out de eventos para SMS. Uma rotina com `channels: ["email","sms"]` passa a gerar as duas
  notificacoes: o telefone vem do proprio evento e um numero ausente ou fora de E.164 e recusado antes
  do outbox, como ja acontece no envio direto.
- Contador `hiram.events.no_route`, com tag do tipo do evento. Evento sem rotina continua sendo ack,
  nao dead-letter, e sem metrica esse caminho era invisivel na operacao.
- Superficies de configuracao abertas ao canal `sms`: `POST /v1/templates`, o campo `channels` de
  `POST /v1/admin/routines` e `POST /v1/consent`. Sem elas nao havia o que um evento entregar por SMS.
- Canal SMS entregue pela Twilio: porta `ISmsProvider`, adapter `twilio-sms` sobre o recurso Messages
  e `PUT /v1/providers/sms` para o tenant configurar a propria conta de operadora. Sem provider
  configurado o envio falha como permanente, sem chamar a rede, porque o credito e do tenant.
- Adapter `twilio-email` como terceiro provider de email, escolhido por tenant ao lado de SMTP e
  Resend. Nenhum tenant existente muda de provider.
- ADR-028 registrando a Twilio como provider multicanal de ultima milha.
- ADR-027 e plano executavel para a migracao incremental ao Hiram Core.
- Primitiva de fila PostgreSQL com claim atomico, lease renovavel, retry agendado e recuperacao de
  leases vencidos.
- Runbook operacional e prova descartavel de backup e restore do PostgreSQL e do key ring no CI.

### Changed
- Torna `subject` opcional por canal. A coluna passa a aceitar nulo por migration e a obrigatoriedade
  vive no dominio, exigida em email e push, ausente em SMS e WhatsApp.
- Estende a mesma regra a `templates.subject`, por migration nova. Um template de email continua
  exigindo subject e um de SMS ou WhatsApp recusa qualquer subject, porque ele nunca seria renderizado.
- Compartilha a leitura da resposta do recurso Messages da Twilio entre os adapters de SMS e WhatsApp,
  para que a classificacao de erros como 21608 e 63016 nao divirja por canal.
- Centraliza a validacao E.164 em `PhoneNumber`, no dominio. O submit direto e o fan-out passam a usar
  a mesma regra em vez de duas copias da expressao.
- Extrai a mecanica comum de entrega (claim, bloqueio, modo shadow, tentativa, dead letter e webhook)
  do processor de email para um processor de canal reutilizavel. Cada canal contribui apenas com a
  montagem da mensagem e a chamada ao adapter.
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Ver ADR de adocao e CLAUDE.md.
- Redefine o Hiram como infraestrutura interna de notificacoes: um host e PostgreSQL no runtime
  alvo, providers externos na ultima milha e extensoes somente com consumidor ativo.
- Torna o dispatcher PostgreSQL o unico transporte do outbox.
- Consolida API e workers PostgreSQL no mesmo host e publica uma unica imagem `hiram`.
- Alinha configuracao, observabilidade, site e metadados ao posicionamento interno do Hiram Core.

### Removed
- Remove metering, creditos e o ledger do caminho de submissao e fan-out. A migration historica
  permanece intacta e a tabela existente fica dormente ate uma limpeza de schema posterior.
- Remove o modulo incompleto de templates WhatsApp. A tabela `whatsapp_templates` segue dormente e
  fora do model: o canal voltou pela tabela `templates` comum, sem reviver a entidade HSM.
- Remove MTA proprio, k3s, KEDA, PgBouncer e a stack conjunta com o Levante, artefatos operacionais
  aposentados pelo ADR-027.
- Remove Redis do runtime. Idempotencia e throttle de uso de API key passam a usar apenas PostgreSQL.
- Remove RabbitMQ, o relay intermediario, consumers AMQP e o Testcontainer do broker. Processadores
  reivindicam o outbox diretamente no PostgreSQL.
- Remove o projeto, processo e imagem Hiram.Dispatcher.
