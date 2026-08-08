#!/usr/bin/env bash
# Idempotent onboarding of the Jornada do Candidato tenant into a running Hiram (issue #112, #123).
# Creates the live tenant, a server api key, the per channel provider config, the approved templates and
# the routines that map the journey's event types to them. Re-running converges instead of duplicating:
# the tenant id and the api key live in state files next to this script, an existing template is reused
# and approved again, the provider config is an upsert, and the admin routines endpoint answers 200 for
# a routine that already exists.
#
# Email, SMS and WhatsApp all reach the fan-out now (ADR-028). JORNADA_CHANNELS picks the mix; the
# email verification link is the one message that stays on email whatever the mix asks for.
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

# One Twilio account and one api key serve the three channels; only the sender and the trial content
# differ per channel. The secret is never echoed, not even inside an error message.
env_default TWILIO_ACCOUNT_SID ""
env_default TWILIO_API_KEY_SID ""
env_default TWILIO_API_KEY_SECRET ""
env_default TWILIO_SMS_FROM ""
env_default TWILIO_WHATSAPP_FROM ""
env_default TWILIO_TRIAL_MODE "false"
env_default TWILIO_SMS_TRIAL_TEMPLATE ""
env_default USE_TWILIO_EMAIL "false"
env_default TWILIO_EMAIL_FROM ""
env_default TWILIO_EMAIL_FROM_NAME ""
env_default TWILIO_EMAIL_TRIAL_SUBJECT ""
env_default TWILIO_EMAIL_TRIAL_HTML ""

# A value still holding the placeholder of .env.jornada.example means unconfigured. Taking it as real
# would write a provider config that could only fail at send time, which is worse than no config: the
# skip says so during the provisioning, the config says so one dead letter later.
for twilio_name in TWILIO_ACCOUNT_SID TWILIO_API_KEY_SID TWILIO_API_KEY_SECRET TWILIO_SMS_TRIAL_TEMPLATE \
  TWILIO_EMAIL_FROM TWILIO_EMAIL_TRIAL_SUBJECT TWILIO_EMAIL_TRIAL_HTML; do
  case "${!twilio_name}" in
    *CHANGE_ME*) printf -v "$twilio_name" '%s' "" ;;
  esac
done
unset twilio_name

BASE="$HIRAM_BASE_URL"

json_field() { grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d\" -f4; }

json_escape() {
  local value="$1"
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//$'\n'/\\n}
  printf '%s' "$value"
}

is_true() { [ "$(printf '%s' "${1:-}" | tr '[:upper:]' '[:lower:]')" = "true" ]; }

is_e164() { printf '%s' "${1:-}" | grep -qE '^\+[1-9][0-9]{1,14}$'; }

# Channel order is not part of the contract, so comparisons happen on the sorted set.
sorted_words() { printf '%s' "${1:-}" | tr ' ' '\n' | sort | tr '\n' ' ' | sed 's/ *$//'; }

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

# Every channel here has a template surface, a routine and a resolver behind it. An unknown name is a
# typo in the mix, not a channel to attempt, so it only produces a warning.
resolve_channels() {
  local requested channel normalized kept=""
  requested=$(printf '%s' "$JORNADA_CHANNELS" | tr ',;' '  ')
  for channel in $requested; do
    normalized=$(printf '%s' "$channel" | tr '[:upper:]' '[:lower:]')
    case "$normalized" in
      email|sms|whatsapp)
        case " $kept " in
          *" $normalized "*) ;;
          *) kept="$kept${kept:+ }$normalized" ;;
        esac
        ;;
      *) echo "aviso: canal $channel desconhecido, ignorado" >&2 ;;
    esac
  done
  printf '%s' "${kept:-email}"
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

twilio_credential_ready() {
  [ -n "$TWILIO_ACCOUNT_SID" ] && [ -n "$TWILIO_API_KEY_SID" ] && [ -n "$TWILIO_API_KEY_SECRET" ]
}

