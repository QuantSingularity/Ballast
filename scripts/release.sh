#!/usr/bin/env bash
# Runs all tests, then packages the API and web build into dist/ballast-<version>.zip.
# Usage: scripts/release.sh <version>
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"

version="${1:-}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { fail "Usage: $0 <major.minor.patch>"; exit 2; }
require zip "Install zip"

scripts/run_tests.sh
rm -rf dist && mkdir -p dist/stage
scripts/build.sh --publish
mv dist/api dist/stage/api
cp -r web-frontend/dist/web-frontend/browser dist/stage/web
(cd dist/stage && zip -qr "../ballast-${version}.zip" .)
rm -rf dist/stage
ok "dist/ballast-${version}.zip"
