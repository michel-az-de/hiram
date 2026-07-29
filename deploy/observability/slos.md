# SLIs, SLOs e alertas do Hiram Core

## Fonte dos sinais

`HiramDiagnostics` publica estes instrumentos OpenTelemetry:

- `hiram.notifications.accepted`
- `hiram.outbox.dispatched`
- `hiram.notifications.sent`
- `hiram.notifications.failed`
- `hiram.notifications.dead_lettered`
- `hiram.notifications.shadowed`
- `hiram.notifications.suppressed`
- `hiram.notifications.poisoned`
- `hiram.notifications.replayed`
- `hiram.webhooks.delivered`
- `hiram.webhooks.failed`
- `hiram.idempotency.replays`
- `hiram.smtp.destination_rejected`
- `hiram.send.duration`, histograma em milissegundos

O nome final no backend depende da tradução OTLP, por exemplo pontos podem virar underscores e
contadores podem receber `_total`. Confirme o nome exportado no coletor antes de salvar queries.
Use `service.name=hiram` e evite `tenant_id` em séries de alto volume.

## Objetivos mínimos

| SLI | Fonte | Objetivo |
|---|---|---|
| Aceite que termina em envio | `sent / accepted`, descontando shadow e suppressed quando aplicável | maior ou igual a 99,9% em 30 dias |
| Duração de envio | p95 e p99 de `hiram.send.duration` | p95 menor ou igual a 60 s; p99 menor ou igual a 5 min |
| Dead letter | `dead_lettered / accepted` | menor ou igual a 0,1% em 1 h |
| Backlog elegível | consulta PostgreSQL abaixo | item elegível mais antigo com idade menor ou igual a 30 s |
| Lease vencido | consulta PostgreSQL abaixo | zero sustentado por mais de dois ciclos do worker |
| Saúde | `/health/ready` | 99,9% em 30 dias |

`sent` significa aceite pelo provider, não leitura nem entrega final ao destinatário. O Hiram só pode
afirmar entrega final quando o provider emitir callback correlacionável.

## Consultas operacionais

Backlog total:

```sql
SELECT count(*)
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL;
```

Idade do item elegível mais antigo:

```sql
SELECT extract(epoch FROM (now() - min(available_at)))
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
  AND available_at <= now()
  AND (lease_until IS NULL OR lease_until <= now());
```

Leases vencidos:

```sql
SELECT count(*)
FROM notifications.outbox_messages
WHERE processed_at_utc IS NULL
  AND lease_until <= now();
```

Esses valores podem ser coletados por um exporter PostgreSQL ou pela ferramenta de observabilidade.
Não adicione um segundo serviço stateful apenas para medi-los.

## Alertas

- `HiramReadyDown`: `/health/ready` falhou por 2 minutos.
- `HiramOutboxLagHigh`: item elegível mais antigo acima de 30 segundos por 5 minutos.
- `HiramExpiredLeases`: leases vencidos persistem por mais de dois ciclos do worker.
- `HiramDeadLetterIncreasing`: `dead_lettered / accepted` acima de 0,1% por 1 hora.
- `HiramSendSuccessLow`: `sent / accepted` abaixo do SLO em janelas curta e longa.
- `HiramProviderLatencyHigh`: p95 de `hiram.send.duration` acima de 60 segundos por 10 minutos.
- `HiramPoisonedMessage`: qualquer incremento de `hiram.notifications.poisoned`.

Todo alerta deve apontar para `docs/operations-runbook.md`. Valide em cada deploy que métricas e
traces aparecem com `service.name=hiram`, sem dados pessoais ou segredos.