# PUT /v1/providers/{channel} is an upsert, so rotating the credential is this same call again. The
# payload travels on stdin because argv of a running process is readable by other processes on the host,
# and the secret is in it.
put_provider() {
  local channel="$1" provider="$2" settings="$3" code
  code=$(printf '{"provider":"%s","settings":{%s},"secret":"%s"}' \
      "$provider" "$settings" "$(json_escape "$TWILIO_API_KEY_SECRET")" \
    | curl -sS -o /dev/null -w '%{http_code}' -X PUT "$BASE/v1/providers/$channel" \
        -H "X-Api-Key: $(api_key)" -H 'Content-Type: application/json' --data-binary @-)

  case "$code" in
    2*) echo "  $channel configurado com $provider (HTTP $code)" ;;
    # The body is never printed: a rejected payload would be echoed back with the secret in it.
    *) echo "  falha ao configurar o provider de $channel (HTTP $code)" >&2; exit 1 ;;
  esac
}

configure_sms_provider() {
  if ! twilio_credential_ready || [ -z "$TWILIO_SMS_FROM" ]; then
    echo "  aviso: sms sem TWILIO_ACCOUNT_SID, TWILIO_API_KEY_SID, TWILIO_API_KEY_SECRET ou TWILIO_SMS_FROM, pulando" >&2
    return
  fi
  is_e164 "$TWILIO_SMS_FROM" \
    || { echo "  TWILIO_SMS_FROM deve estar em E.164, como +15550000000" >&2; exit 1; }

  local settings
  settings=$(printf '"account_sid":"%s","from":"%s","api_key_sid":"%s"' \
    "$(json_escape "$TWILIO_ACCOUNT_SID")" \
    "$(json_escape "$TWILIO_SMS_FROM")" \
    "$(json_escape "$TWILIO_API_KEY_SID")")

  # In trial the carrier only accepts one of its approved messages, so the adapter sends the key of that
  # message instead of the rendered body. Without the key every send fails, so the channel is skipped.
  if is_true "$TWILIO_TRIAL_MODE"; then
    if [ -z "$TWILIO_SMS_TRIAL_TEMPLATE" ]; then
      echo "  aviso: TWILIO_TRIAL_MODE=true exige TWILIO_SMS_TRIAL_TEMPLATE, pulando sms" >&2
      return
    fi
    settings="$settings,\"trial_mode\":\"true\",\"trial_template\":\"$(json_escape "$TWILIO_SMS_TRIAL_TEMPLATE")\""
  fi

  put_provider sms twilio-sms "$settings"
}

configure_whatsapp_provider() {
  if ! twilio_credential_ready || [ -z "$TWILIO_WHATSAPP_FROM" ]; then
    echo "  aviso: whatsapp sem TWILIO_ACCOUNT_SID, TWILIO_API_KEY_SID, TWILIO_API_KEY_SECRET ou TWILIO_WHATSAPP_FROM, pulando" >&2
    return
  fi
  # The console shows the sandbox sender as "whatsapp:+1...", and the adapter adds that prefix itself.
  # Storing it here would send "whatsapp:whatsapp:+1..." and every message would be rejected.
  case "$TWILIO_WHATSAPP_FROM" in
    whatsapp:*)
      echo "  TWILIO_WHATSAPP_FROM nao leva o prefixo whatsapp:, grave so o numero E.164" >&2; exit 1 ;;
  esac
  is_e164 "$TWILIO_WHATSAPP_FROM" \
    || { echo "  TWILIO_WHATSAPP_FROM deve estar em E.164, como +15550000000" >&2; exit 1; }

  # The WhatsApp adapter has no trial mode: the sandbox takes free text inside the 24h session window.
  local settings
  settings=$(printf '"account_sid":"%s","from":"%s","api_key_sid":"%s"' \
    "$(json_escape "$TWILIO_ACCOUNT_SID")" \
    "$(json_escape "$TWILIO_WHATSAPP_FROM")" \
    "$(json_escape "$TWILIO_API_KEY_SID")")

  put_provider whatsapp twilio-whatsapp "$settings"
}

