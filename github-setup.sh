#!/usr/bin/env bash
set -euo pipefail

OWNER="${1:?usage: ./github-setup.sh <owner> <repo>}"
REPO="${2:?usage: ./github-setup.sh <owner> <repo>}"

echo "==> About, merge policy e features"
gh repo edit "$OWNER/$REPO" \
  --description "Multi-tenant notification platform. Email, push, SMS and WhatsApp behind one API, with outbox-guaranteed delivery, credit metering, configurable AI autonomy and end-to-end OpenTelemetry. The word is never lost." \
  --enable-issues \
  --enable-wiki=false \
  --enable-projects=false \
  --enable-squash-merge \
  --enable-merge-commit=false \
  --enable-rebase-merge=false \
  --delete-branch-on-merge \
  --allow-update-branch

echo "==> Topics"
gh repo edit "$OWNER/$REPO" \
  --add-topic dotnet --add-topic csharp --add-topic aspnet-core \
  --add-topic notifications --add-topic multi-tenant --add-topic outbox-pattern \
  --add-topic rabbitmq --add-topic postgresql --add-topic redis \
  --add-topic opentelemetry --add-topic keda --add-topic kubernetes \
  --add-topic clean-architecture --add-topic modular-monolith \
  --add-topic webhooks --add-topic saas

echo "==> Dependabot"
gh api -X PUT "repos/$OWNER/$REPO/vulnerability-alerts"
gh api -X PUT "repos/$OWNER/$REPO/automated-security-fixes"

echo "==> GITHUB_TOKEN somente leitura nos workflows"
gh api -X PUT "repos/$OWNER/$REPO/actions/permissions/workflow" \
  -f default_workflow_permissions=read \
  -F can_approve_pull_request_reviews=false

echo "==> Ruleset do master: sem force push, sem deleção"
gh api -X POST "repos/$OWNER/$REPO/rulesets" --input - <<'JSON'
{
  "name": "protect-master",
  "target": "branch",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" }
  ]
}
JSON

echo ""
echo "Pronto. Passos manuais que a API nao cobre bem:"
echo "  1. Settings > General > Social preview: upload de docs/design/social-preview.png"
echo "  2. Conferir visibilidade Private em Settings > General"
echo "  3. Se rodar agentes de codigo em paralelo: editar o ruleset e ativar"
echo "     'Require a pull request before merging' ate o trabalho paralelo acabar"
