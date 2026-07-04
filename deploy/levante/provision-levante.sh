#!/usr/bin/env bash
# Idempotent onboarding of the Levante tenant into a running Hiram.
#
# Creates: tenant "levante" (live), a server API key, 3 approved email templates and 3
# routines that map Levante's events to those templates. Safe to re-run.
#
# Tenant creation is not idempotent on the Hiram side (no name lookup), so the tenant id is
# persisted to a local state file and reused. The API key clear value is shown once at
# creation; on later runs pass it back via HIRAM_LEVANTE_API_KEY (from the deploy .env). Keep
# the state file and .env off-host in backup alongside the Data Protection key ring.
#
# Usage:
#   HIRAM_BASE_URL=http://localhost:8080 HIRAM_ADMIN_KEY=... ./provision-levante.sh
#   HIRAM_BASE_URL=... HIRAM_ADMIN_KEY=... HIRAM_LEVANTE_API_KEY=hk_live_... ./provision-levante.sh
set -euo pipefail

BASE_URL="${HIRAM_BASE_URL:?set HIRAM_BASE_URL}"
ADMIN_KEY="${HIRAM_ADMIN_KEY:?set HIRAM_ADMIN_KEY}"
STATE_FILE="${PROVISION_STATE_FILE:-$(dirname "$0")/.provision-state}"

command -v jq >/dev/null 2>&1 || { echo "jq is required" >&2; exit 1; }

admin_post() { curl -fsS -X POST "$BASE_URL$1" -H "X-Admin-Key: $ADMIN_KEY" -H 'Content-Type: application/json' -d "$2"; }
tenant_post() { curl -fsS -X POST "$BASE_URL$1" -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' -d "$2"; }

# Templates create returns 409 when they already exist; that is success for re-runs.
tenant_post_idem() {
  local code
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL$1" \
    -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' -d "$2")
  case "$code" in
    2*|409) return 0 ;;
    *) echo "POST $1 failed with $code" >&2; return 1 ;;
  esac
}

TENANT_ID=""
[ -f "$STATE_FILE" ] && TENANT_ID="$(grep -E '^TENANT_ID=' "$STATE_FILE" | cut -d= -f2 || true)"

if [ -z "$TENANT_ID" ]; then
  echo "Creating tenant levante (live)..."
  TENANT_ID=$(admin_post /v1/admin/tenants '{"name":"levante","deliveryMode":"live"}' | jq -r '.id')
  echo "TENANT_ID=$TENANT_ID" > "$STATE_FILE"
  echo "Saved tenant id to $STATE_FILE"
else
  echo "Reusing tenant $TENANT_ID from $STATE_FILE"
fi

API_KEY="${HIRAM_LEVANTE_API_KEY:-}"
if [ -z "$API_KEY" ]; then
  echo "Creating server API key..."
  API_KEY=$(admin_post /v1/admin/api-keys "{\"tenantId\":\"$TENANT_ID\",\"name\":\"levante-emitter\"}" | jq -r '.key')
  echo
  echo "  API KEY (store now, shown only once) -> Hiram:ApiKey / HIRAM_LEVANTE_API_KEY:"
  echo "  $API_KEY"
  echo
fi

create_template() {
  local name="$1" subject="$2" body="$3"
  tenant_post_idem /v1/templates "$(jq -n --arg n "$name" --arg s "$subject" --arg b "$body" \
    '{channel:"email", name:$n, subject:$s, body:$b}')"
  local id
  id=$(curl -fsS "$BASE_URL/v1/templates" -H "X-Api-Key: $API_KEY" | jq -r --arg n "$name" '.[] | select(.name==$n) | .id')
  curl -fsS -X POST "$BASE_URL/v1/templates/$id/approve" -H "X-Api-Key: $API_KEY" >/dev/null
  echo "  template '$name' approved"
}

create_routine() {
  local event="$1" template="$2" category="$3"
  admin_post /v1/admin/routines "$(jq -n --arg t "$TENANT_ID" --arg e "$event" --arg tn "$template" --arg c "$category" \
    '{tenantId:$t, eventType:$e, templateName:$tn, channels:["email"], category:$c, active:true}')" >/dev/null
  echo "  routine '$event' -> '$template'"
}

echo "Seeding templates..."
create_template "newsletter-confirmacao" \
  "Confirme sua inscricao na newsletter" \
  $'Ola,\n\nPara confirmar sua inscricao, acesse:\n{{ confirmUrlBase }}?token={{ token }}\n\nSe nao foi voce, ignore esta mensagem.'
create_template "newsletter-boas-vindas" \
  "Bem-vindo a newsletter do Levante" \
  $'Sua inscricao esta confirmada. Voce recebera os novos artigos do Levante.\n\nPara cancelar, use o link de descadastro no rodape de cada e-mail.'
create_template "moderacao-comentario" \
  "Novo comentario aguardando moderacao" \
  $'Um comentario esta pendente de moderacao.\n\nArtigo: {{ artigoId }}\nComentario: {{ comentarioId }}\nRecebido em: {{ dataCriacao }}\n\nAcesse o painel de moderacao.'

echo "Seeding routines..."
create_routine "assinatura_solicitada" "newsletter-confirmacao" "transactional"
create_routine "assinante_confirmado"  "newsletter-boas-vindas"  "transactional"
create_routine "comentario_pendente"   "moderacao-comentario"    "operational"

echo "Done. Levante tenant $TENANT_ID provisioned."
