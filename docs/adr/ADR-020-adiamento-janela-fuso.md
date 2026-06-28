# ADR-020: Adiamento por janela e fuso via dispatch_at no Postgres

**Status:** Aceito
**Data:** 2026-06-28
**Decisores:** Felipe (arquiteto)

## Contexto

O EasyStok respeita janela horária e fuso do tenant: o cron envia na abertura da janela. Na absorção,
o Hiram precisa do mesmo comportamento. Suprimir um evento fora da janela mudaria o comportamento e
quebraria a paridade no shadow. O Hiram precisa adiar, não suprimir.

## Decisão

Adiar via `dispatch_at` no Postgres, reusando o relay de outbox existente. A mensagem recebe
`dispatch_at` calculado da janela e do fuso do tenant; o relay só publica linhas com `dispatch_at`
vencido. Nada de plugin delayed-message no RabbitMQ.

## Opções consideradas

### Opção A: dispatch_at no Postgres, relay reusado (escolhida)

**Prós:** reusa o poll de outbox e a confiabilidade já madura (FOR UPDATE SKIP LOCKED, publisher
confirms); sem dependência nova; o adiamento é dado, não infraestrutura.
**Contras:** a query do relay ganha um predicado, tocando infra que a cerca additive-only protege.

### Opção B: plugin delayed-message do RabbitMQ

**Prós:** atraso nativo no broker.
**Contras:** dependência nova (ADR à parte), e o estado de atraso sai do Postgres, complicando a
recuperação.

### Opção C: fila separada e relay próprio para o caminho de eventos

**Prós:** isola do caminho direto.
**Contras:** duplica a lógica de confiabilidade do relay; rejeitada por duplicação.

## Decisões de borda cravadas

1. **Reuso do relay com `dispatch_at` nullable.** A query vira `WHERE dispatch_at IS NULL OR
   dispatch_at <= now()`. NULL preserva o comportamento imediato do caminho direto. Como toca infra
   fenceada, o gate de regressão ganha a asserção `DirectNotification_StillPublishesImmediately_WithDeferralQuery`.
2. **Due-check pelo relógio do banco.** `dispatch_at <= now()` usa `now()` do Postgres, não o relógio
   do app, para não sofrer skew entre pods.
3. **Jitter contra thundering herd.** Na abertura da janela, `dispatch_at` recebe jitter dentro da
   janela, e o relay tem rate-limit de publicação, para não estourar publish, KEDA e rate limit do
   provider às 8h.
4. **SLIs distintos.** Backlog agendado (`dispatch_at` no futuro, esperado) é separado de backlog
   atrasado (overdue, vencido e não publicado, que é problema).

## Consequências

- **Fica mais fácil:** paridade com o EasyStok (envia na abertura da janela); adiamento auditável no
  Postgres.
- **Fica mais difícil:** o relay compartilhado agora carrega o predicado de `dispatch_at`; a asserção
  de regressão garante o caminho direto intacto.

## Gatilho de revisão

Volume de mensagens agendadas que torne o poll do relay caro, ou necessidade de atraso de altíssima
precisão que o poll de 1s não entregue.

## Itens de ação

1. [ ] Coluna `dispatch_at` nullable no outbox e predicado no relay, com asserção de regressão (passo 1.7).
2. [ ] Cálculo de `dispatch_at` por janela e fuso, com jitter (passo 1.7).
3. [ ] SLIs de backlog agendado versus overdue (passo 0.11 e 1.11).
