# SLIs, SLOs e alertas do Hiram (ADR-003, passo 0.11)

Fontes já instrumentadas em `HiramDiagnostics` (OpenTelemetry). Os nomes em PromQL abaixo seguem a
convenção do exportador OTLP para Prometheus (contadores viram `_total`, pontos viram underscores,
histogramas ganham sufixo de unidade); confirmar os nomes exatos no Grafana após o primeiro scrape. O
label `tenant` não entra nas séries de alto volume (cardinalidade na VM pequena, ADR-016); usar
subconjunto curado ou exemplars.

## SLIs e SLOs

| SLI | PromQL (aproximado) | SLO |
|---|---|---|
| Aceitação para envio | `sum(rate(hiram_notifications_sent_total[5m])) / sum(rate(hiram_notifications_accepted_total[5m]))` | maior ou igual a 99.9% (email transacional) |
| Latência de envio (p95) | `histogram_quantile(0.95, sum(rate(hiram_send_duration_milliseconds_bucket[5m])) by (le))` | p95 menor ou igual a 60s, p99 menor ou igual a 5 min |
| Lag de outbox (overdue) | idade da pendente mais antiga com `dispatch_at` vencido (gauge derivado) | p95 menor ou igual a 30s |
| Backlog agendado | volume com `dispatch_at` no futuro | informativo, não viola SLO |
| Entrega real | `sum(rate(hiram_notifications_delivered_total[1h])) / sum(rate(hiram_notifications_sent_total[1h]))` | maior ou igual a 98% (só após callbacks, passo 2.3) |
| Bounce | `sum(rate(hiram_notifications_bounced_total[1h])) / sum(rate(hiram_notifications_sent_total[1h]))` | alerta 2%, crítico 5% |
| Dead-letter | `sum(rate(hiram_notifications_dead_lettered_total[1h])) / sum(rate(hiram_notifications_accepted_total[1h]))` | menor ou igual a 0.1% |
| Orçamento de conexões | `pg_stat_activity` físicas / `max_connections` | menor ou igual a 80% |

Enquanto o passo 2.3 (callbacks de provider) não estiver pronto, "entrega real" é não observável e
"sent" significa "aceito pelo provider", não "entregue".

## Alertas (queima de error budget, não threshold instantâneo)

- AceitacaoAbaixoDoSLO: taxa de sent/accepted abaixo de 99.9% sustentada por janela curta e janela
  longa (multi-window burn rate), error budget de ~43 min/mês.
- BounceAlto: bounce maior que 2% (warning) e maior que 5% (crítico, risco de reputação de domínio).
- OutboxLagAlto: lag overdue p95 maior que 30s sustentado (worker parado ou leases acumulando).
- DeadLetterSubindo: dead_lettered/accepted maior que 0.1%.
- OrcamentoConexoes: conexões físicas / max_connections maior que 80% (antes de derrubar o ERP).

## Paridade em shadow (gate de corte, passo 1.11 e 2.1)

Três séries lado a lado por tipo de evento: `easystok` (enviadas pelo local), `hiram` shadowed, e a
taxa de divergência. Critério de corte: contagem dois lados com `|hiram - easystok| / easystok` menor
ou igual a 0.5% por tipo, paridade de decisão (canais e destinatários), e divergência de conteúdo
canonicalizado menor ou igual a 0.1%, por 72h. Over-send (Hiram maior que EasyStok) alerta separado e
mais grave que under-send.

## Validação

A presença de métricas e traces dos dois hosts no Grafana (`Otel_FromBothHosts_AppearsInGrafana`) é
verificável no cluster real após o deploy; não há equivalente local sem o stack rodando.