# Email already leaves by the platform provider (SMTP in the compose, the MTA in production), so this
# only runs when the operator asks for the Twilio Email API instead.
configure_email_provider() {
  if ! is_true "$USE_TWILIO_EMAIL"; then
    echo "  email: mantido no provider da plataforma, USE_TWILIO_EMAIL nao esta true"
    return
  fi
  if [ -z "$TWILIO_API_KEY_SID" ] || [ -z "$TWILIO_API_KEY_SECRET" ] || [ -z "$TWILIO_EMAIL_FROM" ]; then
    echo "  aviso: email sem TWILIO_API_KEY_SID, TWILIO_API_KEY_SECRET ou TWILIO_EMAIL_FROM, pulando" >&2
    return
  fi

  local settings
  settings=$(printf '"from":"%s","api_key_sid":"%s"' \
    "$(json_escape "$TWILIO_EMAIL_FROM")" "$(json_escape "$TWILIO_API_KEY_SID")")
  [ -z "$TWILIO_EMAIL_FROM_NAME" ] \
    || settings="$settings,\"from_name\":\"$(json_escape "$TWILIO_EMAIL_FROM_NAME")\""

  if is_true "$TWILIO_TRIAL_MODE"; then
    if [ -z "$TWILIO_EMAIL_TRIAL_SUBJECT" ] || [ -z "$TWILIO_EMAIL_TRIAL_HTML" ]; then
      echo "  aviso: TWILIO_TRIAL_MODE=true exige TWILIO_EMAIL_TRIAL_SUBJECT e TWILIO_EMAIL_TRIAL_HTML, pulando email" >&2
      return
    fi
    settings="$settings,\"trial_mode\":\"true\""
    settings="$settings,\"trial_subject\":\"$(json_escape "$TWILIO_EMAIL_TRIAL_SUBJECT")\""
    settings="$settings,\"trial_html\":\"$(json_escape "$TWILIO_EMAIL_TRIAL_HTML")\""
  fi

  put_provider email twilio-email "$settings"
}

# A channel in the mix without a provider config reaches delivery and dead letters as
# provider_not_configured, so this runs before anything is emitted. A missing credential is a warning
# and not a failure: the email only mix is the common case and it needs no Twilio at all.
provision_providers() {
  require_hiram
  api_key > /dev/null
  local channels channel
  channels=$(served_channels)
  echo "== providers (canais: $channels) =="

  for channel in $channels; do
    case "$channel" in
      email)    configure_email_provider ;;
      sms)      configure_sms_provider ;;
      whatsapp) configure_whatsapp_provider ;;
    esac
  done
}

create_template() {
  local channel="$1" name="$2" subject="$3" body="$4" payload code id
  if [ -n "$subject" ]; then
    payload=$(printf '{"channel":"%s","name":"%s","subject":"%s","body":"%s"}' \
      "$channel" "$(json_escape "$name")" "$(json_escape "$subject")" "$(json_escape "$body")")
  else
    # SMS and WhatsApp render no subject line and the endpoint answers 400 when one arrives, so the
    # field is left out of the payload instead of sent empty.
    payload=$(printf '{"channel":"%s","name":"%s","body":"%s"}' \
      "$channel" "$(json_escape "$name")" "$(json_escape "$body")")
  fi

  code=$(tenant_post_code /v1/templates "$payload")
  case "$code" in
    2*) echo "  [$channel] template $name criado" ;;
    409) ;;
    *) echo "  [$channel] falha ao criar o template $name (HTTP $code)" >&2; exit 1 ;;
  esac

  id=$(template_id "$channel" "$name" || true)
  [ -n "$id" ] || { echo "  [$channel] template $name nao apareceu na listagem" >&2; exit 1; }
  [ "$code" != 409 ] || sync_template "$channel" "$id" "$name" "$subject" "$body"

  curl -fsS -o /dev/null -X POST "$BASE/v1/templates/$id/approve" -H "X-Api-Key: $(api_key)"
  echo "  [$channel] template $name aprovado ($id)"
}

# A correction to the content has to reach whoever already ran the script, so an existing template is
# updated instead of left alone. The update bumps the template version and drops the approval, and the
# version composes the message key, so it only happens when the content really changed.
sync_template() {
  local channel="$1" id="$2" name="$3" subject="$4" body="$5" payload

  if [ "$(remote_value "$id" subject)" = "$(json_escape "$subject")" ] \
    && [ "$(remote_value "$id" body)" = "$(json_escape "$body")" ]; then
    echo "  [$channel] template $name ja existia com o conteudo atual"
    return
  fi

  if [ -n "$subject" ]; then
    payload=$(printf '{"subject":"%s","body":"%s"}' "$(json_escape "$subject")" "$(json_escape "$body")")
  else
    payload=$(printf '{"body":"%s"}' "$(json_escape "$body")")
  fi

  curl -fsS -o /dev/null -X PUT "$BASE/v1/templates/$id" \
    -H "X-Api-Key: $(api_key)" -H 'Content-Type: application/json' -d "$payload"
  echo "  [$channel] template $name atualizado"
}

