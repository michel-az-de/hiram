# F7, canal WhatsApp via Cloud API

> Plano executável no estilo de plans/F0, F1, F2-1, F2-2 e F2-3. Regras do CLAUDE.md. Um passo por vez (WIP=1), commit por pathspec, teste junto do código. Branch padrão: main, branch curta quando o passo for arriscado. Em nenhum texto use travessão (em dash). Decisão estrutural na ADR-023, aberta antes do código.

## Sequenciamento

WhatsApp é adapter pós-F6 no MASTER-PLAN. Planejar agora não fura a fila; executar respeita o WIP=1 e
entra quando o go-live (issues #1 a #20) abrir espaço. Este plano é autocontido: o consent do WhatsApp
é fiado aqui, sem depender da frente de enforcement geral (#28 a #41).

## Objetivo

O terceiro canal de entrega, depois de email (F1) e push (F2-3), em Nível 1: template outbound com
status loop. Resultado demonstrável:

1. O tenant configura o provider WhatsApp (phone number id, WABA id, token) e registra HSM templates com
   o estado de aprovação da Meta, escopados a ele.
2. Uma notificação com `channel = whatsapp` aponta um HSM aprovado e seus parâmetros. Sem opt-in do
   destinatário, é suprimida na ingestão com registro auditável, inclusive em transacional.
3. O envio chama a Cloud API, grava o `wamid`, classifica erros da Meta em transiente e permanente, e
   reusa outbox, dead-letter, poison, shadow e replay, sem tocar email nem push.
4. O callback da Meta (sent, delivered, read, failed) casa por `wamid` e move um estado de entrega
   derivado, idempotente e tolerante a fora de ordem.
5. O metering cobra por categoria de conversa, reserva na ingestão e reconcilia no callback.
6. Build Release sem warning, suíte inteira verde, CI verde.

## Decisões técnicas fixas

Detalhe e trade-off na ADR-023 (docs/adr/ADR-023-canal-whatsapp-cloud-api.md):

- Cloud API direta da Meta atrás da porta `IWhatsAppProvider`, que espelha `IEmailProvider`. BSP fica
  deferido como segunda implementação atrás da mesma porta.
- HSM modelado em `WhatsAppTemplate`, separado do `Template` Scriban. O corpo não é renderizado no
  submit; a Meta renderiza a partir de parâmetros posicionais.
- Consent obrigatório em toda categoria no WhatsApp, fiado via `ChannelResolver` como fundação, com
  default consciente do canal em `ConsentPolicy`.
- `wamid` gravado como `provider_message_id` no `DeliveryAttempt`, estado de entrega derivado do ADR-019
  com `read` como estado novo.
- Metering por categoria de conversa, `ICreditCalculator` estendido, reserva na ingestão e reconciliação
  no callback, coerente com o ADR-007.
- Callback recebido por endpoint no `Hiram.Api`, evento no outbox, consumer no Dispatcher. Host de
  webhooks dedicado fica deferido.
- Pipeline de resiliência próprio do canal, não o singleton compartilhado por email, push e webhook.

## Passos

0. **ADR-023** (`docs: add adr-023 whatsapp cloud api channel`). Feito.
1. **Canal e template HSM** (`feat: add whatsapp channel and hsm template model`). `NotificationChannel.WhatsApp`, entidade `WhatsAppTemplate` com nome Meta, language, categoria, estado de aprovação e mapa de `data` nomeado para parâmetros posicionais. Guardas de `ParseChannel` e `RoutingKeyFor` para whatsapp. Testes unitários do modelo e das guardas.
2. **Consent obrigatório, política do canal** (`feat(app): deny whatsapp without an explicit opt-in`). Default consciente do canal em `ConsentPolicy`: sem registro de opt-in, WhatsApp nega em toda categoria, divergindo do interesse legítimo de email e push, que ficam inalterados. Testes unitários do default e de regressão. A fiação do `ChannelResolver` no caminho, a supressão com `MarkSuppressed`, o registro auditável da decisão e a remoção do #37 da allowlist dependem de um call site real e andam com a ingestão (passo 7), quando o caminho existir.
3. **Persistência de template e config** (`feat: persist whatsapp templates and provider config`). Tabela `whatsapp_templates` com `tenant_id`, migration nova, `IWhatsAppTemplateStore` e store. `TenantProviderConfig` aceitando o canal WhatsApp. Teste de integração.
4. **CRUD de template e provider** (`feat: expose whatsapp template and provider endpoints`). Endpoints para registrar HSM e configurar o provider, token protegido por Data Protection. Testes.
5. **Adapter Cloud API** (`feat: send whatsapp through the meta cloud api`). Porta `IWhatsAppProvider`, `WhatsAppMessage`, `WhatsAppCloudProvider` chamando a Graph API `/messages`, classificando erro da Meta em `SendOutcome` e capturando o `wamid`. `WhatsAppProviderResolver` no molde de `EmailProviderResolver`. Testes locais com handler HTTP fake, no molde do Resend.
6. **Pipeline de envio** (`feat: deliver whatsapp notifications through the dispatcher`). Fila e routing key em `HiramTopology`, `WhatsAppConsumerWorker` no molde de `PushConsumerWorker`, `WhatsAppNotificationProcessor` no molde de `PushNotificationProcessor` (claim atômico, `DeliveryAttempt` com `wamid`, dead-letter, poison, shadow) com pipeline Polly próprio e gate final de consent. DI e wiring do dispatcher. Testes do processor no molde de `PushDeliveryPipelineTests`. Ao fiar o fan-out, fazer o `EventFanout` lançar ou registrar um canal não fiado em vez de descartá-lo em silêncio, que é o comportamento de hoje (só trata email) e já afeta push.
7. **Ingestão do canal** (`feat: accept whatsapp notifications`). `channel = whatsapp` no submit e no replay, resolução do HSM sem render no submit (monta parâmetros posicionais a partir do `data`), débito reservado por categoria. Fiar aqui o `ChannelResolver` (consent mais block) na ingestão do WhatsApp, com supressão via `MarkSuppressed` e registro auditável da decisão, e remover o #37 da allowlist do `DependencyInjectionOrphanTests`, que o `Allowlist_HasNoStaleEntries` passa a exigir. Testes. Ao aceitar o canal, garantir que a rejeição de um canal ainda não fiado não deixe claim de idempotência órfão no Redis, já que o `RoutingKeyFor` lança antes do release do claim.
8. **Metering por categoria** (`feat: meter whatsapp by conversation category`). `ICreditCalculator` estendido para custo por categoria de conversa, `CreditRates` com as categorias do WhatsApp, reserva na ingestão. Testes de cálculo.
9. **Callback de status** (`feat: ingest whatsapp status callbacks`). Endpoint no `Hiram.Api` com verify por `hub.challenge` e validação `X-Hub-Signature-256`, evento de status no outbox, `WhatsAppStatusConsumer` casando por `wamid`, estado de entrega derivado idempotente por precedência (`read > delivered > sent`, `failed` terminal), `failed` alimentando alerta e o kill-switch por contato do ADR-024 quando existir, reconciliação de crédito pela categoria reportada. Testes de fora de ordem e idempotência.
10. **Auditoria e fechamento** (`feat: audit whatsapp consent and status trail` e `docs: document f7 whatsapp`). Trilha auditável da decisão de consent no envio e das transições de estado de entrega, exposta em `GET /v1/notifications/{id}`. E2E full-stack pelo dispatcher com Meta stub, no molde de `EmailDeliveryEndToEndTests`. README, relatório e fechamento deste plano.

## Definição de pronto

Os cinco pontos do DoD do CLAUDE.md em cada passo, mais o checklist completo em docs/F7-relatorio.md, a
criar no fechamento. Gate final: submit de template WhatsApp, trace do POST ao consumer, callback movendo
o estado para delivered e depois read, supressão por falta de opt-in registrada, e o ledger batendo com a
categoria de conversa.

## Não-objetivos e deferidos

Inbound de mensagens, janela de sessão de 24h, opt-out por STOP recebido, interativas, listas, flows, e
mídia rica além do header de template. Embedded Signup do WABA. BSP como segunda implementação atrás da
porta. Kill-switch por contato (ADR-024). Unificação do pipeline de entrega num `IChannelSender`, gatilho
apontado no F2-3. Reabrem por revisão da ADR-023 ou pelas fases próprias.
