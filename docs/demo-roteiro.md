# Roteiro da demo comercial, 10 minutos

> Issue #16. Ambiente: https://20.98.234.200.sslip.io (ADR-022). Comandos de palco:
> `deploy/demo/demo.sh` na VM. Ensaiado em 2026-07-03: 134 segundos de máquina de ponta a ponta,
> o resto do tempo é narração.

## Preparo, antes da reunião (2 minutos, fora do palco)

```bash
ssh azureuser@20.98.234.200
cd /opt/hiram/deploy/demo
./demo.sh inbox-clear
./demo.sh fixtures        # idempotente: tenants, key nova, template welcome aprovado
./demo.sh urls
```

Abas abertas no navegador: `/learn` (hub), `/mailpit` (inbox), `/scalar` (docs). Terminal com a
sessão SSH pronta. Se a chamada `fixtures` falhar, não comece a demo: algo está fora do ar.

## Minuto 0 a 1, abertura

Aba `/learn`, diagrama do fluxo ponta a ponta.

Fala: "O Hiram nasceu de um incidente real: um sistema de produção ficou mudo, notificações se
perdiam em silêncio e ninguém sabia. A causa raiz é quase universal: gravar o pedido e publicar na
fila são duas operações, e o mundo pode cair entre as duas. O Hiram fecha esse buraco com o padrão
outbox: pedido e mensagem de saída na mesma transação de banco. O que eu vou mostrar não é slide,
é o produto no ar nesta URL pública."

## Minuto 1 a 3, ato 1: provisionar, template, primeiro email

```bash
./demo.sh submit Ana Growth
./demo.sh idempotency
```

Mostrar no `/mailpit`: o email "Bem-vindo, Ana" renderizado pelo template Scriban.

Falas:
- "Tenant, API key e template foram provisionados por API em segundos. Multi-tenant desde a
  primeira migration, não retrofit."
- Sobre o `idempotency`: "duas chamadas com a mesma Idempotency-Key, o mesmo id volta nas duas e
  só existe um email. Retry de rede do seu lado nunca vira email duplicado para o seu cliente."

## Minuto 3 a 5, ato 2: derrube o broker, nada se perde

```bash
./demo.sh broker-down
./demo.sh submit Bruno Enterprise      # 202 accepted com o broker MORTO
./demo.sh status <id>                  # "accepted"
./demo.sh broker-up                    # ~25s depois o status vira "sent" sozinho
```

Fala: "Acabei de matar o RabbitMQ. A API continua aceitando com 202, porque o aceite não depende
da fila: request e outbox gravam na mesma transação Postgres. Religuei, e o relay entregou sem
nenhuma intervenção. É esse o seguro contra o incidente que originou o projeto. Nenhuma
notificação aceita se perde."

Enquanto espera o broker voltar, mostre o `status`: a transição acontece na frente do prospect.

## Minuto 5 a 8, ato 3: falha real, DLQ com razão, replay

```bash
./demo.sh provider-down
./demo.sh submit Clara Scale
# ~20s: tres tentativas com backoff exponencial, depois dead letter
./demo.sh status <id>                  # attempts 1..3 transient_failure, status dead_lettered
./demo.sh provider-up
./demo.sh replay <id>                  # 202 queued
./demo.sh status <id>                  # "sent", email no /mailpit
```

Falas:
- "Agora o provedor de email caiu. O Hiram tentou três vezes com backoff, esgotou e moveu para a
  dead letter com a razão gravada: exhausted_transient. Nada de log perdido, é estado de primeira
  classe consultável pela API."
- "DLQ aqui não é lixeira, é fila de replay: provedor voltou, um comando, e a mensagem original
  foi entregue. Auditoria completa de cada tentativa no mesmo payload."

Nota de palco: o Mailpit da demo é efêmero, a caixa zera quando o provider reinicia. Isso ajuda:
o único email na caixa é exatamente o do replay.

## Minuto 8 a 9, ato 4: a conta fecha

```bash
./demo.sh ledger
```

Fala: "Cada aceite debitou créditos num ledger append-only, na mesma transação do aceite. Quatro
notificações, dois créditos cada, oito debitados. Repare que o retry idempotente do ato 1 não
cobrou duas vezes. A fatura no fim do mês bate com o uso porque é a mesma linha de verdade."

## Minuto 9 a 10, fechamento

Aba `/scalar`.

Fala: "Documentação viva, gerada do código que vocês acabaram de ver rodar. Push web, webhooks
assinados e templates já estão no produto; SMS e WhatsApp plugam na mesma arquitetura. E para
migrar sem fé: shadow mode, o Hiram roda em paralelo registrando tudo sem enviar, vocês comparam,
e cortam quando os números provarem. Próximo passo que eu sugiro: um tenant shadow com eventos
reais de vocês por uma semana."

## Plano B, sem rede

O console em `/learn` tem modo palco com fixtures determinísticas que roda offline no navegador.
A narrativa é a mesma; perde o email chegando de verdade, mantém o fluxo visual. Se só o
projetor tem internet, a demo inteira roda no celular, a URL é pública.

## Tempos medidos no ensaio (2026-07-03)

| Ato | Tempo de máquina |
|---|---|
| fixtures do zero | ~8s |
| ato 1 (submit + idempotência + inbox) | ~10s |
| ato 2 (broker down/up até sent) | ~45s, dominado pelo boot do RabbitMQ |
| ato 3 (retries + DLQ + replay até sent) | ~45s |
| ato 4 (ledger) | ~2s |
| total | 134s |
