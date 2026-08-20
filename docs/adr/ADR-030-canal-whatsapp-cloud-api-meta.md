# ADR-030: Canal WhatsApp pela Cloud API da Meta, como segunda implementação da porta

**Status:** Aceito
**Data:** 2026-08-20
**Decisores:** Felipe (arquiteto)

## Contexto

O ADR-028 escolheu a Twilio como BSP de WhatsApp por um motivo explícito e datado: o sandbox permitia
validar o canal sem WABA própria, sem onboarding manual e sem ciclo de aprovação de template. A Cloud API
direta foi registrada ali como adiada, não descartada, e a mesma porta foi desenhada para receber a
segunda implementação.

A premissa mudou. A conta Twilio está com impedimentos operacionais e existe um número novo disponível
para vincular diretamente à Meta. O gatilho de revisão que o próprio ADR-028 cravou, "quando um WABA
próprio existir", está a poucos passos de onboarding.

O ADR-023 escolhia a Cloud API direta em 2026-07-13 e está supersedido pelo ADR-027. Revivê-lo seria
errado: ele foi escrito antes do processor de canal genérico, antes das portas por canal, antes do
`ProviderNames` e antes do simulador. As decisões dele sobre metering por conversa e sobre enforcement de
consent como passo zero não correspondem mais ao produto. Este ADR o supersede e decide de novo, com a
arquitetura de hoje.

### Estado medido em 2026-08-20

- A porta `IWhatsAppProvider`, o `WhatsAppChannelDelivery` que resolve provider por tenant e o
  `ChannelDeliveryProcessor` genérico existem e estão em produção. Um adapter novo não custa processor,
  claim, tentativa, dead letter nem webhook.
- `WhatsAppMessage` é `(Recipient, Body)`, texto livre. Isso descreve o que a Twilio aceita dentro da
  janela do sandbox e **não** descreve o que a Cloud API aceita fora da janela de 24 horas, que é template
  pré-aprovado com parâmetros posicionais.
- A tabela `whatsapp_templates` existe desde a migration `20260713181535`, com `meta_name`, `language`,
  `category`, `status` e `parameters` jsonb. Não tem entidade, store nem uso. Foi criada para a Cloud API
  e ficou dormente quando o ADR-027 tirou o WhatsApp do escopo.
- `ConsentPolicy` nega WhatsApp sem opt-in explícito em qualquer categoria, inclusive transacional.
- Não existe rota de callback de provider. Os itens 5 e 6 do ADR-028, rota de status e estado de entrega
  derivado, estão adiados desde 2026-08-08 com razão registrada: o trial da Twilio responde 403 na
  consulta individual e devolve listagem vazia, então não havia como comprovar o outro lado.
- `ProviderNames`, `ProviderEndpoints` por configuração, um `HttpClient` nomeado por adapter e o
  `TwilioErrorPolicy` que classifica por código estão nos PRs #140 e #142, ainda não mergeados em `main`.
  Este ADR depende deles e não os antecipa.

### O que a Cloud API muda, e o que não muda

Não muda o transporte. É um POST JSON em `graph.facebook.com`, e o repositório já tem `IHttpClientFactory`,
pipeline de resiliência e classificação de erro por adapter.

Muda o contrato do canal. A Meta só entrega texto livre dentro de uma janela de 24 horas aberta por uma
mensagem do destinatário. Fora dela, e é onde toda notificação transacional do Hiram vive, só sai template
aprovado pela Meta, identificado por nome e idioma, com os valores em posições numeradas. O `Body` que o
fan-out renderiza hoje não tem para onde ir nesse formato.

Muda também o que o produto consegue provar. A Meta entrega webhook de status com `sent`, `delivered`,
`read` e `failed`, mais a categoria e o preço cobrado, sem custo adicional e sem depender de conta paga.
O que estava bloqueado no ADR-028 por falta de contraparte passa a ter contraparte.

## Decisão

A Cloud API da Meta entra como **segunda implementação** de `IWhatsAppProvider`, com o nome estável
`meta-whatsapp`, ao lado de `twilio-whatsapp` e não no lugar dela. A escolha continua sendo por tenant, na
coluna `provider` de `tenant_provider_configs`, exatamente como SMTP, Resend e Twilio coexistem no email.

