# ADR-028: Integracao Twilio como provider multicanal, email, SMS e WhatsApp

**Status:** Aceito
**Data:** 2026-08-06
**Decisores:** Felipe (arquiteto)

## Contexto

O ADR-027 reduziu o produto a um gateway interno enxuto: um host, PostgreSQL e providers externos na
ultima milha. Email e push ficaram no nucleo, o WhatsApp incompleto saiu, e o proprio ADR-027 cravou o
gatilho de reabertura de canal: projeto consumidor, owner operacional e criterio de aceite definidos.

Existe agora demanda de enviar notificacoes por email, SMS e WhatsApp usando o Twilio como provider
unico de ultima milha, com credencial ja provisionada. Este ADR abre a decisao antes do codigo, como
exige o CLAUDE.md, e satisfaz o gatilho do ADR-027.

### Estado medido antes da decisao

- `NotificationChannel` tem `Email`, `Push` e `WhatsApp`, persistido como texto de ate 32 caracteres,
  entao um valor novo nao exige migration de schema.
- A tabela `whatsapp_templates` existe dormente desde a migration `20260713181535`, sem entidade,
  provider ou processor.
- A porta de envio e `IEmailProvider`, com dois adapters, SMTP e Resend, resolvidos por tenant em
  `tenant_provider_configs` com segredo protegido por Data Protection.
- O `EmailNotificationProcessor` concentra a mecanica que nao e especifica de email: claim atomico da
  linha, kill-switch por bloqueio, modo shadow, registro de `DeliveryAttempt`, classificacao de falha,
  dead-letter e enfileiramento do webhook de status.
- `DeliveryAttempt` ja grava `provider_message_id`, o gancho de correlacao previsto pelo ADR-019.
- Nao existe rota de entrada para callback de provider, e o `ApiKeyMiddleware` exige `X-Api-Key` em
  todo caminho sob `/v1`.
- `ConsentPolicy` nega WhatsApp sem opt-in explicito, mas so e chamada pelo fan-out de eventos, nunca
  pelo `POST /v1/notifications` direto.
- `subject` e obrigatorio na entidade e `NOT NULL` na coluna, o que nao corresponde a SMS nem a
  WhatsApp.
- A conta Twilio disponivel e trial, autenticada por API Key, sem numero comprado e sem messaging
  service. O SendGrid, produto de email do Twilio, tem conta e chave proprias, ainda inexistentes.

## Decisao

O Twilio entra como provider de ultima milha em tres canais, atras de portas tipadas por canal, e a
mecanica comum de entrega deixa de ser propriedade do processor de email.

1. **Portas por canal.** `IEmailProvider` permanece, e nascem `ISmsProvider` e `IWhatsAppProvider`,
   cada uma com sua mensagem, seu resolver por tenant e seu pipeline de resiliencia.
2. **Processor de canal generico.** A mecanica hoje presa ao `EmailNotificationProcessor`, claim,
   bloqueio, shadow, tentativa, dead-letter e webhook, e extraida para um processor de canal
   reutilizavel. Cada canal contribui apenas com a montagem da mensagem e a chamada ao adapter.
3. **`subject` opcional.** Migration nova torna a coluna nula, e a obrigatoriedade passa a ser
   validada por canal: exigida em email, ausente em SMS e WhatsApp.
4. **Callback de status.** Uma rota publica fora de `/v1`, autenticada pela assinatura do proprio
   provider, correlaciona o evento pelo `provider_message_id` e alimenta um estado de entrega derivado,
   separado de `NotificationStatus`.
5. **Consentimento no caminho direto.** `ConsentPolicy` passa a ser consultada tambem no
   `POST /v1/notifications`, fechando a lacuna que hoje deixa o gate valer so no fan-out.
6. **Ordem das fatias.** Email por SendGrid primeiro, SMS depois, WhatsApp sandbox por ultimo. A
   primeira fatia valida credencial, DI e testes sem tocar em estrutura; a segunda estreia a porta
   nova; a terceira carrega o peso de consent, template e janela.

## Decisoes de borda cravadas

1. **Portas por canal, nao `IChannelSender` unificado.** O payload de cada canal diverge de verdade,
   assunto e corpo no email, corpo unico e origem em SMS, template com parametros posicionais no
   WhatsApp. A unificacao ficaria em um contrato polimorfico especulativo. O reuso real esta na
   mecanica de entrega, e e ela que e extraida.
2. **Credencial.** O Twilio autentica por API Key, `SK` como usuario e o secret como senha, sobre o
   `AccountSid` que compoe a URL. `AccountSid` e identificadores nao secretos vao para o `settings`
   jsonb de `tenant_provider_configs`; o secret vai para o campo protegido por Data Protection. Nenhum
   valor aparece em codigo, log ou arquivo versionado. No desenvolvimento a origem e user-secrets.
