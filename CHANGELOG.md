# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em Keep a Changelog (https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Added
- ADR-027 e plano executavel para a migracao incremental ao Hiram Core.
- Primitiva de fila PostgreSQL com claim atomico, lease renovavel, retry agendado e recuperacao de
  leases vencidos.

### Changed
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Ver ADR de adocao e CLAUDE.md.
- Redefine o Hiram como infraestrutura interna de notificacoes: um host e PostgreSQL no runtime
  alvo, providers externos na ultima milha e extensoes somente com consumidor ativo.
- Torna o dispatcher PostgreSQL o unico transporte do outbox.

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
