# F0, walking skeleton

> Plano executável. Trabalhe sob as regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec ao final de cada passo. Se algo aqui conflitar com um ADR, o ADR vence e você para para avisar.

## Objetivo

A fatia mais fina possível atravessando todas as camadas. Resultado demonstrável ao final:

1. `docker compose -f docker-compose.dev.yml up -d` sobe Postgres, RabbitMQ, Redis e Aspire Dashboard.
2. `POST /v1/notifications` com API key de dev retorna 202 com id.
3. A request e a linha de outbox nascem na mesma transação no Postgres.
4. O Dispatcher faz o relay do outbox para o RabbitMQ e o consumer de email loga "would send email" com o id.
5. `GET /v1/notifications/{id}` reflete o status.
6. Um único trace no Aspire Dashboard liga API, publish e consume.
7. `dotnet test` verde, incluindo um teste de integração de ponta a ponta com Testcontainers.
8. CI no GitHub Actions verde.

Nada além disso. A lista de não-objetivos no final é tão importante quanto a de objetivos.

## Estrutura da solution

```
hiram/
  Hiram.sln
  docker-compose.dev.yml
  CLAUDE.md
  MASTER-PLAN.md
  docs/adr/
  plans/
  src/
    Hiram.Domain/          (entidades, enums, invariantes; referencia nada)
    Hiram.Application/     (use cases e ports; referencia Domain)
    Hiram.Infrastructure/  (EF Core, RabbitMQ, adapters; referencia Application e Domain)
    Hiram.Contracts/       (DTOs publicos da API; referencia nada)
    Hiram.Api/             (host REST; composition root)
    Hiram.Dispatcher/      (host worker; composition root)
  tests/
    Hiram.UnitTests/
    Hiram.IntegrationTests/
```

## Decisões técnicas fixas desta fase

- .NET 10, EF Core 10 com Npgsql, RabbitMQ.Client 7 (API assíncrona), xUnit, Testcontainers.
- Schema Postgres `notifications` desde a migration inicial. Tabelas: `notification_requests`, `outbox_messages`. Toda tabela com `tenant_id uuid not null`. Convenção snake_case.
- Tenant fixo de desenvolvimento nesta fase: `00000000-0000-0000-0000-000000000001`, semeado por migration. Multi-tenancy real é F1+, mas a coluna existe desde já (princípio do MASTER-PLAN).
- API key de dev via configuração (`Auth:DevApiKey`, user-secrets), validada por middleware no header `X-Api-Key`. Sem tabela de keys ainda.
- Estados da notificação nesta fase: `accepted` ao persistir, `published` quando o relay publica. Só isso.
- Relay do outbox: `BackgroundService` com loop de poll (1s), lendo lotes com `SELECT ... FOR UPDATE SKIP LOCKED LIMIT 50`, publicando no exchange e marcando `processed_at_utc` na mesma transação do lock. Semântica at-least-once, documentada em comentário de porquê no relay.
- Topologia RabbitMQ: exchange direto `hiram.notifications`, routing key `email`, fila `hiram.notifications.email`. Declarada idempotentemente pelos hosts na subida.
- Propagação de trace: injetar `traceparent` (W3C) nos headers AMQP no publish e extrair no consume, com `ActivitySource` próprio `Hiram.Messaging`. Sem isso o item 6 do objetivo falha.
- OTel nos dois hosts: AspNetCore + HttpClient instrumentation, EFCore instrumentation, exporter OTLP apontando para `http://localhost:4317` no dev.

## Passos

### Passo 0, bootstrap do repositório

`.gitignore` (dotnet), `.editorconfig` (4 espaços, file-scoped namespaces, var quando óbvio, warnings como erro em Release), `README.md` mínimo apontando para MASTER-PLAN.md, copiar CLAUDE.md, docs/ e plans/ deste pacote para a raiz.
Commit: `chore: bootstrap repository with conventions and docs`

### Passo 1, solution e projetos

Criar solution, seis projetos de src e dois de tests, com as referências exatamente como na estrutura acima. Adicionar Directory.Build.props com `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` em Release e versão do SDK fixada em `global.json`.
Verificação: `dotnet build` limpo.
Commit: `chore: create solution layout with dependency rules`

### Passo 2, infraestrutura local

`docker-compose.dev.yml` com postgres:17 (volume nomeado, healthcheck), rabbitmq:4-management (UI em 15672), redis:7-alpine, e Aspire Dashboard standalone (`mcr.microsoft.com/dotnet/aspire-dashboard`, UI em 18888, OTLP mapeado para 4317). Redis sobe agora mas só será usado na F1, deixar anotado no compose com comentário de porquê.
Verificação: compose up, healthchecks ok, UI do Rabbit e do Aspire acessíveis.
Commit: `chore: add local infrastructure compose with otlp dashboard`

### Passo 3, domínio

