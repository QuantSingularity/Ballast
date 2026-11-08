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
