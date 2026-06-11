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

F0 complete. The walking skeleton runs end to end: a `POST /v1/notifications` is persisted with its outbox row in one PostgreSQL transaction, relayed to RabbitMQ, consumed, and reflected as `published`, with a single OpenTelemetry trace spanning the API, the publish and the consume. Roadmap, phase plans and architecture decisions live in this repository:

| Document | Purpose |
|---|---|
| [MASTER-PLAN.md](MASTER-PLAN.md) | Charter, architecture, phases F0 to F6 (pt-BR) |
| [docs/adr/](docs/adr/) | Architecture decision records (pt-BR) |
| [docs/BRAND.md](docs/BRAND.md) | Brand and signature system (pt-BR) |
| [docs/design/](docs/design/) | Design system, tokens and logo assets |
| [CLAUDE.md](CLAUDE.md) | Engineering rules for AI-assisted development |

## Quick start

The dev infrastructure runs in Docker, the hosts run on .NET 10. Run everything from the repository root.

1. Start Postgres, RabbitMQ, Redis and the Aspire Dashboard:

   ```bash
   docker compose -f docker-compose.dev.yml up -d
   ```

2. Set the dev API key (kept in user-secrets, never committed):

   ```bash
   dotnet user-secrets set "Auth:DevApiKey" "dev-key-local" --project src/Hiram.Api
   ```

3. Start the API. It applies database migrations on startup and listens on `http://localhost:3357`:

   ```bash
   dotnet run --project src/Hiram.Api
   ```

4. In another shell start the Dispatcher (outbox relay plus email consumer). Start it after the API so the schema already exists:

   ```bash
   dotnet run --project src/Hiram.Dispatcher
   ```

5. Submit a notification and read it back:

   ```bash
   curl -i -X POST http://localhost:3357/v1/notifications \
     -H "X-Api-Key: dev-key-local" -H "Content-Type: application/json" \
     -d '{"channel":"email","recipient":"felipe@example.com","subject":"hello","body":"first slice"}'

   curl -s http://localhost:3357/v1/notifications/<id> -H "X-Api-Key: dev-key-local"
   ```

   The POST returns `202` with the id and status `accepted`. Within about a second the Dispatcher logs `Would send email for notification <id>` and the GET reports `published`.

Where to look:

- API reference (Scalar): `http://localhost:3357/scalar`
- Distributed trace (Aspire Dashboard): `http://localhost:18888`, open the request trace to see the API, publish and consume spans under one trace id
- RabbitMQ management: `http://localhost:15672` (user `hiram`, password `hiram`)

Run the tests with `dotnet test`. The integration suite uses Testcontainers and needs a running Docker engine.

## License

Proprietary. All rights reserved.