3. **SendGrid e conta separada.** O email por Twilio usa a API do SendGrid, com chave propria e
   remetente verificado, nao o `AccountSid`. Isso e um adapter distinto, nao uma variacao do adapter de
   SMS.
4. **SendGrid nao substitui SMTP nem Resend.** Entra como terceiro `IEmailProvider`, escolhido por
   tenant. Nenhum tenant existente muda de provider por causa deste ADR.
5. **Escopo Nivel 1, sem inbound.** Nenhum canal recebe mensagem nesta fatia. Sem opt-out automatico
   por STOP, sem janela de sessao de 24h tratada pelo produto, sem interativas. O opt-out continua
   sendo o registro explicito de consentimento pela API.
6. **Consent do WhatsApp permanece fail-closed.** Ausencia de registro nega o envio em qualquer
   categoria, inclusive transacional, como ja implementado. A novidade e que a politica passa a valer
   no caminho direto, e nao apenas no fan-out.
7. **Estado de entrega derivado.** `NotificationStatus` termina em `Sent`, que significa aceito pelo
   provider. Os estados reportados pelo provider, `delivered`, `undelivered`, `read` e `failed`, vivem
   em um eixo proprio, com precedencia `read > delivered > sent` e `failed` terminal por erro.
8. **Idempotencia do callback.** A chave e `(provider, message_sid, status)`. O callback tolera
   duplicata e chegada fora de ordem. Evento sem correspondencia local vira dead-letter com alerta,
   nunca aceitar e descartar.
9. **Assinatura por provider, sem reuso do `WebhookSignature`.** O `X-Twilio-Signature` e HMAC-SHA1 em
   base64 sobre a URL concatenada com os parametros do formulario ordenados; o Event Webhook do
   SendGrid usa ECDSA. Nenhum dos dois e o HMAC-SHA256 sobre corpo JSON que o Hiram usa nos webhooks de
   saida. Cada verificacao e propria, e assinatura invalida responde 401 sem processar.
10. **Rota de callback fora de `/v1`.** O provider nao carrega `X-Api-Key`. Abrir excecao dentro do
    prefixo protegido enfraqueceria o middleware para toda a superficie; a rota nasce fora dele, com a
    assinatura como unica autenticacao.
11. **Pipeline de resiliencia por canal.** Limites de rate e classificacao de erro diferem entre
    SendGrid e Messaging. Cada canal constroi seu pipeline, no molde do `EmailDeliveryPipeline`.
12. **Testes sem rede no gate.** O CI mantem o padrao do repo, stub de `HttpMessageHandler`, sem
    credencial. Verificacao contra sandbox real e local, com user-secrets, e nunca condiciona o merge.
13. **Limite do ambiente atual.** A conta e trial, sem numero e sem messaging service, e nao existe
    conta SendGrid. Logo, cada fatia entrega adapter e teste deterministico, e a evidencia ponta a ponta
    fica registrada na issue quando a credencial correspondente existir.
14. **Antecipacao consciente do gate de 30 dias.** O plano do Hiram Core condiciona escopo novo a 30
    dias de operacao real, ainda em curso. Esta integracao antecipa esse gate por decisao explicita, e
    a pendencia permanece aberta no plano, nao e considerada cumprida.

## Alternativas consideradas

### Porta de envio

#### Opcao A: processor generico com portas por canal (escolhida)

**Pros:** a logica de caminho critico existe uma vez so, cada canal novo custa um adapter e uma
montagem de mensagem, e a fronteira por canal mantem o contrato honesto sobre o que cada provider
aceita.
**Contras:** exige um refactor cirurgico no processor de email antes do primeiro canal novo, com o
risco concentrado no caminho de entrega em producao.

#### Opcao B: espelhar o email por canal

**Pros:** primeira fatia mais rapida, nenhum refactor no que ja funciona.
**Contras:** triplica claim, tentativa, dead-letter e webhook, e o drift entre copias e questao de
tempo. Rejeitada.

#### Opcao C: `IChannelSender` unificado

**Pros:** fronteira unica, um so ponto de extensao.
**Contras:** contrato polimorfico definido antes de conhecer as tres formas reais de mensagem, e o
refactor mais amplo possivel do caminho critico antes de qualquer envio novo. Rejeitada por
especulacao.

### Tratamento do `subject`

#### Opcao A: coluna nula, obrigatoriedade por canal (escolhida)

**Pros:** modela a realidade, email continua exigindo assunto, SMS e WhatsApp deixam de carregar um
campo que nao existe no canal.
**Contras:** migration em tabela do caminho critico e leitura defensiva em consultas e projecoes.

