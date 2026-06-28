# ADR-017: Ingestão de eventos crus e motor de notificação

**Status:** Aceito
**Data:** 2026-06-28
**Decisores:** Felipe (arquiteto)

## Contexto

A absorção total do EasyStok (plans/easystok-absorcao-total.md) move a decisão de notificação para o
Hiram. Hoje o Hiram só aceita notificações já renderizadas via `POST /v1/notifications`. O EasyStok
emite eventos crus de negócio (pedido criado, estoque baixo, assinatura expirando) e, a partir do
evento, alguém precisa resolver rotina, template, canais permitidos, destinatário e custo. Esse motor
passa a viver no Hiram, ao lado do caminho direto existente (que continua, sob cerca additive-only).

Um evento vira N mensagens (fan-out por canal e destinatário). Isso muda a idempotência: o índice
único atual protege só o caminho direto. Replay de DLQ ou redelivery do RabbitMQ pode refazer o
fan-out e reenviar. E o cutover do EasyStok para o Hiram precisa de uma fronteira determinística que
não perca nem duplique entre dois bancos sem transação compartilhada.

## Decisão

Novo caminho `POST /v1/events`: persiste o evento e a OutboxMessage na mesma transação (estende o
invariante fundador), e o motor resolve rotina, template, consentimento, canais e destinatário,
gerando mensagens com chave de idempotência determinística por mensagem. A emissão do EasyStok para o
Hiram anda numa outbox dedicada do EasyStok, durável, com uma sequência monotônica `emission_seq` que
serve de watermark do cutover.

## Opções consideradas

### Opção A: motor de notificação no Hiram, ingestão de evento cru (escolhida)

**Prós:** cumpre a absorção total; o Hiram vira a plataforma de notificação de verdade; explicável e
auditável; reusa fila, retry, DLQ, metering e observabilidade já prontos.
**Contras:** grande superfície nova de domínio (rotina, consentimento, bloqueio, fallback, adiamento);
risco de paridade no corte.

### Opção B: gateway de entrega (EasyStok mantém o motor, Hiram só entrega)

**Prós:** menor esforço e acoplamento.
**Contras:** não é absorção total; foi descartada por decisão de produto.

### Opção C: Hiram chama de volta o EasyStok para resolver a decisão

**Prós:** reusa a lógica do EasyStok.
**Contras:** inverte a dependência, acopla o Hiram ao schema do ERP.

## Decisões de borda cravadas

1. **Persistência transacional.** Evento mais OutboxMessage na mesma transação. Chave de idempotência
   de evento = `event_id` do EasyStok; índice único `(tenant_id, event_id)`. Reentrega do mesmo evento
   não refaz fan-out.
2. **Idempotência de dois níveis.** Cada mensagem renderizada tem chave determinística
   `hash(event_id, channel, recipient, template_version, dispatch_slot)`, com índice único. A chave vem
   da decisão persistida no fan-out, nunca recalculada na entrega. Para transacional, o slot colapsa
   e a chave degenera para `hash(event_id, channel, recipient, template_version)`.
3. **Claim antes do provider.** O consumer faz claim durável no Postgres, na mesma transação do
   DeliveryAttempt, antes de chamar o provider. Postgres é a única autoridade de "já enviado"; Redis
   só acelera o caminho feliz, nunca decide sozinho.
4. **Recuperação outbox versus RabbitMQ.** O outbox recupera até a publicação confirmada (publisher
   confirms). Depois, a durabilidade é do broker (filas e mensagens persistentes, volume). Perda de
   disco do broker single-node é risco residual aceito, não caso coberto.
5. **Emissão durável e watermark.** A emissão EasyStok para Hiram anda numa outbox dedicada do
   EasyStok, na mesma transação da mutação de negócio, com `emission_seq` `bigserial`. O relay dessa
   emissão é flag-based (marca pendente/processado), nunca cursor, para não pular late-committer.
6. **Fronteira determinística de cutover.** No flip de uma empresa, W = maior `emission_seq` até T0. O
   gate é dos dois lados: EasyStok entrega `emission_seq <= W`, Hiram entrega `> W`. Drain completo do
   local = nenhum pendente menor ou igual a W E nenhuma transação iniciada antes de T0 ainda aberta
   (bigserial commita fora de ordem).
7. **Matching de rotina.** Um evento pode casar N rotinas ativas (todas disparam). Zero rotinas:
   registra no_route e não envia. Rotina que aponta template não aprovado: suprime com motivo e alerta.
8. **Metering em shadow e quota.** Entradas de ledger geradas em shadow têm flag de shadow e ficam fora
   de qualquer quota. Enforcement de quota é não objetivo desta fase (ledger é observabilidade);
   entrará com revisão do ADR-007.
9. **Tipo de evento desconhecido.** Tipo que o Hiram não conhece vira dead-letter mais alerta, nunca
   accept-and-drop silencioso.

## Consequências

- **Fica mais fácil:** o Hiram passa a gerir toda a notificação; auditoria por evento e por mensagem;
  cutover sem perda nem duplicata por fronteira de sequência.
- **Fica mais difícil:** domínio novo grande para construir e testar; a maior parte fecha no CI com
  Testcontainers; o corte exige paridade em shadow antes.

## Gatilho de revisão

Volume que exija particionamento do motor, necessidade de ordenação causal estrita (hoje não objetivo),
ou enforcement de quota.

## Itens de ação

1. [ ] `POST /v1/events` transacional com idempotência de evento (passo 1.1).
2. [ ] Motor de rotinas, consentimento, bloqueio, fallback, adiamento (passos 1.2 a 1.7).
3. [ ] Idempotência de mensagem no fan-out e claim antes do provider (passo 1.8).
4. [ ] Outbox de emissão com `emission_seq` no EasyStok (passo 1.9).
