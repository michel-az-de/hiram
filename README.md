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

Pre-F0. The walking skeleton is being built. Roadmap, phase plans and architecture decisions live in this repository:

| Document | Purpose |
|---|---|
| [MASTER-PLAN.md](MASTER-PLAN.md) | Charter, architecture, phases F0 to F6 (pt-BR) |
| [docs/adr/](docs/adr/) | Architecture decision records (pt-BR) |
| [docs/BRAND.md](docs/BRAND.md) | Brand and signature system (pt-BR) |
| [docs/design/](docs/design/) | Design system, tokens and logo assets |
| [CLAUDE.md](CLAUDE.md) | Engineering rules for AI-assisted development |

## Quick start

Arrives with phase F0. Until then: `plans/F0-walking-skeleton.md`.

## License

Proprietary. All rights reserved.
