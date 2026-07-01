# Hiram, design system

> v0.2, 2026-06-30. Expressão visual da assinatura definida em docs/BRAND.md. Fonte canônica de tokens em docs/design/tokens.json. Referência viva em docs/design/reference.html. A v0.2 documenta as extensões do site de apresentação: abóbada no hero, profundidade contida, chrome de terminal e movimento.

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

## Extensões do site (v0.2)

A landing e o hub learn realizam o sistema com quatro extensões visuais, todas dentro das regras acima. As variáveis novas são de apresentação e vivem no `:root` de `site/styles.css`, sem divergir os valores de cor do tokens.json.

- **Abóbada no hero.** O token `--hero-vault`, um gradiente radial por tema, acende o topo do hero e entrega a leitura de céu que o conceito promete. Aparece só no hero, nunca atrás de texto denso, e convive com o pavimento mosaico.
- **Profundidade contida.** No escuro a hierarquia continua por borda e superfície. O realce fica reservado a dois lugares: o token `--glow`, uma sombra colorida por tema, no CTA primário, e uma elevação mínima no code block. Cards de destaque recebem um colchete de canto neutro, um detalhe de instrumento em cor de borda, que nunca substitui o canto lapidado.
- **Chrome de terminal.** O code block, vitrine do produto, ganha uma barra de topo com rótulo mono e três marcadores quadrados monocromáticos, que ecoam o traço de ponta quadrada do logo. O código permanece sempre escuro nos dois temas.
- **Tinta de acento, `--accent-ink`.** Acentos dourados de texto, como o realce do título, os números de passo, os ids de fase e as setas do fluxo, usam ouro no escuro e ouro profundo (orient-700) no claro, para garantir contraste AA. O ouro de ação segue reservado ao CTA e ao commit do trace. Logos permanecem isentos de contraste.

## Movimento

O movimento é opcional e à prova de palco. Fica todo atrás de `prefers-reduced-motion`, é estático por padrão e revela por progressão, nunca escondendo conteúdo quando o script não roda.

- **Trace do outbox.** No hero, o fluxo Accept, Persist, Relay, Deliver aparece como um diagrama vertical. O caminho é frio, em lodge; só o commit transacional e o pulso vivo usam a cor de ação do tema. Um pulso percorre o caminho e pausa no commit. Sem animação, o diagrama fica completo e legível.
- **Reveals no scroll.** Blocos de conteúdo sobem e surgem ao entrar na viewport, por IntersectionObserver. O estado revelado é o padrão do CSS: se o script não roda ou o movimento está reduzido, tudo aparece.
- **Classes de tempo.** Microinterações, como hover e troca de tema, ficam entre 150 e 250ms. Reveals e movimento ambiente podem ir até cerca de 600ms, com laços lentos para o pulso. Todos usam o easing `cubic-bezier(.2,0,0,1)`.

## Checklist de conformidade

1. Contraste AA mínimo em texto e controles nos dois temas.
2. Foco visível (anel ouro) em todo elemento interativo.
3. Ouro no máximo uma vez por momento de tela como ação.
4. Um canto lapidado por vista, no máximo.
5. Nenhuma cor, fonte ou forma do EasyStok.
6. Reduced motion respeitado. Microinterações entre 150 e 250ms; reveals e movimento ambiente até cerca de 600ms, sempre estáticos por padrão.
