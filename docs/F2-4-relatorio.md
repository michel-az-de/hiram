# F2 parte 4, relatório de fechamento

> Webhooks de status com HMAC. Relatório de Definição de Pronto item a item, com evidências e desvios. Acompanha o plano em plans/F2-4-webhooks.md e a decisão em docs/adr/ADR-015-status-webhooks.md. Fecha a F2.

## Definição de pronto

| Item | Status | Evidência |
|---|---|---|
| ADR-015 aceita antes do código | Feito | `docs/adr/ADR-015-status-webhooks.md`, commit `docs: add adr-015 status webhooks`. |
| Entidade `WebhookEndpoint` | Feito | `Hiram.Domain.Webhooks.WebhookEndpoint`, valida url http ou https. Testes: `WebhookEndpointTests`. |
| Schema `webhook_endpoints` com unique por tenant e url | Feito | `WebhookEndpointConfiguration`, migration `AddWebhookEndpoints`, porta `IWebhookEndpointStore`. Teste: `WebhookEndpointStoreTests`. |
| CRUD de endpoints, segredo gerado e devolvido uma vez | Feito | `POST/GET/DELETE /v1/webhooks`, segredo de 32 bytes base64url cifrado por `ISecretProtector`, devolvido só no cadastro, 409 em url duplicada. Testes: `WebhookEndpointsTests`. |
| Emissão do evento no outbox na transição terminal, condicionada a haver endpoint | Feito | `WebhookOutbox.TryEnqueueAsync` chamado pelos processors de email e push antes do save final. Testes: `Process_EmitsWebhookOutbox_WhenTenantHasEndpoint` e `..._DoesNotEmitWebhook_WhenTenantHasNoEndpoint`. |
| Entrega assinada com HMAC e retry pelo dispatcher | Feito | Fila `hiram.notifications.webhook`, `WebhookConsumerWorker`, `WebhookDeliveryProcessor` que resolve os endpoints, assina o corpo com `WebhookSignature` (HMAC-SHA256, header `X-Hiram-Signature`) e faz POST com retry (transitório em 5xx e timeout, permanente em 4xx). Testes: `WebhookSignatureTests` (local) e `WebhookDeliveryProcessorTests`. |
| Build Release sem warning, suíte unit verde | Feito | Build Release `0 Aviso(s)`. 99 testes unitários verdes localmente, mais os de assinatura por filtro. |
| Nenhuma biblioteca nova | Feito | HMAC-SHA256 e HttpClient sao da BCL. |

## Desvios e notas

### Webhook sem dead letter (ADR-015)

Um POST que esgota o retry é logado em Warning e contado em `hiram.webhooks.failed`, sem virar dead letter. Um webhook nao é uma notificação, entao forçá-lo no `DeadLetterMessage`, que é por notificação e canal, distorceria o modelo. Dead letter e replay de webhook ficam deferidos.

### Verificação local sem Docker

A assinatura HMAC roda localmente em `WebhookSignatureTests`. A emissão, a entrega assinada e a classificação de retry sao validadas pela CI (`WebhookDeliveryProcessorTests` com handler fake e Postgres, `EmailDeliveryPipelineTests` para a emissão). O wiring do consumer espelha o de push, que já foi provado de ponta a ponta na F2 parte 3, entao nao foi repetido um e2e de broker para webhook.

### Key ring de Data Protection

O segredo do endpoint é cifrado pela Api no cadastro e decifrado pelo Dispatcher na hora de assinar. Em dev na mesma máquina e nos testes no mesmo processo funciona; em produção o key ring precisa ser compartilhado e persistido entre os dois hosts, mesmo aviso da F1. A registração de Data Protection e `ISecretProtector` foi movida para `AddHiramInfrastructure`, entao os dois hosts a têm.

### Emissão por entrega

`WebhookOutbox.TryEnqueueAsync` faz uma leitura indexada por entrega para decidir se emite (só emite se o tenant tem endpoint). Cache desse flag fica deferido.

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet run --project src/Hiram.Api
dotnet run --project src/Hiram.Dispatcher

# cadastra um webhook e guarda o segredo devolvido uma vez:
curl -s -X POST http://localhost:3357/v1/webhooks -H "X-Api-Key: hk_live_..." -H "Content-Type: application/json" \
  -d '{"url":"https://meu-app.exemplo.com/hiram-hooks"}'

# envia uma notificação; ao chegar a sent ou dead_lettered, o endpoint recebe um POST assinado.
```

Esperado: o endpoint recebe `{notificationId, channel, status, occurredAt}` com o header `X-Hiram-Signature: sha256=...`, que o tenant recalcula com HMAC-SHA256 do corpo cru usando o segredo do cadastro.

## Estado da F2

A F2 está completa: dead letter e replay (parte 1), templates (parte 2), Web Push (parte 3) e webhooks de status (parte 4). ADRs 011, 013, 014 e 015 escritas, relatório por parte, tudo no main com CI verde.
