# ADR-024: Enforcement de consentimento e bloqueio no caminho de entrega

**Status:** Proposto
**Data:** 2026-07-10
**Decisores:** Felipe (arquiteto)

## Contexto

O ADR-018 modelou o consentimento LGPD e o ADR-017 previu bloqueio no motor. O código construiu
`ConsentPolicy`, `BlockGate` e `ChannelResolver`, com teste unit e registro em
`src/Hiram.Infrastructure/DependencyInjection.cs`, mas nenhum caminho de envio os invoca (medido por
grep: os tipos só aparecem na própria definição e no DI). O `docs/calibration.md` (passos 1.4 a 1.6)
admite que o consumer que fiaria tudo "entra ao fim", e nunca entrou.

Consequência com tenant em modo Live: `POST /v1/consent` grava opt-out e devolve 200, mas o envio não
consulta consentimento, então o sistema envia para quem fez opt-out, exposição LGPD; e um kill-switch
criado em `POST /v1/blocks` não interrompe envio. Ligar o enforcement fixa uma postura de risco jurídico
por `if`, logo precisa de decisão registrada antes do código.

## Decisão

O enforcement entra em dois pontos distintos, pela natureza de cada filtro:

- **Consentimento** entra no `ChannelResolver`, chamado pelo `EventFanout`, porque exige `userId` e
  categoria, que só o caminho de eventos carrega.
- **Bloqueio (kill-switch)** entra no processor de entrega, cobrindo também o caminho direto
  `POST /v1/notifications`, porque um kill-switch de incidente precisa estancar o que já está na fila, e
  o `BlockGate` é keyed por tenant e canal, sem exigir `userId`.

Quando o payload do evento não traz `userId`, o consentimento aplica fail-open para as categorias
transacional e operacional (base legal de interesse legítimo e execução de contrato) e fail-closed para
marketing (exige opt-in verificável).

## Tensão reconhecida com o ADR-018

O ADR-018 (borda 4) crava, para o cutover de leitura de consentimento, "na dúvida, não enviar"
(fail-closed). Este ADR propõe fail-open para transacional sem `userId`. A distinção: a borda 4 do
ADR-018 trata do estado de migração do dado de consentimento (o registro pode não ter migrado); este ADR
trata da ausência de chave (sem `userId` não há o que consultar). Ainda assim, as duas posturas convivem
em tensão, e a escolha é jurídica, não técnica.

Por isso este ADR exige GO humano e depende da issue #38 (confirmar se o EasyStok sempre envia
`RecipientUserId`). Se sempre enviar, o fail-open nunca dispara e a tensão é acadêmica. Se não enviar,
fail-open transacional é a recomendação, revertível para fail-closed total numa linha.

## Opções consideradas

### Opção A: consent no ChannelResolver/EventFanout, block no processor (escolhida)

**Prós:** cada filtro no ponto onde seus insumos existem; o block cobre o tráfego live do caminho
direto; reusa componentes prontos sem reescrever.
**Contras:** block no processor toca a cerca additive-only (decisão explícita, abaixo).

### Opção B: tudo no RoutineResolver

**Prós:** um ponto só.
**Contras:** o `RoutineResolver` não conhece `userId`, categoria nem `now`; poluiria a assinatura e não
cobriria o caminho direto para o block. Rejeitada.

### Opção C: consent e block só na ingestão

**Prós:** não toca o processor.
**Contras:** não estanca o que já está na fila durante um incidente; o kill-switch chegaria tarde.
Rejeitada.

## Decisões de borda cravadas

1. **Ponto do consent.** `ChannelResolver`, invocado pelo `EventFanout`, com `userId` nullable,
   categoria de `Routine.Category` e `now` do `IClock`. O `RoutineResolver` permanece puro (só aprovação
   de template).
2. **Ponto do block.** No processor de entrega, antes de chamar o provider, para os dois caminhos. A
   supressão usa `NotificationStatus.Suppressed` via `MarkSuppressed` (issue #33), com estado auditável.
3. **Cerca additive-only.** Ligar block no processor toca ponto sensível. A cerca
   (`plans/easystok-absorcao-total.md:449-457`) protege endpoint, idempotência de ingestão e relay, mas
   não o processor. Ainda assim, esta é a decisão explícita que a cerca exige: o comportamento do caminho
   direto muda (passa a respeitar block), o que é intencional, com asserção de regressão de que, sem
   block ativo, a entrega segue idêntica.
4. **Fail-open por categoria.** Sem `userId`: transacional e operacional enviam, marketing suprime.
   Coerente com `ConsentPolicy.cs` (default-allow, marketing exige opt-in). Revertível para fail-closed
   total sem mudança estrutural.
5. **Observabilidade obrigatória.** Contador OTel de supressão por consent e por block, por canal e
   categoria, para medir a taxa em shadow antes do cutover live. Uma alta inesperada de supressão é sinal
   de erro de configuração, não de sucesso silencioso.

## Consequências

- **Fica mais fácil:** opt-out e kill-switch passam a valer; conformidade LGPD no envio; base para a
  paridade de decisão do shadow.
- **Fica mais difícil:** o processor ganha consultas por entrega (block sempre, consent no caminho de
  eventos); o caminho direto passa a respeitar block, exigindo a asserção de regressão.

## Gatilho de revisão

Resposta da issue #38 (contrato do EasyStok), mudança na base legal de alguma categoria, ou extensão do
kill-switch para bloqueio por contato, que o ADR-019 alimenta via hard bounce e complaint.

## Itens de ação

1. [ ] `MarkSuppressed` no domínio (issue #33).
2. [ ] Block no processor, cobrindo o caminho direto (issue #36).
3. [ ] Consent no `EventFanout` via `ChannelResolver` (issue #37).
4. [ ] Confirmar contrato `RecipientUserId` do EasyStok (issue #38).
5. [ ] Contadores OTel de supressão (issues #36 e #37).
