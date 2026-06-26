# F2 parte 4, webhooks de status com HMAC

> Plano executável no estilo de plans/F0, F1, F2-1, F2-2 e F2-3. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Branch padrão: main. Em nenhum texto use travessão (em dash). Decisão estrutural na ADR-015, aberta antes do código. Esta parte fecha a F2.

## Objetivo

A última frente da F2: webhooks de status. Resultado demonstrável:

1. O tenant registra, lista e remove endpoints de webhook, escopados a ele, com um segredo gerado e devolvido uma única vez.
2. Quando uma notificação chega a `sent` ou `dead_lettered`, um evento é emitido pelo outbox na mesma transação, só se o tenant tem endpoint.
3. O dispatcher entrega o evento assinado com HMAC-SHA256 no header `X-Hiram-Signature`, com retry (transitório em 5xx e timeout, permanente em 4xx).
4. Build Release sem warning, suíte verde, CI verde.

Sequenciamento da F2: parte 1 dead letter e replay, parte 2 templates, parte 3 Web Push, **parte 4 webhooks (esta, fecha a F2)**.

## Decisões técnicas fixas

Detalhe e trade-off na ADR-015 (docs/adr/ADR-015-status-webhooks.md):

- Dispara na transição terminal, emitido pelo outbox na mesma transação da mudança de estado.
- Assinatura HMAC-SHA256 do corpo cru, segredo por endpoint gerado pelo Hiram, cifrado por Data Protection.
- Falha de entrega: retry e depois descarte com log e métrica. Dead letter e replay de webhook ficam deferidos.
- Emissão condicionada a haver endpoint, para nao amplificar o trabalho do dispatcher.
- Nenhuma biblioteca nova.

## Passos

1. **ADR-015** (`docs: add adr-015 status webhooks`).
2. **Domínio** (`feat: add webhook endpoint model`). Entidade `WebhookEndpoint`. Testes unitários.
3. **Persistência** (`feat: persist webhook endpoints`). Tabela, config, migration, `IWebhookEndpointStore` e store. Teste de integração.
4. **CRUD** (`feat: expose webhook endpoint management`). Endpoints de registro, listagem e remoção, segredo gerado e cifrado. `ISecretProtector` movido para `AddHiramInfrastructure`. Testes.
5. **Emissão** (`feat: emit status webhook events to the outbox`). `WebhookOutboxPayload`, helper `WebhookOutbox`, emissão na transição terminal nos processors de email e push. Testes.
6. **Entrega** (`feat: deliver signed webhooks through the dispatcher`). Fila, `WebhookConsumerWorker`, `WebhookDeliveryProcessor` com assinatura HMAC e retry, `WebhookSignature`, DI e wiring. Testes com handler fake.
7. **Fechamento** (`docs: document f2 part four webhooks`). README, relatório e este plano.

## Definição de pronto

Ver checklist completo em docs/F2-4-relatorio.md.

## Não-objetivos e deferidos

Dead letter e replay de webhook. Filtro de evento por endpoint. Cache do flag de existência de endpoint. Garantia de entrega forte ao tenant. Reabrem pela ADR-015.
