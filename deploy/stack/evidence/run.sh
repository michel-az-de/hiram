#!/usr/bin/env bash
# Reproducible evidence run for the joint stack: trigger a newsletter signup on Levante and capture
# the artifacts that prove the event reached Hiram and was delivered. Run from an already-up stack
# (docker compose up -d) that has been provisioned (provision-levante.sh) with HIRAM_LEVANTE_API_KEY.
set -euo pipefail
cd "$(dirname "$0")/.."

PROJECT="levante-hiram"
NET="${PROJECT}_default"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="evidence/artifacts/$STAMP"
EMAIL="prospect-$(date +%s)@example.com"
mkdir -p "$OUT"

echo "A1: compose ps"
docker compose ps > "$OUT/01-compose-ps.txt"

echo "Trigger: newsletter signup for $EMAIL (levante-api /newsletter, in-network)"
# The public path is browser -> Caddy -> levante-web (BFF) -> levante-api. Here we hit levante-api
# in-network for a deterministic trigger; swap for a curl against https://$SITE_HOST if the web form
# route is known. A throwaway curl container joins the compose network.
docker run --rm --network "$NET" curlimages/curl:8.11.0 -sS -X POST \
  http://levante-api:8080/newsletter -H 'content-type: application/json' \
  -d "{\"email\":\"$EMAIL\"}" -w '\nHTTP %{http_code}\n' | tee "$OUT/03-signup.txt"

echo "Waiting for the relay to emit and Hiram to deliver..."
sleep 8

echo "A4: Mailpit message"
curl -sS "http://127.0.0.1:8025/api/v1/search?query=to%3A$EMAIL" > "$OUT/04-mailpit.json" || true

echo "A6: Hiram DeliveryAttempt"
docker compose exec -T postgres psql -U hiram -d hiram -c \
  "select channel, outcome, created_at_utc from notifications.delivery_attempts order by created_at_utc desc limit 5;" \
  > "$OUT/06-hiram-delivery-attempt.txt" 2>&1 || true

echo "A7: Levante emission marked Enviada (outbox in Atlas; status 1 = Enviada)"
# Mongo is external (Atlas): there is no local 'mongo' service to exec into. Query Atlas with a
# throwaway mongosh container using the connection string from the environment or from .env.
MONGO_URI="${MONGO_CONNECTION_STRING:-}"
if [ -z "$MONGO_URI" ] && [ -f .env ]; then
  MONGO_URI="$(grep -E '^MONGO_CONNECTION_STRING=' .env | head -n1 | cut -d= -f2- || true)"
fi
if [ -n "$MONGO_URI" ]; then
  docker run --rm mongo:7 mongosh "$MONGO_URI" --quiet --eval \
    "db.getSiblingDB('levante').outbox.find({'dados.email':'$EMAIL'},{tipo:1,status:1,emissionSeq:1,enviadoEm:1,_id:0}).forEach(printjson)" \
    > "$OUT/07-levante-emission.txt" 2>&1 || echo "mongosh query against Atlas failed" > "$OUT/07-levante-emission.txt"
else
  echo "skipped: set MONGO_CONNECTION_STRING (or fill it in .env) to query the Atlas outbox; Mongo is external, there is no local mongo service" \
    > "$OUT/07-levante-emission.txt"
fi

cat > "$OUT/README.md" <<MD
# Evidence $STAMP (signup: $EMAIL)

| # | Artifact | Proves |
|---|----------|--------|
| A1 | 01-compose-ps.txt | all services healthy / one-shots exited(0) |
| A3 | 03-signup.txt | newsletter signup returns 202 |
| A4 | 04-mailpit.json | confirmation email captured (subject + confirm link with SITE_URL) |
| A6 | 06-hiram-delivery-attempt.txt | Hiram DeliveryAttempt row, outcome=sent |
| A7 | 07-levante-emission.txt | Levante outbox emission marked Enviada (status 1) |

Manual (Grafana over the 127.0.0.1:3000 tunnel):
- A5 trace: search service.name=levante-api, open the signup trace; the Tempo waterfall shows one
  traceId across levante-api POST /newsletter -> HTTP /v1/events -> hiram-api ingest -> publish/consume
  -> send email.
- A8 metrics: hiram notifications sent counter for event_type="assinatura_solicitada", channel="email".
MD

echo "Evidence written to $OUT"
