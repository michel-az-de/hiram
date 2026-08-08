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
- A conta Twilio disponivel e trial, autenticada por API Key, com 30 dias de validade. O remetente de
  SMS e WhatsApp e um numero emprestado pela Twilio, nao um numero comprado, e por isso nao aparece em
  `IncomingPhoneNumbers`. O email nao passa pelo SendGrid: a Twilio Email API responde em
  `https://comms.twilio.com/v1/Emails` com a mesma credencial da conta.
- Em trial, os tres canais aceitam somente conteudo pre-aprovado. Isso foi verificado contra a API, nao
  apenas na documentacao, e esta registrado na secao de verificacao empirica deste ADR.

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
6. **Ordem das fatias.** Email pela Twilio Email API primeiro, SMS depois, WhatsApp por ultimo. A
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
3. **Email pela Twilio Email API, nao pelo SendGrid.** O canal usa `POST https://comms.twilio.com/v1/Emails`
   com a mesma credencial de conta dos demais canais, o que dispensa criar conta e chave de SendGrid. A
   API exige `from.address`, `to[].address`, `content.subject` e `content.html`, responde de forma
   assincrona e devolve um identificador de operacao, que e o `provider_message_id` deste canal.
4. **O adapter `twilio-email` nao substitui SMTP nem Resend.** Entra como terceiro `IEmailProvider`,
   escolhido por tenant. Nenhum tenant existente muda de provider por causa deste ADR.
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
9. **Assinatura por provider, sem reuso do `WebhookSignature`.** O `X-Twilio-Signature` do Messaging e
   HMAC-SHA1 em base64 sobre a URL concatenada com os parametros do formulario ordenados, e nao e o
   HMAC-SHA256 sobre corpo JSON que o Hiram usa nos webhooks de saida. O esquema do callback de status
   da Email API sera confirmado na fatia correspondente e nao e presumido igual ao do Messaging.
   Assinatura invalida responde 401 sem processar.
10. **Rota de callback fora de `/v1`.** O provider nao carrega `X-Api-Key`. Abrir excecao dentro do
    prefixo protegido enfraqueceria o middleware para toda a superficie; a rota nasce fora dele, com a
    assinatura como unica autenticacao.
11. **Pipeline de resiliencia por canal.** Limites de rate e classificacao de erro diferem entre a
    Email API e o Messaging, e o WhatsApp ainda impoe espacamento proprio entre mensagens. Cada canal
    constroi seu pipeline, no molde do `EmailDeliveryPipeline`.
12. **Testes sem rede no gate.** O CI mantem o padrao do repo, stub de `HttpMessageHandler`, sem
    credencial. Verificacao contra sandbox real e local, com user-secrets, e nunca condiciona o merge.
13. **Modo trial e configuracao de tenant, nao variavel de ambiente.** Enquanto a conta for trial, o
    conteudo enviado ao provider nao e o corpo da notificacao, e sim um conteudo pre-aprovado. Isso vive
    em `tenant_provider_configs.settings`, com `trial_mode` e a chave do template, porque o Hiram e
    multi-tenant e uma variavel global decidiria por todos os tenants ao mesmo tempo. Sair do trial passa
    a ser uma atualizacao de configuracao, sem deploy. O corpo real continua persistido em
    `notification_requests`, e o `DeliveryAttempt` registra que o envio ocorreu em modo trial, para que o
    historico nao afirme ter entregue um texto que nunca saiu.
14. **Evidencia de status e limitada no trial.** Uma mensagem aceita retorna identificador, mas a
    consulta individual responde 403 e a listagem da conta nao a exibe. Logo, no trial o estado de
    entrega nao pode depender de consulta ao provider; o callback e o unico caminho, e o produto nao
    deve nascer com um pollador que so funciona em conta paga.
15. **Verify e OTP ficam fora.** O Hiram entrega notificacao, nao gera nem valida codigo de verificacao.
    O produto Verify da Twilio, indisponivel no trial, nao entra neste ADR nem em fatia derivada dele.
16. **Antecipacao consciente do gate de 30 dias.** O plano do Hiram Core condiciona escopo novo a 30
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

