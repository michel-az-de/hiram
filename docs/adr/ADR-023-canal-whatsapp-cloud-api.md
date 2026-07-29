# ADR-023: Canal WhatsApp via Cloud API direta, template outbound com status loop

**Status:** Supersedido pelo ADR-027
**Data:** 2026-07-13
**Decisores:** Felipe (arquiteto)

## Contexto

O MASTER-PLAN sempre tratou WhatsApp como adapter pós-F6, com a arquitetura nascendo pronta para
plugar, mas sem ADR. Este registro abre a decisão antes do código, como exige o CLAUDE.md. O produto
já tem multitenancy, resolução de provider por tenant, outbox transacional, dead-letter com replay,
webhooks de status de saída e ledger de créditos. WhatsApp é o terceiro canal de entrega, depois de
email (F1) e push (F2-3), e reusa esse caminho, não o reinventa.

Três atritos são o valor real desta fatia, porque nenhum deles é resolvido por copiar o canal push:

1. **Consent não é fiado no caminho real.** `ChannelResolver`, `ConsentPolicy` e `BlockGate` estão no DI
   e testados, mas nenhum é chamado na ingestão ou no envio (documentado em
   `DependencyInjectionOrphanTests`, allowlist item #37). Enviar template pelo WhatsApp sem opt-in viola
   a política de mensagens da Meta e a LGPD, então o canal não pode nascer plugado num gate morto.
2. **Metering por conversa, não por payload.** O `CreditCalculator` cobra `base_do_canal + KB` na
   ingestão. O WhatsApp cobra por categoria de conversa, e o custo real só é conhecido no envio ou no
   callback da Meta. O modelo por KB não deriva esse custo.
3. **HSM não é Scriban.** O `Template` atual é texto livre renderizado no submit (ADR-013). O HSM da Meta
   é pré-aprovado, com parâmetros posicionais, e quem renderiza é a Meta. O `Approved` local não é o
   approval da Meta.

Escopo desta fatia, cravado em Nível 1: template outbound mais webhook de status. Sem inbound de
mensagens, sem janela de sessão de 24h, sem interativas ou flows. A escolha de integração é Cloud API
direta da Meta, com porta abstrata para um BSP entrar depois como segunda implementação.

## Decisão

O canal WhatsApp entra como novo valor de `NotificationChannel`, atrás de uma porta `IWhatsAppProvider`
que espelha `IEmailProvider`, com um único adapter nesta fatia: a Cloud API da Meta (Graph API
`/messages`). A configuração vive por tenant em `TenantProviderConfig` do canal WhatsApp. O template HSM
é modelado numa entidade nova, separada do `Template` Scriban. O consent é fiado no caminho do WhatsApp
via `ChannelResolver` como primeiro passo, sem depender da frente de enforcement geral (#28 a #41). O
status loop realiza o ADR-019 para este canal: grava o `wamid` no `DeliveryAttempt`, recebe o callback
assinado da Meta e aplica uma máquina de estado de entrega derivada. O metering estima o custo por
categoria de conversa na ingestão e reconcilia pela categoria reportada no callback.

## Opções consideradas

### Integração com o WhatsApp

#### Opção A: Cloud API direta, porta abstrata (escolhida)

| Dimensão | Avaliação |
|---|---|
| Complexidade | Média, onboarding do WABA é manual nesta fatia |
| Custo | Só a conversa da Meta, sem markup |
| Escala | Boa, limite por número da Meta, sem intermediário |
| Aderência ao projeto | Alta, casa com custo mínimo e abstração de provider própria do MASTER-PLAN |

**Prós:** controle total, custo mínimo, o adapter atrás de `IWhatsAppProvider` deixa um BSP entrar como
segunda implementação sem tocar o pipeline, exatamente como SMTP e Resend coexistem no email.
**Contras:** compliance e onboarding do WABA ficam do lado do Hiram, aprovação de template é assíncrona
na Meta.

#### Opção B: BSP agregador (Twilio, 360dialog, Infobip, Zenvia)

**Prós:** onboarding e aprovação de template mais simples, suporte gerenciado.
**Contras:** markup por mensagem, lock-in, menos valor de portfolio. Rejeitada como implementação
primária, mantida como segunda implementação futura atrás da mesma porta.

#### Opção C: multi-provider desde o dia um

**Prós:** anti lock-in máximo.
**Contras:** dobra a superfície de teste e onboarding no primeiro passo, e a segunda implementação é que
justifica a interface, não a primeira (CLAUDE.md). Rejeitada por especulação.

### Modelagem do template HSM

#### Opção A: entidade `WhatsAppTemplate` nova (escolhida)

**Prós:** modela o ciclo de aprovação assíncrono da Meta e os parâmetros posicionais sem poluir o
`Template` Scriban nem quebrar o invariante de renderizar no submit.
**Contras:** superfície nova de persistência e CRUD.

#### Opção B: estender `Template` com campos nullable da Meta

**Prós:** menos entidades.
**Contras:** mistura dois modelos opostos, força o `render no submit` a ser condicional por canal, e o
`Approved` local passa a significar duas coisas. Rejeitada.

### Recepção do callback de status

#### Opção A: endpoint no `Hiram.Api`, evento no outbox, consumer no Dispatcher (escolhida)

**Prós:** reusa outbox e o padrão de consumer, endurece um endpoint só, sem host novo.
**Contras:** o `Hiram.Api` ganha uma rota pública a mais para proteger.

#### Opção B: host `Hiram.Webhooks` dedicado

**Prós:** separação de deploy, previsto no MASTER-PLAN.
**Contras:** um host inteiro por um endpoint é over-engineering agora. Deferido até haver volume ou um
segundo consumidor de callbacks. Rejeitada por YAGNI.

### Enforcement de consent

Escolhida a opção de fiar o `ChannelResolver` no caminho do WhatsApp como Passo 0 desta fatia, tornando
o canal seguro sem depender do sequenciamento das issues #28 a #41. Rejeitadas: depender daquela frente
(cria acoplamento entre workstreams) e checar só no envio (debita crédito antes de conhecer o consent e
não suprime cedo).

## Decisões de borda cravadas

1. **Escopo Nível 1.** Só template outbound (categorias utility, authentication, marketing) mais webhook
   de status. Inbound de mensagens, janela de sessão de 24h, opt-out por STAP recebido, interativas e
   flows ficam deferidos, reabrem por revisão deste ADR. O opt-out por STOP recebido depende de inbound,
   que é Nível 2.
2. **Consent obrigatório em toda categoria.** No WhatsApp, ausência de registro de opt-in nega o envio,
   inclusive transacional e operacional. Isso diverge do default de interesse legítimo que email e push
   usam em `ConsentPolicy`. A divergência é resolvida por um default consciente do canal, não por burlar
   a política. Marketing continua exigindo opt-in explícito em todos os canais.
3. **Template HSM.** `WhatsAppTemplate` guarda nome do template na Meta, language code, categoria e
   estado de aprovação da Meta (pending, approved, rejected, paused, disabled), mais o mapeamento de
   `data` nomeado para parâmetros posicionais. O corpo não é renderizado no submit; a Meta renderiza. O
   fan-out por rotina só dispara com template no estado approved, análogo ao `TemplateApprovalLookup`.
4. **Identidade da entrega.** Ao chamar a Cloud API, grava-se o `wamid` retornado como
   `provider_message_id` no `DeliveryAttempt`. Isso realiza o item de ação 1 do ADR-019, hoje pendente.
   Callback sem correspondência vira dead-letter mais alerta, nunca accept-and-drop.
5. **Estado de entrega derivado.** Reusa a máquina do ADR-019, separada do `NotificationStatus`. Os
   estados da Meta mapeiam para `sent`, `delivered`, `read` e `failed`. A precedência do eixo de sucesso
   é `read > delivered > sent`, `failed` é terminal por erro. `read` é o estado novo que o ADR-019 ainda
   não modelava. Idempotente por `(provider, wamid, status)`, tolera fora de ordem e duplicata.
6. **Metering por categoria.** `ICreditCalculator` passa a aceitar a categoria de conversa do WhatsApp.
   A ingestão reserva o custo estimado pela categoria do template; o callback reconcilia pela categoria e
   pricing que a Meta reporta. Append-only e reconciliação assíncrona, coerente com o ADR-007. Falha de
   envio que vira dead-letter não estorna, mesma regra dos outros canais.
7. **Efeito colateral de failed.** Códigos de erro da Meta que indicam bloqueio pelo usuário ou queda de
   qualidade alimentam o kill-switch por contato do ADR-024 quando ele existir. Enquanto não existe, o
   `failed` é registrado no estado derivado e alertado, sem bloquear silenciosamente.
8. **Assinatura do callback.** O endpoint valida o handshake por `hub.challenge` no verify e a assinatura
   `X-Hub-Signature-256` (HMAC SHA256 com o app secret) em cada evento, reusando o padrão de assinatura
   dos webhooks. Assinatura inválida responde 401 e não é processada.
9. **Pipeline de resiliência próprio.** O canal usa um `ResiliencePipeline` próprio, não o singleton
   compartilhado por email, push e webhook, porque os limites de rate e a classificação de erro da Cloud
   API diferem. Erros da Meta são classificados em `SendOutcome` transiente ou permanente pelo adapter.
10. **Onboarding manual do WABA.** Nesta fatia o tenant informa phone number id, WABA id e access token,
    com o token protegido por Data Protection como os demais secrets. Embedded Signup fica deferido.

## Consequências

- **Fica mais fácil:** terceiro canal de entrega no ar, status loop real fechado pela primeira vez
  (delivered e read, não só aceito pelo provider), consent finalmente fiado ao menos no caminho do
  WhatsApp, e a base do ADR-019 saindo do papel.
- **Fica mais difícil:** um endpoint público a mais para endurecer, dependência do ciclo de aprovação
  assíncrono da Meta, metering que deixa de ser função pura do payload, e a pressão pela unificação do
  pipeline num `IChannelSender` que o F2-3 já apontava como gatilho no terceiro canal.

## Gatilho de revisão

Entrada de um BSP como segunda implementação (vira multi-provider e a precedência de status pode virar
por provider), subida de escopo para Nível 2 (inbound, sessão de 24h, opt-out por STOP), ou volume que
justifique Embedded Signup e host de webhooks dedicado.

## Itens de ação

1. [ ] Fiar `ChannelResolver` (consent mais block) no caminho do WhatsApp e remover o item #37 da
   allowlist do `DependencyInjectionOrphanTests`.
2. [ ] `NotificationChannel.WhatsApp`, entidade `WhatsAppTemplate` e migration com `tenant_id`.
3. [ ] Porta `IWhatsAppProvider`, `WhatsAppProviderResolver` e adapter `WhatsAppCloudProvider`.
4. [ ] Config por tenant e onboarding manual do WABA.
5. [ ] Fila, `WhatsAppConsumerWorker` e `WhatsAppNotificationProcessor` com `wamid` no `DeliveryAttempt`.
6. [ ] Ingestão do canal com resolução de HSM sem render no submit.
7. [ ] Metering por categoria de conversa.
8. [ ] Endpoint de callback da Meta e estado de entrega derivado com `read`.
9. [ ] Trilha de auditoria da decisão de consent e das transições de status.
