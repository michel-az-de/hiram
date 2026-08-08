# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em Keep a Changelog (https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Added
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
  vive no dominio, exigida em email e push, ausente em SMS.
- Estende a mesma regra a `templates.subject`, por migration nova. Um template de email continua
  exigindo subject e um de SMS recusa qualquer subject, porque ele nunca seria renderizado.
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
- Remove o modulo incompleto de templates WhatsApp. O valor do canal permanece apenas como tombstone
  de compatibilidade e o submit continua rejeitando-o antes de persistir.
- Remove MTA proprio, k3s, KEDA, PgBouncer e a stack conjunta com o Levante, artefatos operacionais
  aposentados pelo ADR-027.
- Remove Redis do runtime. Idempotencia e throttle de uso de API key passam a usar apenas PostgreSQL.
- Remove RabbitMQ, o relay intermediario, consumers AMQP e o Testcontainer do broker. Processadores
  reivindicam o outbox diretamente no PostgreSQL.
- Remove o projeto, processo e imagem Hiram.Dispatcher.
