# Deploy do Hiram no ambiente do EasyStok

Artefatos de implantação conforme ADR-016 (k3s + KEDA single-node na VM do EasyStok, Postgres
compartilhado com database `hiram` dedicado). Estes arquivos são templates: valores específicos do
ambiente (senhas, `max_connections`, hostnames, tamanhos de pool) são preenchidos contra a VM real, e
a validação final acontece no CI (build das imagens, lint de manifests) e no ambiente real (cluster).

## Estrutura

- `sql/` provisiona o database `hiram` e o role de menor privilégio (passo 0.7).
- `dr/` backup lógico do banco e do volume do key ring de Data Protection (passo 0.7).
- `pgbouncer/` pooler dedicado ao Hiram e o orçamento de conexões (passo 0.8).
- `k8s/` manifests do cluster: Api, Dispatcher, RabbitMQ, Redis, Job de migração, probes, limits (0.9).
- `k8s/keda/` ScaledObject do Dispatcher por profundidade de fila (passo 0.10).
- `observability/` stack LGTM e dashboards/SLOs (passo 0.11).

## Ordem de provisionamento

1. `sql/001-hiram-database-and-role.sql` no Postgres do EasyStok, como admin, sem `--single-transaction`.
2. Isolamento cruzado: aplicar o REVOKE CONNECT documentado no SQL, na base do EasyStok (ação do operador).
3. Secrets do Hiram a partir de `.env.hiram.example`, como Secret do k8s.
4. Job de migração `--migrate-only` antes de promover a Api (passo 0.4).
5. Subir Api, Dispatcher, RabbitMQ, Redis e KEDA pelos manifests.
6. Agendar `dr/hiram-backup.sh` em cron, com saída para fora da VM.

## Hardening do host (VM do EasyStok)

Ações do operador no host e no compose do EasyStok, fora dos manifests do Hiram:

- Proteger o Postgres do OOM killer: `oom_score_adj` negativo (e reserva de memória) no serviço Postgres
  do compose do EasyStok, para que, sob pressão, a vítima não seja o banco do ERP (ADR-016).
- Isolamento de banco: REVOKE CONNECT na base do EasyStok para PUBLIC (ver `sql/`).
- Orçamento de memória da VM: somar limits do compose do EasyStok mais os limits do k3s, com folga para
  o kernel, antes de elevar o teto de réplica do KEDA.

## Restore

- Banco: `pg_restore --clean --if-exists --dbname=hiram hiram-<stamp>.dump`.
- Key ring: extrair `hiram-keyring-<stamp>.tar.gz` no volume montado em `DataProtection__KeysPath` antes
  de subir Api e Dispatcher, senão segredos de provider e tenant ficam indecifráveis.
