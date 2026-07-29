# Hiram Core, infraestrutura interna de notificações

## 1. Por que este projeto existe

O Hiram nasceu depois de um blackout P0 de notificações no EasyStok. Seu trabalho é receber uma
notificação, persistir a responsabilidade de entrega e deixar evidência suficiente para operar
falhas sem depender de memória, logs soltos ou reenvio manual.

O Hiram não é um SaaS horizontal. É infraestrutura interna reutilizável para produtos próprios e
clientes selecionados. Produção continua acima de portfólio e reputação.

## 2. Produto em uma frase

Gateway multi-tenant para notificações transacionais confiáveis, com outbox PostgreSQL, providers
substituíveis, retry, auditoria, dead-letter e replay.

## 3. Fronteira

### Núcleo

- tenants e API keys;
- submissão e consulta de notificações;
- idempotência durável;
- outbox transacional;
- lease, retry, tentativas, dead-letter e replay;
- email por SMTP e provider HTTP;
- configuração de provider por tenant;
- consentimento e bloqueio;
- webhooks de status;
- health checks e OpenTelemetry opcional.

### Extensões de compatibilidade

Templates, eventos, rotinas e Web Push permanecem apenas quando houver projeto consumidor ativo.
Não recebem expansão especulativa.

### Fora do produto

- billing, créditos, quotas e metering;
- IA e autonomia configurável;
- Portal Blazor;
- SMS e WhatsApp sem demanda contratada;
- MTA próprio;
- k3s, KEDA e PgBouncer;
- deploy de aplicações clientes;
- stack de observabilidade acoplada.

## 4. Arquitetura alvo

Uma unidade de deploy:

| Componente | Responsabilidade |
|---|---|
| Hiram Core | API REST, autenticação, submissão, consulta, workers e adapters |

Uma peça obrigatória de estado:

| Componente | Responsabilidade |
|---|---|
| PostgreSQL | tenants, notificações, outbox, leases, tentativas e dead-letter |

Dependências externas:

| Componente | Responsabilidade |
|---|---|
| Provider de email | última milha SMTP ou HTTP |
| Endpoint do tenant | callback de status assinado |
| Coletor OTLP | telemetria opcional |

```text
Cliente -> Hiram Core -> PostgreSQL -> worker interno -> provider
```

O cliente chama a API com API key e `Idempotency-Key`. O Hiram grava `NotificationRequest` e
`OutboxMessage` na mesma transação. O worker reivindica a linha com lease, chama o provider, registra
a tentativa e conclui ou agenda retry. Falha esgotada vira dead-letter replayable.

## 5. Invariantes

1. Notificação e outbox são gravados na mesma transação.
2. PostgreSQL é a autoridade de idempotência e processamento.
3. Toda tabela de domínio possui `tenant_id`.
4. Toda chamada ao provider gera evidência.
5. Estado terminal não chama o provider novamente.
6. Resultado incerto pós-provider é explicitado, nunca escondido por reenvio cego.
7. Segredos não entram em código, log ou repositório.

## 6. Stack alvo

- .NET 10 LTS, ASP.NET Core e EF Core 10;
- PostgreSQL 17;
- MailKit e provider HTTP para email;
- Polly v8;
- OpenTelemetry;
- Scalar;
- Docker Compose;
- xUnit e Testcontainers PostgreSQL.

O PostgreSQL é a única autoridade de fila e entrega. Os processadores reivindicam o outbox
diretamente por leases recuperáveis.

## 7. Migração

| Etapa | Entrega | Gate |
|---|---|---|
| C0 | ADR, plano e documentação | fronteira aprovada |
| C1 | retirada de escopo de produto e deploys extras | email direto sem regressão |
| C2 | idempotência PostgreSQL-only | concorrência e replay verdes |
| C3 | outbox com lease e dispatch PostgreSQL | retry, crash e dead-letter verdes |
| C4 | consolidação do host | um binário e uma imagem |
| C5 | Compose, backup, restore e runbook | instalação e operação comprovadas |
| C6 | uso real | 30 dias sem perda silenciosa |

Uma etapa só fecha com build Release, suíte completa, CI e aceite da issue verdes.

## 8. Critérios para extensões

Uma extensão entra ou permanece somente quando todos forem verdade:

1. existe projeto consumidor identificado;
2. há owner operacional;
3. o contrato ou benefício interno paga sua manutenção;
4. existe teste ponta a ponta;
5. não adiciona serviço stateful sem benchmark que o justifique.

## 9. Operação

O padrão é uma instância central multi-tenant. Instância isolada por cliente é serviço contratado e
inclui deploy, upgrade, backup, observabilidade e suporte.

Métricas mínimas:

- notificações aceitas;
- envios concluídos;
- falhas e dead-letters;
- retries;
- leases vencidos;
- duração de ingestão e entrega;
- crescimento do outbox.

## 10. Métricas de sucesso

- EasyStok e projetos ativos usam o mesmo gateway sem integração duplicada;
- onboarding de um novo projeto exige horas, não dias;
- runtime padrão usa apenas Hiram e PostgreSQL;
- manutenção recorrente cabe em até um dia por mês;
- zero perda silenciosa;
- 30 dias de produção estável antes de qualquer expansão.

## 11. Fonte da verdade

- decisão arquitetural: `docs/adr/ADR-027-hiram-core.md`;
- execução: `plans/hiram-core.md`;
- operação: runbook a ser entregue na etapa C5;
- histórico: `CHANGELOG.md` e issues/PRs.
