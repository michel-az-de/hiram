# F1, email em produção

> Plano executável. Trabalhe sob as regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec ao final de cada passo. Se algo aqui conflitar com um ADR, o ADR vence e você para para avisar. O branch padrão do repositório é main.

## Objetivo

O Hiram envia email de verdade, com segurança de produção, e o EasyStok passa a operar em shadow mode. Resultado demonstrável ao final:

1. Tenants e API keys reais: chave criada via endpoint administrativo, exibida uma única vez, armazenada como hash, revogável. O middleware resolve o tenant pela chave.
2. `Idempotency-Key` honrada: requisição repetida devolve o mesmo id, sem segunda notificação.
3. Email real entregue via dois providers intercambiáveis por configuração de tenant: SMTP (MailKit) e Resend (HTTP). No dev, o SMTP entrega no Mailpit.
4. Pipeline de envio com retries: erro transitório tenta com backoff e jitter, erro permanente falha de imediato, cada tentativa registrada em `DeliveryAttempt`.
5. Máquina de estados completa da fase: `accepted -> queued -> sending -> sent | failed | suppressed`.
6. Shadow mode: tenant com `delivery_mode = shadow` processa tudo até a borda do envio e registra `shadow_would_send` sem chamar provider.
7. Consulta de auditoria: listagem paginada de notificações com filtros e detalhe com tentativas.
8. Scalar com a seção Handshake, exemplos reais e catálogo de erros ProblemDetails.
9. Testes verdes, incluindo ponta a ponta com Mailpit, e CI verde.

## Decisões técnicas fixas desta fase

- Bibliotecas novas permitidas, e somente estas: MailKit (SMTP) e Polly v8 (`ResiliencePipeline`). Polly já consta da stack oficial do MASTER-PLAN; MailKit fica registrado aqui como decisão da fase (SmtpClient da BCL é inadequado para produção). Mocks HTTP do Resend via `HttpMessageHandler` fake próprio, sem biblioteca.
- Cliente Redis: `StackExchange.Redis`. O Redis já é stack decidida no MASTER-PLAN (seções 4 e 5: fast-path de quota, idempotência, rate limit, cache). O cliente .NET é a execução dessa decisão, análogo ao Npgsql sob o ADR-002, então entra sem ADR dedicado. `Testcontainers.Redis` para os testes de integração, mesma família dos módulos já em uso.
- Provider HTTP escolhido: Resend. Justificativa registrada: DX e API limpas, verificação de domínio simples, free tier suficiente para a fase. A abstração de provider torna a troca barata; SES entra em avaliação se custo apertar em escala.
- Modelo de provider: port `IEmailProvider` na Application com resultado explícito `SendOutcome` (`Sent`, `TransientFailure(reason)`, `PermanentFailure(reason)`). A classificação de erro mora no adapter de cada provider, não no pipeline.
- Resolução por tenant: `EmailProviderResolver` (factory) lê a configuração do tenant e devolve o provider. Configuração em `tenant_provider_configs` (tenant_id, channel, provider, settings JSONB, secret protegido). Segredos de tenant cifrados com ASP.NET Data Protection antes de persistir. Fallback: provider padrão da plataforma via configuração de ambiente.
- API keys: formato `hk_live_` + 32 bytes aleatórios em base62. Armazenar somente SHA-256 da chave e o prefixo de exibição (8 primeiros caracteres). Entropia alta dispensa hash lento. `last_used_at` atualizado com throttle de 5 minutos via Redis para não escrever a cada request.
- Endpoints administrativos provisórios até o Portal (F5): `POST /v1/admin/tenants`, `POST /v1/admin/api-keys`, `DELETE /v1/admin/api-keys/{id}` protegidos por `Hiram:AdminKey` de ambiente, em header `X-Admin-Key`. Documentar como provisórios no Scalar.
- Idempotência: header `Idempotency-Key`, escopo por tenant, janela de 24h. Fast-path em Redis (`SET NX` com TTL) e índice único parcial no Postgres em (tenant_id, idempotency_key) como garantia durável. Replay devolve 202 com o id original e header `Idempotency-Replayed: true`.
- Retries: pipeline Polly no consumer, 3 tentativas para transitório, backoff exponencial com jitter (base 2s), timeout por tentativa de 10s. Após esgotar, status `failed` e ack na fila (sem requeue: DLQ e replay são F2, e requeue sem DLQ é loop infinito). Cada tentativa gera uma linha em `delivery_attempts` com outcome, erro e duração.
- Estados: migration nova converte o `published` legado da F0 em `sent` e introduz `queued`, `sending`, `failed`, `suppressed`. Não alterar migrations aplicadas.
- Compose dev ganha Mailpit (SMTP em 1025, UI em 8025). Os testes de integração de envio usam Testcontainers com Mailpit.
- Métricas novas: `hiram.notifications.sent`, `hiram.notifications.failed`, `hiram.send.duration` (histogram, por provider), `hiram.idempotency.replays`. Todo log do pipeline carrega `tenant_id` e `notification_id` em scope.

