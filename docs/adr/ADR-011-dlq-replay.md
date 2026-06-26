# ADR-011: Dead letter e replay com store no Postgres, sobre o outbox

**Status:** Aceito
**Data:** 2026-06-26
**Decisores:** Felipe (arquiteto)

## Contexto

A F1 entregou retry de envio (Polly, 3 tentativas) mas deixou duas lacunas explícitas no código, anotadas como trabalho da F2:

- O `EmailConsumerWorker` faz `BasicNackAsync(requeue: false)` numa exceção, então uma mensagem envenenada é descartada e perdida.
- O `EmailNotificationProcessor`, ao esgotar as tentativas, marca a notificação `Failed`, o worker dá ack, e nao existe caminho de reprocessamento.

A máquina de estados do MASTER-PLAN prevê `failed (apos esgotar retries) -> dead_lettered`, estado que ainda nao existe. A ADR-005 (item de ação 2) delegou a esta ADR a estratégia de DLQ e replay. A pergunta que esta ADR responde: onde mora o morto, como se reprocessa, e como nada some em silêncio, sem trair a espinha do projeto (outbox e Postgres como fonte da verdade).

## Decisão

Dead letter com fonte da verdade no Postgres (tabela `dead_letter_messages`), replay que reenfileira escrevendo uma nova `OutboxMessage` na mesma transação, e uma parking lot fina no broker (dead letter exchange mais fila) só para mensagens verdadeiramente não parseáveis, para que nada seja descartado em silêncio. Novo estado `DeadLettered`. Replay e consulta sempre escopados por tenant.

## Opções consideradas

### Opção A: DLX nativo do broker com replay por shovel

Configurar `x-dead-letter-exchange` na fila de trabalho, ligar uma dead letter queue, e reprocessar movendo mensagens de volta ao exchange principal.

**Prós:** menos código de domínio, retry com delay nativo via TTL por mensagem mais DLX que reentrega vem quase de graça.
**Contras:** o RabbitMQ nao permite mutar argumentos de uma fila durável já declarada (406 PRECONDITION_FAILED), então adicionar o DLX à `hiram.notifications.email` existente exigiria versionar a fila e drenar o que estiver dentro; a mensagem morta no broker nao carrega `tenant_id` nem auditoria, e o replay vira um movimento cego, nao um ato de domínio observável. A capacidade que abrimos mão ao nao seguir A é justamente o retry com delay nativo, que está atado ao não-objetivo retry agendado e é o gatilho de revisão desta ADR.

### Opção B: store no Postgres mais replay via outbox, mais parking lot fina

O morto de domínio (entrega esgotada ou permanente) vira linha em `dead_letter_messages`. O replay reescreve uma `OutboxMessage` na mesma transação e o relay e o consumer existentes fazem o resto. A parking lot do broker recebe só o poison não parseável, por publish explícito no DLX feito pelo consumer, sem depender de argumento de fila.

**Prós:** reusa o invariante outbox, que é o ativo central do projeto; é por tenant, auditável, consultável e reprocessável por API sem tocar no broker; o publish explícito no DLX contorna a restrição de mutação de fila sem versionar nada; é a tese do artigo da fase, a DLQ como fila de replay e nao como lixeira.
**Contras:** há dois lugares de morte (banco para o domínio, fila fina para o poison), mantidos propositalmente mínimos; retém conteúdo em repouso (ver Consequências).

### Opção C: framework com retry agendado (MassTransit)

**Prós:** retry e scheduling prontos.
**Contras:** rejeitada na ADR-005, esconde exatamente o mecanismo que o projeto exibe.

## Análise de trade-off

O critério decisivo é o mesmo da ADR-005: demonstrar o padrão às claras e manter o Postgres como fonte da verdade. A opção B paga um custo de uma tabela e um endpoint, e em troca dá um morto auditável por tenant, replay operável por API e reuso total do relay. O custo da opção A (versionar fila, perder auditoria por tenant) compra uma capacidade (retry com delay) que esta fatia declara explicitamente fora de escopo. B vence.

## Decisões de borda cravadas

Endurecidas por revisão adversarial antes do código.