# Compares the value as JSON, which is what both sides already have in hand. Content whose encoding
# differs from json_escape costs one extra update, never a wrong one. A null subject matches nothing and
# has to read as absent, not as a failed pipeline, which is what the fallback is for.
remote_value() {
  curl -fsS "$BASE/v1/templates/$1" -H "X-Api-Key: $(api_key)" \
    | { grep -oE "\"$2\":\"([^\"\\\\]|\\\\.)*\"" || true; } \
    | head -1 \
    | sed -E "s/^\"$2\":\"//; s/\"\$//"
}

# Splitting the array on the opening brace isolates each template: id, channel and name are serialized
# before subject and body, so a Scriban placeholder in the body never lands in front of the name it
# matches. The channel is part of the lookup because the same name now exists once per channel.
template_id() {
  curl -fsS "$BASE/v1/templates" -H "X-Api-Key: $(api_key)" \
    | tr '{' '\n' \
    | grep "\"channel\":\"$1\"" \
    | grep "\"name\":\"$2\"" \
    | head -1 \
    | json_field id
}

provision_templates() {
  require_hiram
  # Resolve the key here so a missing state file says so, instead of surfacing as a 401 per template.
  api_key > /dev/null
  local channels channel
  channels=$(resolve_channels)
  echo "== templates (canais: $channels) =="

  # The confirmation link belongs in an inbox: there is no verification by SMS or WhatsApp in the
  # journey, so this template exists on email whatever the mix asks for.
  create_template email "verificacao-de-email" \
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

  for channel in $channels; do
    case "$channel" in
      email)         create_email_templates ;;
      sms|whatsapp)  create_short_templates "$channel" ;;
    esac
  done
}

create_email_templates() {
  create_template email "candidato-encaminhado" \
    "Sua Jornada avancou: voce foi encaminhado a uma Loja" \
    "$(cat <<'TEXTO'
Ola, {{ Nome }}.

A sua candidatura de protocolo {{ Protocolo }} foi encaminhada a uma Loja, que dara seguimento ao seu processo.

Voce sera avisado por e-mail a cada nova etapa concluida. Nenhuma acao e necessaria neste momento.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"

  create_template email "candidato-aprovado" \
    "Sua Jornada foi concluida com sucesso!" \
    "$(cat <<'TEXTO'
Ola, {{ Nome }}.

A sua candidatura de protocolo {{ Protocolo }} foi aprovada pela Loja e a sua Jornada esta concluida.

A Loja entrara em contato com voce para os proximos passos.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"

  create_template email "candidato-recebido-pela-loja" \
    "A Loja confirmou seu recebimento" \
    "$(cat <<'TEXTO'
Ola, {{ Nome }}.

A Loja confirmou o recebimento da sua candidatura de protocolo {{ Protocolo }}.

O seu processo segue em analise e voce sera avisado a cada mudanca de etapa.

Confederacao Colunas de Luz, Jornada do Candidato.
TEXTO
)"
}

# SMS and WhatsApp carry the same short body under the same names: the template index is per name and
# channel, so the journey keeps one vocabulary and the routine does not care which one is firing. Only
# Protocolo is interpolated, which keeps the message inside one segment and asks less of the emitter.
create_short_templates() {
  local channel="$1"

  create_template "$channel" "candidato-encaminhado" "" \
    "Jornada do Candidato: seu perfil foi apresentado a uma Loja Confederada. Protocolo {{ Protocolo }}."

  create_template "$channel" "candidato-aprovado" "" \
    "Parabens! Sua jornada na Colunas de Luz foi concluida. Protocolo {{ Protocolo }}. A Loja entrara em contato."

  create_template "$channel" "candidato-recebido-pela-loja" "" \
    "Jornada do Candidato: a Loja confirmou o recebimento do seu perfil. Protocolo {{ Protocolo }}."
}

