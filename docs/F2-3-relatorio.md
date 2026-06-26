# F2 parte 3, relatório de fechamento

> Web Push com VAPID. Relatório de Definição de Pronto item a item, com evidências e desvios. Acompanha o plano em plans/F2-3-web-push.md e a decisão em docs/adr/ADR-014-web-push.md.

## Definição de pronto

| Item | Status | Evidência |
|---|---|---|
| ADR-014 aceita antes do código (biblioteca, VAPID, modelo de subscription, caminho próprio) | Feito | `docs/adr/ADR-014-web-push.md`, commit `docs: add adr-014 web push delivery`. |
| Canal `Push` e entidade `PushSubscription` | Feito | `NotificationChannel.Push`, `Hiram.Domain.Push.PushSubscription`. Testes: `PushSubscriptionTests`. |
| Schema `push_subscriptions` com unique por tenant e endpoint | Feito | `PushSubscriptionConfiguration`, migration `AddPushSubscriptions`, porta `IPushSubscriptionStore`. Teste: `PushSubscriptionStoreTests`. |
| CRUD de subscriptions e chave pública VAPID | Feito | `POST/GET/DELETE /v1/push-subscriptions` e `GET /v1/push/vapid-public-key`, escopados por tenant, 409 em endpoint duplicado. Testes: `PushEndpointsTests`. |
| Envio Web Push com VAPID | Feito | Pacote WebPush 1.0.13, porta `IPushSender`, adapter `WebPushSender` com cifra, assinatura VAPID e classificação de outcome (404/410 permanente, 429/5xx transitório). Testes locais: `WebPushSenderTests`. |
| Entrega de push pelo dispatcher reusando dead letter, poison e shadow | Feito | Fila `hiram.notifications.push`, `PushConsumerWorker`, `PushNotificationProcessor` (resolve subscription, retry, attempts, dead letter, poison, shadow). Testes: `PushDeliveryPipelineTests`. |
| Submit com `channel = push` apontando para a subscription por id | Feito | `ParseChannel` e `RoutingKeyFor` aceitam push no submit e no replay. E2e: `PushToUnknownSubscription_DeadLettersThroughTheDispatcher`. |
| Biblioteca nova só com ADR, build Release sem warning, suíte unit verde | Feito | WebPush sob a ADR-014. Build Release `0 Aviso(s)`. 93 testes unitários verdes localmente, mais os 4 do `WebPushSender` por filtro. |

## Desvios e notas

### Duplicação consciente do pipeline de entrega (ADR-014)

O `PushNotificationProcessor` repete a orquestração do `EmailNotificationProcessor` (retry, attempts, dead letter, poison, shadow) em vez de extrair um `IChannelSender` comum agora. O critério: mudanças de entrega só são validáveis na CI, sem Docker no ambiente de desenvolvimento, então tocar o caminho de email que já está verde é risco desproporcional. A unificação fica registrada como dívida na ADR-014, com gatilho no terceiro canal de entrega.

### Verificação local sem Docker

O envio Web Push de verdade (cifra, VAPID, classificação) roda localmente em `WebPushSenderTests`, com chaves VAPID geradas e uma subscription válida, sem container. O processor e o e2e de push são validados pela CI. O e2e cobre o caminho completo submit push, relay, consumer, processor, dead letter, usando uma subscription inexistente, então não depende de um push service real.

### Subscription morta não é podada (ADR-014, deferido)

Uma subscription que devolve 404 ou 410 vira dead letter; remover a subscription morta do banco fica deferido com nota de rastreio.

### VAPID de plataforma

Par de chaves e subject em configuração. VAPID por tenant, fan-out por usuário e templates de push ficam deferidos.

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
# gerar chaves VAPID e configurar:
dotnet user-secrets set "Hiram:Push:Vapid:PublicKey" "<base64url>" --project src/Hiram.Api
dotnet user-secrets set "Hiram:Push:Vapid:PrivateKey" "<base64url>" --project src/Hiram.Api
dotnet run --project src/Hiram.Api
dotnet run --project src/Hiram.Dispatcher

curl -s http://localhost:3357/v1/push/vapid-public-key -H "X-Api-Key: hk_live_..."

curl -s -X POST http://localhost:3357/v1/push-subscriptions -H "X-Api-Key: hk_live_..." -H "Content-Type: application/json" \
  -d '{"endpoint":"https://push.example.com/...","p256dh":"...","auth":"..."}'

curl -i -X POST http://localhost:3357/v1/notifications -H "X-Api-Key: hk_live_..." -H "Content-Type: application/json" \
  -d '{"channel":"push","recipient":"<subscription-id>","subject":"Hi","body":"hello"}'
```

Esperado: o navegador inscrito recebe o push; um `recipient` que não resolve termina `dead_lettered` com `permanent_failure`.
