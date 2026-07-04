# Stack conjunta Levante + Hiram (VM)

Sobe os dois serviços numa VM via um único `docker-compose`: o Levante (blog, .NET + MongoDB +
Next) emite eventos para o Hiram (notificações, .NET + Postgres) por `POST /v1/events`, e o Hiram
entrega e-mail no Mailpit. Observabilidade unificada num único coletor OTLP (`grafana/otel-lgtm`),
com trace único Levante → Hiram → provider. Só o Caddy (80/443) é público; UIs de gestão ficam em
`127.0.0.1` (acesso por túnel SSH).

## Pré-requisitos

1. **Imagens no GHCR**: `hiram-api`, `hiram-dispatcher`, `levante-api` e `levante-web`. Os três
   primeiros já são publicados pelos CIs. **`levante-web` exige estender o CI do Levante para
   buildar/publicar a imagem** (o Dockerfile existe em `src/web/Dockerfile`); enquanto isso, use o
   escape hatch de build local (descomente o bloco `build:` de `levante-web` e rode com
   `LEVANTE_IMAGE_TAG=local`).
2. **Domínio + TLS**: `SITE_HOST`/`SITE_URL`/`ACME_EMAIL` no `.env`. GAP-A ainda aberto: até o
   domínio final, um host `sslip.io` funciona.
3. **`.env`**: `cp .env.example .env`, preencher, `chmod 600 .env`.

## Bring-up

```bash
docker compose up -d
# Espera: mongo-rs-init, hiram-migrate, keyring-init saem com exit 0; api/web/dispatcher healthy.

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

## Go-live / rollback

- **Go-live**: fixe `HIRAM_IMAGE_TAG`/`LEVANTE_IMAGE_TAG` em SHAs revisados → `docker compose pull`
  → `docker compose up -d` → smoke (`evidence/run.sh`, confira A4/A6/A7) → abra o DNS para o host.
- **Rollback**: re-fixe os `*_IMAGE_TAG` no SHA anterior → `docker compose up -d` (imagens
  imutáveis, rollback é pull+recreate). Migration destrutiva no Hiram ⇒ restaure o Postgres do dump.

## Backups e RPO

Off-host, cron: `pg_dump` (Hiram), `mongodump` (Levante), tar do volume `keyring` (Data Protection)
e o `deploy/levante/.provision-state`. **RPO do outbox = intervalo do `mongodump`**: perda do volume
`mongodata` perde as emissões não enviadas desde o último dump. Para newsletter, dump de hora em hora
(RPO ≤ 1h) é aceitável; para RPO menor, oplog/PITR. **Nunca** `docker compose down -v` em produção
(apaga o replica set e os dados).

## Itens que exigem validação/endurecimento na VM

- **`levante-web` no CI** (publicar a imagem) ou build local.
- **Mongo single node**: replica set sem auth, só na rede interna do Docker (porta não publicada).
  Em produção real, Atlas com usuário de privilégio mínimo (regra de segurança do Levante) ou
  habilitar auth + keyfile aqui. O timing do `mongo-rs-init` (bloqueia até PRIMARY) deve ser
  confirmado na VM.
- **Provider de e-mail real**: trocar `Hiram__Email__Platform__*` do `hiram-dispatcher` para
  Resend/SMTP com domínio verificado quando o GAP-A fechar (troca de env, sem mudar topologia).
- **`/mailpit`**: proteger no Caddy (basic_auth ou IP allowlist) antes de expor de verdade.
- **Limites de recurso e proteção OOM** para os data stores (Mongo/Postgres) conforme a RAM da VM.