#### Opcao A: Twilio Email API como terceiro adapter (escolhida)

**Pros:** usa a credencial que a conta ja tem, sem conta nem chave adicionais, mantem SMTP e Resend
intactos, e a mesma API continua valendo depois do upgrade, entao o adapter nao e descartavel.
**Contras:** no trial o remetente e fixo e o conteudo e pre-aprovado, entao a fatia comprova o caminho
de entrega, nao o conteudo.

#### Opcao B: SendGrid com conta e chave proprias

**Pros:** superficie madura, com modo de validacao que nao entrega e nao consome cota.
**Contras:** exige criar conta, verificar remetente e administrar uma segunda credencial, para um
resultado que a Twilio Email API ja entrega com a credencial existente. Rejeitada.

#### Opcao C: manter o email fora do Twilio

**Pros:** nenhuma superficie nova.
**Contras:** deixa a primeira fatia sem caminho de menor risco e joga a estreia da arquitetura nova
direto no canal com mais regra de compliance. Rejeitada.

## Verificacao empirica em 2026-08-06

As bordas acima nao vieram so da documentacao. Foram medidas contra a API da conta de trial, e o que
foi medido diverge do que o levantamento inicial do console afirmava.

| Verificacao | Resultado |
|---|---|
| SMS com `Body=sms_2fa` para o numero verificado | aceito, `queued`. A Twilio expandiu a chave no texto canonico do template, o corpo devolvido nao e a chave |
| Numero remetente em `IncomingPhoneNumbers` | lista vazia. O remetente de trial nao e um numero provisionado na conta |
| Email com assunto e HTML livres | rejeitado com `400 Invalid template: email content does not match any approved template` |
| Email com a chave do template no corpo, em seis variacoes | todas rejeitadas. A validacao compara o conteudo em si, nao aceita identificador |
| `content.subject` e `content.html` ausentes | rejeitado como parametro obrigatorio, mesmo em trial |
| Consulta da mensagem aceita, por identificador | `403 Forbidden` |
| Listagem de mensagens da conta apos o envio | vazia |

Consequencia direta: o trial comprova o caminho de entrega, aceite, persistencia, outbox, tentativa e
classificacao de erro, e nao comprova conteudo nem estado de entrega. O conteudo exato aprovado para
email so e visivel no console autenticado, entao a fatia de email depende desse dado ser fornecido pelo
operador, e o adapter o trata como configuracao, nunca como constante no codigo.

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
- o trial impoe conteudo pre-aprovado nos tres canais, entao a evidencia de ponta a ponta comprova o
  caminho e nao o conteudo, e a comprovacao completa depende do upgrade da conta;
- o modo trial e um caminho a mais no adapter, que precisa morrer quando a conta for paga, sob risco de
  virar codigo morto permanente.

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

1. [x] Extrair a mecanica de entrega do `EmailNotificationProcessor` para um processor de canal
   generico, sem mudanca de comportamento observavel.
2. [x] Adapter `twilio-email` como terceiro `IEmailProvider`, contra a Twilio Email API, com testes de
   stub, classificacao de erro e conteudo aprovado vindo de configuracao.
2.1 [x] Modo trial na configuracao de provider por tenant, com registro no `DeliveryAttempt`.
3. [x] `subject` nulo: migration, ajuste da entidade e validacao por canal no endpoint.
4. [x] Canal SMS: valor no enum, chave de roteamento, `ISmsProvider`, resolver, adapter Twilio
   Messages, normalizacao E.164 e configuracao por tenant.
5. [ ] Rota de callback de status fora de `/v1`, com verificacao de assinatura por provider.
6. [ ] Estado de entrega derivado, idempotente por `(provider, message_sid, status)`.
7. [ ] `ConsentPolicy` no caminho direto de `POST /v1/notifications`.
8. [x] Canal WhatsApp pelo sandbox do Twilio, com template de conteudo e consentimento obrigatorio.
9. [x] Documentar onboarding de credencial por tenant no runbook de operacao.

## Criterio de conclusao

