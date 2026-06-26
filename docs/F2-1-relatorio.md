# F2 parte 1, relatório de fechamento

> Dead letter e replay. Relatório de Definição de Pronto item a item, com evidências e desvios reportados. Acompanha o plano em plans/F2-1-dead-letter-replay.md e a decisão em docs/adr/ADR-011-dlq-replay.md.

## Definição de pronto

| Item | Status | Evidência |
|---|---|---|
| ADR-011 aceita antes do código, 15 decisões cravadas | Feito | `docs/adr/ADR-011-dlq-replay.md`, commit `docs: add adr-011 dead letter and replay strategy`. Item de ação 2 da ADR-005 marcado. |
| Estado `DeadLettered`, modelo e tabela com migration nova e unique parcial de dead letter aberta | Feito | `NotificationStatus.DeadLettered`, `DeadLetterMessage`, migration `AddDeadLetterMessages`, índice `ux_dead_letter_messages_open` com `HasFilter("replayed_at_utc IS NULL")`. Testes: `DeadLetterMessageTests`, `DeadLetterPersistenceTests`. |
| Concorrência de replay coberta por teste, envio exatamente uma vez (T1) | Feito | Transição guardada por `ExecuteUpdateAsync` em `DeadLetterReplay`. Teste `DeadLetterReplayTests.ConcurrentReplay_WritesOneOutbox_AndSecondConflicts`. |
| permanent vs transient com `AttemptCount` cravado e testado (T2) | Feito | `permanent_failure` em 1 tentativa, `exhausted_transient` em 3. Testes `EmailDeliveryPipelineTests.Process_DeadLettersWithoutRetry_WhenPermanent` e `..._AfterThreeAttempts_WhenAlwaysTransient`. |
| Fronteira poison vs transitório documentada e testada (T3, T6) | Feito | `PoisonMessageException` e classificação no `EmailConsumerWorker`. Testes `Process_ThrowsPoison_WhenNotificationMissing`, `Process_ThrowsPoison_WhenPayloadIsEmpty`, `Process_ThrowsNonPoison_WhenDatabaseUnreachable`, e `EmailDeliveryEndToEndTests.PoisonMessage_LandsInDeadLetterParkingLot`. |
| Raio de `Failed -> DeadLettered` reconciliado, nenhuma métrica silencia | Feito | Terminal vivo agora `DeadLettered`; `hiram.notifications.failed` ainda emitido na exaustão; guard de settled inclui `Failed` para linhas históricas; três testes citados atualizados (ver Desvios). |
| `POST /replay` reenfileira via outbox, escopado por tenant, 409 codificado, 404 cross-tenant (T4, T5) | Feito | `POST /v1/notifications/{id}/replay`, códigos `not_dead_lettered` e `already_replayed`. Testes `DeadLetterReplayTests` (`Replay_WritesExactlyOneOutbox...`, `Replay_Conflicts_WhenNotDeadLettered`, `Replay_NotFound_ForOtherTenant`, `Replay_TargetsOpenDeadLetter_AcrossCycles`) e o e2e `Replay_RedeliversDeadLetteredNotification_ToMailpit`. |
| Consulta por `dead_lettered` e detalhe com a razão | Feito | `ParseStatus` aceita `dead_lettered`; `NotificationDetailResponse.DeadLetter`. Testes `NotificationQueryTests.List_FiltersByDeadLettered` e `Detail_IncludesDeadLetter_WhenPresent`. |
| `hiram.notifications.poisoned`, `dead_lettered`, `replayed` e span `replay notification` | Feito | Counters em `HiramDiagnostics`, span na rota de replay. Ver Desvios sobre a distribuição entre passos. |
| Nenhuma biblioteca nova, nenhuma migration aplicada alterada, build Release sem warning, suíte unit verde | Feito | Sem pacote novo. Migration nova `AddDeadLetterMessages`. Build Release `0 Aviso(s)`. 78 testes unitários verdes localmente. |
| Toda decisão de borda como CRAVADO na ADR-011 ou PARK | Feito | Seções Decisões de borda cravadas e Gatilho de revisão na ADR-011; PARK no plano. |
| Histórico legível, um passo por commit, por pathspec, sem rodapé de IA | Feito | Oito commits por pathspec, um por passo, mensagens conventional sem rodapé. |

## Desvios e notas

### Verificação local sem Docker

O ambiente de execução não tinha o Docker disponível, então a suíte de integração com Testcontainers não rodou localmente, mesma situação relatada na F1. O gate local de cada passo foi build Release sem warning (que compila os testes de integração) mais os 78 testes unitários verdes. Os testes de integração e ponta a ponta novos seguem os padrões existentes (`OutboxRelayTests`, `EmailDeliveryEndToEndTests`, `TenancySchemaTests`) e são validados pela CI no GitHub Actions, que roda contra o Docker do runner. Confirmar a run verde após o push antes de tratar a fatia como fechada.

### Reconciliação de Failed (cravado 5)

O terminal do caminho vivo passou de `Failed` para `DeadLettered`. Para nenhuma métrica silenciar, `hiram.notifications.failed` continua sendo incrementado no momento da falha de entrega, e `hiram.notifications.dead_lettered` foi somado. O guard de idempotência do processor passou a `Sent or Failed or DeadLettered` para inertizar linhas históricas da F1. Três testes que esperavam o terminal `Failed` foram atualizados: `EmailDeliveryPipelineTests` (dois asserts) e o e2e renomeado `PermanentFailure_DeadLetters_WithSinglePermanentAttempt`.

### Telemetria distribuída entre passos

Os três counters foram definidos no passo onde cada um é emitido pela primeira vez (`dead_lettered` no Passo 4, `poisoned` no Passo 5, `replayed` no Passo 6) e o span `replay notification` entrou junto da rota de replay, em vez de um commit de telemetria separado. O meter `Hiram.Notifications` e o source `Hiram.Messaging` já são registrados por nome, então os instrumentos novos fluem para o OTLP sem registro adicional. O resultado é o mesmo do plano, com commits mais coesos.

### PII em repouso (cravado 12, PARK)

As colunas `dead_letter_messages.payload` e `reason` retêm conteúdo e possível PII em claro, consistente com `notification_requests.body` hoje. Retenção, purga e cifra ficam deferidas com nota de rastreio na ADR-011.

## Verificação manual de referência

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet user-secrets set "Hiram:AdminKey" "admin-dev-local" --project src/Hiram.Api
dotnet run --project src/Hiram.Api
dotnet run --project src/Hiram.Dispatcher

# tenant com SMTP inalcançável força dead letter, depois reprocessa pelo provider de plataforma:
curl -i -X POST http://localhost:3357/v1/notifications/<id>/replay -H "X-Api-Key: hk_live_..."
curl -s "http://localhost:3357/v1/notifications?status=dead_lettered" -H "X-Api-Key: hk_live_..."
curl -s http://localhost:3357/v1/notifications/<id> -H "X-Api-Key: hk_live_..."
```

Esperado: a notificação que esgota entrega fica `dead_lettered` com a razão no detalhe; o replay devolve 202 `queued` e o GET seguinte mostra `sent`; uma mensagem não parseável aparece na fila `hiram.notifications.dead-letter` no RabbitMQ management.
