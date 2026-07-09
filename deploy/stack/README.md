# Stack conjunta Levante + Hiram (VM)

Sobe os dois serviços numa VM via um único `docker-compose`: o Levante (blog, .NET + MongoDB +
Next) emite eventos para o Hiram (notificações, .NET + Postgres) por `POST /v1/events`, e o Hiram
entrega e-mail no Mailpit. Observabilidade unificada num único coletor OTLP (`grafana/otel-lgtm`),
com trace único Levante → Hiram → provider. Só o Caddy (80/443) é público; UIs de gestão ficam em
`127.0.0.1` (acesso por túnel SSH).

## Pré-requisitos

1. **Imagens no GHCR**: `hiram-api`, `hiram-dispatcher`, `levante-api` e `levante-web` — todas
   publicadas pelos CIs (o do Levante passou a publicar `levante-api`+`levante-web`, ver Levante
   ADR 0003). Escape hatch de build local continua disponível (descomente o bloco `build:` de
   `levante-*` e rode com `LEVANTE_IMAGE_TAG=local`).
2. **Domínio + TLS**: `SITE_HOST`/`SITE_URL`/`ACME_EMAIL` no `.env`. GAP-A ainda aberto: até o
   domínio final, um host `sslip.io` funciona.
3. **MongoDB Atlas**: `MONGO_CONNECTION_STRING` no `.env` (usuário de **privilégio mínimo**); o IP
   público da VM no allowlist do Atlas. O Mongo não roda mais como container aqui.
4. **`.env`**: `cp .env.example .env`, preencher, `chmod 600 .env`.

## Bring-up

```bash
docker compose up -d
# Espera: hiram-migrate, keyring-init saem com exit 0; api/web/dispatcher healthy.

# Onboarding do tenant Levante no Hiram (via o túnel do hiram-api em 127.0.0.1:8080):
HIRAM_BASE_URL=http://127.0.0.1:8080 HIRAM_ADMIN_KEY=<HIRAM_ADMIN_KEY do .env> \
  ../levante/provision-levante.sh
# Copie a API key impressa (uma vez) para HIRAM_LEVANTE_API_KEY no .env e recrie o levante-api:
docker compose up -d levante-api

# Evidência ponta a ponta:
./evidence/run.sh
```

## Observabilidade

Grafana em `http://127.0.0.1:3000` (túnel SSH). Traces em Tempo, logs em Loki, métricas em
Prometheus, tudo no container `lgtm`. O `traceparent` é propagado automaticamente do Levante para o
Hiram pela instrumentação de HttpClient; o trace nasce no `levante-api` (para nascer no edge/BFF,
instrumentar o Next com OTel — follow-up). Labels padronizados: `event_type`/`channel`/`outcome`,
sem cardinalidade alta (nada de tenant/event_id em série de volume).

## Deploy / rollback

Deploy escopado por app: `./deploy-app.sh <levante|hiram> <sha>` fixa só o `*_IMAGE_TAG` daquele app
(`sed` ancorado, sob `flock`) e recria só os serviços dele, deixando o outro app e a infra
compartilhada (Postgres, RabbitMQ, Redis, Caddy, lgtm) intactos. O CI de cada repo chama esse script
por SSH — o do Levante fica **inerte até `DEPLOY_ENABLED=true`**.

- **Go-live**: bring-up completo (acima) → provisione o tenant → smoke (`evidence/run.sh`, confira
  A4/A6/A7) → fixe os `*_IMAGE_TAG` em SHAs revisados → abra o DNS para o host.
- **Deploy incremental**: `./deploy-app.sh levante <sha>` (ou `hiram`). Imagens imutáveis ⇒ é
  pull+recreate.
- **Rollback**: `./deploy-app.sh <app> <sha-anterior>`. Migration destrutiva no Hiram ⇒ restaure o
  Postgres do dump (rollback cobre código, não dados).

## Backups e RPO

Off-host, cron: `pg_dump` (Hiram) + tar do volume `keyring` (Data Protection do Hiram) + o
`deploy/levante/.provision-state`. **O Mongo do Levante é Atlas** — backup automático + PITR são
gerenciados lá (tier M10+), então o outbox não depende de `mongodump` de volume local. Perder o
`keyring` torna segredos de tenant/provider **indecifráveis**: backup **com restore testado** é
obrigatório. **Nunca** `docker compose down -v` em produção (apaga Postgres/RabbitMQ e o keyring).

## Itens que exigem validação/endurecimento na VM

- **`levante-web` no CI**: publicado (o CI do Levante builda `levante-api`+`levante-web`, ver Levante
  ADR 0003). Escape hatch de build local continua disponível.
- **MongoDB Atlas**: confirmar `MONGO_CONNECTION_STRING`, IP da VM no allowlist e **usuário de
  privilégio mínimo** — o boot do `levante-api` aborta em Produção se o usuário tiver role
  administrativa.
- **Provider de e-mail real**: trocar `Hiram__Email__Platform__*` do `hiram-dispatcher` para
  Resend/SMTP com domínio verificado quando o GAP-A fechar (troca de env, sem mudar topologia).
- **`/mailpit`**: fora do edge público (removido do Caddy); acesso só via túnel SSH em
  `127.0.0.1:8025`.
- **Limites de recurso/OOM**: `deploy.resources.limits.memory` nos data stores e nos serviços do
  Levante são **placeholders** — ajustar à RAM real da VM.