1. **Adapter próprio, zero dependência nova.** `MetaWhatsAppProvider` sobre `HttpClient`, no molde do
   `TwilioWhatsAppProvider`, mais um `MetaErrorPolicy` no molde do `TwilioErrorPolicy`.
2. **`WhatsAppMessage` passa a descrever as duas formas de mensagem**, corpo livre e template com
   parâmetros posicionais. O adapter que não aceita uma delas recusa com falha permanente nomeada, em vez
   de fingir que enviou.
3. **`whatsapp_templates` sai da dormência.** A entidade nasce sobre a tabela que já existe, sem migration
   nova. Quem renderiza o template é a Meta, não o Scriban.
4. **A versão da Graph API é configuração**, com padrão em `ProviderEndpoints`, nunca constante compilada.
5. **O webhook de status realiza os itens 5 e 6 do ADR-028**, em rota fora de `/v1`, autenticada pela
   assinatura da própria Meta.
6. **Ordem das fatias por risco crescente**, com o canal provado contra o simulador antes de qualquer
   dependência de onboarding.

## Alternativas consideradas

### Como falar com a Cloud API

#### Opção A: adapter próprio com `HttpClient` (escolhida)

| Dimensão | Avaliação |
|---|---|
| Custo de código | Cerca de 150 linhas, no molde de um adapter que já existe |
| Dependências novas | Nenhuma |
| Aderência ao repo | Total: `ProviderNames`, `ClientFor`, `SendOutcome`, pipeline de resiliência |

**Prós:** a classificação de erro, que é o valor real do adapter, fica sob controle do repositório. Nenhuma
biblioteca decide por nós o que é transiente.
**Contras:** mudança de contrato da Meta chega como manutenção nossa, não como bump de pacote.

#### Opção B: `WhatsappBusiness.CloudApi` (gabrieldwight)

Medido em 2026-08-20: versão 1.0.101 de 20/07/2026, 563,8 mil downloads, 461 estrelas, licença MIT,
multi-target de net472 e netstandard2.0 até net10.

**Prós:** cobertura ampla, inclusive mídia, interativas, flows e um helper de assinatura de webhook.
Manutenção ativa e recente.
**Contras:** dois deles vieram da leitura do código, não do README, e são fatais para um gateway
multi-tenant. O endereço base é estado estático de processo,
`public static Uri BaseAddress { get; private set; } = new Uri("https://graph.facebook.com/v25.0/")`,
então a versão da Graph API passaria a ser global e não por tenant, que é a mesma classe de falha da issue
#139. E `WhatsAppBusinessClientFactory.Create(config)` devolve `new WhatsAppBusinessClient(config)` a cada
chamada, sem `IHttpClientFactory`, o que num caminho que resolve credencial por tenant a cada envio esgota
socket. Somam-se a isso o `Polly` próprio, redundante com o pipeline do repo, o multi-target até net472
impondo um denominador comum abaixo do que o repo usa, e a superfície de erro por exceção
(`WhatsappBusinessCloudAPIException`), quando o `SendOutcome` existe justamente para não decidir retry
dentro de um `catch`. Rejeitada como dependência e **adotada como referência de leitura** para o cálculo
de assinatura e o mapa de erro.

#### Opção C: SDK gerado (`apimatic/whatsapp-dotnet-sdk`)

Medido em 2026-08-20: 5 estrelas, 3 commits, versão padrão da Graph API `v13.0`.

**Prós:** cobertura derivada do OpenAPI da Meta.
**Contras:** abandonado, e o padrão treze versões atrás do que a Meta serve hoje. Rejeitada.

#### Opção D: bibliotecas sobre WhatsApp Web (Baileys, WAHA)

**Rejeitada por política, não por técnica.** São engenharia reversa do cliente, violam os termos da Meta e
custam o número.

#### Opção E: Azure Communication Services Advanced Messaging

A alternativa mais séria que a pesquisa encontrou, e a única com código pronto de qualidade industrial.
SDK .NET mantido pela Microsoft, conecta uma WABA existente ou cria uma nova, entrega relatório de `sent`,
`delivered` e `read` por Event Grid, e já documenta o suporte a BSUID.

