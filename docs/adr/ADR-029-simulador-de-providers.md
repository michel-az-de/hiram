# ADR-029: Simulador de providers em `tools/`, com duplo HTTP e endereço de provider por configuração

**Status:** Aceito
**Data:** 2026-08-18
**Decisores:** Felipe (arquiteto)

## Contexto

O ADR-028 fechou os três canais Twilio, e o critério de conclusão dele continua descumprido: faltam a
rota de status callback e o estado de entrega derivado. A decisão de sair do trial para conta paga,
tomada em 2026-08-18, elegeu o simulador como a primeira fatia, porque é ele que torna as fatias
seguintes verificáveis sem crédito, sem número verificado e sem expor a máquina.

### Estado medido antes da decisão

- O CI prova os adapters com stub de `HttpMessageHandler`, o que cobre a unidade e não a orquestração.
  Outbox, lease, claim, tentativa, classificação de erro e dead letter só aparecem juntos contra um
  provider que responda de verdade.
- O endereço de cada provider era constante compilada em `DependencyInjection`. Nenhum duplo conseguiria
  interceptar sem recompilar.
- `AddHttpClient<TClient, TImplementation>` deriva o nome lógico do cliente de `TClient`. O repositório
  registrava dois adapters atrás de `IEmailProvider`, então os dois caíam no mesmo `HttpClient` e a última
  configuração de endereço valia para ambos. Medido em 2026-08-18, resolvendo o container real:
  `IEmailProvider` respondia `https://comms.twilio.com/v1/`. Como o `ResendEmailProvider` monta o caminho
  relativo `emails`, todo tenant configurado com `resend` enviava para o host da Twilio com credencial do
  Resend. É a issue #139, aberta a partir desta medição.
- O único console existente é o `site/learn`, do ADR-021: navegador, voltado a demonstração de palco e
  servido apenas em Development. Ele não exercita o caminho de entrega, e sim a API pública.
- A solution tem `src/` e `tests/`. Não existe fronteira para ferramenta de desenvolvimento.

## Decisão

Nasce `tools/`, uma terceira fronteira da solution, e nela o `Hiram.Simulator`: um console que hospeda um
duplo HTTP dos providers e conduz um roteiro de ponta a ponta contra a API real do Hiram.

1. **Endereço de provider é configuração.** `ProviderEndpoints` vive na Application como registro puro,
   com os valores de produção como `Production`. A Infrastructure lê a seção
   `Hiram:Providers:Endpoints` e registra o resultado; quem não configura nada continua falando com os
   providers reais.
2. **Um cliente HTTP nomeado por adapter, nunca por porta.** O nome é o mesmo identificador estável que o
   adapter já expõe em `Name` e que a coluna `provider` de `tenant_provider_configs` guarda, agora
   centralizado em `ProviderNames`. Isso corrige a issue #139 e impede que um adapter novo herde o
   endereço de outro.
3. **O duplo é um processo HTTP real, não um stub de handler.** Ele responde nos mesmos formatos que
   `TwilioMessagesApi` e `TwilioEmailProvider` já classificam, e por isso exercita serialização, header de
   autorização, form encoding, timeout e o pipeline de resiliência inteiro.
4. **O simulador fala com o Hiram só pela API pública.** Nenhum atalho pelo banco. O que o roteiro prova é
   o que um emissor real veria.
5. **Dois modos, e o seguro é o padrão.** Sem argumento, o duplo sobe e o roteiro roda contra ele.
   `--live` desliga o duplo e conduz o mesmo roteiro contra a Twilio real, com credencial vinda de
   user-secrets ou do ambiente. Gastar dinheiro exige um argumento explícito, nunca um esquecimento.

## Decisões de borda cravadas

1. **`tools/` fora de `src/` e de `tests/`.** O projeto entra em `Hiram.sln` para que o build o cubra, e
   nenhum projeto de produção o referencia. A dependência é de mão única: o simulador conhece os contratos
   públicos, e o produto não sabe que ele existe.
2. **`TryAddSingleton` para os endpoints, e `AddHiramProviderEndpoints` antes de `AddHiramInfrastructure`.**
   A infraestrutura só preenche o que ficou faltando. Um host que não chama nada mantém produção, o que
   preserva o comportamento atual sem exigir mudança em quem já compõe o container.
3. **Endereço relativo falha no startup, não na entrega.** Um `BaseAddress` relativo transformaria todo
   envio em requisição contra nada, e o sintoma apareceria como erro de transporte na hora errada. A
   validação nomeia a chave ofensora.
4. **O duplo não valida credencial, mas exige que ela exista.** Ele recusa requisição sem `Authorization`
   parseável, para que um adapter que esqueça de autenticar falhe no simulador como falharia na Twilio.
5. **Identificadores determinísticos.** O duplo gera `SM` e o restante do SID a partir de um contador, para
   que o roteiro seja reproduzível e comparável entre execuções.
