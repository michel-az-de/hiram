# Contributing to Hiram

Thanks for your interest. Hiram is built as a production system and a portfolio piece, so the bar for changes is the same as for the rest of the codebase.

## Prerequisites

- .NET 10 SDK
- Docker, for the integration suite (Testcontainers spins up Postgres, RabbitMQ, Redis and Mailpit)

## Build and test

```bash
dotnet build Hiram.sln
dotnet test Hiram.sln
```

The unit tests run anywhere. The integration tests need a running Docker engine. The Release build treats warnings as errors and doubles as the warning gate.

## How we work

- Conventional commits, written as a human would, with no AI footer, no co-authored-by bot and no emoji. Example: `feat: persist webhook endpoints`.
- One step at a time. Small commits scoped to a single change, staged by pathspec. Do not use `git add .` or `git add -A`.
- Tests ship with the code that needs them, not afterwards. Behavior names, not `Test1`.
- A short branch for a risky step, straight to main when the change is additive and covered by a test. When in doubt, branch.
- CI green is a precondition for merge. A flaky test is a P1 bug.

## Architecture

Dependencies point inward: Domain references nothing, Application references Domain, Infrastructure references Application and Domain, and the hosts (Api, Dispatcher) are composition roots. Domain and Application do not know about EF Core, RabbitMQ, Redis or HTTP.

A new structural decision (a library, a pattern, a boundary change) needs an Architecture Decision Record in [docs/adr/](docs/adr/) before the code. The full engineering rules are in [CLAUDE.md](CLAUDE.md), and the charter and roadmap are in [MASTER-PLAN.md](MASTER-PLAN.md).
