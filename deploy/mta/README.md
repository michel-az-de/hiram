# MTA próprio do Hiram (Stalwart)

Scaffolding do servidor de email próprio decidido no ADR-026. É opt-in e dormant: nada aqui muda o
comportamento em runtime até o dispatcher apontar para o Stalwart. O Stalwart é dono da fila, da
assinatura DKIM e das tentativas; o último salto sai por um smarthost autenticado na porta 587, porque
a porta 25 de saída está bloqueada na Azure e no Brasil (Gerência de Porta 25 do CGI.br).

Este diretório é um template. Antes de ligar, alinhe o schema do `config.toml` e os caminhos internos da
imagem com a versão do Stalwart que você pinar (a config segue o modelo atual, mas as chaves evoluem
entre versões). Consulte a documentação do Stalwart.

## Pré-requisitos (não são código, ver ADR-026 trilha A)

- Domínio próprio registrado (candidato `hiram.dev`, confirmar disponibilidade; `.com.br` como fallback).
- Host dimensionado. Na VM de demo B2s/4GB, só suba o Stalwart se houver pelo menos 700MB livres
  sustentados com Postgres, Redis, RabbitMQ e API já rodando; senão, host dedicado.
- Uma conta de relay/smarthost que aceite submissão autenticada na 587 (SES SMTP ou relay dedicado).

## Passo a passo

1. `cp .env.mta.example .env` e preencha (o `.env` é git-ignored).
2. Gere a chave DKIM RSA-2048 em `./stalwart/dkim/hiram.key` (git-ignored, nunca commitada):
   `openssl genrsa -out stalwart/dkim/hiram.key 2048`
   e extraia a parte pública para publicar no DNS:
   `openssl rsa -in stalwart/dkim/hiram.key -pubout`
3. Publique os registros DNS no domínio remetente (ver abaixo).
4. Suba com o profile: `docker compose --profile mta up -d` (na mesma rede do dispatcher, ver "Rede").
5. Aponte o dispatcher para o Stalwart trocando o env do serviço (o caminho zero-código do default de
   plataforma):
   ```
   Hiram__Email__Platform__Provider=smtp
   Hiram__Email__Platform__Settings__host=stalwart
   Hiram__Email__Platform__Settings__port=587
   Hiram__Email__Platform__Settings__security=starttls
   Hiram__Email__Platform__Settings__from=notifications@<seu-dominio>
   Hiram__Email__Platform__Settings__username=dispatcher
   Hiram__Email__Platform__Secret=<MTA_SUBMISSION_SECRET>
   ```

## DNS (o que publicar no domínio remetente)

- SPF: autorize o domínio do relay a enviar em nome do seu domínio (o registro TXT SPF do relay, ex.:
  `v=spf1 include:amazonses.com -all` para SES).
- DKIM: um TXT em `<selector>._domainkey.<dominio>` com a chave pública gerada no passo 2.
- DMARC: um TXT em `_dmarc.<dominio>`, começando em `p=none` para observar e endurecer depois.
- PTR/rDNS do host de envio batendo com o `MTA_HOSTNAME`.

## Assinatura e alinhamento (crítico, ADR-026 decisão de borda M4)

O Stalwart assina DKIM para o SEU domínio. Ao mesmo tempo, o domínio remetente precisa estar verificado
no relay (SES/etc.) e o `From` precisa alinhar com o domínio assinado, senão o DMARC falha ou o relay
reescreve headers e quebra a assinatura. Garanta:

- O `From` das notificações usa o `MTA_SENDING_DOMAIN`.
- O `Return-Path`/envelope sender alinha com o mesmo domínio (paridade de envelope).
- O relay não re-assina de forma conflitante (configure o relay para não reescrever o From).

## Rede

O compose é standalone (rede própria). Para o dispatcher alcançar `stalwart:587`, os dois precisam
compartilhar uma rede Docker. Duas opções:

- Adicionar o serviço `stalwart` ao compose onde o dispatcher roda (ex.: `deploy/demo`), reaproveitando a
  rede default daquele projeto.
- Criar uma rede externa e anexar os dois serviços a ela.

## Segurança

- Segredos só via `.env` (relay e submission) e via arquivo montado (chave DKIM). Nunca inline no
  `config.toml`, nunca commitados. O `.gitignore` já cobre `deploy/mta/**/*.key`, `*.pem` e o `.env`.
- O relay exige TLS com certificado válido (`tls.allow-invalid-certs = false`).
- A submissão fica na rede interna, sem porta publicada no host, e a porta 25 nunca é vinculada.

## Verificação

- `docker compose --profile mta config` valida o YAML do profile (profiles opt-in escapam do render
  default do CI, então valide explicitamente).
- Depois de ligar de verdade (com domínio e relay), o aceite do PoC está no ADR-026 trilha A: 100% de
  aprovação de SPF/DKIM/DMARC e score de mail-tester maior ou igual a 8 de 10, com warmup como
  pré-requisito da taxa de inbox.
