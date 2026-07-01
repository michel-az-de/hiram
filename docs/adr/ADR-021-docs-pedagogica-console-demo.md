# ADR-021: Documentação pedagógica interativa e console de demo servidos pela Hiram.Api em dev

**Status:** Aceito
**Data:** 2026-06-30
**Decisores:** Felipe (arquiteto)

## Contexto

O Hiram precisa de uma superfície para vender e ensinar o produto em demonstrações ao vivo (vendas e eventos) que mostre os conceitos vendáveis executando de verdade, e de um Swagger temático e enriquecido para estudo em teste de desenvolvimento. Hoje existe o site de marketing estático em `site/` (ADR-006), o design system fechado em `docs/design/` e o Swagger via OpenAPI nativo mais Scalar em `src/Hiram.Api/OpenApi`. Nenhum deles executa chamadas reais nem ensina os fluxos passo a passo.

O ADR-006 decidiu deliberadamente um site de marketing estático, página única, sem build, e reservou o gatilho de revisão para quando o site crescesse em páginas ou exigisse conteúdo dinâmico. Adicionar um console que dispara requests reais contra a API e um hub pedagógico com os dez conceitos aciona esse gatilho, e o CLAUDE.md exige ADR antes de mudança de fronteira. Este ADR amenda o ADR-006 nesse ponto.

Restrições que pesam: o repositório é monolíngue .NET, sem Node nem npm (ADR-001 e ADR-006); a demo precisa ser à prova de wifi ruim em palco; segredos de operador não podem aparecer no telão nem serem versionados; e a fonte pedagógica deve continuar publicável estaticamente no futuro, sem fragmentar o design system.

## Decisão

A documentação pedagógica e o console de demo vivem em `site/learn/`, irmãos do `site/index.html`, reusando `site/styles.css` e os tokens canônicos de `docs/design/tokens.json`, em HTML, CSS e JavaScript vanilla com módulos ES nativos, sem build e sem dependência nova.

Em Development, a Hiram.Api serve `site/` estaticamente sob o prefixo `/learn`, na mesma origem que `/scalar`, via `UseStaticFiles` com `PhysicalFileProvider`, dentro de um gate `IsDevelopment()`. A demo nunca é servida em produção. A mesma pasta continua publicável estaticamente depois, sem cópia.

O console é híbrido: modo Live dispara `fetch` same-origin contra a API local, e modo palco reproduz respostas determinísticas de fixtures, à prova de rede. O palco é o padrão. Um endpoint utilitário `POST /demo/bootstrap`, dev-only e guardado por `X-Admin-Key`, provisiona idempotentemente um tenant de demo em shadow mais uma api-key, para o apresentador não manipular a chave de operador diante da plateia.

O Swagger recebe tema completo alinhado ao design system (fontes e paleta dark e light dos tokens) e metadados para estudo (descrições de tag, summaries, exemplos no caminho crítico e security por operação), permanecendo disponível em qualquer ambiente como está hoje.

## Opções consideradas

### Opção A: `site/learn/` servido pela Api em dev, publicável estático (escolhida)

**Prós:** fonte única real, a mesma pasta é servida em dev e publicada estaticamente depois; reusa `site/styles.css` sem cópia; mesma origem elimina CORS; zero toolchain novo; edição com F5, sem recompilar.
**Contras:** a Api ganha `UseStaticFiles` gated a Development, um leve alargamento do composition root do host.

### Opção B: `docs/learn/` servido pela Api

**Prós:** mantém a demo perto da documentação de produto.
**Contras:** fragmenta o design system, exige copiar ou reimportar `styles.css` para fora de `site/`, criando duas fontes de CSS. Rejeitada por duplicação.

### Opção C: site estático separado consumindo a Api via CORS

**Prós:** mantém a fronteira do ADR-006 intocada.
**Contras:** exige política de CORS na Api (pipeline novo, ADR à parte) e dois processos para a demo de dev. Rejeitada; mesma origem é mais simples e não pede novo ADR.

### Opção D: página dentro do Hiram.Portal (Blazor, F5)

**Prós:** cem por cento .NET.
**Contras:** o Portal não existe ainda e é admin interno; acoplaria a vitrine a um app stateful e adiaria a entrega para F5. Rejeitada, mesma lógica do ADR-006.

## Decisões de borda cravadas

1. **Gate dev-only.** Estáticos de `/learn` e o `POST /demo/bootstrap` entram num único `if (app.Environment.IsDevelopment())`. `MapOpenApi` e `MapScalarApiReference` ficam fora do gate, preservando o comportamento atual em qualquer ambiente. Em produção `/learn/*` cai no 404 padrão.
2. **ApiKeyMiddleware inalterado.** O middleware já só exige `X-Api-Key` sob `/v1` fora de `/v1/admin`, então `/learn`, seus assets, `/scalar`, `/openapi` e `/demo/bootstrap` passam livres. O único header de tenant que o console injeta é o `X-Api-Key` nas chamadas a `/v1/notifications`, que é o ponto pedagógico.
3. **Sem CORS.** Mesma origem por decisão. Introduzir CORS é pipeline novo e exigiria ADR próprio; não fazer.
4. **Palco como padrão, honestidade nos conceitos não executáveis.** Os conceitos que dependem de fases ainda não prontas (cutover, ledger, IA, e a narrativa de paridade do shadow) rodam só em palco e são marcados com badge de indisponível ao vivo, sem prometer o que não existe.
5. **Bootstrap sem efeito externo.** O tenant de demo nasce em `shadow`, que exercita o pipeline inteiro sem disparar email real; a api-key é rotacionada a cada bootstrap, pois a chave clara só existe na emissão. A `X-Admin-Key` nunca é versionada nem embutida no HTML.
6. **Fonte única de tokens.** `site/learn/console.css` e o tema do Scalar consomem apenas as CSS vars já derivadas de `docs/design/tokens.json`; nenhuma cor nova entra sem passar primeiro pela fonte canônica, mantendo a mitigação de drift do ADR-006.

## Consequências

- **Fica mais fácil:** demonstrar os fluxos reais em palco de forma confiável; estudar a API num Swagger temático e didático; um `dotnet run` entrega API, Swagger e demo juntos, na mesma origem.
- **Fica mais difícil:** o host da Api carrega serving estático gated a Development e um endpoint utilitário de demo, superfície que precisa ficar contida no gate; as fixtures do palco exigem manutenção quando os contratos mudam.
- **A revisitar:** se o hub pedagógico crescer para muitas páginas ou exigir i18n, reabrir a escolha de gerador estático, o mesmo gatilho do ADR-006.

## Gatilho de revisão

Necessidade de servir a demo fora de Development, de apontar o console para outra origem (que traria CORS), ou de um volume de conteúdo pedagógico que a autoria manual em HTML não sustente.

## Itens de ação

1. [x] Servir `site/` sob `/learn` na Hiram.Api, gated a Development, com `site/learn/index.html` (passo 1).
2. [x] Hub pedagógico dos dez conceitos, diagrama E2E e console híbrido Live mais palco (passos 2 a 5, 7).
3. [x] Endpoint `POST /demo/bootstrap` dev-only, idempotente, guardado por `X-Admin-Key` (passo 6).
4. [x] Tema completo do Scalar e metadados OpenAPI para estudo (passos 8 a 10).
