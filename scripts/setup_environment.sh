#!/usr/bin/env bash
# Checks toolchain versions, installs web dependencies and creates a local .env with a generated secret.
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"

header "Toolchain"
require dotnet "Install the .NET 10 SDK from https://dotnet.microsoft.com/download"
if [ "$(dotnet_major)" -lt 10 ]; then
  fail ".NET SDK 10 or newer is required (found $(dotnet --version))"
  exit 1
fi
ok ".NET SDK $(dotnet --version)"

require node "Install Node.js 22 or newer"
if [ "$(node_major)" -lt 22 ]; then
  fail "Node.js 22 or newer is required (found $(node --version))"
  exit 1
fi
ok "Node.js $(node --version)"
require npm "npm ships with Node.js"

require python3 "Install Python 3.11 or newer"
if ! python_ok python3; then
  fail "Python 3.11 or newer is required (found $(python3 --version 2>&1))"
  exit 1
fi
ok "$(python3 --version)"

header "Model service (Python)"
if [ ! -x "$AI_DIR/.venv/bin/python" ]; then
  python3 -m venv "$AI_DIR/.venv"
  ok "created code/ai_models/.venv"
fi
"$AI_DIR/.venv/bin/python" -m pip install --quiet --upgrade pip
"$AI_DIR/.venv/bin/python" -m pip install --quiet -r "$AI_DIR/requirements-dev.txt"
ok "Python packages installed"

header "Web dependencies"
(cd web-frontend && npm ci --no-audit --no-fund)
ok "npm packages installed"

header "Environment file"
if [ -f .env ]; then
  ok ".env already exists, leaving it unchanged"
else
  if have openssl; then secret="$(openssl rand -hex 32)"; else secret="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"; fi
  sed "s/^BALLAST_SECRET_KEY=.*/BALLAST_SECRET_KEY=${secret}/" .env.example > .env
  ok "created .env with a generated BALLAST_SECRET_KEY"
fi
