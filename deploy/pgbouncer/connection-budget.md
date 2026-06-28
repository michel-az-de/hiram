# Orçamento de conexões do Hiram (ADR-016, passo 0.8)

Regra dura: a infraestrutura de notificação não pode esgotar as conexões do Postgres compartilhado e
derrubar o ERP. O Hiram fala com o PgBouncer (transaction pooling); o PgBouncer multiplexa para o
Postgres com um pool pequeno e fixo. Logo a pegada física do Hiram no Postgres é o pool de servidor do
PgBouncer, não o número de réplicas do Dispatcher.

## Invariante

    pool_servidor_PgBouncer + alocacao_fixa_do_ERP + reserva_admin  <  max_connections

## Exemplo trabalhado (confirmar contra a VM real)

| Item | Valor exemplo | Origem |
|---|---|---|
| max_connections | 100 | padrão do Postgres; confirmar com `SHOW max_connections` |
| alocacao_fixa_do_ERP | 40 | somar os pools de Web, Worker e Admin do EasyStok |
| reserva admin e pg_dump | 10 | conexões administrativas e backup |
| pool de servidor do PgBouncer | 25 | default_pool_size 20 + reserve_pool_size 5 |
| Soma | 75 | menor que 100, com folga |

Como o Hiram passa pelo PgBouncer, o teto de réplica do Dispatcher (KEDA `maxReplicaCount`, passo 0.10)
é limitado pela capacidade de cliente do PgBouncer (`max_client_conn`), não pelas conexões físicas do
Postgres. Cada réplica abre poucas conexões de cliente ao PgBouncer e elas enfileiram por um slot de
servidor.

## Npgsql com transaction pooling

- Manter `Max Auto Prepare = 0` (padrão): transaction pooling não suporta prepared statements de sessão.
- `Maximum Pool Size` baixo e explícito na connection string de cada host, para o cliente não abrir
  mais conexões ao PgBouncer que o necessário.
- O caminho de dados do EasyStok não passa por aqui: os advisory locks de sessão do outbox do ERP são
  incompatíveis com transaction pooling.

## Como confirmar e operar na VM

1. `SHOW max_connections;` no Postgres.
2. Medir as conexões do EasyStok em pico por usuário (`pg_stat_activity`).
3. Ajustar `default_pool_size`/`reserve_pool_size` e o `maxReplicaCount` do KEDA até o invariante fechar
   com folga.
4. Alertar quando `conexões físicas / max_connections` passar de 80% (SLI do plano).
