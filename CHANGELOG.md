# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em Keep a Changelog (https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Added
- Adapter `meta-whatsapp`, a Cloud API da Meta como segunda implementacao de `IWhatsAppProvider`, ao lado
  de `twilio-whatsapp` e nao no lugar dela (ADR-030, itens 2 e 3). Envia corpo livre como `type: text` e
  template como `type: template` com os parametros nas posicoes, e devolve o `wamid` de `messages[0].id`
  como `provider_message_id`, que e a chave que o callback de status vai correlacionar. A versao da Graph
  API e configuracao, com padrao em `Hiram:Providers:Endpoints:MetaGraphVersion` e sobrescrita por tenant
  em `settings.graph_version`: a Meta poe a versao no caminho e forca upgrade de quem fica para tras, e em
  2026-08-20 tres fontes deram tres respostas sobre qual usar. `MetaErrorPolicy` classifica pelo codigo da
  Meta e nao pela faixa de status, porque a faixa erra nos dois sentidos: 131000 chega como 500 e merece
  outra tentativa, enquanto 131047 e todo erro de template chegam como 400 e nenhuma repeticao resolve.
  Codigo nao mapeado cai na faixa, entao codigo novo da Meta fica generico em vez de mal rotulado.
- ADR-030: o canal WhatsApp passa a ter a Cloud API da Meta como segunda implementacao de
  `IWhatsAppProvider`, com o nome estavel `meta-whatsapp`, ao lado de `twilio-whatsapp` e nao no lugar
  dela. A Twilio fica como plano B ate a Meta estar em producao, e o ADR crava o gatilho de remocao para
  que ficar seja decisao e nao esquecimento. A mudanca real nao e de transporte: fora da janela de 24h a
  Meta so entrega template pre-aprovado com parametros posicionais, entao `WhatsAppMessage` deixa de ser
  so corpo livre e a tabela `whatsapp_templates`, dormente desde a migration `20260713181535`, sai da
  dormencia. O ganho colateral e o status loop: a Meta reporta `sent`, `delivered`, `read` e `failed` com
  categoria e preco, o que torna realizaveis os itens 5 e 6 do ADR-028, adiados desde 2026-08-08 porque o
  trial da Twilio nao tinha contraparte para comprovar. Nenhuma dependencia nova: adapter proprio sobre
  `HttpClient`, e a biblioteca `WhatsappBusiness.CloudApi` fica como referencia de leitura, nao como
  pacote: o endereco base dela e estado estatico de processo e a factory devolve um cliente novo por
  chamada, dois padroes que um gateway multi-tenant nao suporta. Nao existe SDK oficial da Meta para .NET,
  e o unico oficial que existiu, o de Node, foi arquivado pela propria Meta em 2023. O Azure Communication
  Services foi avaliado e rejeitado por manter um intermediario, nao por qualidade. O ADR tambem crava o
  tratamento do BSUID, o identificador que a Meta passou a emitir em webhook desde 2026-03-31 no lugar do
  telefone: o Nivel 1 nao quebra porque a correlacao e por `wamid`, e isso passa a ser razao declarada em
  vez de coincidencia. Supersede o ADR-023.
- Contagem de segmento de SMS na resposta de ingestao. `POST /v1/notifications` devolve `segments` no
  canal SMS e `null` nos demais. GSM-7 cabe 160 caracteres em mensagem avulsa e UCS-2 apenas 70, e em
  portugues as vogais com til e circunflexo estao fora do GSM-7 enquanto o e agudo e a cedilha estao
  dentro: 148 caracteres custam 3 segmentos com elas e 1 sem. Aspas curvas sao normalizadas na criacao da
  requisicao, em qualquer caminho, porque aspa colada de editor de texto e ruido de digitacao que dobrava
  a conta; acento e preservado, porque carrega significado.

- Simulador de providers em `tools/Hiram.Simulator` (ADR-029). Ele sobe um duplo HTTP da Twilio, que
  responde `Messages.json` e `Emails` nos mesmos formatos que os adapters ja classificam, e conduz um
  roteiro de tres atos contra a API publica do Hiram: uma entrega aceita, uma recusada e uma pelo
  fan-out de eventos. O cenario de falha e escolhido por argumento (`21408`, `21610`, `30007`, `63016`,
  `429`, `500`), porque provocar o caminho ruim e o que um stub de handler cobre pior. O duplo nao entra
  no gate de CI; o que o CI cobre e a paridade entre o que ele responde e o que os adapters classificam.
- Endereco de cada provider por configuracao, na secao `Hiram:Providers:Endpoints`, com os valores de
  producao como padrao. Um valor relativo e recusado no startup, nomeando a chave, em vez de virar erro
  de transporte na hora da entrega.

