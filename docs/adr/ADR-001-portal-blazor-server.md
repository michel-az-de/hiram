# ADR-001: Blazor Server no Portal admin

**Status:** Aceito
**Data:** 2026-06-10
**Decisores:** Felipe (arquiteto)

## Contexto

O Hiram.Portal é o admin interno da plataforma: gestão de tenants, API keys, templates, dashboards de uso e status de entrega em tempo quase real. Público pequeno (operador e, futuramente, admins de tenant), interatividade alta. O projeto inteiro é .NET e o portfolio quer provar profundidade em arquitetura .NET, não amplitude fullstack JS. O fluxo de desenvolvimento usa Claude Code, que rende mais em repositório monolíngue.

## Decisão

Portal em Blazor com modelo unificado do .NET 8+: SSR estático por padrão e ilhas Interactive Server apenas onde há interatividade real (grids, dashboards, editores). Sem WebAssembly nesta fase.

## Opções consideradas

### Opção A: ASP.NET Core MVC

| Dimensão | Avaliação |
|---|---|
| Complexidade | Baixa no início, alta conforme a interatividade cresce |
| Custo | Zero licença, alto custo de manutenção do JS acumulado |
| Aderência ao caso | Fraca para dashboard e edição interativa |
| Familiaridade | Alta |

**Prós:** modelo simples, request/response previsível, time-to-first-page ótimo.
**Contras:** um admin interativo em MVC vira Razor + camadas de jQuery/Alpine/htmx, um híbrido sem identidade que envelhece mal e não conta história de portfolio.

### Opção B: React SPA + API

| Dimensão | Avaliação |
|---|---|
| Complexidade | Alta (segundo toolchain, build, estado, auth no front) |
| Custo | npm supply chain e manutenção de dependências |
| Aderência ao caso | Boa para UI, excessiva para admin interno |
| Familiaridade | Média |

**Prós:** ecossistema enorme, padrão de mercado para produto customer-facing.
**Contras:** duplica contratos (TS vs C#), quebra o repo monolíngue, sinaliza fullstack JS quando o posicionamento é arquiteto .NET, e um React mediano enfraquece a narrativa em vez de fortalecer.

### Opção C: Blazor (SSR + Interactive Server)

| Dimensão | Avaliação |
|---|---|
| Complexidade | Média, dentro do ecossistema já dominado |
| Custo | Zero toolchain extra |
| Aderência ao caso | Alta para admin interativo de baixo volume |
| Familiaridade | Média, com transferência direta de C# |

**Prós:** C# de ponta a ponta, DTOs e validadores compartilhados com a API, zero npm, render modes resolvem a objeção clássica de tudo ser stateful, peça forte de portfolio (.NET de ponta a ponta).
**Contras:** circuito Server exige conexão persistente e sticky session para escalar horizontalmente, irrelevante para um admin de poucas sessões; latência percebida depende da rede do operador.

## Análise de trade-off

A disputa real é B vs C e se decide pelo objetivo do portfolio e pelo perfil do produto. Admin interno de baixo volume não exercita as forças do React (escala de UI, time de front dedicado) e paga todos os custos dele. O ponto fraco do Blazor Server (estado por circuito) só dói em escala de usuários simultâneos que este Portal não terá.

## Consequências

- Fica mais fácil: compartilhar contratos e validação com a API, manter um único toolchain, evoluir com Claude Code.
- Fica mais difícil: aproveitar bibliotecas de UI do ecossistema React; contratar ajuda externa de front puro.
- Mitigação: componentes de UI via ecossistema Blazor (ex.: MudBlazor) avaliados sob ADR próprio antes de adotar.

## Gatilho de revisão

Se o Portal virar produto customer-facing com centenas de sessões simultâneas, ou se nascer um dev portal público rico em interação, reavaliar com React ou Blazor WebAssembly em ADR novo. A heurística do strangler fig vale aqui também: migração dirigida por fricção medida, não por estética.

## Itens de ação

1. [ ] F5: criar Hiram.Portal com SSR padrão e ilhas interativas explícitas.
2. [ ] Compartilhar Hiram.Contracts entre Api e Portal.