**Prós:** muito menos código próprio, SDK mantido por terceiro com SLA, e a conta Azure já existe.
**Contras:** é um BSP, exatamente o papel que este ADR foi aberto para remover do caminho. Troca a
dependência da Twilio pela dependência do Azure, soma o preço do serviço ao preço por mensagem da Meta, e
acopla um gateway que hoje roda em qualquer lugar a um provedor de nuvem específico. Rejeitada pelo motivo
declarado do ADR, não por qualidade. Fica registrada como o caminho de menor esforço caso a decisão de
operar a WABA diretamente se mostre cara demais em compliance.

#### Nota sobre a ausência de SDK oficial

Não existe SDK oficial da Meta para .NET, e nunca existiu. O único oficial foi
`WhatsApp/WhatsApp-Nodejs-SDK`, **arquivado pela própria Meta em 2023-06-07**. Os exemplos oficiais em
`fbsamples/whatsapp-api-examples` cobrem Node, Python e Java, e não C#. Isso não é lacuna de mercado a
preencher: é a Meta afirmando que a Cloud API é HTTP simples o bastante para não justificar SDK, e é o
argumento mais forte a favor da opção A.

### O que fazer com o adapter `twilio-whatsapp`

#### Opção A: manter como plano B (escolhida)

**Prós:** custo marginal zero, já existe e é testado, e é o caminho alternativo se a verificação de negócio
na Meta demorar ou for recusada. Multi-tenant significa que dois tenants podem estar em providers
diferentes durante a transição, sem deploy.
**Contras:** duas superfícies de WhatsApp no repositório, e o risco de o adapter da Twilio virar código
morto permanente por esquecimento. Endereçado pelo gatilho de revisão abaixo.

#### Opção B: remover assim que a Meta estiver verde

**Contras:** joga fora o plano B no exato momento em que ele seria mais útil, que é a transição. Rejeitada
por sequenciamento, não por mérito.

### Onde a mudança de contrato de `WhatsAppMessage` acontece

#### Opção A: a mensagem descreve as duas formas, cada adapter aceita a sua (escolhida)

**Prós:** o contrato passa a dizer a verdade sobre o que cada provider aceita. A Twilio continua com corpo
livre sem mudança observável, e um template enviado a ela falha nomeando o motivo, em vez de virar texto.
**Contras:** um tipo com duas formas é mais complexo que um `record` de dois campos, e todo consumidor
precisa decidir o que faz com a forma que não espera.

#### Opção B: campos nulos no `record` atual

**Contras:** permite estados que não existem, como template com corpo e sem nome, e empurra a validação
para o adapter, onde ela já é tarde. Rejeitada, mesma razão que rejeitou o `Template` com campos nullable
da Meta no ADR-023.

## Decisões de borda cravadas

1. **Nome estável `meta-whatsapp`.** É o valor da coluna `provider`, a chave do resolver e o nome do
   `HttpClient`. Entra em `ProviderNames`, e o endereço entra em `ProviderEndpoints` com `AddressFor`
   mapeando explicitamente. A issue #139 é a prova de que adapter novo herdando endereço de outro é falha
   silenciosa que só aparece em produção.

2. **Versão da Graph API por configuração.** Medido em 2026-08-20: a documentação de get-started da Meta usa
   `v23.0` e o changelog do Graph API já está em `v26.0`. Versão sem manutenção sofre force-upgrade. O valor
   vive em `ProviderEndpoints` como padrão e pode ser sobrescrito por tenant em `settings`, porque um tenant
   pode precisar de uma versão diferente durante uma migração.

3. **Credencial.** `phone_number_id`, `waba_id` e `graph_version` são identificadores não secretos e vão para
   o `settings` jsonb. O token de System User vai para o campo protegido por Data Protection, como todos os
   demais. O **App Secret**, que é a chave HMAC do webhook, é secret de aplicação e não de tenant, e por isso
   vive na configuração do host, não em `tenant_provider_configs`.