- Provisionamento do tenant Jornada do Candidato em `deploy/jornada/`: script idempotente que cria o
  tenant live, emite a api key do emissor, configura o provider de cada canal, aprova os templates da
  jornada e liga cada eventType ao seu template. `JORNADA_CHANNELS` escolhe entre e-mail, SMS e
  WhatsApp; o e-mail de verificacao permanece so no canal de e-mail. Reexecucao reaproveita o que ja
  existe em vez de duplicar.
- Onboarding de credencial Twilio por tenant no runbook operacional: o que vai em `settings` e o que vai
  no segredo protegido, a dependencia do key ring, os limites da conta trial, o sandbox do WhatsApp,
  rotacao e o roteiro de smoke manual, que fica fora do gate de CI.
  tenant live, emite a api key do emissor, aprova os templates de e-mail da jornada e liga cada
  eventType ao seu template. Reexecucao reaproveita o que ja existe em vez de duplicar.
- Registro da substituicao de conteudo em modo trial na tentativa de entrega. Quando o adapter
  `twilio-sms` ou `twilio-email` envia o conteudo pre-aprovado no lugar do corpo da notificacao, a
  coluna nova `delivery_attempts.trial_content` marca a tentativa e o detalhe da notificacao passa a
  expor o campo. Sem isso o historico afirmava ter entregue um texto que nunca saiu. Fecha o item de
  acao 2.1 do ADR-028.
- Canal WhatsApp entregue pelo sandbox da Twilio: porta `IWhatsAppProvider`, adapter
  `twilio-whatsapp` sobre o mesmo recurso Messages do SMS e `PUT /v1/providers/whatsapp`. O prefixo
  `whatsapp:` do `To` e do `From` e montado somente no adapter, entao a notificacao guarda o numero em
  E.164 puro. Texto livre fora da janela de 24 h e recusado com 63016, que vira falha permanente
  carregando a razao do provider: a notificacao fica em dead-letter nomeada e pode ser reenviada por
  replay depois que o destinatario refizer o join.
- Superficies abertas ao canal `whatsapp`: `POST /v1/notifications` (com a mesma validacao E.164 do
  SMS), `POST /v1/templates`, o campo `channels` de `POST /v1/admin/routines` e `POST /v1/consent`.
  O consentimento e o mais importante deles: a politica e fail-closed para WhatsApp em toda
  categoria, entao sem registro de opt-in o canal nunca envia.
- Fan-out de eventos para WhatsApp, no mesmo formato do SMS e reaproveitando a persistencia comum.
- Fan-out de eventos para SMS. Uma rotina com `channels: ["email","sms"]` passa a gerar as duas
  notificacoes: o telefone vem do proprio evento e um numero ausente ou fora de E.164 e recusado antes
  do outbox, como ja acontece no envio direto.
- Contador `hiram.events.no_route`, com tag do tipo do evento. Evento sem rotina continua sendo ack,
  nao dead-letter, e sem metrica esse caminho era invisivel na operacao.
- Superficies de configuracao abertas ao canal `sms`: `POST /v1/templates`, o campo `channels` de
  `POST /v1/admin/routines` e `POST /v1/consent`. Sem elas nao havia o que um evento entregar por SMS.
- Canal SMS entregue pela Twilio: porta `ISmsProvider`, adapter `twilio-sms` sobre o recurso Messages
  e `PUT /v1/providers/sms` para o tenant configurar a propria conta de operadora. Sem provider
  configurado o envio falha como permanente, sem chamar a rede, porque o credito e do tenant.
- Adapter `twilio-email` como terceiro provider de email, escolhido por tenant ao lado de SMTP e
  Resend. Nenhum tenant existente muda de provider.
- ADR-028 registrando a Twilio como provider multicanal de ultima milha.
- ADR-027 e plano executavel para a migracao incremental ao Hiram Core.
- Primitiva de fila PostgreSQL com claim atomico, lease renovavel, retry agendado e recuperacao de
  leases vencidos.
- Runbook operacional e prova descartavel de backup e restore do PostgreSQL e do key ring no CI.

### Changed
- Torna `subject` opcional por canal. A coluna passa a aceitar nulo por migration e a obrigatoriedade
  vive no dominio, exigida em email e push, ausente em SMS e WhatsApp.
- Estende a mesma regra a `templates.subject`, por migration nova. Um template de email continua
  exigindo subject e um de SMS ou WhatsApp recusa qualquer subject, porque ele nunca seria renderizado.