6. **Cenários de falha por argumento, não por código.** `21408`, `21610`, `30007`, `63016`, `429` e `500`
   são selecionáveis na linha de comando, porque o valor do simulador está justamente em provocar o
   caminho ruim, que é o que o stub de CI cobre pior.
7. **O duplo não entra no gate de CI nesta fatia.** O CI continua sem rede e sem porta aberta. O gatilho de
   revisão é a rota de status callback: quando ela existir, avaliar hospedar o duplo em porta efêmera
   dentro de um teste de integração, porque aí o valor passa a superar o custo.
8. **Nenhum segredo versionado.** O modo `--live` lê de user-secrets ou variável de ambiente e nunca ecoa
   valor. O modo `--fake` não tem segredo para vazar.
9. **O simulador não substitui o `site/learn`.** O ADR-021 continua valendo: aquele console é vitrine de
   palco, em navegador, e este é ferramenta de engenharia, em terminal. Não há sobreposição de propósito.

## Alternativas consideradas

### Opção A: duplo HTTP em `tools/`, com endereço por configuração (escolhida)

**Prós:** exercita o pipeline HTTP inteiro, provoca o caminho ruim sob demanda, roda offline, e a mesma
configuração que aponta para o duplo serve para apontar para um ambiente de homologação no futuro.
**Contras:** uma fronteira nova na solution e um endereço a mais para configurar errado, mitigado pela
validação de startup.

### Opção B: continuar só com stub de `HttpMessageHandler`

**Prós:** zero superfície nova, e é o que o CI já faz.
**Contras:** o stub prova o adapter e não a orquestração. Ele nunca vai revelar um erro de composição do
container, que é exatamente a classe de defeito da issue #139. Rejeitada por não cobrir o risco que motiva
a fatia.

### Opção C: gravar e reproduzir tráfego real (VCR)

**Prós:** fidelidade máxima ao provider.
**Contras:** exige tráfego real gravado para existir, e o trial não produz o tráfego que interessa
(status callback, erro de operadora, janela de 24h). Além disso, gravação de tráfego de provider carrega
credencial e destinatário, o que é exatamente o que não pode ser versionado. Rejeitada.

### Opção D: expor a máquina com túnel para receber callback real

**Prós:** prova o caminho de verdade.
**Contras:** depende de conta paga, número verificado e serviço de túnel de terceiro, e não roda em CI nem
offline. Continua sendo o teste de fumaça manual do runbook, não o gate de desenvolvimento.

## Consequências

### Positivas

- o caminho de entrega passa a ser exercitável de ponta a ponta sem crédito e sem número verificado;
- o defeito de composição da issue #139 fica coberto por teste de regressão, e não por leitura de código;
- apontar o Hiram para outro ambiente de provider deixa de exigir deploy;
- as fatias seguintes, status callback e Content API do WhatsApp, nascem com onde ser provadas.

### Negativas

- mais uma fronteira na solution para manter e mais um endereço para configurar errado;
- o duplo é código que imita um contrato de terceiro, e vai divergir quando a Twilio mudar. A divergência
  é silenciosa por natureza, e o antídoto é que o duplo produza exatamente os formatos que os
  classificadores de produção já consomem, sem uma segunda cópia da regra;
- o modo `--live` cria um caminho que gasta dinheiro real a partir da linha de comando.

## Limites e gatilhos de revisão

Rever a decisão de manter o duplo fora do CI quando a rota de status callback existir. Rever a fronteira
`tools/` quando houver uma segunda ferramenta, para decidir se elas compartilham projeto ou não. Rever o
duplo inteiro se a Twilio publicar um simulador oficial que cubra status callback.

## ADRs afetados

- **ADR-021**, console de demo: não alterado. Aquele console é vitrine em navegador, este é ferramenta em
  terminal, e nenhum dos dois passa a servir o propósito do outro.
- **ADR-028**, integração Twilio: complementado. O simulador é onde os itens 5 e 6 vão ser provados.

## Itens de ação

1. [x] `ProviderEndpoints` e `ProviderNames` na Application, com os valores de produção como padrão.
2. [x] Um cliente HTTP nomeado por adapter, corrigindo a issue #139, com teste de regressão.
3. [x] Projeto `tools/Hiram.Simulator` na solution, sem referência de produção para ele.
4. [x] Duplo HTTP de `Messages.json` e de `Emails`, nos formatos que os classificadores já consomem.
5. [x] Cenários de falha selecionáveis por argumento.
6. [x] Roteiro de console que dispara evento e acompanha aceite, tentativa e desfecho.
7. [x] Seção no runbook explicando como rodar os dois modos.

## Critério de conclusão

Uma notificação submetida em cada canal, com o Hiram apontado para o duplo, é aceita, persistida,
entregue, e o resultado aparece no detalhe da notificação com a tentativa correta. Um cenário de falha
selecionado produz dead letter nomeada com o código do provider no motivo. Build Release e suíte completa
verdes, sem segredo no repositório e sem teste de rede no gate de merge.
