# ADR-013: Templates de mensagem com Scriban, renderizados no submit

**Status:** Aceito
**Data:** 2026-06-26
**Decisores:** Felipe (arquiteto)

## Contexto

A F2 prevê templates de mensagem por tenant. Hoje o cliente manda `subject` e `body` prontos no `POST /v1/notifications`. Templates deixam o tenant cadastrar um modelo com variáveis e enviar só os dados, com o conteúdo final montado pela plataforma. O MASTER-PLAN já aponta Scriban na linha da F2, mas Scriban é biblioteca de runtime nova e o CLAUDE.md proíbe biblioteca nova sem ADR, então a escolha e o desenho de renderização ficam registrados aqui.

Duas perguntas a responder: qual motor de template, e em que momento renderizar.

## Decisão

Scriban como motor de template. Renderização no momento do submit, na borda síncrona da API: o template é resolvido e renderizado em `subject` e `body`, que são persistidos na `NotificationRequest` e no payload do outbox, exatamente como o caminho direto faz hoje. Variáveis indefinidas falham a renderização (modo estrito), virando 400 síncrono para o cliente. A sintaxe do template é validada no cadastro, não no envio.

## Opções consideradas

### Opção A: Scriban

Motor de template .NET nativo, linguagem própria com condicionais, laços e filtros, sem execução de código arbitrário.

**Prós:** já apontado no MASTER-PLAN; rápido e em memória, cabe no caminho quente do submit; seguro por construção (não roda C# nem shell); DX boa e familiar a quem conhece Liquid; suporta o modo estrito de variáveis que queremos.
**Contras:** mais uma dependência de runtime para auditar.

### Opção B: Fluid (Liquid)

Implementação .NET do Liquid da Shopify.

**Prós:** também seguro e em memória, sintaxe Liquid conhecida.
**Contras:** não é o que o MASTER-PLAN apontou; menos idiomático em alguns pontos; não traz vantagem que justifique divergir da direção já registrada.

### Opção C: Razor

**Prós:** poderosíssimo, é a engine de view do ASP.NET.
**Contras:** compila C#, então abre superfície de execução de código e exige sandbox; peso e tempo de compilação desproporcionais para montar um email; overkill para o problema.

### Opção D: string.Format ou interpolação própria

**Prós:** zero dependência.
**Contras:** fraco demais; sem condicional nem laço, todo template não trivial vira código no servidor. Não atende o produto.

## Análise de trade-off

O critério é o mesmo do projeto: a escolha já apontada, segura e explicável vence quando não há motivo forte para divergir. Scriban é seguro, rápido o suficiente para o caminho quente e já está na direção do MASTER-PLAN. Razor traz risco de execução de código sem necessidade; interpolação não atende. Scriban vence.

Sobre o momento de renderizar, renderizar no submit e guardar o conteúdo final é coerente com a decisão de replay da ADR-011: o replay reenvia o conteúdo armazenado, fiel ao que foi tentado, sem re-renderizar. Renderizar no envio quebraria essa fidelidade e jogaria erros de dado para o caminho assíncrono, onde virariam dead letter em vez de um 400 que o cliente entende na hora. Logo, render no submit.

## Decisões de borda cravadas

1. **Render no submit.** O conteúdo renderizado é persistido na `NotificationRequest` e no payload do outbox. O dispatcher, o dead letter e o replay continuam tratando `subject` e `body` opacos, sem conhecer template.
2. **Variáveis estritas.** Variável usada e não fornecida nos dados falha a renderização e vira 400 no submit, em vez de enviar uma mensagem meio renderizada. Modo leniente com valor padrão fica fora desta fatia.
3. **Validação de sintaxe no cadastro.** `POST /v1/templates` rejeita template com erro de sintaxe Scriban com 400. No submit, erro de renderização (variável faltando, tipo incompatível) também é 400, com mensagem do motor truncada.
4. **Escopo do template.** Único por `(tenant_id, channel, name)`. Resolução no submit por nome e canal, sempre escopada ao tenant autenticado.
5. **Sem execução de código.** Scriban roda em modo padrão sem importar funções de sistema. O template nunca acessa arquivo, rede ou processo.
6. **Imutabilidade do já enviado.** Alterar ou apagar um template não muda notificações já submetidas, porque o conteúdo já foi renderizado e guardado.
7. **Cache.** Leitura do template no Postgres por submit, escopada e indexada. O cache de template em Redis citado no MASTER-PLAN é otimização e fica deferido.

## Consequências

- **Fica mais fácil:** o cliente manda só dados; o conteúdo final é auditável e replayável sem re-render; mudanças de template não afetam o passado.
- **Fica mais difícil:** o conteúdo renderizado fica em repouso na request e no payload, com a mesma exposição de PII já existente em `notification_requests.body`; templates com muitas variáveis exigem que o cliente mande todos os dados, senão recebe 400.
- A superfície da API cresce com o CRUD de templates e um segundo modo no submit.

## Gatilho de revisão

Necessidade de partials e includes entre templates, versionamento de template, multi-idioma com fallback, ou render no envio para late binding de dados que só existem na hora do disparo.

## Itens de ação

1. [ ] Entidade `Template` e schema `templates` com unique por tenant, canal e nome.
2. [ ] Porta `ITemplateRenderer` e adapter Scriban com modo estrito e validação de sintaxe.
3. [ ] CRUD de templates escopado por tenant.
4. [ ] Segundo modo do submit: template mais dados, render no submit, 400 em erro de render e 404 em template inexistente.
