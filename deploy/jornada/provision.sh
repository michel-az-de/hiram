#!/usr/bin/env bash
# Idempotent onboarding of the Jornada do Candidato tenant into a running Hiram (issue #112).
# Creates the live tenant, a server api key, the approved email templates and the routines that map
# the journey's event types to them. Re-running converges instead of duplicating: the tenant id and
# the api key live in state files next to this script, an existing template is reused and approved
# again, and the admin routines endpoint answers 200 for a routine that already exists.
#
# This is the email phase. SMS and WhatsApp arrive with the Twilio slices of ADR-028, so the channel
# list is already a variable, but only email reaches a routine today.
set -euo pipefail
cd "$(dirname "$0")"

TENANT_FILE=.jornada-tenant
KEY_FILE=.jornada-key

# The environment wins over .env, and .env wins over the local dev default. The file is read one
# known name at a time, never sourced, so a stray line in it cannot execute anything.
env_default() {
  local name="$1" fallback="$2" from_file=""
  if [ -z "${!name:-}" ] && [ -f .env ]; then
    from_file=$(grep -E "^${name}=" .env | tail -1 | cut -d= -f2- || true)
  fi
  printf -v "$name" '%s' "${!name:-${from_file:-$fallback}}"
}

env_default HIRAM_BASE_URL "http://localhost:3357"
env_default HIRAM_ADMIN_KEY "admin-dev-local"
env_default HIRAM_JORNADA_API_KEY ""
env_default JORNADA_TENANT_NAME "jornada-do-candidato"
env_default JORNADA_CHANNELS "email"
env_default JORNADA_TEST_USER_IDS ""

BASE="$HIRAM_BASE_URL"

json_field() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d\" -f4; }

json_escape() {
  local value="$1"
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//$'\n'/\\n}
  printf '%s' "$value"
}

read_state() { tr -d '[:space:]' < "$1"; }

admin_post() {
  curl -fsS -X POST "$BASE$1" -H "X-Admin-Key: $HIRAM_ADMIN_KEY" -H 'Content-Type: application/json' -d "$2"
}

tenant_post_code() {
  curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE$1" \
    -H "X-Api-Key: $(api_key)" -H 'Content-Type: application/json' -d "$2"
}

require_hiram() {
  curl -fsS -o /dev/null "$BASE/health/ready" \
    || { echo "Hiram nao respondeu em $BASE/health/ready" >&2; exit 1; }
}

tenant_id() {
  [ -f "$TENANT_FILE" ] || { echo "tenant ainda nao provisionado, rode '$0 tenant' antes" >&2; exit 1; }
  read_state "$TENANT_FILE"
}

api_key() {
  [ -z "$HIRAM_JORNADA_API_KEY" ] || { printf '%s' "$HIRAM_JORNADA_API_KEY"; return; }
  [ -f "$KEY_FILE" ] || { echo "api key ausente, rode '$0 tenant' antes ou exporte HIRAM_JORNADA_API_KEY" >&2; exit 1; }
  read_state "$KEY_FILE"
}

# Only email reaches the fan-out today: POST /v1/admin/routines accepts email and push, and the
# journey's SMS and WhatsApp surfaces open with the Twilio slices of ADR-028.
resolve_channels() {
  local requested channel email_kept=""
  requested=$(printf '%s' "$JORNADA_CHANNELS" | tr ',;' '  ')
  for channel in $requested; do
    case "$(printf '%s' "$channel" | tr '[:upper:]' '[:lower:]')" in
      email) email_kept="email" ;;
      sms|whatsapp)
        echo "aviso: canal $channel ainda nao existe no Hiram (ADR-028), seguindo so com email" >&2 ;;
      *) echo "aviso: canal $channel desconhecido, ignorado" >&2 ;;
    esac
  done
  printf '%s' "${email_kept:-email}"
}

