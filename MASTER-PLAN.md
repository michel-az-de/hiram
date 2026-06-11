# Hiram, plataforma de notificações multi-tenant

> Documento Mestre v0.1, 2026-06-10.
> Nome definitivo: **Hiram**, malhete batido em 2026-06-10. Identidade e assinatura de marca em docs/BRAND.md.

## 1. Por que este projeto existe

Três objetivos de primeira classe, nesta ordem de prioridade quando entrarem em conflito:

1. **Produção**: resolver de forma definitiva as notificações do EasyStok. A origem do projeto é o incidente P0 de blackout de notificações, resolvido com o padrão outbox. O Hiram extrai essa solução para um produto standalone.
2. **Portfolio**: demonstrar arquitetura .NET sênior de ponta a ponta, com decisões registradas e defensáveis (ADRs), código limpo e operação real.
3. **Reputação**: cada fase gera pelo menos um artigo técnico derivado de problema real, não de tutorial. Pipeline na seção 9.

Regra de desempate: se uma escolha melhora o portfolio mas arrisca a produção, a produção vence.

## 2. O produto em uma frase

API multi-tenant de notificações (email, push, SMS, WhatsApp) com entrega confiável via outbox, cobrança por créditos, enriquecimento por IA com autonomia configurável por tenant e operação totalmente observável.

## 3. Princípios inegociáveis

- **Confiabilidade antes de feature.** O outbox é a fundação. Nenhuma notificação aceita pode se perder.
- **Modular monolith.** Fronteiras por contexto, extração futura possível, sem microservices theater.
- **Assíncrono entre módulos.** Comunicação interna via RabbitMQ. RPC síncrono interno é exceção justificada por ADR.
- **Multi-tenant em tudo, desde a primeira migration.** Toda tabela de domínio carrega `tenant_id`. Providers, quotas e autonomia são configuração por tenant.
- **ADR antes de código estrutural.** Decisão sem registro não existe.
- **Código 100% humanizado.** Regras operacionais no CLAUDE.md. A IA escreve, mas o código não pode ter cara de IA.
- **Custo mínimo de operação.** Um VPS, stack open source, IA que escala a zero quando não há trabalho.

## 4. Arquitetura macro

Cinco unidades de deploy sobre três peças de estado.

| Unidade | Responsabilidade | Escala |
|---|---|---|
| Hiram.Api | Ingestão REST, API keys, validação, débito de quota, gravação request + outbox em uma transação | HPA (CPU/req) |
| Hiram.Dispatcher | Relay do outbox para o RabbitMQ, consumers por canal, resolução do provider do tenant, registro de DeliveryAttempt | KEDA (profundidade de fila) |
| Hiram.Webhooks | Callbacks de status para tenants com assinatura HMAC e retries | KEDA |
| Hiram.Intelligence | Enriquecimento por IA conforme modo de autonomia, decision log | KEDA, escala a zero |
| Hiram.Portal | Admin Blazor Server: tenants, templates, uso, dashboards | 1 réplica |

| Estado | Papel |
|---|---|
| PostgreSQL | Dados de domínio, outbox, ledger de créditos, decision log (ADR-002) |
| Redis | Quotas fast-path, idempotência, rate limit, cache de template |
| RabbitMQ | Exchanges e filas por canal, DLQ, fonte de métrica para o KEDA |

Observabilidade transversal: OpenTelemetry em todos os hosts, exportando para Grafana LGTM self-hosted (ADR-003). Dev local usa Aspire Dashboard standalone.

Fluxo principal: cliente do tenant chama a API com API key. A API valida, consulta Redis (quota, idempotência) e grava `NotificationRequest` + `OutboxMessage` na mesma transação Postgres. O Dispatcher faz o relay do outbox para o RabbitMQ, o consumer do canal resolve o provider configurado do tenant, envia e registra `DeliveryAttempt`. Eventos de status alimentam o Webhooks (callback ao tenant) e a telemetria.

## 5. Stack

- .NET 10 LTS, ASP.NET Core, EF Core 10
- PostgreSQL 17 (schemas por contexto, JSONB para payloads variáveis)
- RabbitMQ 4 (client oficial, sem MassTransit, ver backlog ADR-005)
- Redis 7
- OpenTelemetry, Prometheus, Grafana, Loki, Tempo (prod), Aspire Dashboard (dev)
- Polly v8 (resiliência), Scalar (OpenAPI UI)
- Blazor Server no Portal (ADR-001)
- Docker Compose no dev, k3s + KEDA em produção (backlog ADR-010)
- Testes: xUnit, Testcontainers, k6 para carga (F6)
- IA: Claude API atrás de abstração própria (backlog ADR-008)

## 6. Domínio essencial, primeiro corte

Entidades centrais: `Tenant`, `ApiKey`, `NotificationRequest`, `DeliveryAttempt`, `OutboxMessage`, `Template`, `WebhookEndpoint`, `CreditLedgerEntry`, `AiDecision`.

Máquina de estados da notificação:

```
accepted -> enriching (opcional, conforme autonomia)
accepted | enriching -> queued
queued -> sending -> delivered
              |----> failed (apos esgotar retries) -> dead_lettered
accepted -> suppressed (quota, supressao, validacao de negocio)
```

Metering: custo em créditos por requisição, calculado como `base_do_canal + ceil(payload_kb) * custo_por_kb + tokens_ia * custo_por_token`. Ledger append-only no Postgres, enforcement rápido via contador Redis, reconciliação assíncrona. Quota estourada responde 429 com payload claro.