## Passos

### Passo 0, herança da F0

Garantir `git push` do commit de alinhamento pendente. Auditar o commit `15ed4f0`: confirmar que as credenciais ali são exclusivamente de container local de desenvolvimento (guest/guest ou equivalente em appsettings de dev); se houver qualquer segredo real, mover para user-secrets imediatamente e reportar antes de prosseguir. Commitar docs/adr/ADR-005-rabbitmq-puro.md e este plano.
Commit: `docs: add adr-005 and f1 phase plan`

### Passo 1, migration de tenancy e estados

Tabelas `tenants` (id, name, delivery_mode live|shadow, created_at_utc), `api_keys` (id, tenant_id, name, key_hash, key_prefix, created_at_utc, revoked_at_utc, last_used_at_utc), `tenant_provider_configs` (tenant_id, channel, provider, settings jsonb, secret_protected, updated_at_utc). Novos valores de status com conversão `published -> sent`. Índice único parcial de idempotência em `notification_requests`. Tenant de dev da F0 atualizado para `delivery_mode = live`.
Commit: `feat: add tenancy, api keys and provider config schema`

### Passo 2, autenticação real

Middleware resolve tenant via SHA-256 da chave apresentada, rejeita revogadas (401 ProblemDetails), popula o contexto da requisição com o tenant, atualiza `last_used_at` com throttle Redis. Endpoints administrativos de tenants e keys com `X-Admin-Key`. A chave é retornada em claro somente na resposta de criação.
Testes: unit do hash e do throttle, integração criando tenant, chave e autenticando.
Commit: `feat: authenticate tenants with hashed api keys`

### Passo 3, idempotência

Comportamento conforme decisões fixas. Corrida tratada: se o Redis falhar ou expirar, o índice único do Postgres decide e a violação vira replay, não erro.
Testes: repetição devolve mesmo id; corrida simulada com Redis limpo; chaves iguais em tenants diferentes não colidem.
Commit: `feat: honor idempotency keys with redis fast path`

### Passo 4, port de provider e resolver

`IEmailProvider`, `SendOutcome`, `EmailProviderResolver` com leitura da config do tenant, proteção de segredo via Data Protection e fallback de plataforma. Nenhum provider concreto ainda.
Testes: resolver escolhe por tenant, decifra segredo, cai no fallback.
Commit: `feat: add email provider port and per-tenant resolver`

### Passo 5, provider SMTP

Adapter MailKit com classificação de erros (timeout e 4xx de conexão transitórios; autenticação e recipient recusado permanentes). Mailpit no compose dev. Configuração de host, porta, credenciais e from por tenant ou plataforma.
Teste de integração com Testcontainers entregando no Mailpit e lendo pela API dele.
Commit: `feat: add smtp provider via mailkit`

### Passo 6, provider Resend

HttpClient tipado, autenticação Bearer, mapeamento de respostas para `SendOutcome` (429 e 5xx transitórios, 4xx de validação permanentes), from e domínio configuráveis.
Testes com handler fake: sucesso, 429, 422, timeout.
Commit: `feat: add resend http provider`

### Passo 7, pipeline de envio

No consumer de email: carregar a notificação, transicionar para `sending`, resolver provider, executar com o pipeline Polly da fase, registrar `DeliveryAttempt` por tentativa, transicionar para `sent` ou `failed`, ack sempre ao final. Comentário de porquê documentando a ausência deliberada de requeue até a F2.
Testes: transitório recupera na segunda tentativa; permanente falha sem retry; attempts registrados com duração.
Commit: `feat: send email through resilient provider pipeline`

### Passo 8, shadow mode

Quando o tenant está em `shadow`, o pipeline para na borda. Decisão fixa: notificação em shadow termina em `sent` com `shadowed = true` na linha do attempt (provider resolvido, destinatário, hash do payload), e a listagem expõe o campo. Estado dedicado foi descartado por confundir a paridade. Métrica `hiram.notifications.shadowed`.
Testes: tenant shadow não toca provider (verificado por fake), attempt correto.
Commit: `feat: add shadow delivery mode per tenant`

