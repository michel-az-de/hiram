#!/usr/bin/env bash
# Stage commands for the ten minute commercial demo (docs/demo-roteiro.md, issue #16).
# Runs on the demo VM from this directory; reads .env and keeps state files next to it.
set -euo pipefail
cd "$(dirname "$0")"

[ -f .env ] || { echo "missing .env, copy .env.demo.example first"; exit 1; }
DEMO_HOST=$(grep "^DEMO_HOST=" .env | cut -d= -f2)
ADMIN_KEY=$(grep "^HIRAM_ADMIN_KEY=" .env | cut -d= -f2)
BASE="https://$DEMO_HOST"
COMPOSE="docker compose -f docker-compose.demo.yml"

json_field() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d\" -f4; }

live_key() {
  [ -f .demo-live-key ] || { echo "run '$0 fixtures' first" >&2; exit 1; }
  cat .demo-live-key
}

case "${1:-help}" in

  # Idempotent stage setup: console tenant (shadow), live tenant with a fresh key, approved template.
  fixtures)
    echo "== bootstrap do tenant do console (shadow) =="
    curl -sf -X POST "$BASE/demo/bootstrap" -H "X-Admin-Key: $ADMIN_KEY" \
      | sed 's/hk_live_[A-Za-z0-9_-]*/hk_live_[demo key emitida, cole no console]/'
    echo

    if [ -f .demo-live-tenant ]; then
      TENANT=$(cat .demo-live-tenant)
      echo "== tenant live existente: $TENANT =="
    else
      echo "== criando tenant live =="
      TENANT=$(curl -sf -X POST "$BASE/v1/admin/tenants" -H "X-Admin-Key: $ADMIN_KEY" \
        -H "Content-Type: application/json" \
        -d '{"name":"hiram-demo-live","deliveryMode":"live"}' | json_field id)
      echo "$TENANT" > .demo-live-tenant
      echo "tenant: $TENANT"
    fi

    echo "== emitindo api key live =="
    KEY=$(curl -sf -X POST "$BASE/v1/admin/api-keys" -H "X-Admin-Key: $ADMIN_KEY" \
      -H "Content-Type: application/json" \
      -d "{\"tenantId\":\"$TENANT\",\"name\":\"demo-live\"}" | json_field key)
    echo "$KEY" > .demo-live-key && chmod 600 .demo-live-key
    echo "key gravada em .demo-live-key (${KEY:0:8}...)"

    echo "== template welcome (409 se ja existe, ok) =="
    TPL=$(curl -s -X POST "$BASE/v1/templates" -H "X-Api-Key: $KEY" \
      -H "Content-Type: application/json" \
      -d '{"channel":"email","name":"welcome","subject":"Bem-vindo, {{ name }}","body":"Ola {{ name }}, sua conta no plano {{ plan }} esta ativa. Entrega confiavel comeca no aceite: request e outbox na mesma transacao."}')
    TPL_ID=$(echo "$TPL" | json_field id)
    if [ -n "$TPL_ID" ]; then
      curl -s -o /dev/null -X POST "$BASE/v1/templates/$TPL_ID/approve" -H "X-Api-Key: $KEY"
      echo "template welcome criado e aprovado ($TPL_ID)"
    else
      echo "template welcome ja existia"
    fi
    echo "== fixtures prontas =="
    ;;

  # Submit through the approved template; prints the notification id for the follow-up commands.
  submit)
    NAME="${2:-Ana}"; PLAN="${3:-Growth}"
    curl -sf -X POST "$BASE/v1/notifications" -H "X-Api-Key: $(live_key)" \
      -H "Content-Type: application/json" \
      -d "{\"channel\":\"email\",\"recipient\":\"prospect@example.com\",\"template\":\"welcome\",\"data\":{\"name\":\"$NAME\",\"plan\":\"$PLAN\"}}"
    echo
    ;;

  # Same submit twice under one Idempotency-Key: the second answer replays the first.
  idempotency)
    IK="demo-idem-$(date +%s)"
    for i in 1 2; do
      echo "-- chamada $i (Idempotency-Key: $IK):"
      curl -sf -X POST "$BASE/v1/notifications" -H "X-Api-Key: $(live_key)" \
        -H "Idempotency-Key: $IK" -H "Content-Type: application/json" \
        -d '{"channel":"email","recipient":"prospect@example.com","template":"welcome","data":{"name":"Ana","plan":"Growth"}}'
      echo
    done
    ;;

  status)
    [ -n "${2:-}" ] || { echo "uso: $0 status <notification-id>"; exit 1; }
    curl -sf "$BASE/v1/notifications/$2" -H "X-Api-Key: $(live_key)"
    echo
    ;;

  dispatcher-down) $COMPOSE stop dispatcher  >/dev/null && echo "dispatcher PARADO: a API continua aceitando no outbox" ;;
  dispatcher-up)   $COMPOSE start dispatcher >/dev/null && echo "dispatcher de volta: o outbox volta a escoar" ;;
  provider-down) $COMPOSE stop mailpit  >/dev/null && echo "provider de email PARADO: proximas entregas viram retry e dead letter" ;;
  provider-up)   $COMPOSE start mailpit >/dev/null && echo "provider de volta: replay reentrega" ;;

  replay)
    [ -n "${2:-}" ] || { echo "uso: $0 replay <notification-id>"; exit 1; }
    curl -sf -X POST "$BASE/v1/notifications/$2/replay" -H "X-Api-Key: $(live_key)"
    echo
    ;;

  inbox-clear)
    curl -sf -X DELETE "$BASE/mailpit/api/v1/messages" >/dev/null && echo "inbox da demo zerada"
    ;;

  urls)
    echo "hub e console:  $BASE/learn"
    echo "api reference:  $BASE/scalar"
    echo "inbox (mailpit): $BASE/mailpit"
    echo "health:         $BASE/health/ready"
    ;;

  *)
    echo "uso: $0 {fixtures|submit [nome] [plano]|idempotency|status <id>|dispatcher-down|dispatcher-up|provider-down|provider-up|replay <id>|inbox-clear|urls}"
    ;;
esac
