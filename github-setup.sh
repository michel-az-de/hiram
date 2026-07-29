#!/usr/bin/env bash
set -euo pipefail

OWNER="${1:?usage: ./github-setup.sh <owner> <repo>}"
REPO="${2:?usage: ./github-setup.sh <owner> <repo>}"

echo "==> About, merge policy e features"
gh repo edit "$OWNER/$REPO" \
  --description "Internal multi-tenant notification gateway for .NET products and selected clients, with PostgreSQL outbox, retries, audit and replay." \
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
  --remove-topic keda --remove-topic kubernetes --remove-topic saas \
  --add-topic dotnet --add-topic csharp --add-topic aspnet-core \
  --add-topic notifications --add-topic multi-tenant --add-topic outbox-pattern \
  --add-topic postgresql --add-topic transactional-email \
  --add-topic opentelemetry --add-topic internal-tools \
  --add-topic clean-architecture --add-topic modular-monolith \
  --add-topic webhooks

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