#### Opcao B: assunto sintetico

**Pros:** nenhuma migration.
**Contras:** grava um valor que mente sobre o que e, vaza para listagem, detalhe e webhooks, e
contamina qualquer consumidor futuro. Rejeitada.

### Integracao com o WhatsApp

#### Opcao A: Twilio como BSP (escolhida agora)

**Pros:** o sandbox permite validar o canal sem WABA proprio, sem onboarding manual e sem aprovacao
previa de template, e reusa a mesma credencial do SMS.
**Contras:** markup por mensagem e dependencia de intermediario. A Cloud API direta, que o ADR-023
escolhia, continua sendo caminho valido no futuro, atras da mesma porta.

#### Opcao B: Cloud API direta da Meta

**Pros:** custo menor, controle total.
**Contras:** exige WABA, onboarding manual e ciclo de aprovacao proprio antes de qualquer teste, o que
inviabiliza a validacao imediata. Adiada, nao descartada.

### Email pelo Twilio

#### Opcao A: SendGrid como terceiro adapter (escolhida)

**Pros:** valida a integracao com o menor risco estrutural, e mantem SMTP e Resend intactos.
**Contras:** depende de conta e verificacao de remetente que ainda nao existem.

#### Opcao B: manter o email fora do Twilio

**Pros:** nenhuma conta nova.
**Contras:** deixa a primeira fatia sem caminho de menor risco e joga a estreia da arquitetura nova
direto no canal com mais regra de compliance. Rejeitada.

## Consequencias

### Positivas

- a mecanica de entrega passa a ter um dono unico, e canal novo deixa de custar um processor inteiro;
- o status loop fecha pela primeira vez, com evidencia do que o provider realmente entregou;
- o consentimento deixa de valer so no fan-out e passa a proteger o caminho direto;
- o item de acao 1 do ADR-019 sai do papel.

### Negativas

- uma rota publica a mais para endurecer, com tres esquemas de assinatura diferentes no repositorio;
- o refactor toca o caminho critico de entrega antes de entregar valor visivel;
- `subject` nulo exige leitura defensiva em todo consumidor da tabela;
- dependencia de conta trial, que limita a evidencia ponta a ponta ate haver numero, join code e chave
  do SendGrid.

## Limites e gatilhos de revisao

Rever a escolha do Twilio como BSP de WhatsApp quando o volume tornar o markup relevante, ou quando um
WABA proprio existir. Rever a decisao de portas por canal quando houver um quarto canal cujo payload
seja identico a um existente. Rever o escopo Nivel 1 quando houver demanda concreta de inbound,
opt-out por STOP ou janela de sessao, que reabrem este ADR.

## ADRs afetados

- **ADR-018**, consentimento: estendido, o gate passa a valer no caminho direto de submissao.
- **ADR-019**, callbacks de provider: parcialmente realizado, o item de correlacao por
  `provider_message_id` e o estado derivado saem do papel.
- **ADR-023**, WhatsApp por Cloud API: permanece supersedido. Este ADR nao o revive, redefine o canal
  sobre outro provider e com escopo menor.
- **ADR-027**, Hiram Core: alterado no ponto em que lista o WhatsApp como fora do produto. SMS e
  WhatsApp voltam ao escopo pelo gatilho previsto no proprio ADR-027, com owner e criterio de aceite
  definidos aqui.

## Itens de acao

1. [ ] Extrair a mecanica de entrega do `EmailNotificationProcessor` para um processor de canal
   generico, sem mudanca de comportamento observavel.
2. [ ] Adapter SendGrid como terceiro `IEmailProvider`, com testes de stub e classificacao de erro.
3. [ ] `subject` nulo: migration, ajuste da entidade e validacao por canal no endpoint.
4. [ ] Canal SMS: valor no enum, chave de roteamento, `ISmsProvider`, resolver, adapter Twilio
   Messages, normalizacao E.164 e configuracao por tenant.
5. [ ] Rota de callback de status fora de `/v1`, com verificacao de assinatura por provider.
6. [ ] Estado de entrega derivado, idempotente por `(provider, message_sid, status)`.
7. [ ] `ConsentPolicy` no caminho direto de `POST /v1/notifications`.
8. [ ] Canal WhatsApp pelo sandbox do Twilio, com template de conteudo e consentimento obrigatorio.
9. [ ] Documentar onboarding de credencial por tenant no runbook de operacao.

## Criterio de conclusao

Uma notificacao submetida em cada um dos tres canais e aceita, persistida, entregue pelo Twilio,
correlacionada pelo callback de status e visivel no detalhe da notificacao com a tentativa e o estado
derivado. Build Release e suite completa verdes, sem credencial no repositorio e sem teste de rede no
gate de merge.
