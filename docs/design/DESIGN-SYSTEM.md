# Hiram, design system

> v0.1, 2026-06-10. Expressão visual da assinatura definida em docs/BRAND.md. Fonte canônica de tokens em docs/design/tokens.json. Referência viva em docs/design/reference.html.

## Conceito: a pedra e o céu

Dois temas, uma narrativa. O tema escuro é a **abóbada estrelada**, o teto da loja: azul-noite profundo com luzes em ouro. O tema claro é a **pedra polida**: neutros quentes de calcário com tinta azul-loja. O escuro é o tema padrão, porque o produto é dev-facing e a abóbada é a cara da marca.

Diferenciação deliberada do EasyStok: nada de indigo, slate, laranja, cubo ou Inter. O EasyStok é diurno, comercial e quente; o Hiram é noturno, cerimonial e sóbrio. As duas marcas não podem ser confundidas nem em miniatura.

## Logo

O símbolo oficial é o **envelope monoline com linhas de movimento**, em traço de 3.5 sobre a abóbada (ouro) ou sobre a pedra (lodge-700). Arquivos: `hiram-logo.svg` (símbolo completo, herda cor via currentColor) e `hiram-logo-small.svg` (traço grosso, sem linhas de movimento, para favicon, avatar e qualquer aplicação abaixo de 48px, onde linha fina desaparece). O monograma do pórtico (`hiram-mark.svg`, `hiram-mark-outline.svg`, `hiram-badge.svg`) permanece como marca alternativa de arquivo, sem uso oficial.

O wordmark é **HIRAM∴** em Space Grotesk, caixa alta, tracking 0.32em, peso 600. O espaçamento largo dá a qualidade de inscrição em pedra sem recorrer a serifa romana. O ∴ encerra o nome, sempre na mesma cor do texto, nunca colorido separadamente.

Proibições de logo: nunca distorcer, nunca aplicar gradiente, nunca aproximar de esquadro e compasso ou qualquer iconografia explícita (regra 2 do BRAND.md). Área de respiro mínima: a largura de uma coluna do monograma.

## Cor

Quatro famílias proprietárias e duas semânticas. Escalas completas no tokens.json.

| Família | Papel | Âncoras |
|---|---|---|
| `lodge` | Azul de loja. Estrutura, ações no tema claro, fundos no escuro | 400 #5D86C2 · 700 #1B3A6B · 950 #0A1428 |
| `orient` | Ouro. Acento cerimonial, CTA primário no escuro, foco | 300 #DFC25B · 500 #C9A227 · 700 #86691B |
| `ashlar` | Pedra. Neutros quentes, superfícies do tema claro, texto | 50 #F8F7F4 · 500 #8A867A · 900 #262522 |
| `acacia` | Verde. Sucesso e entrega confirmada | 500 #6C9B5D · 700 #426637 |
| `danger` | Terracota discreta para falha | 500 #B3402F |
| `warning` | Âmbar terroso, distinto do ouro | 500 #B45309 |

Regras de uso: o ouro é cerimonial, nunca decorativo. Um CTA primário, o anel de foco, um destaque por tela. Se o ouro aparece três vezes na mesma vista, uma delas está errada. Status do domínio: `delivered` acacia, `queued` lodge-300/400, `failed` danger, `suppressed` ashlar.

## Tipografia

| Papel | Fonte | Pesos | Uso |
|---|---|---|---|
| Display | Space Grotesk | 500, 600, 700 | Títulos, wordmark, números de destaque |
| Corpo e UI | IBM Plex Sans | 400, 500, 600 | Texto corrido, labels, controles |
| Mono | IBM Plex Mono | 400, 500 | Código, chaves, payloads, tabelas técnicas |

A narrativa: Space Grotesk é geometria de compasso e esquadro; Plex é sobriedade institucional; o mono é cidadão de primeira classe porque o produto fala com devs. O tratamento memorável é o **estilo lapidar**: caixa alta + tracking 0.32em + tamanho pequeno (12 a 13px) para eyebrows, labels de seção e o wordmark. Nunca aplicar caixa alta em texto corrido.

Escala (base 16): 12, 14, 16, 18, 20, 24, 32, 40, 56. Line-height 1.6 no corpo, 1.15 no display.

## Espaço, forma e profundidade

- Espaçamento base 4px: 4, 8, 12, 16, 24, 32, 48, 64, 96.
- Radius: 6 (controles), 10 (cards), 14 (modais), full (pills).
- **Canto lapidado (ashlar cut)**: a assinatura formal do sistema. Um único canto superior direito chanfrado a 45° (16px) via clip-path, aplicado somente a cards de destaque e ao badge da marca. É a ousadia única do sistema; todo o resto permanece quieto. Nunca chanfrar botões, inputs ou mais de um elemento por vista.
- Profundidade: no escuro, hierarquia por bordas (rgba de lodge-200 a 8 e 14%) e variação de superfície, sem sombras. No claro, sombras frias quase invisíveis.
- Textura: o **pavimento mosaico** (xadrez em 4% de opacidade) aparece apenas em hero e footer. Nunca atrás de texto denso.

## Temas

| Token | Escuro (abóbada) | Claro (pedra) |
|---|---|---|
| bg | #0A1428 | #F8F7F4 |
| surface | #0F1B30 | #FFFFFF |
| raised | #142440 | #F0EEE8 |
| text | #E8ECF4 | #1C2433 |
| text-muted | #94A3BD | #6B675D |
| border | rgba(179,200,231,.14) | #E2DFD6 |
| action-primary | orient-500 (texto lodge-950) | lodge-700 (texto branco) |
| link e foco | orient-300 | lodge-600, foco orient-600 |

## Componentes essenciais

- **Botões**: primário (ouro no escuro, lodge no claro), secundário (outline), ghost, danger. Altura 40px, radius 6, peso 500, sem caixa alta.
- **Inputs**: superfície sutil, borda de 1px, foco com anel ouro de 2px. Label em lapidar 12px.
- **Badges de status**: pill, fundo na cor a 14%, texto na cor 300 (escuro) ou 700 (claro), ponto de 6px à esquerda.
- **Card ashlar**: card de destaque com canto lapidado, para a marca, planos e momentos cerimoniais.
- **Code block**: sempre escuro nos dois temas (abóbada), Plex Mono 13px, é a vitrine do produto.
- **Tabelas**: linhas com borda inferior fina, números em mono, sem zebra.

## Voz na interface

Sóbria, precisa, ativa. Sentence case em tudo exceto o wordmark e labels lapidares. Verbos exatos nos controles (Enviar notificação, Revogar chave). Erros dizem o que aconteceu e como resolver, sem desculpas e sem mistério. A lenda só aparece onde o BRAND.md autoriza (404 das docs). Nenhum jargão maçônico na semântica da interface.

## Checklist de conformidade

1. Contraste AA mínimo em texto e controles nos dois temas.
2. Foco visível (anel ouro) em todo elemento interativo.
3. Ouro no máximo uma vez por momento de tela como ação.
4. Um canto lapidado por vista, no máximo.
5. Nenhuma cor, fonte ou forma do EasyStok.
6. Reduced motion respeitado; transições entre 150 e 250ms.
