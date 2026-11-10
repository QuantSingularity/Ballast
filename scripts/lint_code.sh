#!/usr/bin/env bash
# Formatting and static checks. Usage: scripts/lint_code.sh [--fix]
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"

fix=0
[ "${1:-}" = "--fix" ] && fix=1
status=0

header "C# (dotnet format)"
require dotnet "Install the .NET 10 SDK"
for sln in code/Ballast.slnx mobile-frontend/Ballast.Mobile.slnx; do
  if [ "$fix" -eq 1 ]; then dotnet format "$sln" --no-restore; else dotnet format "$sln" --verify-no-changes --no-restore || status=1; fi
done

header "Python (ruff)"
py="$(ai_python)"
if [ -n "$py" ] && "$py" -m ruff --version >/dev/null 2>&1; then
  if [ "$fix" -eq 1 ]; then
    (cd "$AI_DIR" && "$py" -m ruff check --fix . && "$py" -m ruff format .)
  else
    (cd "$AI_DIR" && "$py" -m ruff check . && "$py" -m ruff format --check .) || status=1
  fi
else
  warn "ruff not installed, run scripts/setup_environment.sh"
fi

header "Web (prettier)"
if [ -d web-frontend/node_modules ]; then
  if [ "$fix" -eq 1 ]; then
    (cd web-frontend && npx prettier --write "src/**/*.{ts,html,css}")
  else
    (cd web-frontend && npx prettier --check "src/**/*.{ts,html,css}") || status=1
  fi
else
  warn "web-frontend/node_modules missing, run scripts/setup_environment.sh"
fi

header "Shell (shellcheck)"
if have shellcheck; then
  shellcheck -x -P SCRIPTDIR scripts/*.sh infrastructure/validate.sh || status=1
else
  warn "shellcheck not installed, skipping"
fi

header "Text policy"
if grep -rIl --exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj --exclude-dir=.angular --exclude-dir=dist --exclude-dir=.git \
     --exclude-dir=.venv --exclude-dir=__pycache__ --exclude-dir=.ruff_cache --exclude-dir=.pytest_cache \
     --exclude=package-lock.json -e $'\xe2\x80\x94' . >/dev/null 2>&1; then
  fail "em dashes found:"
  grep -rIn --exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj --exclude-dir=.angular --exclude-dir=dist --exclude-dir=.git \
     --exclude-dir=.venv --exclude-dir=__pycache__ --exclude-dir=.ruff_cache --exclude-dir=.pytest_cache \
     --exclude=package-lock.json -e $'\xe2\x80\x94' . | head -10
  status=1
else
  ok "no em dashes"
fi
exit "$status"
