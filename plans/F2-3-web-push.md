# F2 parte 3, Web Push com VAPID

> Plano executável no estilo de plans/F0, F1, F2-1 e F2-2. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Branch padrão: main. Em nenhum texto use travessão (em dash). Decisão estrutural na ADR-014, aberta antes do código.

## Objetivo

A terceira frente da F2: o canal Web Push. Resultado demonstrável:

1. O tenant registra, lista e remove subscriptions de navegador, escopadas a ele, e lê a chave pública VAPID para assinar no frontend.
2. Uma notificação com `channel = push` aponta para uma subscription pelo id no `recipient`.
3. O envio usa VAPID e cifra Web Push; 404 e 410 do push service são permanentes, 429 e 5xx são transitórios.
4. A entrega reusa outbox, dead letter, poison, parking lot, shadow e replay, sem tocar o caminho de email.
5. Build Release sem warning, suíte verde, CI verde.

Sequenciamento da F2: parte 1 dead letter e replay, parte 2 templates, **parte 3 Web Push (esta)**, depois webhooks (HMAC).

## Decisões técnicas fixas

Detalhe e trade-off na ADR-014 (docs/adr/ADR-014-web-push.md):

- Biblioteca WebPush, adicionada sob esta ADR. VAPID de plataforma em configuração.
- Subscription registrada por tenant; o `recipient` da notificação é o id da subscription.
- Caminho de entrega de push próprio (fila, consumer, processor), reusando dead letter, poison e shadow. O email não é tocado; a unificação num `IChannelSender` fica deferida.
- Conteúdo montado no submit e persistido, como na ADR-013. Templates de push ficam fora.
- Subscription inexistente no consumo é falha permanente; subscription morta não é podada nesta fatia.

## Passos

1. **ADR-014** (`docs: add adr-014 web push delivery`).
2. **Canal e modelo** (`feat: add push channel and subscription model`). `NotificationChannel.Push`, entidade `PushSubscription`. Testes unitários.
3. **Persistência** (`feat: persist push subscriptions`). Tabela, config, migration, `IPushSubscriptionStore` e store. Teste de integração.
4. **CRUD e chave VAPID** (`feat: expose push subscription endpoints`). Endpoints de subscription e `GET /v1/push/vapid-public-key`, `PushVapidOptions`, `AddHiramPush`. Testes.
5. **Sender VAPID** (`feat: send web push with vapid`). Pacote WebPush, porta `IPushSender`, adapter `WebPushSender` com classificação de outcome. Testes locais com handler fake e chaves geradas.
6. **Pipeline de push** (`feat: deliver push notifications through the dispatcher`). Topologia da fila de push, `PushConsumerWorker`, `PushNotificationProcessor` reusando dead letter, poison e shadow; DI e wiring do dispatcher. Testes do processor.
7. **Submit e fechamento** (`feat: accept push notifications` e `docs: document f2 part three web push`). `channel = push` no submit e no replay, e2e pelo dispatcher, README, relatório e este plano.

## Definição de pronto

Ver checklist completo em docs/F2-3-relatorio.md.

## Não-objetivos e deferidos

VAPID por tenant. Fan-out por usuário lógico. Poda automática de subscription morta. Templates de push. Unificação do pipeline de entrega num `IChannelSender` (gatilho no terceiro canal de entrega, SMS ou WhatsApp). Reabrem pela ADR-014 ou pelas fases próprias.
