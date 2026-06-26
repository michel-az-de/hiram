# F2 parte 1, dead letter e replay

> Plano executável no estilo de plans/F0-walking-skeleton.md e plans/F1-email-em-producao.md. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Branch padrão: main. Em nenhum texto use travessão (em dash). As bordas de concorrência e falha estão cravadas na ADR-011, aberta antes do código.

## Objetivo

A primeira frente da F2: confiabilidade depois do envio. Resultado demonstrável:

1. Uma notificação que esgota a entrega (transitório esgotado ou permanente) termina `dead_lettered` com a causa registrada, auditável por tenant.
2. Mensagem que nunca pode ser processada (payload inválido, notificação inexistente) vai para uma parking lot no broker, contada por métrica, em vez de sumir.
3. `POST /v1/notifications/{id}/replay` reenfileira pelo outbox existente e a notificação chega a `sent`, escopado por tenant, com 409 codificado e 404 entre tenants.
4. Consulta por `status=dead_lettered` e detalhe com a razão.
5. Métricas `dead_lettered`, `replayed`, `poisoned` e span de replay no trace.
6. Build Release sem warning, suíte verde, CI verde.

Sequenciamento da F2 (uma frente por rodada): parte 1 dead letter e replay (esta), depois templates (Scriban), depois Web Push (VAPID), depois webhooks (HMAC).

## Decisões técnicas fixas

Resumo, detalhe e trade-off na ADR-011 (docs/adr/ADR-011-dlq-replay.md):

- Dead letter com fonte da verdade no Postgres (`dead_letter_messages`), replay reenfileirando via outbox na mesma transação, parking lot fina no broker só para poison não parseável.
- Nenhuma biblioteca nova: reuso de RabbitMQ.Client, EF Core 10, Polly e o outbox da F0.
- Migration nova, nunca alterar aplicada. `DeadLettered` é valor novo de um status varchar sem check constraint.
- Concorrência de replay por transição guardada `DeadLettered -> Queued` via `ExecuteUpdateAsync`, zero linhas vira 409. Índice unique parcial garante no máximo uma dead letter aberta por notificação.
- Replay reenvia o `Payload` armazenado na dead letter, não um re-render da notificação.
- Terminal vivo passa a `DeadLettered`, mas `hiram.notifications.failed` continua emitido na exaustão.
- Classificação poison vs transitório no consumer: só payload inválido e not-found determinístico estacionam; transitório reenfileira com backoff curto.
- Escopo por tenant em replay e consulta, sempre.

## Passos

1. **ADR-011** (`docs: add adr-011 dead letter and replay strategy`). Decisão, opções A/B/C, 15 decisões de borda cravadas, gatilho de revisão. Item de ação 2 da ADR-005 endereçado.
2. **Domínio** (`feat: add dead lettered state and dead letter domain model`). `NotificationStatus.DeadLettered`, `MarkDeadLettered` e `RequeueForReplay` guardados, `DeadLetterMessage`. Testes unitários de transição e invariantes.
3. **Persistência** (`feat: persist dead letter messages`). DbSet, configuração (`payload` jsonb, `reason varchar(256)`, índice parcial `ux_dead_letter_messages_open`), migration `AddDeadLetterMessages`. Teste de integração de round trip e da unique parcial.
4. **Processor dead-leta** (`feat: dead letter notifications that exhaust delivery`). Grava dead letter e marca `DeadLettered` no outcome final não `Sent`; `AttemptCount` e reason cravados; guard de settled e métrica reconciliados; três testes da F1 atualizados.
5. **Parking lot e poison** (`feat: route poison messages to a dead letter parking lot`). DLX, fila e bind aditivos; `PoisonMessageException`; consumer classifica poison (park) vs transitório (requeue com backoff); contador `poisoned`. Testes de classificação e a parking lot e2e.
6. **Replay** (`feat: replay dead lettered notifications through the outbox`). Porta `IDeadLetterReplay`, `DeadLetterReplay` transacional com transição guardada, endpoint `POST /v1/notifications/{id}/replay`, 409 codificado, 404 entre tenants. Testes de concorrência, conflito, isolamento e ciclo.
7. **Consulta** (`feat: expose dead letter status in notification queries`). `status=dead_lettered` no parse, `DeadLetterView` no detalhe. Testes de filtro e detalhe.
8. **Telemetria**. Counters `dead_lettered`, `poisoned`, `replayed` e span `replay notification` entregues junto dos passos 4, 5 e 6 (o meter e o source registram por nome).
9. **Ponta a ponta e fechamento** (`test: add dead letter and replay end to end scenarios` e `docs: document f2 part one dead letter and replay`). E2e falha permanente -> `dead_lettered` -> replay -> `sent` no Mailpit; README e relatório.

## Matriz de testes

| Teste | O que prova | Local |
|---|---|---|
| T1 | Replays simultâneos: um outbox novo, um envio, segundo 409 | `DeadLetterReplayTests` |
| T2 | Permanente termina `DeadLettered`, `AttemptCount = 1`, sem 3 tentativas | `EmailDeliveryPipelineTests` |
| T3 | Transitório no consumer reenfileira, não estaciona recuperável | `EmailDeliveryPipelineTests` |
| T4 | Dead letter dupla: replay sempre na aberta, `AttemptCount` por ciclo | `DeadLetterReplayTests` |
| T5 | Sem duplo publish: replay leva a `sent`, original não republica | `DeadLetterReplayTests`, e2e |
| T6 | Poison incrementa contador e cai na fila dead-letter | `EmailDeliveryPipelineTests`, e2e |

## Definição de pronto

Ver checklist completo em docs/F2-1-relatorio.md.

## Não-objetivos e PARK

Retry agendado ou com delay nativo (gatilho de revisão da ADR-011). Replay em lote ou por janela. Histórico e agrupamento por ciclo na API. Retenção, purga e cifra de payload e reason (PII deferida com nota de rastreio). Escopo de permissão distinto para replay. Reconstrução de payload multi-canal (esta fatia é email-only). Expiração da parking lot.
