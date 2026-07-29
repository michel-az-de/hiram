<p align="center">
  <img src="docs/design/hiram-logo-gold.svg" width="190" alt="Hiram">
</p>

<h1 align="center">H I R A M &#8756;</h1>

<p align="center"><em>The word is never lost.</em></p>

<p align="center">
  Internal multi-tenant notification gateway for .NET products and selected clients.<br>
  Durable submission, provider-independent email delivery, retries, audit and replay.
</p>

<p align="center">
  <a href="https://github.com/michel-az-de/hiram/actions/workflows/ci.yml"><img src="https://github.com/michel-az-de/hiram/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg" alt=".NET 10">
</p>

---

## What this is

Hiram accepts a notification, persists it with an outbox row in the same PostgreSQL transaction,
and takes responsibility from there. It authenticates tenants, honors idempotency keys, resolves an
email provider, retries transient failures, records every attempt and exposes dead-letter replay.

Hiram is internal infrastructure, not a general-purpose notification SaaS. Email is the required
channel. Templates, raw events, routines and Web Push are compatibility extensions that remain only
while an active project uses them.

## Current status

The Hiram Core runtime is implemented and covered by CI. It runs as:

- one Hiram host for HTTP and background delivery;
- PostgreSQL.

[ADR-027](docs/adr/ADR-027-hiram-core.md) defines this smaller target, with providers external to
the runtime. The remaining operational work is tracked in [plans/hiram-core.md](plans/hiram-core.md).

## Current architecture

```mermaid
flowchart LR
  client[Tenant app] -->|API key| hiram[Hiram]
  hiram -->|request plus outbox in one transaction| pg[(PostgreSQL)]
  pg -->|lease claim| hiram
  hiram --> provider[SMTP or Resend]
  hiram --> attempts[(Delivery attempts)]
```

PostgreSQL is the durable authority for notifications, idempotency and the leased outbox queue.
Channel processors claim outbox rows directly with bounded leases and recover expired work.
Workers are enabled by default. `Hiram:Workers:Enabled=false` is reserved for migrations and
diagnostics; it is not a second deployment topology.

## Core guarantees

- tenant isolation;
- hashed, revocable API keys;
- `Idempotency-Key` scoped by tenant;
- notification and outbox persisted atomically;
- provider-independent email delivery;
- structured retries and delivery attempts;
- dead-letter and replay;
- channel blocks and consent enforcement in the event-routing path;
- signed status webhooks;
- OpenTelemetry instrumentation.

No system can guarantee exactly one provider call across every crash boundary. Hiram treats the
post-provider uncertainty explicitly through durable claims, provider callbacks and fail-safe
recovery rather than silently resending.

## Quick start

Start Hiram, PostgreSQL and the local development tools with Docker:

```bash
docker compose -f docker-compose.dev.yml up -d --build
```

The reference Compose uses `admin-dev-local` as its development-only admin key. Set
`HIRAM_ADMIN_KEY` before starting it to override that value.

Create a tenant and API key:

```bash
curl -s -X POST http://localhost:3357/v1/admin/tenants \
  -H "X-Admin-Key: admin-dev-local" \
  -H "Content-Type: application/json" \
  -d '{"name":"example","deliveryMode":"live"}'

curl -s -X POST http://localhost:3357/v1/admin/api-keys \
  -H "X-Admin-Key: admin-dev-local" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"<tenant-id>","name":"example-server"}'
```

Send and query a notification:

```bash
curl -i -X POST http://localhost:3357/v1/notifications \
  -H "X-Api-Key: hk_live_..." \
  -H "Idempotency-Key: evt-0001" \
  -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"ops@example.com","subject":"hello","body":"from hiram"}'

curl -s http://localhost:3357/v1/notifications/<notification-id> \
  -H "X-Api-Key: hk_live_..."
```

Development tools:

- Scalar: `http://localhost:3357/scalar`
- Mailpit: `http://localhost:8025`
- Aspire Dashboard: `http://localhost:18888`

## Verification

```bash
dotnet build Hiram.sln --configuration Release
dotnet test Hiram.sln --configuration Release
```

Integration tests use Testcontainers and require Docker. CI runs the complete suite.

## Documentation

| Document | Purpose |
|---|---|
| [MASTER-PLAN.md](MASTER-PLAN.md) | Hiram Core charter and success criteria |
| [plans/hiram-core.md](plans/hiram-core.md) | Executable migration |
| [docs/adr/ADR-027-hiram-core.md](docs/adr/ADR-027-hiram-core.md) | Architecture decision |
| [docs/adr/](docs/adr/) | Decision history |

## License

MIT. See [LICENSE](LICENSE).