Autonomia da IA, por tenant e por feature: `off` (Aprendiz: execução literal), `assist` (Companheiro: IA sugere, humano aprova), `auto` (Mestre: IA decide dentro de guardrails, tudo registrado em `AiDecision` com input, decisão, justificativa, tokens e custo).

## 7. Fases

Escopo cruel. Uma fase só abre quando a anterior fecha o DoD. SMS e WhatsApp são adapters pós-F6 (verificação Meta Business e custo por conversa não são coisa de MVP, a arquitetura já nasce pronta para plugar).

| Fase | Objetivo | Entregas principais | DoD resumido | Artigo candidato |
|---|---|---|---|---|
| F0 | Walking skeleton | Solution, compose, API aceita e persiste com outbox, relay, consumer loga, OTel ponta a ponta, CI | Trace único do POST ao consumer no dashboard, testes verdes, ver plans/F0 | Walking skeleton com outbox desde o dia zero |
| F1 | Email em produção | SMTP + um provider HTTP (Resend ou SendGrid), idempotência, retries com Polly, status, API keys reais, Scalar | EasyStok em shadow mode comparando os dois sistemas, depois corte | Shadow mode: migrando notificações críticas sem fé |
| F2 | Push, templates, webhooks | Web Push VAPID, templates (Scriban), webhooks HMAC, DLQ com replay | PWA do Casa da Baba recebendo push real | DLQ não é lixeira, é fila de replay |
| F3 | Metering e quotas | Ledger, contadores Redis, reconciliação, rate limit, API de uso | Fatura simulada de um mês bate com o ledger | Cobrando por créditos: ledger append-only na prática |
| F4 | Intelligence | Abstração de IA, modos de autonomia, enrichment, decision log, budget de tokens | Tenant em modo auto com 100% das decisões auditáveis | IA com autonomia configurável e log de decisão |
| F5 | Portal e dev portal | Admin Blazor, gestão de tenants e templates, dashboards de uso, docs públicas | Operação do dia a dia sem tocar no banco | Blazor Server para admin: o caso a favor |
| F6 | Produção de verdade | k3s + KEDA em VPS, k6, runbooks, hardening, retenção de telemetria | Teste de carga publicado com números, runbook de incidente | Escalando workers a zero com KEDA num VPS barato |

## 8. Registro de decisões

Decididos (arquivos em docs/adr/):

- ADR-001: Blazor Server no Portal admin
- ADR-002: PostgreSQL como banco único, schemas por contexto e JSONB
- ADR-003: Observabilidade open source, OTel + Grafana LGTM self-hosted, Aspire Dashboard no dev
- ADR-004: API pública REST/JSON, gRPC adiado para adapter de batch

Backlog de ADRs (escrever quando a fase correspondente abrir, direção já apontada entre parênteses):

- ADR-005: RabbitMQ puro vs MassTransit (direção: client puro com outbox próprio, o padrão é ativo do portfolio, não detalhe a esconder)
- ADR-006: Estratégia multi-tenancy no Postgres (direção: shared schema com `tenant_id` + filtros globais EF, avaliar Row Level Security como defesa em profundidade)
- ADR-007: Modelo de metering e ledger (direção: append-only, Redis fast-path, reconciliação assíncrona)
- ADR-008: Provider de IA e abstração (direção: Claude API atrás de port próprio, budget por tenant)
- ADR-009: Versionamento da API pública (direção: versão na rota, `/v1`)
- ADR-010: Topologia k3s + KEDA num VPS único (F6)
- ADR-011: Estratégia de retry, DLQ e replay por canal (F2)
- ADR-012: Adapter gRPC de ingestão em batch (pós-F6, só com benchmark medido)

## 9. Pipeline de artigos

Um artigo por fase, sempre derivado de problema real com código de produção. Ordem sugerida de publicação: F0, F1 (shadow mode é o mais forte para reputação), JSONB vs Mongo (sai do ADR-002), metering, autonomia de IA, KEDA. Publicar em PT-BR no canal principal, versão EN opcional no dev.to.

## 10. Riscos e mitigações

- **Escopo crescer.** Mitigação: fases com DoD fechado, WIP=1, este documento é a fonte da verdade.
- **Agentes paralelos quebrarem o master.** Lição do EasyStok pós-recuperação de 14 dias. Mitigação: branch curta por passo, commits por pathspec, nunca `git add .`, CI obrigatório antes de merge.
- **Custo de IA.** Mitigação: budget de tokens por tenant, cache de respostas, Intelligence escala a zero.
- **Lock-in de provider de envio.** Mitigação: abstração de provider com duas implementações desde a F1.
- **VPS pequeno sufocar a telemetria.** Mitigação: retenção curta, sampling de traces, alertas de disco.

## 11. Fluxo de trabalho

Este chat atua como arquiteto adversarial: revisa planos, escreve ADRs, caça cheiro de IA no código. O Claude Code executa fase a fase a partir dos planos em plans/, sob as regras do CLAUDE.md. Cada passo do plano vira commit pequeno e verificável. Nenhuma decisão estrutural sem ADR aberto antes.

Métricas de sucesso do projeto: EasyStok 100% migrado e estável por 30 dias, p99 de ingestão publicado, seis artigos no ar, zero notificação perdida desde a F1.
