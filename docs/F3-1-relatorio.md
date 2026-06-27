# F3 parte 1, relatório de fechamento

> Ledger de créditos append-only e cobrança na ingestão. Relatório de Definição de Pronto item a item, com evidências e desvios. Acompanha o plano em plans/F3-1-credit-ledger.md e a decisão em docs/adr/ADR-007-metering-ledger.md.

## Definição de pronto

| Item | Status | Evidência |
|---|---|---|
| ADR-007 aceita antes do código | Feito | `docs/adr/ADR-007-metering-ledger.md`, commit `docs: add adr-007 metering and credit ledger`. |
| Entidade `CreditLedgerEntry` append-only | Feito | `Hiram.Domain.Metering.CreditLedgerEntry`, valor com sinal, fábrica `Debit`. Testes: `CreditLedgerEntryTests`. |
| Cálculo de custo por canal e tamanho, configurável | Feito | Porta `ICreditCalculator`, `CreditRates` por configuração, `CreditCalculator` (`base + ceil(kb) * por_kb`, mínimo 1). Testes: `CreditCalculatorTests`. |
| Débito gravado na mesma transação que request e outbox | Feito | `INotificationStore.SaveAsync` recebe o lançamento e grava os três numa transação. Teste de integração `NotificationStoreTests` prova que o ledger entra junto e some junto no rollback. |
| Cobrança na aceitação, sem cobrar replay idempotente | Feito | `SubmitNotificationHandler` calcula o custo, monta o débito e grava na transação; replay devolve o id sem novo lançamento. Testes: `Submit_ChargesADebitForTheAcceptedNotification`, `Submit_WithKnownIdempotencyKey_DoesNotCharge`. |
| Build Release sem warning, suíte unit verde | Feito | Build Release `0 Aviso(s)`. 112 testes unitários verdes localmente. |
| Nenhuma biblioteca nova | Feito | Só configuração e EF Core. |

## Desvios e notas

### Cobra mas ainda nao bloqueia

Esta rodada cobra todo notificação aceita, mas nao impõe quota: nao há contador Redis nem 429 de quota estourada ainda. O bloqueio chega na próxima rodada da F3. O ledger já é a verdade contra a qual o contador rápido será reconciliado.

### Invariante estendido

O débito entra na mesma transação que a request e o outbox. O teste de rollback prova que, se a transação falha, nem a request, nem o outbox, nem o lançamento ficam. Notificação aceita é notificação cobrada, ou nada é gravado.

### Tamanho do payload

O custo usa o tamanho em bytes de subject mais body. A parcela de tokens de IA fica zero até a F4.

### Verificação local sem Docker

O modelo de ledger, o cálculo de custo e a cobrança no handler rodam por testes unitários locais. A atomicidade no banco é validada pela CI (`NotificationStoreTests` com Postgres).

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet run --project src/Hiram.Api
dotnet run --project src/Hiram.Dispatcher

curl -i -X POST http://localhost:3357/v1/notifications -H "X-Api-Key: hk_live_..." -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"ops@example.com","subject":"hi","body":"hello"}'

# no banco, a tabela notifications.credit_ledger tem um lançamento de débito ligado a esta notificação.
```

Esperado: cada notificação aceita gera uma linha de débito em `credit_ledger`, com `amount` negativo, na mesma transação da request e do outbox.

## Próximas rodadas da F3

Quota com contador Redis e 429, reconciliação assíncrona do contador contra a soma do ledger, rate limit, e API de uso.
