#!/usr/bin/env bash
# Builds the solution, the Angular app and the mobile UI library.
# Usage: scripts/build.sh [--skip-web] [--publish]
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"

skip_web=0; publish=0
for arg in "$@"; do
  case "$arg" in
    --skip-web) skip_web=1 ;;
    --publish) publish=1 ;;
    *) fail "Unknown option $arg"; exit 2 ;;
  esac
done

require dotnet "Install the .NET 10 SDK"

header "Backend and AI models"
dotnet build code/Ballast.slnx -c Release --nologo -v quiet
ok "code/Ballast.slnx"

header "Mobile UI library and tests"
dotnet build mobile-frontend/Ballast.Mobile.slnx -c Release --nologo -v quiet
ok "mobile-frontend/Ballast.Mobile.slnx (the MAUI host is built separately, see mobile-frontend/Ballast.Mobile.App/README.md)"

if [ "$skip_web" -eq 0 ]; then
  header "Web frontend"
  require npm "Install Node.js 22 or newer"
  (cd web-frontend && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npm run build)
  ok "web-frontend/dist"
fi

if [ "$publish" -eq 1 ]; then
  header "Publish API"
  dotnet publish code/backend/app/Ballast.Api.csproj -c Release -o dist/api --nologo -v quiet
  ok "dist/api"
fi
