# ADR-006: Site de marketing estático em HTML e CSS

**Status:** Aceito
**Data:** 2026-06-26
**Decisores:** Felipe (arquiteto)

## Contexto

O Hiram precisa de presença pública para divulgação e vendas, e mais tarde para o pipeline de artigos técnicos previsto no MASTER-PLAN. Hoje não existe nenhuma página voltada ao público: o repositório é .NET puro, sem HTML servido, sem camada de SEO e sem assets web publicados. O Portal (ADR-001) é admin interno em Blazor, não é vitrine de produto, e só nasce na fase F5.

O material de marca já está pronto e fechado: tagline, paleta, tipografia e componentes vivem em docs/design (tokens.json, DESIGN-SYSTEM.md, reference.html) e as regras de discrição em docs/BRAND.md. Falta apenas uma superfície pública que aplique tudo isso.

Restrições que pesam na escolha: o repo é deliberadamente monolíngue .NET, sem Node nem npm (ver ADR-001), e o fluxo de desenvolvimento com Claude Code rende mais sem um segundo toolchain. A primeira entrega é uma landing page única, não um portal de conteúdo.

## Decisão

Site de marketing como página estática em HTML e CSS vanilla, sem Node, sem build step, em uma nova pasta `site/` na raiz do repositório. O CSS reaproveita os tokens canônicos de docs/design/tokens.json e os componentes de reference.html. A página entrega o baseline completo de SEO: title e meta description, canonical, Open Graph, Twitter Card, dados estruturados JSON-LD, robots.txt, sitemap.xml, favicons e manifest. JavaScript fica restrito ao toggle de tema, sem dependências.

`site/` fica desacoplado de `docs/` para não conflitar com uma futura publicação de documentação. A hospedagem alvo é GitHub Pages ou qualquer host estático, sem servidor de aplicação.

## Opções consideradas

### Opção A: Gerador estático (Astro, 11ty, Hugo)

| Dimensão | Avaliação |
|---|---|
| Complexidade | Média a alta (segundo toolchain, build, deploy) |
| Custo | npm supply chain e manutenção de dependências |
| Aderência ao caso | Excessiva para uma página única |
| Familiaridade | Média |

**Prós:** ótimo para um blog crescente, componentização, coleções de conteúdo, SEO em escala.
**Contras:** traz Node e npm, quebra o repo monolíngue, adiciona um build que hoje não existe, e é desproporcional para uma landing page só. A força do SSG aparece com volume de conteúdo que ainda não temos.

### Opção B: Página dentro do Hiram.Portal (Blazor SSR)

| Dimensão | Avaliação |
|---|---|
| Complexidade | Alta (host ainda inexistente, F5) |
| Custo | Acopla marketing ao admin |
| Aderência ao caso | Fraca, mistura vitrine com ferramenta interna |
| Familiaridade | Média |

**Prós:** 100% .NET, sem toolchain extra, coerente com ADR-001.
**Contras:** o Portal não existe ainda e é admin interno; servir marketing por ele acopla deploy, autenticação e ciclo de vida de coisas que devem ser independentes. Uma landing precisa de cache de CDN e disponibilidade pública que não combinam com um app de circuito stateful.

### Opção C: HTML e CSS estático vanilla em `site/`

| Dimensão | Avaliação |
|---|---|
| Complexidade | Baixa, sem build |
| Custo | Zero toolchain, zero dependências |
| Aderência ao caso | Alta para uma landing page única com SEO |
| Familiaridade | Alta, mesmo CSS já validado em reference.html |

**Prós:** zero npm, mantém o repo monolíngue, reaproveita o design system pronto, hospeda em qualquer lugar com cache trivial, e abre rápido para verificação local.
**Contras:** sem build, a sincronia dos tokens com docs/design é manual; conteúdo cresce em HTML na mão, o que não escala para um blog grande.

## Análise de trade-off

A disputa real é A vs C e se decide pelo tamanho da primeira entrega e pela restrição de toolchain. Para uma página única, o SSG paga todos os custos do Node sem exercitar suas forças. O ponto fraco da opção C, sincronizar tokens à mão, é pequeno enquanto a fonte canônica (tokens.json) tem uma página só consumindo. Quando nascer um blog de verdade, a fricção medida justifica reabrir a decisão.

## Consequências

- Fica mais fácil: publicar e versionar a vitrine, manter o repo monolíngue, abrir o site localmente sem servidor, hospedar com cache de CDN.
- Fica mais difícil: escalar para muitas páginas e um blog rico; manter os tokens sincronizados com docs/design sem automação.
- Mitigação: a fonte de tokens continua sendo docs/design/tokens.json; qualquer divergência se resolve copiando de lá, e o site referencia a mesma escala de cores e tipografia.

## Gatilho de revisão

Se o site crescer para um blog com a série de artigos do MASTER-PLAN, ou exigir i18n e muitas páginas, reavaliar um gerador estático em ADR novo. A heurística é a mesma do ADR-001: migração dirigida por fricção medida, não por estética.

## Itens de ação

1. [ ] Criar `site/` com index.html, styles.css e o baseline de SEO.
2. [ ] Reaproveitar tokens de docs/design/tokens.json e componentes de reference.html.
3. [ ] Confirmar o domínio canônico antes do uso comercial (hiram.dev é o candidato atual).