4. **Token permanente, nunca o temporário.** O token que o painel gera no primeiro acesso expira em 24 horas.
   Produção exige System User com `whatsapp_business_messaging` e `whatsapp_business_management`. Um token
   expirado responde código 190, que é classificado como falha permanente de configuração e não como
   transiente: repetir não renova nada.

5. **Classificação por código, não por faixa de status.** A Meta devolve 400 para casos transientes e aceita
   com 200 casos que ainda vão falhar, exatamente como a Twilio. `MetaErrorPolicy` lê `error.code` e a faixa
   é fallback para código não mapeado, para que um código desconhecido seja genérico e não mal rotulado.

   | Código | Significado | Classificação |
   |---|---|---|
   | 4, 80007, 130429 | rate limit de app, de WABA, de throughput | transiente |
   | 131048 | bloqueio por qualidade, passa sozinho | transiente |
   | 131056 | mensagens demais para o mesmo destinatário | transiente |
   | 131000 | erro desconhecido do lado da Meta | transiente |
   | 131047 | 24 horas desde a última resposta, exige template | permanente, `Configuration` |
   | 132001 | template inexistente naquele idioma, ou não aprovado | permanente, `Configuration` |
   | 132000, 132012 | contagem ou formato de parâmetro divergente | permanente, `Configuration` |
   | 132007, 132015 | conteúdo viola política, ou template pausado | permanente, `Configuration` |
   | 131042 | meio de pagamento da WABA | permanente, `Configuration` |
   | 133010 | número não registrado na plataforma | permanente, `Configuration` |
   | 190 | token expirado | permanente, `Configuration` |
   | 368, 131031 | WABA restrita por violação de política | permanente, `Configuration` |
   | 131026 | destinatário não está no WhatsApp | permanente, `InvalidDestination` |
   | não mapeado | 429 e 5xx transiente, resto permanente | fallback por faixa |

6. **`wamid` é o identificador de entrega.** A resposta de aceite traz `messages[0].id`, e é ele que vai para
   `provider_message_id` no `DeliveryAttempt`. É a chave que o callback de status correlaciona, e correlacionar
   por ela e nunca pelo telefone é o que torna este canal imune ao BSUID da borda 15. Isso deixa de ser
   coincidência e passa a ser razão declarada.

7. **Webhook de status fora de `/v1`.** Mesma razão da borda 10 do ADR-028: a Meta não carrega `X-Api-Key`, e
   abrir exceção dentro do prefixo protegido enfraqueceria o `ApiKeyMiddleware` para toda a superfície.
   O `GET` responde o handshake devolvendo `hub.challenge` cru quando `hub.verify_token` confere. O `POST`
   valida `X-Hub-Signature-256`, HMAC-SHA256 do **corpo cru** com o App Secret, em comparação de tempo
   constante. Corpo cru é requisito duro: qualquer middleware que desserialize e reserialize antes da
   conferência quebra a assinatura. Assinatura inválida responde 401 sem processar.

8. **Idempotência do callback por `(provider, wamid, status)`**, tolerando duplicata e chegada fora de ordem.
   A Meta reentrega por até 7 dias com frequência decrescente, então duplicata não é exceção, é rotina.
   Evento sem correspondência local vira dead letter com alerta, nunca aceitar e descartar.

9. **Estado derivado com precedência `read > delivered > sent`**, `failed` terminal por erro, separado de
   `NotificationStatus`. É a mesma máquina do ADR-019 e do ADR-028, e `read` é o estado que só este canal
   produz.

10. **TLS válido é requisito de infraestrutura, não de código.** A Meta recusa certificado autoassinado e
    exige endereço público alcançável. É o único requisito novo de infraestrutura deste ADR, e ele bloqueia
    apenas a fatia do webhook.

11. **Consent permanece fail-closed.** Ausência de registro de opt-in nega o envio em qualquer categoria,
    inclusive transacional, como já implementado. Este ADR não afrouxa nada. A lacuna do caminho direto
    (`POST /v1/notifications` sem `userId` nem `category`) continua sendo do item 7 do ADR-028 e não é
    resolvida aqui.

