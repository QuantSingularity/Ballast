#!/usr/bin/env bash
# Starts the published API, signs in as a demo user and checks the main endpoints.
# Usage: scripts/smoke_test.sh [port]
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"
require dotnet "Install the .NET 10 SDK"
require curl "Install curl"
require python3 "Install python3"

port="${1:-5097}"
base="http://127.0.0.1:${port}"
out="$(mktemp -d)"
dotnet publish code/backend/app/Ballast.Api.csproj -c Release -o "$out" --nologo -v quiet >/dev/null

BALLAST_ENV=development BALLAST_SECRET_KEY="smoke-test-secret-smoke-test-secret-0123" ASPNETCORE_URLS="$base" \
  Logging__LogLevel__Default=Warning dotnet "$out/Ballast.Api.dll" >"$out/api.log" 2>&1 &
pid=$!
trap 'kill "$pid" 2>/dev/null || true; rm -rf "$out"' EXIT

for _ in $(seq 1 60); do curl -fsS "$base/health" >/dev/null 2>&1 && break; sleep 0.5; done
curl -fsS "$base/health" >/dev/null || { fail "API did not start"; cat "$out/api.log"; exit 1; }
ok "health"

token="$(curl -fsS -X POST "$base/api/auth/login" -H 'content-type: application/json' \
  -d '{"email":"treasurer@ballast.example","password":"Ballast-Demo-2026!"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')"
ok "login"

code="$(curl -s -o /dev/null -w '%{http_code}' "$base/api/v1/cash/summary")"
[ "$code" = "401" ] || { fail "unauthenticated request returned $code, expected 401"; exit 1; }
ok "unauthenticated request rejected"

for path in cash/summary cash/ladder "forecast?horizonDays=30" fx/exposures fx/recommendations risk/metrics risk/limits \
            debt/covenants payments inhouse-bank/netting/preview ai/models audit/verify; do
  code="$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" "$base/api/v1/$path")"
  [ "$code" = "200" ] || { fail "GET /api/v1/$path returned $code"; exit 1; }
  ok "GET /api/v1/$path"
done
ok "smoke test passed"
