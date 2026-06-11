# Hiram, identidade e assinatura de marca

> v0.1, 2026-06-10. Este documento define a assinatura da marca: um conjunto pequeno e coeso de sinais que um irmão reconhece em qualquer país e rito, e que um profano lê apenas como bom design. A força da assinatura está na dupla leitura, nunca na explicitação.

## O nome

Hiram, o arquiteto do Templo. A lenda central da maçonaria universal trata de uma palavra que se perde porque seu portador morreu sem entregá-la, e morreu por se recusar a entregá-la a quem não tinha direito. O produto é a inversão técnica da lenda: com o outbox, a palavra nunca se perde; com autenticação, só é entregue a quem tem direito. Integridade de entrega e controle de acesso, os dois pilares da plataforma, contados por uma história que qualquer irmão reconhece de imediato.

## A assinatura em quatro camadas

### Camada 1, wordmark: Hiram∴

O nome encerrado pelos três pontos em triângulo (U+2234). Para o irmão, é a abreviação da escrita maçônica, reconhecida da França ao Brasil. Para o profano, são três pontos, o símbolo universal de mensagem chegando, o typing indicator. Nenhuma outra marca do sistema carrega tanto com tão pouco.

Uso: wordmark no cabeçalho do site e das docs e na capa de artigos. Nunca dentro de texto técnico, nome de pacote, namespace, endpoint ou identificador, por legibilidade, busca e renderização.

O símbolo oficial da marca é o **envelope monoline com linhas de movimento**, escolhido pelo fundador em 2026-06-11 (docs/design/hiram-logo.svg, versão de traço grosso para favicon e avatar em hiram-logo-small.svg). Com o símbolo neutro, o peso do reconhecimento maçônico passa integralmente ao wordmark Hiram∴ e às camadas 2 a 4. O monograma do pórtico (hiram-mark.svg, hiram-mark-outline.svg) permanece no repositório como marca alternativa, sem status oficial.

### Camada 2, cor: azul de loja e ouro

Paleta primária em azul profundo com acento dourado, as cores universais da loja simbólica. O profano lê uma paleta corporativa de confiança, padrão de fintech. Ponto de partida: azul #1B3A6B como primário, ouro #C9A227 como acento, neutros quentes de apoio. Escala tonal completa, temas (abóbada e pedra) e componentes em docs/design/DESIGN-SYSTEM.md, com tokens canônicos em docs/design/tokens.json.

### Camada 3, tagline: a palavra não se perde

Em inglês: the word is never lost. É simultaneamente a proposta de valor literal do produto (entrega garantida por outbox) e a referência que todo mestre reconhece no primeiro segundo. Usar na home, na primeira linha do README e no fechamento dos artigos da série.

### Camada 4, assinaturas de uso

Sinais que ninguém anuncia e que o irmão descobre usando o produto:

- A seção de autenticação da documentação chama-se **Handshake**. Termo técnico legítimo em qualquer API; reconhecimento mútuo para quem sabe ler.
- O header de assinatura dos webhooks é `X-Hiram-Signature`. A assinatura criptográfica da casa é, literalmente, a assinatura da casa.
- O changelog público chama-se **Trestleboard**, o boletim que a loja envia aos irmãos. O nome que a marca perdeu para o registro vive aqui.
- O rodapé do site e das docs traz o ano civil e o Anno Lucis: `© 2026 · A∴L∴ 6026`.
- Os modos de autonomia da IA chamam-se, no produto, **Aprendiz**, **Companheiro** e **Mestre**. Os valores técnicos da API permanecem `off`, `assist`, `auto`.
- A página 404 das docs diz: "A palavra que procuras se perdeu." Único lugar do produto onde a lenda aparece em texto.
- A porta padrão da API no ambiente de dev é **3357**: três, cinco e sete.

## Regras de discrição

1. **A API é profana por definição.** Endpoints, campos, status, erros e logs usam vocabulário técnico claro e universal. A assinatura mora na marca e na documentação, nunca na semântica. Nenhum dev paga custo cognitivo pelo que não precisa ver.
2. **Proibida iconografia explícita.** Sem esquadro e compasso, sem a letra G, sem avental, sem templo. A assinatura é para quem sabe ler, não um outdoor, e a discrição é parte do respeito à ordem.
3. **Easter eggs têm orçamento fechado.** Os desta página e mais nenhum. Excesso transforma assinatura em parque temático e mata a elegância.
4. **Apenas domínio público.** Todos os sinais usados aqui são amplamente documentados e abertos. Nada de conteúdo reservado, em nenhuma hipótese.

## Aplicação imediata

- README do repositório abre com o wordmark Hiram∴ e a tagline.
- F0 já usa a porta 3357 (ver plans/F0-walking-skeleton.md).
- F1 implementa `X-Hiram-Signature` nos webhooks quando eles nascerem (F2) e o Scalar ganha a seção Handshake.
- F5 aplica a paleta no Portal e publica o Trestleboard.
- Antes de qualquer uso comercial: verificação de disponibilidade em registro.br, INPI e domínios (hiram.dev, usehiram.com, hiram.app), além da org no GitHub.