12. **Escopo Nível 1.** Sem inbound de mensagens, sem opt-out automático por STOP recebido, sem janela de
    sessão de 24 horas tratada pelo produto, sem interativas, botões ou flows, sem Embedded Signup. O
    onboarding é manual, um tenant por vez.

13. **Sem metering.** O ADR-027 tirou credit ledger, metering e quotas do produto, e este ADR não os traz de
    volta. A categoria e o preço que o callback reporta são **registrados**, porque são a base de qualquer
    conta futura, e nada é debitado. Esta é a divergência deliberada em relação ao ADR-023, que modelava
    metering por conversa.

14. **Testes sem rede no gate.** Stub de `HttpMessageHandler` no CI e duplo HTTP no simulador. Verificação
    contra a Meta real é local, com user-secrets, e nunca condiciona merge. Mesmo padrão do ADR-028 e do
    ADR-029.

15. **Identidade do destinatário e o BSUID.** A Meta está lançando usernames no WhatsApp, e criou o
    business-scoped user ID para continuar identificando quem é quem quando o telefone deixar de aparecer.
    Formato `{ISO 3166 alpha-2}.{até 128 alfanuméricos}`, por exemplo `US.13491208655302741918`, opaco,
    estável quando o usuário troca de username, regenerado quando troca de telefone, e **escopado por
    business portfolio**, ou seja, o mesmo usuário tem BSUID diferente para cada empresa. Linha do tempo
    medida em 2026-08-20, com os três marcos já vencidos: BSUIDs em webhook de produção desde 2026-03-31,
    Contact Book desde o início de abril, e envio para BSUID desde junho.

    O que isso significa aqui, sem dramatizar. **O escopo Nível 1 é outbound puro, e o outbound não quebra:**
    o tenant continua fornecendo E.164 e o campo `to` continua aceitando telefone. **O status loop também
    não quebra**, porque a correlação é por `wamid` e não por telefone, conforme a borda 6. O impacto real é
    menor do que a primeira leitura sugere, e este parágrafo existe para que ninguém redescubra isso com
    susto.

    O que muda de fato, e é o que fica cravado:

    - No callback, `statuses[].recipient_user_id` sempre traz o BSUID, enquanto `contacts[].wa_id`
      **pode vir ausente**. Nenhum código do webhook pode ler telefone e presumir que ele existe.
    - Quando o BSUID vier, ele é **gravado** junto da tentativa. Não é usado para correlacionar, mas
      descartá-lo agora custaria uma migração de dados no dia em que o Nível 2 abrir.
    - `PhoneNumber`, que centraliza a regra E.164, passa a valer para o endereço que o tenant fornece e
      não para o que o provider devolve. A validação de saída continua; a de entrada não presume formato.
    - **Templates de autenticação one-tap, zero-tap e copy-code exigem telefone e não aceitam BSUID.**
      Se um tenant pedir esse tipo de template, o BSUID não é substituto e o envio depende do número.
    - O telefone continua aparecendo no webhook se houve interação nos últimos 30 dias **por aquele número
      de origem**, não por portfolio, ou se o usuário está no Contact Book. Depender disso seria depender
      de um estado que expira, então o produto não depende.

    Inbound, resposta a usuário que só tem username, e uso de BSUID como destinatário são Nível 2 e reabrem
    este ADR. O que o Nível 1 assume é a obrigação de não jogar fora o identificador que já chega.

## Onboarding, e por que o número novo não entra primeiro

Um número não pode estar registrado na Cloud API da Meta e ser sender WhatsApp na Twilio ao mesmo tempo.
Registrar na Meta o remove da Twilio, e o caminho de volta custa desregistro, novo código de verificação e
nova aprovação de display name. O número também não pode ter conta WhatsApp comum ou WhatsApp Business app
ativa: se tiver, a conta precisa ser apagada no aparelho antes, e o histórico se perde.

A Meta fornece um número de teste no app de desenvolvimento, com `phone_number_id` real, o template
`hello_world` já aprovado e uma allowlist de até cinco destinatários. Ele responde no mesmo endpoint, emite
os mesmos webhooks e devolve os mesmos códigos de erro. Portanto o canal é construído e provado com ele, e o
número novo entra por último, quando o único delta é trocar `phone_number_id` e token na configuração do
tenant.