create_routine() {
  local event="$1" template="$2" channels="$3" array="" channel response actual
  for channel in $channels; do
    array="$array${array:+,}\"$channel\""
  done

  response=$(admin_post /v1/admin/routines \
    "{\"tenantId\":\"$(tenant_id)\",\"eventType\":\"$event\",\"templateName\":\"$template\",\"channels\":[$array],\"category\":\"transactional\",\"active\":true}")

  # The endpoint answers with the routine already there instead of updating it, so a mix changed after
  # the first run would be ignored in silence. Comparing what came back is the only warning there is.
  actual=$(printf '%s' "$response" \
    | { grep -o '"channels":\[[^]]*\]' || true; } \
    | sed -E 's/"channels":\[//; s/\]//; s/"//g; s/,/ /g')

  if [ -n "$actual" ] && [ "$(sorted_words "$actual")" != "$(sorted_words "$channels")" ]; then
    echo "  rotina $event ja existia com [$actual] e a API nao atualiza canais; pedido era [$channels]" >&2
    return
  fi
  echo "  rotina $event -> $template [$channels]"
}

provision_routines() {
  require_hiram
  # Same reason: without the tenant id the admin call would answer 404 instead of naming the cause.
  tenant_id > /dev/null
  local channels
  channels=$(resolve_channels)
  echo "== rotinas (canais: $channels) =="

  # A verification link has nowhere to land on SMS or WhatsApp, so this one routine stays on email even
  # when the mix asks for the other channels.
  create_routine "VerificacaoDeEmailSolicitada" "verificacao-de-email" "email"
  create_routine "CandidatoEncaminhado" "candidato-encaminhado" "$channels"
  create_routine "CandidatoAprovadoPelaLoja" "candidato-aprovado" "$channels"
  create_routine "CandidatoStatusAlterado" "candidato-recebido-pela-loja" "$channels"
}

# The verification routine keeps email in play in every mix, so a provider and a consent record are due
# on it even when JORNADA_CHANNELS does not name it. Only the three journey templates and their routines
# follow the mix literally.
served_channels() {
  local channels
  channels=$(resolve_channels)
  case " $channels " in
    *" email "*) printf '%s' "$channels" ;;
    *) printf 'email %s' "$channels" ;;
  esac
}

# On email and SMS this is explicitness: transactional and operational already pass by legitimate
# interest. On WhatsApp it is the requirement, because ConsentPolicy denies that channel in every
# category without an explicit record, transactional included, and the fan-out suppresses the message.
provision_consent() {
  require_hiram
  local ids user channel category channels code
  ids=$(printf '%s' "$JORNADA_TEST_USER_IDS" | tr ',;' '  ')
  if [ -z "${ids// /}" ]; then
    echo "== consentimento: JORNADA_TEST_USER_IDS vazio, nada a registrar =="
    return
  fi
  api_key > /dev/null
  channels=$(served_channels)

  echo "== consentimento (canais: $channels) =="
  for user in $ids; do
    for channel in $channels; do
      for category in transactional operational; do
        code=$(tenant_post_code /v1/consent \
          "{\"userId\":\"$user\",\"channel\":\"$channel\",\"category\":\"$category\",\"optIn\":true}")
        case "$code" in
          2*) ;;
          *) echo "  falha ao registrar o opt-in de $user em $channel/$category (HTTP $code)" >&2; exit 1 ;;
        esac
      done
    done
    echo "  opt-in registrado para $user em [$channels], transactional e operational"
  done
}

case "${1:-help}" in
  tenant)    provision_tenant ;;
  providers) provision_providers ;;
  templates) provision_templates ;;
  routines)  provision_routines ;;
  consent)   provision_consent ;;
  all)
    provision_tenant
    provision_providers
    provision_templates
    provision_routines
    provision_consent
    echo "== jornada provisionada no tenant $(tenant_id) =="
    ;;
  *)
    echo "uso: $0 {tenant|providers|templates|routines|consent|all}"
    echo
    echo "  tenant     cria o tenant live e emite a api key (guardados em $TENANT_FILE e $KEY_FILE)"
    echo "  providers  configura o provider de cada canal de JORNADA_CHANNELS (Twilio em sms e whatsapp)"
    echo "  templates  cria e aprova os templates da jornada em cada canal pedido"
    echo "  routines   liga cada eventType da jornada ao seu template"
    echo "  consent    registra opt-in para JORNADA_TEST_USER_IDS em cada canal pedido"
    echo "  all        executa os cinco na ordem, seguro para repetir"
    ;;
esac
