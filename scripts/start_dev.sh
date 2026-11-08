#!/usr/bin/env bash
# Starts the API on :5000 and the Angular dev server on :4200 (with /api proxied to the API).
# Usage: scripts/start_dev.sh [--api-only]
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"

require dotnet "Install the .NET 10 SDK"
# shellcheck disable=SC1091
if [ -f .env ]; then set -a; . ./.env; set +a; fi
if [ -z "${BALLAST_SECRET_KEY:-}" ]; then
  suffix="$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')"
  export BALLAST_SECRET_KEY="dev-only-${suffix}"
  warn "BALLAST_SECRET_KEY not set, using a throwaway key for this session"
fi
export BALLAST_ENV="${BALLAST_ENV:-development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:5000}"

pids=()
cleanup() { for p in "${pids[@]}"; do kill "$p" 2>/dev/null || true; done; }
trap cleanup EXIT INT TERM

header "API on $ASPNETCORE_URLS"
dotnet run --project code/backend/app/Ballast.Api.csproj --no-launch-profile &
pids+=("$!")

if [ "${1:-}" != "--api-only" ]; then
  require npm "Install Node.js 22 or newer"
  header "Web on http://localhost:4200"
  (cd web-frontend && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npx ng serve) &
  pids+=("$!")
fi

ok "Demo users (development only): treasurer@ballast.example, approver@ballast.example, approver2@ballast.example, admin@ballast.example, analyst@ballast.example"
ok "Demo password: ${BALLAST_DEMO_PASSWORD:-Ballast-Demo-2026!}"
wait
