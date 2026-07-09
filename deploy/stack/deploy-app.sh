#!/usr/bin/env bash
# Deploy escopado de um app na stack conjunta: fixa <APP>_IMAGE_TAG no .env e recria SO os servicos
# daquele app, deixando o outro app e a infra compartilhada (Postgres, RabbitMQ, Redis, Caddy, lgtm)
# intactos. Chamado pelo CI de cada repo via SSH, ou manualmente. Idempotente; rollback = re-rodar
# com o SHA anterior. Ver Levante docs/adr/0003.
#
# Uso: ./deploy-app.sh <levante|hiram> <tag-sha>
set -euo pipefail

APP="${1:?uso: deploy-app.sh <levante|hiram> <tag>}"
TAG="${2:?uso: deploy-app.sh <levante|hiram> <tag>}"
cd "$(dirname "$0")"

case "$APP" in
  levante) VAR="LEVANTE_IMAGE_TAG"; SERVICES="levante-api levante-web" ;;
  hiram)   VAR="HIRAM_IMAGE_TAG";   SERVICES="hiram-api hiram-dispatcher" ;;
  *) echo "app invalido: '$APP' (use levante|hiram)" >&2; exit 2 ;;
esac

# Serializa contra um deploy concorrente do outro app (ambos editam o mesmo .env).
exec 9>.deploy.lock
flock 9

# sed ancorado: toca so a linha do tag deste app; nunca segredos nem o tag do outro app.
sed -i -E "s|^${VAR}=.*|${VAR}=${TAG}|" .env

docker compose pull ${SERVICES}
docker compose up -d ${SERVICES}
echo "deploy ok: ${APP} -> ${TAG} (${SERVICES})"
