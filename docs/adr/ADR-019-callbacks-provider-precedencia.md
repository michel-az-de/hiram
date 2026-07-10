# ADR-019: Callbacks de provider, máquina de estados de entrega idempotente por precedência

**Status:** Proposto
**Data:** 2026-07-10
**Decisores:** Felipe (arquiteto)

## Contexto

A absorção do EasyStok (passo 2.3 de `plans/easystok-absorcao-total.md`) exige fechar o status loop da
entrega. Hoje o Hiram marca `Sent` quando o provider aceita a mensagem, mas aceito pelo provider não é
entregue na caixa. Delivered, bounce e complaint chegam depois, de forma assíncrona, por callback do
provider (Resend na F1). Sem processar esses callbacks, o cutover de entrega fica cego para bounce, e o
adendo do ADR-017 (recuperação de `Sending` órfão) não tem fonte de verdade para decidir reenvio seguro.

Os callbacks chegam fora de ordem e podem repetir, porque o provider reentrega o webhook até receber
2xx. Um delivered tardio não pode sobrescrever um bounce anterior. A referência da issue #13 a
"ADR-017" estava incorreta: o ADR-017 é ingestão e motor de notificação, não callbacks. Este ADR é o
registro dessa decisão, até aqui inexistente.

Não confundir com o ADR-015: lá o Hiram emite webhooks de status assinados para o tenant. Aqui o Hiram
recebe callbacks do provider de entrega. Sentidos opostos, o padrão de assinatura HMAC é reusado.

## Decisão

Um endpoint de callback por provider recebe os eventos de status, valida a assinatura, casa pelo
`provider_message_id` gravado na entrega, e aplica uma transição idempotente por precedência sobre um
estado de entrega derivado, separado do `NotificationStatus` de aceitação. A ordem de chegada não altera
o estado final: a precedência entre estados decide.

## Opções consideradas

### Opção A: máquina de estados por precedência, estado de entrega derivado (escolhida)

**Prós:** idempotente por construção (reaplicar o mesmo callback é no-op, um evento de menor precedência
não rebaixa o estado); tolera fora de ordem e duplicata sem lock; audita cada transição.
**Contras:** exige um modelo de estado de entrega novo, separado do `NotificationStatus`.

### Opção B: last-write-wins por timestamp do evento

**Prós:** simples.
**Contras:** o relógio do provider não é confiável entre eventos; um delivered com timestamp enviesado
sobrescreveria um bounce. Rejeitada.

### Opção C: aplicar o callback direto no `NotificationStatus`

**Prós:** sem modelo novo.
**Contras:** mistura aceitação (Accepted/Sending/Sent) com entrega (delivered/bounce/complaint), quebra a
máquina de transição do `NotificationRequest` e a polui. Rejeitada.

## Decisões de borda cravadas

1. **Identidade da entrega.** Ao chamar o provider, grava-se o `provider_message_id` retornado no
   `DeliveryAttempt`. O callback casa por esse id. Callback sem correspondência vira dead-letter mais
   alerta, nunca accept-and-drop silencioso (coerente com a decisão 9 do ADR-017).
2. **Precedência dos estados.** Ordem parcial: `complaint > hard_bounce > soft_bounce > delivered >
   sent`. Um evento só avança o estado se tiver precedência maior que o atual; caso contrário é
   registrado e ignorado para efeito de estado. Complaint e hard bounce são terminais.
3. **Idempotência.** Reaplicar o mesmo evento é no-op. A chave de dedup é
   `(provider, provider_message_id, event_type)`.
4. **Assinatura.** O callback é autenticado pela assinatura do provider (HMAC no Resend, reusando o
   padrão dos webhooks de status). Assinatura inválida é rejeitada com 401, não processada.
5. **Efeito colateral por estado.** Hard bounce e complaint disparam bloqueio do destinatário
   (kill-switch por contato, ver ADR-024) e alimentam a supressão futura. Soft bounce não bloqueia.
6. **Relação com a recuperação de `Sending`.** Uma vez que o callback fecha o estado de entrega, a
   recuperação de `Sending` órfão do adendo ao ADR-017 pode consultar o estado real: delivered confirma
   entrega e fecha sem reenvio; ausência de callback além do limiar mantém a política fail-safe
   (dead-letter com alerta).

## Consequências

- **Fica mais fácil:** status loop fechado, cutover de entrega com visibilidade de bounce, fonte de
  verdade para a recuperação de `Sending` órfão.
- **Fica mais difícil:** modelo de estado de entrega novo, um endpoint público a mais para endurecer,
  dependência da configuração de webhook no provider.

## Gatilho de revisão

Segundo provider com semântica de status diferente (a precedência pode precisar ser por provider), ou
necessidade de reconciliação ativa por poll quando o provider não entrega o callback.

## Itens de ação

1. [ ] `provider_message_id` gravado no `DeliveryAttempt` (issue #13).
2. [ ] Endpoint de callback assinado por provider, com dedup (issue #13).
3. [ ] Estado de entrega derivado e transição por precedência (issue #13).
4. [ ] Ligação com a recuperação de `Sending` do adendo ao ADR-017 (issue #35).
