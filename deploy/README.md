# Operação do Hiram Core

Este diretório contém somente os artefatos operacionais ainda mantidos pelo Hiram Core. O ADR-027
aposentou o MTA próprio, k3s, KEDA, PgBouncer e a stack conjunta com o Levante.

## Estrutura

- `demo/`: ambiente público provisório em Docker Compose.
- `dr/`: backup lógico do banco e do key ring de Data Protection.
- `observability/`: objetivos de nível de serviço e orientação de observabilidade.
- `sql/`: provisionamento do database `hiram` e da role de menor privilégio.

O ambiente local é definido por `docker-compose.dev.yml` na raiz. Ele sobe um único host Hiram,
PostgreSQL, Mailpit e o dashboard Aspire. Produção publica uma única imagem `hiram`.

## Restore

- Banco: `pg_restore --clean --if-exists --dbname=hiram hiram-<stamp>.dump`.
- Key ring: extrair `hiram-keyring-<stamp>.tar.gz` no volume configurado em
  `DataProtection__KeysPath` antes de subir o Hiram.

O procedimento completo, consultas de diagnóstico e resposta a incidentes estão em
`docs/operations-runbook.md`. O script `dr/verify-backup.sh` prova banco e key ring em PostgreSQL
descartável e faz parte do CI.
