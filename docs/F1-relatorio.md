# F1, relatório de fechamento

> Email em produção. Relatório de Definição de Pronto item a item, com evidências e desvios reportados. Acompanha o plano em plans/F1-email-em-producao.md.

## Os 9 itens do objetivo

| # | Objetivo | Status | Evidência |
|---|---|---|---|
| 1 | Tenants e API keys reais (chave exibida uma vez, hash, revogável, tenant resolvido pela chave) | Feito | Passos 1 e 2. `ApiKey`, `ApiKeyIssuer` (`hk_live_` + base62), `ApiKeyHasher` (SHA-256), middleware resolvendo por hash. Testes: `ApiKeyHasherTests`, `ApiKeyIssuerTests`, `AuthenticationTests`. |
| 2 | `Idempotency-Key` honrada (repetição devolve mesmo id, sem segunda notificação) | Feito | Passo 3. Fast-path Redis `SET NX` + índice único parcial no Postgres. Testes: `IdempotencyTests`, `SubmitNotificationHandlerTests`, e o cenário replay em `EmailDeliveryEndToEndTests`. |
| 3 | Email real via dois providers intercambiáveis por tenant (SMTP/MailKit e Resend/HTTP; dev entrega no Mailpit) | Feito | Passos 5 e 6. `SmtpEmailProvider`, `ResendEmailProvider`, `EmailProviderResolver`. Testes: `SmtpDeliveryTests` (Mailpit), `ResendEmailProviderTests` (handler fake), `EmailDeliveryEndToEndTests` (live até o Mailpit). |
| 4 | Pipeline de envio com retries (transitório com backoff e jitter, permanente falha de imediato, cada tentativa em `DeliveryAttempt`) | Feito | Passo 7. Pipeline Polly (3 tentativas, base 2s, jitter), timeout de 10s por tentativa, `DeliveryAttempt` por tentativa. Testes: `EmailDeliveryPipelineTests`. |
| 5 | Máquina de estados `accepted -> queued -> sending -> sent | failed | suppressed` | Feito | Passos 1 e 7. Enum `NotificationStatus`, transições no consumer, conversão `published -> sent` na migration. |
| 6 | Shadow mode (registra `shadow_would_send` sem chamar provider) | Feito | Passo 8. Caminho shadow no consumer, attempt com `shadowed` e hash do payload, métrica `hiram.notifications.shadowed`. Testes: cenário shadow em `EmailDeliveryPipelineTests` e `EmailDeliveryEndToEndTests`. |
| 7 | Consulta de auditoria (listagem paginada com filtros, detalhe com tentativas) | Feito | Passo 9. `GET /v1/notifications` (cursor, filtros, escopo por tenant) e `GET /v1/notifications/{id}` com attempts. Testes: `NotificationQueryTests`. |
| 8 | Scalar com Handshake, exemplos reais e catálogo de erros ProblemDetails | Feito (com ressalva) | Passo 10. `HiramApiDocs`. Teste: `ApiDocsTests`. Ressalva no catálogo 409/422, ver Desvios e pendências. |
| 9 | Testes verdes, incluindo ponta a ponta com Mailpit, e CI verde | Feito | Build Release sem warnings, 66 unit verdes, suíte de integração verde no CI (run `dce79d7`, conclusão success), com `EmailDeliveryEndToEndTests` cobrindo o fluxo live até o Mailpit. |

## Definição de pronto da F1

- [x] Os 9 itens do objetivo demonstrados (item 9 depende da confirmação do CI).
- [ ] Email real entregue em produção com domínio verificado. Operacional, fora do dev. Em dev, entrega no Mailpit (evidência: `SmtpDeliveryTests`, `EmailDeliveryEndToEndTests`). O corte de produção depende de DNS (SPF/DKIM/DMARC) verificado no provider.
- [ ] EasyStok integrado em shadow e 3 dias de paridade. Integração mora no repositório do EasyStok e é ato operacional pós-F1; o lado Hiram (shadow mode + listagem de auditoria) está pronto.
- [ ] Nenhuma biblioteca além de MailKit e Polly. Desvio reportado abaixo.
- [x] CI verde, zero warnings, histórico um passo por commit, desvios reportados. CI verde na run `dce79d7` (success); build Release sem warnings; um passo por commit; desvios abaixo.

## Desvios reportados

