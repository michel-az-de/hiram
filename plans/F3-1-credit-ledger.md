# F3 parte 1, ledger de créditos e cobrança na ingestão

> Plano executável no estilo das fases anteriores. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Branch padrão: main. Em nenhum texto use travessão (em dash). Decisão estrutural na ADR-007, aberta antes do código.

## Objetivo

A fatia fundadora da F3: o metering. Resultado demonstrável:

1. Toda notificação aceita gera um lançamento de débito em créditos, na mesma transação que a request e o outbox.
2. O custo é calculado do canal e do tamanho do payload, configurável.
3. Replay idempotente nao cobra de novo; rollback nao deixa lançamento órfão.
4. Build Release sem warning, suíte verde, CI verde.

Sequenciamento da F3: **parte 1 ledger e cobrança (esta)**, depois quota com contador Redis e 429, depois reconciliação e rate limit, depois API de uso.

## Decisões técnicas fixas

Detalhe e trade-off na ADR-007 (docs/adr/ADR-007-metering-ledger.md):

- Ledger append-only no Postgres, saldo por soma, valor com sinal.
- Débito atômico com request e outbox, estendendo o invariante fundador.
- Custo na aceitação: `base_do_canal + ceil(payload_kb) * por_kb`, mínimo 1, taxas por configuração. Tokens de IA na F4.
- Enforcement de quota, 429, reconciliação e API de uso ficam para as próximas rodadas.

## Passos

1. **ADR-007** (`docs: add adr-007 metering and credit ledger`).
2. **Domínio** (`feat: add credit ledger entry model`). `CreditLedgerEntry` append-only com fábrica `Debit`. Testes unitários.
3. **Cálculo de custo** (`feat: calculate notification credit cost`). `ICreditCalculator`, `CreditRates` por configuração, `CreditCalculator`, `AddHiramMetering`. Testes unitários.
4. **Persistência e cobrança atômica** (`feat: charge credits atomically when a notification is accepted`). Tabela `credit_ledger`, config, migration; `INotificationStore.SaveAsync` grava o débito junto; `SubmitNotificationHandler` calcula e cobra; sem cobrar replay. Testes de integração e unitários.
5. **Fechamento** (`docs: document f3 part one credit ledger`). README, relatório e este plano.

## Definição de pronto

Ver checklist completo em docs/F3-1-relatorio.md.

## Não-objetivos e deferidos

Enforcement de quota e 429. Contador Redis. Reconciliação assíncrona. Rate limit. API de uso. Pacotes de crédito, recarga e multi-moeda. Tokens de IA no custo (F4). Reabrem pela ADR-007 ou pelas próximas rodadas da F3.
