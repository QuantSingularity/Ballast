#!/usr/bin/env bash
# Applies the Kubernetes overlay for an environment. Dry run unless --apply is given.
# Usage: scripts/deploy_automation.sh <dev|prod> [--apply]
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"

env="${1:-}"
[ "$env" = "dev" ] || [ "$env" = "prod" ] || { fail "Usage: $0 <dev|prod> [--apply]"; exit 2; }
require kubectl "Install kubectl and configure a cluster context"

overlay="infrastructure/kubernetes/overlays/$env"
if ! kubectl -n ballast get secret ballast-secrets >/dev/null 2>&1; then
  fail "Secret ballast-secrets is missing in namespace ballast. See infrastructure/README.md"
  exit 1
fi
if [ "${2:-}" = "--apply" ]; then
  kubectl apply -k "$overlay"
  kubectl -n ballast rollout status deployment/ballast-backend --timeout=180s
  kubectl -n ballast rollout status deployment/ballast-web --timeout=120s
  ok "deployed $env"
else
  kubectl apply -k "$overlay" --dry-run=server
  warn "dry run only, pass --apply to deploy"
fi