`NotificationRequest` (id, tenantId, channel, recipient, subject, body, status, createdAtUtc), enum `NotificationChannel` (apenas `Email` por ora), enum `NotificationStatus` (`Accepted`, `Published`), `OutboxMessage` (id, tenantId, type, payload JSON, createdAtUtc, processedAtUtc nullable). Invariantes no construtor, sem setters públicos anêmicos onde houver regra.
Testes unitários dos invariantes.
Commit: `feat: add notification request and outbox domain model`

### Passo 4, application

Use case `SubmitNotification` (port de entrada) recebendo um comando e devolvendo o id aceito. Port de saída para persistência transacional que garante request + outbox numa única transação (a Application define a intenção, a Infrastructure executa). Port de relógio se necessário. Nenhuma referência a EF aqui.
Teste unitário do handler com fake do port.
Commit: `feat: add submit notification use case`

### Passo 5, persistência

DbContext no schema `notifications`, configurações por entidade, migration inicial incluindo seed do tenant de dev. Implementação do port transacional gravando request e outbox em uma transação. Connection string via configuração.
Teste de integração com Testcontainers provando que as duas linhas nascem ou morrem juntas (forçar falha após a primeira escrita e verificar rollback).
Commit: `feat: persist request and outbox row in one transaction`

### Passo 6, API

Minimal API: `POST /v1/notifications` (202, Location e body com id e status) e `GET /v1/notifications/{id}`. Middleware de API key. Validação de entrada com ProblemDetails (400 para payload inválido, 401 sem key). OpenAPI habilitado com Scalar UI em `/scalar`.
Verificação manual com curl conforme seção de verificação.
Commit: `feat: expose ingestion endpoint with api key middleware`

### Passo 7, relay do outbox

No Dispatcher: `BackgroundService` de relay conforme decisões fixas (poll, SKIP LOCKED, publish, marca processado, commit). Conexão RabbitMQ resiliente na subida (espera com backoff simples enquanto a infra não responde, sem Polly ainda). Declaração idempotente da topologia.
Teste de integração: dado um outbox pendente, o relay publica e marca processado.
Commit: `feat: relay outbox rows to rabbitmq with skip locked batches`

### Passo 8, consumer de email

Consumer da fila `hiram.notifications.email` que loga estruturado "Would send email for notification {NotificationId}" e atualiza o status para `Published` se ainda não estiver. Ack manual após sucesso.
Commit: `feat: consume email queue and log would-send`

### Passo 9, telemetria

OTel nos dois hosts (traces, métricas, logs via OTLP), `ActivitySource` `Hiram.Messaging` com injeção e extração de `traceparent` nos headers AMQP. Duas métricas custom: `hiram.notifications.accepted` (counter) e `hiram.outbox.dispatched` (counter).
Verificação: um POST gera trace único API -> publish -> consume no Aspire Dashboard.
Commit: `feat: wire opentelemetry with trace propagation over amqp`

### Passo 10, teste de ponta a ponta

Teste de integração que sobe Postgres e RabbitMQ via Testcontainers, executa API e relay in-process (WebApplicationFactory + host do worker), faz o POST e aguarda o status `Published` com timeout. Este teste é o contrato vivo da F0.
Commit: `test: add end to end walking skeleton test`

### Passo 11, CI

GitHub Actions: build e test em ubuntu-latest (Testcontainers funciona nativo), cache de NuGet, gate de warnings.
Commit: `ci: add build and test workflow`

### Passo 12, fechamento

Atualizar README com quick start real (compose, secrets, curl de exemplo, onde ver o trace). Conferir o DoD abaixo item a item e listar qualquer desvio.
Commit: `docs: document f0 quick start and verification`

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet user-secrets set "Auth:DevApiKey" "dev-key-local" --project src/Hiram.Api

curl -i -X POST http://localhost:3357/v1/notifications \
  -H "X-Api-Key: dev-key-local" -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"felipe@example.com","subject":"hello","body":"first slice"}'

curl -s http://localhost:3357/v1/notifications/{id} -H "X-Api-Key: dev-key-local"
```

Esperado: 202 com id, log do Dispatcher com would-send, status `published` no GET, trace único em http://localhost:18888.

## Definição de pronto da F0

- [ ] Os 8 itens do objetivo demonstrados.
- [ ] Teste forçando falha entre as duas escritas prova o rollback conjunto.
- [ ] Nenhum warning de build, CI verde.
- [ ] Nenhuma biblioteca fora das decisões fixas (em especial: sem MediatR, sem MassTransit, sem AutoMapper).
- [ ] Histórico de commits legível, um passo por commit, sem rodapés de IA.

## Não-objetivos da F0 (não implemente, nem "de graça")

Retry com Polly, DLQ, idempotência por chave do cliente, múltiplos canais, envio real de email, templates, quotas e metering, multi-tenancy real, tabela de API keys, webhooks, Portal, KEDA, Kubernetes, rate limit, cache Redis. Tudo isso tem fase própria no MASTER-PLAN.
