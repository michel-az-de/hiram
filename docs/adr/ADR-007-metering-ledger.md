# ADR-007: Metering por ledger append-only, cobrado na aceitação

**Status:** Aceito
**Data:** 2026-06-27
**Decisores:** Felipe (arquiteto)

## Contexto

A F3 cobra por créditos. O invariante fundador do projeto é gravar `NotificationRequest` e `OutboxMessage` na mesma transação para nunca perder uma notificação aceita. O metering estende esse invariante: uma notificação aceita tem que ser cobrada de forma atômica com a aceitação, senão o sistema entrega sem cobrar ou cobra sem entregar. O MASTER-PLAN define o custo como `base_do_canal + ceil(payload_kb) * custo_por_kb + tokens_ia * custo_por_token`, ledger append-only no Postgres, fast-path de quota em Redis e reconciliação assíncrona. A direção da ADR-007 no backlog é exatamente essa.

## Decisão

Ledger de créditos append-only no Postgres (`credit_ledger`). Cada notificação aceita grava um lançamento de débito na mesma transação que a request e o outbox. O custo é calculado na aceitação a partir do canal e do tamanho do payload. O saldo é a soma dos lançamentos. Esta rodada entrega o ledger e a cobrança na ingestão. O enforcement de quota com contador Redis e resposta 429, a reconciliação e a API de uso ficam para as rodadas seguintes da F3.

## Opções consideradas

### Opção A: ledger append-only, saldo por soma

**Prós:** auditável, sem débito duplo, cada cobrança é um fato imutável; casa com o invariante transacional; explicável em artigo (a tese da fase é o ledger append-only na prática).
**Contras:** o saldo é uma agregação; em volume alto pode exigir snapshot de saldo.

### Opção B: coluna de saldo mutável no tenant

**Prós:** ler saldo é O(1).
**Contras:** update concorrente sem o histórico perde a auditoria e abre corrida de débito; contraria o modelo append-only.

### Opção C: sistema de billing externo

**Prós:** pronto.
**Contras:** custo e lock-in, contra a operação self-hosted de custo mínimo; o ledger é ativo de portfolio, não detalhe a terceirizar.

## Decisões de borda cravadas

1. **Cobrança atômica.** O lançamento de débito entra na mesma transação que a request e o outbox, estendendo o invariante fundador: notificação aceita é notificação cobrada, ou nada é gravado.
2. **Custo na aceitação.** O custo é calculado no submit a partir do canal e do tamanho do payload (`base + ceil(payload_kb) * por_kb`). Os tokens de IA entram na F4; por ora a parcela de IA é zero.
3. **Sem cobrança em replay idempotente.** Uma requisição repetida pela mesma `Idempotency-Key` devolve o id original sem novo lançamento, porque a original já cobrou. A violação de unique no Postgres faz a transação inteira voltar, então nao há débito órfão.
4. **Valor com sinal.** O lançamento tem `amount` com sinal: débito negativo, crédito (recarga) positivo. O saldo é a soma. Lançamento é imutável, sem update.
5. **Taxas por configuração.** Base por canal e custo por KB vêm de configuração, com defaults sensatos, para ajustar preço sem deploy de código.
6. **Enforcement e reconciliação deferidos.** O contador Redis de quota, o 429 de quota estourada, a reconciliação assíncrona contra a soma do ledger e a API de uso sao as próximas rodadas da F3, nao esta.

## Consequências

- **Fica mais fácil:** auditar cobrança lançamento a lançamento; reconciliar o contador rápido contra a verdade do ledger; explicar o modelo.
- **Fica mais difícil:** o saldo é uma soma, que em escala vai pedir snapshot; cobrar sem quota ainda (esta rodada cobra mas nao bloqueia), o bloqueio chega na rodada de quota.

## Gatilho de revisão

Volume que torne a soma do saldo cara o suficiente para exigir saldo materializado, necessidade de pacotes de crédito ou multi-moeda, ou metering que precise de mais dimensões que canal e tamanho.

## Itens de ação

1. [ ] Entidade `CreditLedgerEntry` append-only e schema `credit_ledger`.
2. [ ] Cálculo de custo por canal e tamanho, configurável.
3. [ ] Gravar o débito na mesma transação que a request e o outbox.
4. [ ] Quota com contador Redis, 429, reconciliação e API de uso nas próximas rodadas.