provision_tenant() {
  require_hiram

  if [ -f "$TENANT_FILE" ]; then
    echo "tenant existente: $(read_state "$TENANT_FILE")"
  else
    local id
    id=$(admin_post /v1/admin/tenants \
      "{\"name\":\"$(json_escape "$JORNADA_TENANT_NAME")\",\"deliveryMode\":\"live\"}" | json_field id || true)
    [ -n "$id" ] || { echo "resposta sem id ao criar o tenant" >&2; exit 1; }
    echo "$id" > "$TENANT_FILE"
    chmod 600 "$TENANT_FILE"
    echo "tenant criado: $id"
  fi

  if [ -n "$HIRAM_JORNADA_API_KEY" ]; then
    echo "api key veio de HIRAM_JORNADA_API_KEY, nada a emitir"
    return
  fi
  if [ -f "$KEY_FILE" ]; then
    echo "api key ja emitida, reaproveitando $KEY_FILE"
    return
  fi

  local key
  key=$(admin_post /v1/admin/api-keys \
    "{\"tenantId\":\"$(tenant_id)\",\"name\":\"jornada-emitter\"}" | json_field key || true)
  [ -n "$key" ] || { echo "resposta sem key ao emitir a api key" >&2; exit 1; }
  echo "$key" > "$KEY_FILE"
  chmod 600 "$KEY_FILE"

  # The Hiram keeps only the hash, so this is the one moment the clear value exists outside the file.
  echo
  echo "  API KEY DA JORNADA (exibida uma unica vez, o Hiram guarda so o hash):"
  echo "  $key"
  echo "  gravada em $PWD/$KEY_FILE, copie agora para o segredo do emissor."
  echo
}

create_template() {
  local name="$1" subject="$2" body="$3" payload code id
  payload=$(printf '{"channel":"email","name":"%s","subject":"%s","body":"%s"}' \
    "$(json_escape "$name")" "$(json_escape "$subject")" "$(json_escape "$body")")

  code=$(tenant_post_code /v1/templates "$payload")
  case "$code" in
    2*) echo "  template $name criado" ;;
    409) ;;
    *) echo "  falha ao criar o template $name (HTTP $code)" >&2; exit 1 ;;
  esac

  id=$(template_id "$name" || true)
  [ -n "$id" ] || { echo "  template $name nao apareceu na listagem" >&2; exit 1; }
  [ "$code" != 409 ] || sync_template "$id" "$name" "$subject" "$body"

  curl -fsS -o /dev/null -X POST "$BASE/v1/templates/$id/approve" -H "X-Api-Key: $(api_key)"
  echo "  template $name aprovado ($id)"
}

# A correction to the content has to reach whoever already ran the script, so an existing template is
# updated instead of left alone. The update bumps the template version and drops the approval, and the
# version composes the message key, so it only happens when the content really changed.
sync_template() {
  local id="$1" name="$2" subject="$3" body="$4"

  if [ "$(remote_value "$id" subject)" = "$(json_escape "$subject")" ] \
    && [ "$(remote_value "$id" body)" = "$(json_escape "$body")" ]; then
    echo "  template $name ja existia com o conteudo atual"
    return
  fi

  curl -fsS -o /dev/null -X PUT "$BASE/v1/templates/$id" \
    -H "X-Api-Key: $(api_key)" -H 'Content-Type: application/json' \
    -d "$(printf '{"subject":"%s","body":"%s"}' "$(json_escape "$subject")" "$(json_escape "$body")")"
  echo "  template $name atualizado"
}

# Compares the value as JSON, which is what both sides already have in hand. Content whose encoding
# differs from json_escape costs one extra update, never a wrong one.
remote_value() {
  curl -fsS "$BASE/v1/templates/$1" -H "X-Api-Key: $(api_key)" \
    | grep -oE "\"$2\":\"([^\"\\\\]|\\\\.)*\"" \
    | head -1 \
    | sed -E "s/^\"$2\":\"//; s/\"\$//"
}

# Splitting the array on the opening brace isolates each template: id and name are serialized before
# subject and body, so a Scriban placeholder in the body never lands in front of the name it matches.
template_id() {
  curl -fsS "$BASE/v1/templates" -H "X-Api-Key: $(api_key)" \
    | tr '{' '\n' \
    | grep "\"name\":\"$1\"" \
    | head -1 \
    | json_field id
}