1. **Replay concorrente.** Dois POST de replay simultâneos da mesma notificação sao serializados por uma transição guardada no banco `DeadLettered -> Queued` via `ExecuteUpdateAsync` com filtro de status; zero linhas afetadas vira 409. Isso confina o controle de concorrência à borda do replay e nao altera o modelo at-least-once do processor. Um índice unique parcial garante no máximo uma dead letter aberta por notificação.
2. **Fonte do payload.** O replay reenvia o `Payload` armazenado na dead letter, que é fiel ao que foi tentado, e nao um re-render da notificação. A redundância morta de reconstruir o payload da notificação nao existe. O replay é email-shaped nesta fatia e generaliza depois pela coluna `Channel` já guardada.
3. **Poison vs transitório no consumer.** Só payload nulo ou invalido e not-found determinístico viram parking lot. O invariante outbox garante que a `NotificationRequest` existe quando a mensagem é consumida (request e outbox nascem na mesma transação, o relay lê após o commit), então not-found é poison, nao corrida de visibilidade. Falha transitória de infra (banco indisponível ao carregar) reenfileira com `requeue:true` e backoff curto. O hazard de spin-loop fica nomeado: enquanto a dependência estiver fora, há reentrega em laço; retry com delay verdadeiro é não-objetivo.
4. **Permanent vs transient.** `permanent_failure` termina em uma tentativa, porque o pipeline Polly da F1 só retenta transitório: `AttemptCount = 1`, reason `permanent_failure:{motivo}`. Transitório esgotado: `AttemptCount = 3`, reason `exhausted_transient:{ultimo motivo}`. A coluna `reason` é `varchar(256)` e o motivo do provider é truncado para caber.
5. **Raio de Failed.** O terminal do caminho vivo passa a ser `DeadLettered`. A métrica `hiram.notifications.failed` continua sendo emitida no momento da falha de entrega, para nenhuma métrica silenciar, e `hiram.notifications.dead_lettered` é somada no parking de domínio. O estado `Failed` deixa de ser produzido como terminal pelo caminho vivo, mas permanece no guard de settled do processor para inertizar linhas históricas da F1 e redelivery.
6. **Múltiplas dead letters.** O replay mira sempre a dead letter aberta (`replayed_at_utc IS NULL`), garantida única pelo índice parcial. O detalhe da notificação expõe a mais recente.
7. **Invariante do outbox.** A linha de outbox original está terminal (`processed_at_utc` setado) antes de qualquer replay, porque `DeadLettered` só é alcançado depois que o relay publicou e o worker deu ack. O relay nunca republica a original (filtro `processed_at_utc IS NULL`), então replay nao gera duplo envio pela original.
8. **Continuidade de tentativas.** `attempt_number` em `delivery_attempts` reinicia por ciclo de entrega; o `AttemptCount` da dead letter é por ciclo, nao cumulativo.
9. **Duplicata por late-success.** Um attempt que estourou o timeout mas cuja entrega de fato saiu, seguido de replay, gera envio duplo. É trade-off aceito da postura at-least-once de todo o sistema; dedup no provider ou no destinatário fica fora.
10. **Poison contado.** Estacionar incrementa `hiram.notifications.poisoned` e loga em Warning. O objetivo é nada sumir em silêncio.
11. **409 desambiguado.** O 409 do replay carrega código no corpo: `not_dead_lettered` quando a notificação nunca esteve dead lettered, `already_replayed` quando perdeu a corrida ou já foi reprocessada.
12. **PII em repouso.** As colunas `payload` e `reason` retêm conteúdo e possível PII em claro, consistente com `notification_requests.body` hoje. Retenção, purga e cifra ficam deferidas, com esta nota de rastreio, como decisão consciente e não-objetivo desta fatia.
13. **Armadilhas de EF.** O índice parcial usa `HasFilter` com o nome físico `"replayed_at_utc IS NULL"`. A coluna `reason` é `varchar(256)`. Enums sao persistidos por `HasConversion<string>()`. A transição guardada usa `ExecuteUpdateAsync`, que respeita a conversão de enum, sem SQL cru nem switch à mão.
14. **Topologia idêntica.** O dead letter exchange, a fila parking lot e o bind sao declarados no único site `RabbitMqConnection.DeclareTopologyAsync`, com argumentos idênticos para todos os declarantes, evitando 406 PRECONDITION_FAILED num segundo declare.
15. **Autorização do replay.** O replay reusa o auth de tenant do submit conscientemente, porque nao há sistema de escopos hoje. Um escopo distinto de replay fica deferido para quando existir granularidade de permissão.

## Consequências

- **Fica mais fácil:** inspecionar, consultar e reprocessar mortos por tenant; operar replay por API sem tocar no broker; manter o trace de ponta a ponta no reenvio, que reusa o caminho do outbox.
- **Fica mais difícil:** há dois lugares de morte, mantidos mínimos; o conteúdo fica em repouso em claro até uma fatia futura de retenção e cifra; o replay aceita duplicata at-least-once em casos de late-success.
- A F1 muda de terminal: `Failed` vivo vira `DeadLettered`, com a métrica `hiram.notifications.failed` preservada e os testes da F1 reconciliados.

## Gatilho de revisão

Necessidade real de retry agendado ou com delay (tentar de novo em N minutos), replay em lote por janela de tempo, ou volume de mortos que justifique infraestrutura de delay nativa do broker (TTL mais DLX). Qualquer um desses reabre a comparação com a opção A.

## Itens de ação

1. [ ] Estado `DeadLettered` e modelo `DeadLetterMessage` com tabela e índice parcial.
2. [ ] Processor dead-leta ao esgotar entrega; consumer classifica e estaciona o poison.
3. [ ] Use case e endpoint de replay sobre o outbox, escopado por tenant.
4. [ ] Métricas `dead_lettered`, `replayed`, `poisoned` e span de replay.
