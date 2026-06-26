<p align="center">
  <img src="docs/design/hiram-logo-gold.svg" width="190" alt="Hiram">
</p>

<h1 align="center">H I R A M &#8756;</h1>

<p align="center"><em>The word is never lost.</em></p>

<p align="center">
  Multi-tenant notification platform. Email, push, SMS and WhatsApp behind one API,<br>
  with outbox-guaranteed delivery, credit metering, configurable AI autonomy<br>
  and end-to-end OpenTelemetry.
</p>

---

## What this is

Hiram accepts a notification request, persists it together with an outbox row in a single PostgreSQL transaction, and takes responsibility from there: channel routing, per-tenant providers, retries, status webhooks signed with `X-Hiram-Signature`, and a credit ledger for usage. If the database confirmed it, it will be delivered.

**Stack:** .NET 10 · ASP.NET Core · EF Core · PostgreSQL · RabbitMQ · Redis · OpenTelemetry · Docker · k3s + KEDA

## Status

F1 complete. Email ships end to end on top of the F0 outbox skeleton, with production safety: real tenants and hashed API keys, honored `Idempotency-Key`, two interchangeable providers per tenant (SMTP via MailKit, Resend over HTTP), a Polly send pipeline that records one `DeliveryAttempt` per try, the full state machine (`accepted -> queued -> sending -> sent | failed | suppressed`), per-tenant shadow mode, and cursor-paginated audit queries. The first F2 slice is in: a delivery that exhausts its retries or fails permanently is dead lettered with the reason recorded, and replayable through `POST /v1/notifications/{id}/replay` over the same outbox, while a message that can never be processed is parked in a dedicated dead letter queue instead of being dropped. Templates render with Scriban at submit time, so a tenant manages named per-channel templates and sends only the data, with strict variables turning a missing field into a 400. Roadmap, phase plans and architecture decisions live in this repository:

| Document | Purpose |
|---|---|
| [MASTER-PLAN.md](MASTER-PLAN.md) | Charter, architecture, phases F0 to F6 (pt-BR) |
| [docs/adr/](docs/adr/) | Architecture decision records (pt-BR) |
| [docs/BRAND.md](docs/BRAND.md) | Brand and signature system (pt-BR) |
| [docs/design/](docs/design/) | Design system, tokens and logo assets |
| [CLAUDE.md](CLAUDE.md) | Engineering rules for AI-assisted development |

## Quick start

The dev infrastructure runs in Docker, the hosts run on .NET 10. Run everything from the repository root.

1. Start Postgres, RabbitMQ, Redis, Mailpit and the Aspire Dashboard:

   ```bash
   docker compose -f docker-compose.dev.yml up -d
   ```

2. Set the admin key that guards the provisional admin endpoints (kept in user-secrets, never committed):

   ```bash
   dotnet user-secrets set "Hiram:AdminKey" "admin-dev-local" --project src/Hiram.Api
   ```

3. Start the API (applies migrations on startup, listens on `http://localhost:3357`) and, in another shell, the Dispatcher (outbox relay plus email consumer). Start the Dispatcher after the API so the schema already exists:

   ```bash
   dotnet run --project src/Hiram.Api
   dotnet run --project src/Hiram.Dispatcher
   ```

4. Create a tenant and issue an API key. The clear key (`hk_live_...`) is shown only once:

   ```bash
   curl -s -X POST http://localhost:3357/v1/admin/tenants \
     -H "X-Admin-Key: admin-dev-local" -H "Content-Type: application/json" \
     -d '{"name":"easystok","deliveryMode":"live"}'

   curl -s -X POST http://localhost:3357/v1/admin/api-keys \
     -H "X-Admin-Key: admin-dev-local" -H "Content-Type: application/json" \
     -d '{"tenantId":"<tenant-id>","name":"easystok-server"}'
   ```

5. Send a notification and read it back. An `Idempotency-Key` makes the call safe to retry:

   ```bash
   curl -i -X POST http://localhost:3357/v1/notifications \
     -H "X-Api-Key: hk_live_..." -H "Idempotency-Key: evt-0001" -H "Content-Type: application/json" \
     -d '{"channel":"email","recipient":"ops@example.com","subject":"hello","body":"f1"}'

   curl -s http://localhost:3357/v1/notifications/<id> -H "X-Api-Key: hk_live_..."
   ```

   The POST returns `202` with the id and status `accepted`. The Dispatcher delivers through the dev SMTP provider and the GET reports `sent` with its delivery attempts. Repeating the POST with the same `Idempotency-Key` returns the original id and the header `Idempotency-Replayed: true`. A tenant created with `"deliveryMode":"shadow"` reaches `sent` but records a `shadow_would_send` attempt without delivering.

Where to look:

- API reference (Scalar): `http://localhost:3357/scalar`
- Delivered email (Mailpit): `http://localhost:8025`
- Distributed trace (Aspire Dashboard): `http://localhost:18888`, one trace id spanning the API, publish, consume and send spans
- RabbitMQ management: `http://localhost:15672` (user `hiram`, password `hiram`)

Run the tests with `dotnet test`. The integration suite uses Testcontainers and needs a running Docker engine.

## License

Proprietary. All rights reserved.