Uma notificacao submetida em cada um dos tres canais e aceita, persistida, entregue pelo Twilio,
correlacionada pelo callback de status e visivel no detalhe da notificacao com a tentativa e o estado
derivado. Build Release e suite completa verdes, sem credencial no repositorio e sem teste de rede no
gate de merge.

## Adendo de 2026-08-08: o que a onda entregou e o que ficou

Esta e a segunda onda do ADR, depois da fatia de email que entrou pelos PRs #104, #106 e #109. Ela
fechou os canais de mensagem e o elo operacional, e deixou o status loop para depois. O criterio de
conclusao acima **nao esta cumprido**: falta a parte do callback e do estado derivado.

### Entregue

- **SMS no fan-out.** `PhoneNumber` centraliza a regra E.164, `EventFanout` renderiza e grava a
  requisicao de SMS na mesma transacao do outbox, e template, rotina e consentimento passaram a aceitar
  o canal. Itens 3 e 4. PRs #116 e #118.
- **Canal WhatsApp pelo sandbox.** Porta `IWhatsAppProvider`, adapter `twilio-whatsapp`, resolver por
  tenant e fan-out. O prefixo `whatsapp:` do endereco vive so no adapter, entao a requisicao, o fan-out e
  a regra de E.164 guardam o numero puro. Item 8. PR #120.
- **`trial_content` na tentativa.** O adapter informa quando o que saiu foi o conteudo pre-aprovado, e o
  `DeliveryAttempt` grava isso, para que o historico nao afirme ter entregue um texto que nunca saiu.
  Item 2.1. PR #122.
- **Onboarding de credencial no runbook.** Secao 3 do `docs/operations-runbook.md`: o que vai em
  `settings` e o que vai no `secret`, a dependencia do key ring, os limites do trial, o sandbox do
  WhatsApp, rotacao e o roteiro de smoke manual, que nao entra no gate de CI. Item 9. PR #124.
- **Provisionamento multicanal do tenant da Jornada.** `deploy/jornada/provision.sh` ganhou o subcomando
  `providers` e passou a tratar `JORNADA_CHANNELS` de verdade. PRs #113 e #124.

Nenhum destes PRs estava mergeado quando este adendo foi escrito: os itens estao marcados como
concluidos porque o trabalho existe e foi verificado de ponta a ponta, e a fila de merge e o que resta.

### Adiado, com a razao

- **Callback de status e estado derivado (itens 5 e 6).** Exige uma rota publica fora de `/v1`, com um
  terceiro esquema de assinatura no repositorio, e o trial nao oferece caminho alternativo: a consulta
  individual responde 403 e a listagem vem vazia, entao nao da nem para conferir o resultado por
  polling enquanto a rota nao existe. Fazer a rota agora seria endurecer superficie publica sem
  conseguir comprovar o outro lado. Volta quando a conta sair do trial ou quando houver ambiente com
  entrada publica.
- **`ConsentPolicy` no caminho direto (item 7).** `POST /v1/notifications` nao carrega `userId` nem
  `category`, que sao os dois argumentos da politica. Aplica-la ali exigiria acrescentar os dois campos
  ao contrato publico, e isso muda o contrato de todo emissor existente, incluindo o EasyStok. E uma
  decisao de contrato, nao um detalhe de implementacao, e merece a sua propria fatia. O gate continua
  valendo no fan-out, que e por onde a Jornada emite.
- **Pipeline de resiliencia por canal (borda 11).** Cada canal continua no pipeline comum. Separar os
  limites hoje seria tuning sem medicao: nao ha volume real de SMS nem de WhatsApp para dizer qual
  limite esta errado. O gatilho e o primeiro `429` recorrente ou o espacamento exigido pelo WhatsApp
  aparecer em producao.
- **Dead letter para evento sem rotina.** Hoje o evento sem rotina e logado, contado em
  `hiram.events.no_route` e acked. Transformar isso em dead letter mudaria a semantica do ack para os
  tenants que ja emitem tipos nao roteados de proposito, e o contador ja torna o caso visivel em painel.
  Fica como decisao consciente, nao como esquecimento (ver issue #32).
