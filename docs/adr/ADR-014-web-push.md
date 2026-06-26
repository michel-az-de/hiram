# ADR-014: Web Push com VAPID, assinatura por id e caminho de entrega próprio

**Status:** Aceito
**Data:** 2026-06-26
**Decisores:** Felipe (arquiteto)

## Contexto

A F2 prevê Web Push. Até aqui o único canal é email, com `IEmailProvider`, `EmailNotificationProcessor`, `EmailConsumerWorker` e a fila `hiram.notifications.email`. Web Push é um segundo canal com mecânica própria: o navegador do usuário final cria uma subscription (endpoint mais chaves `p256dh` e `auth`), e o servidor envia um payload cifrado (aes128gcm) assinado por VAPID para o endpoint do push service. Web Push traz biblioteca nova, então a escolha e o desenho ficam registrados aqui.

Três perguntas: qual biblioteca, como uma notificação aponta para uma subscription, e como encaixar a entrega sem desestabilizar o email.

## Decisão

Biblioteca WebPush (web-push-libs) para cifra e assinatura VAPID. VAPID no nível da plataforma, com par de chaves em configuração. Uma subscription do navegador é registrada por tenant e recebe um id; uma notificação de push aponta para ela pelo id no campo `recipient`. A entrega de push é um caminho próprio (fila, consumer e processor de push), separado do email, reusando a infraestrutura de dead letter, poison e parking lot já existente. O caminho de email não é tocado.

## Opções consideradas

### Biblioteca

- **WebPush (web-push-libs/web-push-csharp), escolhida.** Implementa a cifra aes128gcm e a assinatura VAPID, é a referência da comunidade .NET, API direta (`SendNotificationAsync(subscription, payload, vapidDetails)`), gera par VAPID por `VapidHelper`.
- Implementar a cifra e o VAPID à mão: rejeitado, criptografia própria é risco sem ganho.
- Serviço gerenciado (FCM, OneSignal): rejeitado, contraria a operação self-hosted de custo mínimo e o lock-in que o projeto evita.

### Como apontar para a subscription

- **Por id no `recipient`, escolhida.** O tenant registra a subscription, recebe um id e envia a notificação com `channel = push` e `recipient` igual ao id. Simples, escopado, sem fan-out.
- Por id lógico de usuário com fan-out para todas as subscriptions dele: deferido, traz multiplicidade de entrega e dedup que não cabem na primeira fatia.

### Onde encaixar a entrega

- **Caminho próprio de push, reusando dead letter e parking lot, com o email intocado. Escolhida.** Critério decisivo: mudanças no pipeline de entrega só são validáveis na CI, sem Docker no ambiente de desenvolvimento, então tocar o `EmailNotificationProcessor` que já funciona é risco desproporcional. Push ganha consumer e processor próprios; a orquestração comum (retry, attempts, dead letter, poison, shadow) é repetida de forma deliberada.
- Generalizar agora um `IChannelSender` e refatorar o email para usá-lo: é a direção certa a prazo e o CLAUDE.md diz que a segunda implementação justifica a interface, mas a refatoração do caminho de email sem poder testá-la localmente arrisca a entrega que já está verde. Fica deferido para uma rodada de unificação com teste adequado, registrado como dívida nesta ADR.

## Decisões de borda cravadas

1. **VAPID de plataforma.** Par de chaves e subject (mailto) em configuração via user-secrets no dev e variável de ambiente em produção. O `GET /v1/push/vapid-public-key` devolve a chave pública para o frontend do tenant assinar. VAPID por tenant fica deferido, análogo ao provider config de email.
2. **Subscription por tenant.** `push_subscriptions` com `endpoint`, `p256dh` e `auth`, escopada ao tenant. Registro, listagem e remoção. Sem rotação automática.
3. **Recipient é o id da subscription.** No submit com `channel = push`, `recipient` é o id. O processor resolve a subscription escopada ao tenant; subscription inexistente é poison determinístico, igual ao not-found do email, porque o id não vai resolver num retry.
4. **Classificação de outcome.** 404 e 410 do push service significam subscription morta, logo falha permanente; 429, 5xx e timeout são transitórios. Mesma máquina de retry e dead letter do email.
5. **Conteúdo renderizado no submit.** Vale o mesmo da ADR-013: o payload de push é montado no submit e persistido; o processor trata `subject` e `body` opacos. Templates de push são canal futuro, não cobertos aqui.
6. **Shadow mode.** Tenant em shadow registra `shadow_would_send` para push também, sem tocar o push service, para manter paridade entre canais.
7. **Sem poda automática de subscription.** Uma subscription que devolve 404 ou 410 vira dead letter; remover a subscription morta do banco fica deferido com nota de rastreio.
8. **PII e segredo em repouso.** `endpoint`, `p256dh` e `auth` são tokens de entrega, não segredo de plataforma, e ficam em claro como o `body` hoje. A chave privada VAPID é segredo e nunca vai para o banco nem para log.

## Consequências

- **Fica mais fácil:** push entra reusando outbox, dead letter, replay e parking lot; o email continua estável; o tenant opera subscriptions por API.
- **Fica mais difícil:** há duplicação consciente entre o processor de email e o de push, que uma rodada de unificação deve resolver; sem poda, subscriptions mortas acumulam dead letters até a limpeza manual.

## Gatilho de revisão

Terceiro canal de entrega (SMS ou WhatsApp), que torna a duplicação cara o suficiente para justificar a unificação num `IChannelSender`. Ou necessidade de VAPID por tenant, fan-out por usuário, ou poda automática de subscription morta.

## Itens de ação

1. [ ] Canal `Push` e entidade `PushSubscription` com schema próprio.
2. [ ] CRUD de subscriptions e exposição da chave pública VAPID.
3. [ ] Porta `IPushSender` e adapter WebPush com VAPID de plataforma.
4. [ ] Fila, consumer e processor de push reusando dead letter, poison e shadow.
5. [ ] Submit com `channel = push` apontando para a subscription por id.