### Passo 9, consulta e auditoria

`GET /v1/notifications` com filtros (status, channel, since, until), paginação por cursor (created_at + id), limite 100. `GET /v1/notifications/{id}` inclui as tentativas. Sempre escopado ao tenant autenticado.
Testes: paginação estável, isolamento entre tenants.
Commit: `feat: expose notification query endpoints`

### Passo 10, documentação viva

Scalar: seção de autenticação nomeada Handshake, exemplos de criação de chave e envio, catálogo dos ProblemDetails da API (401, 400, 409 de idempotência conflitante, 422), nota dos endpoints admin provisórios. Tema escuro alinhado à paleta (azul #0A1428, acento #C9A227) se o Scalar permitir por configuração simples; não investir além disso.
Commit: `docs: document handshake and error catalog in scalar`

### Passo 11, telemetria da fase

Métricas e scopes das decisões fixas. Dashboard local: confirmar no Aspire Dashboard que um envio real mostra trace API -> fila -> consumer -> provider HTTP/SMTP com spans nomeados.
Commit: `feat: add delivery metrics and tenant log scopes`

### Passo 12, ponta a ponta da fase

Testes e2e: fluxo live completo até o Mailpit; fluxo shadow; replay idempotente; falha permanente com attempts. Suíte inteira verde.
Commit: `test: add f1 end to end delivery scenarios`

### Passo 13, fechamento

README: quick start atualizado (admin key, criação de tenant e chave, envio, Mailpit, Scalar). Relatório DoD item a item com evidências.
Commit: `docs: document f1 quick start and verification`

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet user-secrets set "Hiram:AdminKey" "admin-dev-local" --project src/Hiram.Api

curl -s -X POST http://localhost:3357/v1/admin/tenants \
  -H "X-Admin-Key: admin-dev-local" -H "Content-Type: application/json" \
  -d '{"name":"easystok","deliveryMode":"shadow"}'

curl -s -X POST http://localhost:3357/v1/admin/api-keys \
  -H "X-Admin-Key: admin-dev-local" -H "Content-Type: application/json" \
  -d '{"tenantId":"<id>","name":"easystok-server"}'

curl -i -X POST http://localhost:3357/v1/notifications \
  -H "X-Api-Key: hk_live_..." -H "Idempotency-Key: evt-0001" \
  -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"ops@example.com","subject":"hello","body":"f1"}'
```

Esperado: shadow registra `shadow_would_send` sem entrega; trocando o tenant para live com SMTP de dev, o email aparece em http://localhost:8025; repetição do curl devolve o mesmo id com `Idempotency-Replayed: true`.

## Definição de pronto da F1

- [ ] Os 9 itens do objetivo demonstrados.
- [ ] Email real entregue em ambiente de produção com domínio verificado (Resend ou SMTP real), evidência anexada ao relatório.
- [ ] EasyStok integrado em shadow (ver seção abaixo) e relatório de paridade de 3 dias sem divergência inexplicada.
- [ ] Nenhuma biblioteca além de MailKit e Polly.
- [ ] CI verde, zero warnings, histórico um passo por commit, desvios reportados.

## Integração EasyStok (fora deste repositório)

Contrato do lado EasyStok, a planejar no repo dele: duplo envio em cada ponto de notificação (legado continua dono da entrega; chamada ao Hiram em fire-and-forget com timeout de 2s e circuit breaker para jamais derrubar o fluxo do EasyStok), `Idempotency-Key` igual ao identificador do evento de domínio, e auditoria de paridade comparando o que o legado enviou com a listagem do Hiram por janela de tempo. Critério de corte (ato operacional pós-F1, não bloqueia a F2): 7 dias de paridade estável ou decisão do fundador, então o tenant vira `live` e o caminho legado é desligado.

Pré-requisito operacional que não é código: DNS do domínio remetente do EasyStok (SPF, DKIM, DMARC) verificado no provider escolhido antes do corte.

## Não-objetivos da F1 (não implemente, nem "de graça")

DLQ e replay, requeue com delay, webhooks para tenants, webhooks de status do provider (delivered, bounce), templates, push, SMS, WhatsApp, metering e quotas, rate limit, Portal, KEDA, test mode de chaves, rotação automática de chaves. Tudo isso tem fase própria no MASTER-PLAN.