- Compartilha a leitura da resposta do recurso Messages da Twilio entre os adapters de SMS e WhatsApp,
  para que a classificacao de erros como 21608 e 63016 nao divirja por canal.
- Centraliza a validacao E.164 em `PhoneNumber`, no dominio. O submit direto e o fan-out passam a usar
  a mesma regra em vez de duas copias da expressao.
- Extrai a mecanica comum de entrega (claim, bloqueio, modo shadow, tentativa, dead letter e webhook)
  do processor de email para um processor de canal reutilizavel. Cada canal contribui apenas com a
  montagem da mensagem e a chamada ao adapter.
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Ver ADR de adocao e CLAUDE.md.
- Redefine o Hiram como infraestrutura interna de notificacoes: um host e PostgreSQL no runtime
  alvo, providers externos na ultima milha e extensoes somente com consumidor ativo.
- Torna o dispatcher PostgreSQL o unico transporte do outbox.
- Consolida API e workers PostgreSQL no mesmo host e publica uma unica imagem `hiram`.
- Alinha configuracao, observabilidade, site e metadados ao posicionamento interno do Hiram Core.

### Removed
- Remove metering, creditos e o ledger do caminho de submissao e fan-out. A migration historica
  permanece intacta e a tabela existente fica dormente ate uma limpeza de schema posterior.
- Remove o modulo incompleto de templates WhatsApp. A tabela `whatsapp_templates` segue dormente e
  fora do model: o canal voltou pela tabela `templates` comum, sem reviver a entidade HSM.
- Remove MTA proprio, k3s, KEDA, PgBouncer e a stack conjunta com o Levante, artefatos operacionais
  aposentados pelo ADR-027.
- Remove Redis do runtime. Idempotencia e throttle de uso de API key passam a usar apenas PostgreSQL.
- Remove RabbitMQ, o relay intermediario, consumers AMQP e o Testcontainer do broker. Processadores
  reivindicam o outbox diretamente no PostgreSQL.
- Remove o projeto, processo e imagem Hiram.Dispatcher.

### Fixed
- Erro de provider passa a ser classificado pelo codigo, nao pela faixa de status. O caso caro e o
  `30007`, filtragem por spam da operadora, que chega como `201` com status terminal: uma regra por faixa
  que o lesse como retentavel faria o Hiram piorar a propria reputacao de remetente a cada tentativa. O
  `30003` e o oposto e volta a ser retentavel. `TwilioMessagesApi` passa a ler `error_code`, sem o qual os
  vereditos de operadora eram invisiveis, e uma falha permanente carrega um `DeliveryFailureKind`, entao
  regiao fora das geo permissions da conta e destinatario que respondeu STOP deixam de ser o mesmo
  registro de dead letter.
  Uma janela fechada de WhatsApp responde `21654` e nao o `63016` que a documentacao preve, medido seis
  vezes contra a sandbox em 2026-08-10 (issue #133), e os dois passam a ler como erro de configuracao,
  assim como o `30034` de numero dos EUA sem campanha 10DLC registrada. Cada um desses codigos tem
  cenario proprio no simulador, entao o caminho ruim se reproduz sem conta paga.
- Endereco de provider exige URL absoluta em `http` ou `https`, e nao apenas absoluta. No Linux o parser
  de URI aceita um caminho puro como URI absoluto de arquivo, entao `/twilio/` passava la e falhava no
  Windows.

- Cada adapter de provider passa a ter o seu proprio cliente HTTP nomeado. `AddHttpClient<TClient,
  TImplementation>` deriva o nome logico de `TClient`, entao os dois adapters registrados atras de
  `IEmailProvider` compartilhavam um `HttpClient` e o ultimo endereco configurado valia para os dois.
  Medido contra o container real, o cliente de `IEmailProvider` respondia `https://comms.twilio.com/v1/`,
  o que fazia todo tenant configurado com `resend` enviar para o host da Twilio com credencial do
  Resend. O nome do cliente passa a ser o identificador que o adapter ja expoe em `Name` e que
  `tenant_provider_configs` guarda.

- Replay de dead letter volta a funcionar em SMS e WhatsApp. `DeadLetterReplay` mapeava a chave de
  roteamento apenas de e-mail e push, entao `POST /v1/notifications/{id}/replay` respondia 500 nos dois
  canais da ADR-028 e a mensagem ficava sem caminho de volta. A transacao ja fazia rollback, entao
  nenhum estado corrompeu: o efeito era a recuperacao prometida nunca acontecer. E o caso normal do
  sandbox do WhatsApp, onde a janela de 24 h fecha e o replay depois do join novo e a unica saida.
