# ADR-015: Webhooks de status emitidos pelo outbox e assinados com HMAC

**Status:** Aceito
**Data:** 2026-06-26
**Decisores:** Felipe (arquiteto)

## Contexto

A F2 fecha com webhooks de status: quando uma notificação chega a um estado terminal, o Hiram avisa o tenant chamando uma URL dele com um evento assinado, para o tenant reagir sem fazer polling. Não há biblioteca nova (HMAC-SHA256 e HttpClient são da BCL), mas é um padrão novo, integração de saída com assinatura e retry, então a decisão fica registrada.

Três perguntas: o que dispara o webhook, como o evento atravessa o sistema, e como o tenant confia no que recebeu.

## Decisão

O webhook dispara na transição terminal da notificação (`sent` e `dead_lettered`). O evento é emitido pelo outbox na mesma transação da mudança de estado, então um webhook só existe se a entrega foi de fato persistida. O dispatcher consome o evento, resolve os endpoints do tenant, assina o corpo com HMAC-SHA256 usando o segredo do endpoint e faz POST com retry. A assinatura vai no header `X-Hiram-Signature` no formato `sha256=<hex>`. O segredo é gerado pelo Hiram no cadastro, devolvido uma única vez e guardado cifrado por Data Protection.

## Opções consideradas

### O que dispara

- **Transição terminal via outbox, escolhida.** O processor de cada canal, ao marcar `sent` ou `dead_lettered`, enfileira um evento de webhook no outbox, na mesma transação. Reusa a espinha do projeto: o evento só nasce se a mudança de estado nasceu, e o relay e a topologia já existentes entregam.
- Polling de notificações mudadas: rejeitado, custo e latência sem ganho.
- Eventos de domínio com dispatcher central: a direção certa a prazo, mas é refatoração maior do que a fatia pede.

### Como o tenant confia

- **HMAC-SHA256 com segredo por endpoint, escolhido.** Simples, padrão de mercado (estilo GitHub), o tenant recalcula a assinatura sobre o corpo cru e compara. Sem dependência nova.
- mTLS ou JWT assinado: mais cerimônia do que a primeira fatia precisa.

### Falha de entrega do webhook

- **Retry e depois descarte com log e métrica, escolhido.** O POST tenta com a mesma política de retry da entrega (transitório em 5xx e timeout, permanente em 4xx). Esgotou, loga em Warning e conta a falha. Dead letter e replay de webhook ficam deferidos: um webhook não é uma notificação, então forçá-lo no modelo de dead letter de notificação distorceria o `DeadLetterMessage`, que é por notificação e canal.

## Decisões de borda cravadas

1. **Emissão condicional.** O evento só entra no outbox se o tenant tem ao menos um endpoint de webhook, evitando amplificar o trabalho do dispatcher para a maioria que não usa webhook. É uma leitura indexada por entrega; cache do flag fica deferido.
2. **Assinatura sobre o corpo cru.** O dispatcher serializa o evento público uma vez e assina exatamente os bytes que envia, sem reserialização entre assinar e postar.
3. **Segredo cifrado.** O segredo do endpoint é gerado no cadastro, devolvido uma vez em claro e guardado cifrado por `ISecretProtector` (Data Protection), como os segredos de provider. O mesmo aviso da F1 vale: o key ring precisa ser compartilhado entre Api que cifra e Dispatcher que assina, em produção.
4. **Envelope interno separado do corpo público.** O payload do outbox carrega `tenant_id` para o consumer achar os endpoints; o corpo postado ao tenant é só o evento (`notificationId`, `channel`, `status`, `occurredAt`), sem o `tenant_id` interno.
5. **Poison e idempotência.** Payload de webhook não parseável vira poison na parking lot, como os outros canais. Reentrega at-least-once pode chamar o endpoint do tenant mais de uma vez; o tenant deve ser idempotente, e isso é documentado.
6. **Sem filtro de evento.** Esta fatia entrega todos os eventos terminais; filtro por tipo de evento por endpoint fica deferido.

## Consequências

- **Fica mais fácil:** o tenant reage a estado por callback assinado; o evento herda a consistência do outbox; o caminho reusa relay, fila e parking lot.
- **Fica mais difícil:** webhook sem dead letter perde entregas após o retry esgotar, aceito nesta fatia; há uma leitura por entrega para decidir emitir; o key ring de Data Protection vira pré-requisito de produção.

## Gatilho de revisão

Necessidade de replay de webhook, filtro de evento por endpoint, garantia de entrega forte ao tenant, ou volume que torne a leitura por entrega cara o suficiente para exigir cache do flag de endpoints.

## Itens de ação

1. [ ] Entidade `WebhookEndpoint` e schema próprio.
2. [ ] CRUD de endpoints com segredo gerado e cifrado.
3. [ ] Emissão do evento no outbox na transição terminal, condicionada a haver endpoint.
4. [ ] Fila, consumer e processor de webhook com assinatura HMAC e retry.