## Ordem das fatias

| Fatia | Conteúdo | Depende da Meta |
|---|---|---|
| 0 | Este ADR | não |
| 1 | `WhatsAppMessage` descreve corpo livre e template, Twilio intacta | não |
| 2 | `MetaWhatsAppProvider`, `MetaErrorPolicy`, `ProviderNames`, endpoint | app criado e token de System User |
| 3 | Duplo da Meta no simulador, com os cenários da tabela da borda 5 | não |
| 4 | Entidade `WhatsAppTemplate` e resolução na ingestão e no fan-out | número de teste |
| 5 | Webhook de status, fecha os itens 5 e 6 do ADR-028 | verificação, número e TLS público |

As fatias 1 a 3 provam o canal de ponta a ponta contra o simulador, sem crédito e sem número. Se o
onboarding travar, o trabalho até ali permanece verde e mergeado.

**Dependência de sequenciamento:** as fatias 2 e 3 exigem `ProviderNames`, `ProviderEndpoints` por
configuração e o `tools/Hiram.Simulator`, que estão nos PRs #140 e #142 e ainda não estão em `main`.

## Consequências

### Positivas

- O status loop fecha de verdade, com `delivered` e `read` medidos, e não apenas o aceite do provider. Os
  itens 5 e 6 do ADR-028 saem de adiados por impossibilidade de comprovação.
- Custo por mensagem sem markup de intermediário.
- A segunda implementação atrás de `IWhatsAppProvider` prova que a porta era necessária. Até aqui ela tinha
  um implementador só, e o CLAUDE.md diz que é a segunda que a justifica.
- `whatsapp_templates` deixa de ser tabela morta no banco de produção.

### Negativas

- Uma rota pública a mais para endurecer, e um quarto esquema de assinatura no repositório, ao lado do
  HMAC-SHA256 sobre JSON dos webhooks de saída e do HMAC-SHA1 sobre form da Twilio.
- Compliance e onboarding do WABA passam a ser responsabilidade do Hiram, incluindo o ciclo assíncrono de
  aprovação de template, que não tem prazo garantido.
- Dois adapters de WhatsApp para manter enquanto durar a transição.
- Endereço público com TLS válido vira pré-requisito de infraestrutura, o que o produto não exigia até aqui.
- A mudança de forma de `WhatsAppMessage` toca um contrato da Application usado pelo caminho crítico.

## Gatilho de revisão

- **Remoção do `twilio-whatsapp`:** quando nenhum tenant apontar para ele por 30 dias com a Meta em produção.
  Até lá ele fica, e este parágrafo é o registro de que ficar é decisão e não esquecimento.
- **Subida para Nível 2:** demanda concreta de inbound, opt-out por STOP recebido ou janela de sessão tratada
  pelo produto reabre este ADR.
- **Embedded Signup:** quando houver mais de um tenant por onboarding manual.
- **Volume:** primeiro `429` recorrente reabre a borda 11 do ADR-028, pipeline de resiliência por canal.
- **BSUID:** primeiro callback observado sem `wa_id`, ou primeira demanda de responder a usuário que só tem
  username, reabre a borda 15 e provavelmente exige uma decisão própria sobre identidade de destinatário,
  que vale para todos os canais e não só para este.

## Lacunas conhecidas, não medidas

Registradas para que ninguém as leia como fato.

1. A tarifa de `utility` no Brasil não foi confirmada em fonte primária da Meta. Sabe-se que desde
   2026-07-01 a cobrança é por mensagem entregue e não mais por conversa de 24 horas, e que a faixa global de
   `utility` vai de USD 0,004 a USD 0,0456. O valor do Brasil deve ser medido no painel da própria WABA.
2. A versão da Graph API a fixar deve ser confirmada no painel do app no momento da fatia 2. Três valores
   diferentes foram observados em 2026-08-20 e nenhum é fonte primária de qual usar: `v23.0` na
   documentação de get-started, `v26.0` no changelog do Graph API, e `v25.0` como padrão da biblioteca
   `WhatsappBusiness.CloudApi`. A divergência é em si o argumento da borda 2, versão como configuração.