### Bibliotecas adicionadas além de MailKit e Polly

Todas são framework, execução de stack já decidida, ou ferramenta de teste, não bibliotecas de runtime de terceiros no sentido de MailKit/Polly:

- `StackExchange.Redis`: cliente do Redis, que já é stack decidida no MASTER-PLAN (fast-path de quota, idempotência, rate limit). Aprovado por você no Passo 2 e anotado no plano da fase.
- `Microsoft.AspNetCore.DataProtection`: framework, nomeado explicitamente no plano (Passo 4) para cifrar segredos de tenant.
- `Microsoft.Extensions.Http`: framework, para o HttpClient tipado do Resend que o plano pede (Passo 6).
- `Testcontainers.Redis`: módulo de teste da mesma família dos módulos já em uso (Postgres, RabbitMq).
- Ferramenta: `dotnet-ef` 10.0.4 pinado em `.config/dotnet-tools.json` (o CLI global era 9.x, incompatível com EF Core 10).

### Catálogo 409/422 vs design assíncrono (Passo 10)

O `POST` é assíncrono (outbox) e devolve 202; o envio ocorre em background. Logo:
- Um 422 do provider vira um `DeliveryAttempt` `permanent_failure` e a notificação termina `failed`, não um HTTP 422 síncrono. Documentado na seção Delivery do Scalar.
- O 409 só ocorre num conflito de idempotência irresolúvel (raro). A Passo 3 devolve 202 para todo replay, sem comparar o corpo do request. Se o desejado é 409 para "mesma `Idempotency-Key` com corpo diferente", isso é uma melhoria da idempotência (fingerprint do request) a decidir.

### Decisões pendentes (sinalizadas ao longo da fase)

- `key_prefix` fica constante `"hk_live_"` por seguir o texto do plano (8 primeiros caracteres). Pouco útil para distinguir chaves numa listagem.
- O throttle de `last_used_at` é coberto por teste de integração (Redis `SET NX` atômico), não unit.
- Key ring do Data Protection precisa ser compartilhado e persistido entre Api (cifra) e Dispatcher (decifra) em produção. Em dev na mesma máquina funciona; em produção sem chave compartilhada o Dispatcher não decifra segredos de tenant.
- Idioma da documentação do Scalar: escrita em inglês para casar com a superfície da API.

## Nota de CI

Durante a maior parte da fase o acesso a api.github.com esteve bloqueado no ambiente de execução, o que escondeu duas falhas no CI:

- Um teste comparando o `settings` jsonb pela string crua (`TenantProviderConfigStoreTests`), que o Postgres reformata no round trip. Entrou no Passo 4 e deixou o CI vermelho do Passo 4 ao 12. Corrigido para comparar por conteúdo (commit `dce79d7`).
- Uma regressão de injeção de dependência na `WalkingSkeletonTests` (o consumer passou a exigir o resolver e o pipeline, não registrados no host da Api), do Passo 7 ao 10, corrigida no Passo 11.

Com os dois corrigidos, a run `dce79d7` fechou verde (success): 66 testes unitários e a suíte de integração inteira, incluindo a ponta a ponta com Mailpit, passando.

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet user-secrets set "Hiram:AdminKey" "admin-dev-local" --project src/Hiram.Api
dotnet run --project src/Hiram.Api
dotnet run --project src/Hiram.Dispatcher

curl -s -X POST http://localhost:3357/v1/admin/tenants \
  -H "X-Admin-Key: admin-dev-local" -H "Content-Type: application/json" \
  -d '{"name":"easystok","deliveryMode":"live"}'

curl -s -X POST http://localhost:3357/v1/admin/api-keys \
  -H "X-Admin-Key: admin-dev-local" -H "Content-Type: application/json" \
  -d '{"tenantId":"<id>","name":"easystok-server"}'

curl -i -X POST http://localhost:3357/v1/notifications \
  -H "X-Api-Key: hk_live_..." -H "Idempotency-Key: evt-0001" -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"ops@example.com","subject":"hello","body":"f1"}'
```

Esperado: a notificação termina `sent`, o email aparece em http://localhost:8025, repetir o curl com a mesma `Idempotency-Key` devolve o mesmo id com `Idempotency-Replayed: true`, e um tenant `shadow` termina `sent` registrando `shadow_would_send` sem entregar.
