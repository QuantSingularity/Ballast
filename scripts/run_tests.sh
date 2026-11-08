#!/usr/bin/env bash
# Runs every test suite and prints a summary. Exits non-zero if any suite fails.
# Usage: scripts/run_tests.sh [--skip-web] [--skip-mobile] [--filter <name fragment>]
set -uo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT" || exit 1

skip_web=0; skip_mobile=0; filter=""
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-web) skip_web=1 ;;
    --skip-mobile) skip_mobile=1 ;;
    --filter) shift; filter="${1:-}" ;;
    *) fail "Unknown option $1"; exit 2 ;;
  esac
  shift
done

require dotnet "Install the .NET 10 SDK"
results=()
status=0

run_dotnet_suite() {
  local name="$1" project="$2"
  header "$name"
  if dotnet build "$project" -c Release --nologo -v quiet >/tmp/ballast-build.log 2>&1; then
    local dll
    dll="$(dirname "$project")/bin/Release/net10.0/$(basename "${project%.csproj}").dll"
    if dotnet "$dll" ${filter:+"$filter"}; then results+=("PASS  $name"); else results+=("FAIL  $name"); status=1; fi
  else
    cat /tmp/ballast-build.log
    results+=("FAIL  $name (build)"); status=1
  fi
}

run_dotnet_suite "AI models" code/ai_models/tests/Ballast.AiModels.Tests.csproj
run_dotnet_suite "Backend (unit and API integration)" code/backend/tests/Ballast.Backend.Tests.csproj
if [ "$skip_mobile" -eq 0 ]; then
  run_dotnet_suite "Mobile UI (client, view models, pages)" mobile-frontend/Ballast.Mobile.Tests/Ballast.Mobile.Tests.csproj
fi

if [ "$skip_web" -eq 0 ]; then
  header "Web frontend"
  require npm "Install Node.js 22 or newer"
  if (cd web-frontend && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npx ng test --watch=false); then
    results+=("PASS  Web frontend")
  else
    results+=("FAIL  Web frontend"); status=1
  fi
fi

header "Summary"
for r in "${results[@]}"; do
  case "$r" in PASS*) printf '%s%s%s\n' "$C_GREEN" "$r" "$C_RESET" ;; *) printf '%s%s%s\n' "$C_RED" "$r" "$C_RESET" ;; esac
done
exit "$status"
