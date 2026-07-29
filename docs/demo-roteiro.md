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

## Minuto 3 a 5, ato 2: um host, uma fila durável

```bash
docker compose -f docker-compose.demo.yml ps
./demo.sh submit Bruno Enterprise
./demo.sh status <id>                  # "sent", com a tentativa registrada
```

Fala: "A API e o worker vivem no mesmo host; o PostgreSQL é a única dependência stateful. Request e
outbox gravam na mesma transação, e o worker reivindica a linha com lease. Menos peças, com a mesma
evidência de entrega."

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

## Minuto 8 a 9, ato 4: operação explícita

```bash
./demo.sh urls
curl -s "https://$DEMO_HOST/health/ready"
```

Fala: "Health, referência da API, inbox de desenvolvimento e consulta de tentativas ficam no mesmo
ponto operacional. Não há broker, cache ou segundo processo para vigiar."

## Minuto 9 a 10, fechamento

Aba `/scalar`.

Fala: "Documentação viva, gerada do código que vocês acabaram de ver rodar. O núcleo é
deliberadamente pequeno: aceite durável, entrega, auditoria e replay. Extensões só permanecem
quando um projeto real paga sua manutenção."

## Plano B, sem rede

O console em `/learn` tem modo palco com fixtures determinísticas que roda offline no navegador.
A narrativa é a mesma; perde o email chegando de verdade, mantém o fluxo visual. Se só o
projetor tem internet, a demo inteira roda no celular, a URL é pública.

## Tempos medidos no ensaio (2026-07-03)

| Ato | Tempo de máquina |
|---|---|
| fixtures do zero | ~8s |
| ato 1 (submit + idempotência + inbox) | ~10s |
| ato 2 (host único e auditoria) | ~10s |
| ato 3 (retries + DLQ + replay até sent) | ~45s |
| ato 4 (health e operação) | ~2s |
| total | ~85s |