3. O comportamento do número de teste e do `hello_world` foi lido na documentação, não medido contra a API.
   O ADR-028 mediu a documentação da Twilio divergindo da realidade em pelo menos dois pontos, e a issue
   #133 registra um deles. Presumir que a Meta é diferente seria ingenuidade. A fatia 2 mede antes de cravar.
4. O BSUID da borda 15 foi levantado na documentação da Meta e na do Azure Communication Services, que
   concordam entre si, e **não** foi observado num payload real. A afirmação de que o Nível 1 não quebra é
   raciocínio sobre o desenho, correlação por `wamid`, e não medição. A fatia 5 confirma contra o webhook
   real antes de fechar o critério de conclusão.
5. O custo do Azure Communication Services, alternativa E, não foi levantado. A rejeição dele é por
   arquitetura, manter um intermediário, e não por preço. Se a decisão voltar a ser discutida por custo, o
   número precisa ser medido primeiro.

## ADRs afetados

- **ADR-023**, canal WhatsApp por Cloud API: **supersedido por este ADR**. Ele já estava supersedido pelo
  ADR-027 e permanece histórico. Este ADR decide de novo o mesmo canal, com a arquitetura de hoje, e diverge
  dele em dois pontos: não traz metering por conversa, e não trata enforcement de consent como passo zero,
  porque o `ConsentPolicy` já nega WhatsApp sem opt-in.
- **ADR-028**, integração Twilio multicanal: **alterado**. A Twilio deixa de ser o único caminho de WhatsApp
  e passa a plano B. Os itens 5 e 6 dele, adiados por impossibilidade de comprovação no trial, passam a ser
  realizáveis e são realizados pela fatia 5 deste ADR.
- **ADR-019**, callbacks de provider: realizado para este canal, com `read` incluído.
- **ADR-029**, simulador de providers: estendido com um segundo duplo.
- **ADR-027**, Hiram Core: coerente. O WhatsApp voltou ao escopo pelo ADR-028, e este ADR troca o provider,
  não reabre a superfície que o ADR-027 cortou.

## Itens de ação

1. [ ] `WhatsAppMessage` descreve corpo livre e template, com o adapter Twilio recusando template por falha
   permanente nomeada.
2. [ ] `ProviderNames.MetaWhatsApp`, entrada em `ProviderEndpoints`, mapeamento em `AddressFor` e registro no
   DI.
3. [ ] `MetaWhatsAppProvider` e `MetaErrorPolicy`, com teste de stub cobrindo aceite e cada linha da tabela
   da borda 5.
4. [ ] Duplo da Meta em `tools/Hiram.Simulator`, com cenários derivados do enum.
5. [ ] Entidade e store de `WhatsAppTemplate` sobre a tabela existente, sem migration nova.
6. [ ] Resolução de template na ingestão e no fan-out, com mapeamento de `data` nomeado para parâmetros
   posicionais e fan-out só com template aprovado na Meta.
7. [ ] Endpoints de template e provider do WhatsApp, fechando a issue #53.
8. [ ] Rota de callback com handshake, assinatura sobre corpo cru e estado derivado idempotente, lendo
   `statuses[].recipient_user_id` sem presumir que `contacts[].wa_id` existe (borda 15).
9. [ ] Gravar o BSUID na tentativa quando ele vier, sem usá-lo para correlacionar.
10. [ ] Onboarding de credencial da Meta no `docs/operations-runbook.md`, ao lado da seção da Twilio.
11. [ ] Medir e registrar a tarifa real de `utility` no Brasil, fechando a lacuna 1.
12. [ ] Verificar se o canal `twilio-whatsapp`, já em produção, lê telefone de algum payload de provider. Se
    ler, o BSUID o atinge antes de atingir a Meta, e o conserto não espera este ADR.

## Critério de conclusão

Uma notificação de WhatsApp submetida a um tenant configurado com `meta-whatsapp` é aceita, persistida,
enviada como template aprovado, correlacionada pelo `wamid` no callback e visível no detalhe da notificação
com a tentativa e o estado derivado chegando a `delivered`. Build Release e suíte completa verdes, sem
credencial no repositório e sem teste de rede no gate de merge.
