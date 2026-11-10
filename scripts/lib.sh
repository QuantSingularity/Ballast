#!/usr/bin/env bash
# Shared helpers sourced by the other scripts.

if [ -t 1 ]; then
  C_RESET=$'\033[0m'; C_GREEN=$'\033[32m'; C_RED=$'\033[31m'; C_YELLOW=$'\033[33m'; C_BLUE=$'\033[34m'
else
  C_RESET=""; C_GREEN=""; C_RED=""; C_YELLOW=""; C_BLUE=""
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export REPO_ROOT
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

header() { printf '\n%s== %s ==%s\n' "$C_BLUE" "$1" "$C_RESET"; }
ok()     { printf '%s[ok]%s %s\n' "$C_GREEN" "$C_RESET" "$1"; }
warn()   { printf '%s[warn]%s %s\n' "$C_YELLOW" "$C_RESET" "$1"; }
fail()   { printf '%s[fail]%s %s\n' "$C_RED" "$C_RESET" "$1" >&2; }
have()   { command -v "$1" >/dev/null 2>&1; }

require() {
  if ! have "$1"; then
    fail "$1 is required but was not found. $2"
    exit 1
  fi
}

dotnet_major() { dotnet --version 2>/dev/null | cut -d. -f1; }
node_major()   { node --version 2>/dev/null | sed 's/^v//' | cut -d. -f1; }

AI_DIR="$REPO_ROOT/code/ai_models"
export AI_DIR

# Interpreter for the model service: the project's virtualenv if present, else python3 on the PATH.
ai_python() {
  if [ -x "$AI_DIR/.venv/bin/python" ]; then echo "$AI_DIR/.venv/bin/python"
  elif have python3; then echo python3
  else echo ""; fi
}
python_ok() { "$1" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 11) else 1)' >/dev/null 2>&1; }
ai_deps_ok() { "$1" -c 'import numpy, fastapi, uvicorn' >/dev/null 2>&1; }

# Starts the model service in the background and waits for /health. Sets AI_PID.
start_ai_service() {
  local py="$1" port="$2"
  (cd "$AI_DIR" && exec "$py" -m ballast_ai.service --host 127.0.0.1 --port "$port") &
  AI_PID=$!
  for _ in $(seq 1 100); do
    curl -fsS "http://127.0.0.1:${port}/health" >/dev/null 2>&1 && return 0
    kill -0 "$AI_PID" 2>/dev/null || { fail "model service exited during startup"; return 1; }
    sleep 0.3
  done
  fail "model service did not become healthy on port ${port}"
  return 1
}