provision_templates() {
  require_hiram
  # Resolve the key here so a missing state file says so, instead of surfacing as a 401 per template.
  api_key > /dev/null
  echo "== templates de email =="

  create_template "verificacao-de-email" \
    "Confirme seu e-mail na Jornada do Candidato" \
    "$(cat <<'TEXTO'
Ola,

Recebemos o seu cadastro na Jornada do Candidato. O protocolo desta solicitacao e {{ Protocolo }}.

Para confirmar o seu endereco de e-mail, acesse o link abaixo:

{{ LinkVerificacao }}

O link e valido ate {{ ExpiraEm }}. Depois disso sera necessario solicitar uma nova confirmacao.

Se voce nao reconhece este cadastro, ignore esta mensagem.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"

  create_template "candidato-encaminhado" \
    "Sua Jornada avancou: voce foi encaminhado a uma Loja" \
    "$(cat <<'TEXTO'
Ola, {{ Nome }}.

A sua candidatura de protocolo {{ Protocolo }} foi encaminhada a uma Loja, que dara seguimento ao seu processo.

Voce sera avisado por e-mail a cada nova etapa concluida. Nenhuma acao e necessaria neste momento.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"

  create_template "candidato-aprovado" \
    "Sua Jornada foi concluida com sucesso!" \
    "$(cat <<'TEXTO'
Ola, {{ Nome }}.

A sua candidatura de protocolo {{ Protocolo }} foi aprovada pela Loja e a sua Jornada esta concluida.

A Loja entrara em contato com voce para os proximos passos.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"

  create_template "candidato-recebido-pela-loja" \
    "A Loja confirmou seu recebimento" \
    "$(cat <<'TEXTO'
Ola, {{ Nome }}.

A Loja confirmou o recebimento da sua candidatura de protocolo {{ Protocolo }}.

O seu processo segue em analise e voce sera avisado a cada mudanca de etapa.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"
}

create_routine() {
  local event="$1" template="$2" channels="$3" array="" channel
  for channel in $channels; do
    array="$array${array:+,}\"$channel\""
  done

  admin_post /v1/admin/routines \
    "{\"tenantId\":\"$(tenant_id)\",\"eventType\":\"$event\",\"templateName\":\"$template\",\"channels\":[$array],\"category\":\"transactional\",\"active\":true}" \
    > /dev/null
  echo "  rotina $event -> $template [$channels]"
}

provision_routines() {
  require_hiram
  # Same reason: without the tenant id the admin call would answer 404 instead of naming the cause.
  tenant_id > /dev/null
  local channels
  channels=$(resolve_channels)
  echo "== rotinas (canais: $channels) =="

  create_routine "VerificacaoDeEmailSolicitada" "verificacao-de-email" "$channels"
  create_routine "CandidatoEncaminhado" "candidato-encaminhado" "$channels"
  create_routine "CandidatoAprovadoPelaLoja" "candidato-aprovado" "$channels"
  create_routine "CandidatoStatusAlterado" "candidato-recebido-pela-loja" "$channels"
}

# Transactional email already passes by legitimate interest, so this is only for the test users that
# need an explicit record, and for the day a category without that default is added to the journey.
provision_consent() {
  require_hiram
  local ids user code
  ids=$(printf '%s' "$JORNADA_TEST_USER_IDS" | tr ',;' '  ')
  if [ -z "${ids// /}" ]; then
    echo "== consentimento: JORNADA_TEST_USER_IDS vazio, nada a registrar =="
    return
  fi
  api_key > /dev/null

  echo "== consentimento de email transacional =="
  for user in $ids; do
    code=$(tenant_post_code /v1/consent \
      "{\"userId\":\"$user\",\"channel\":\"email\",\"category\":\"transactional\",\"optIn\":true}")
    case "$code" in
      2*) echo "  opt-in registrado para $user" ;;
      *) echo "  falha ao registrar o opt-in de $user (HTTP $code)" >&2; exit 1 ;;
    esac
  done
}

case "${1:-help}" in
  tenant)    provision_tenant ;;
  templates) provision_templates ;;
  routines)  provision_routines ;;
  consent)   provision_consent ;;
  all)
    provision_tenant
    provision_templates
    provision_routines
    provision_consent
    echo "== jornada provisionada no tenant $(tenant_id) =="
    ;;
  *)
    echo "uso: $0 {tenant|templates|routines|consent|all}"
    echo
    echo "  tenant     cria o tenant live e emite a api key (guardados em $TENANT_FILE e $KEY_FILE)"
    echo "  templates  cria e aprova os templates de email da jornada"
    echo "  routines   liga cada eventType da jornada ao seu template"
    echo "  consent    registra opt-in de email transacional para JORNADA_TEST_USER_IDS"
    echo "  all        executa os quatro na ordem, seguro para repetir"
    ;;
esac
