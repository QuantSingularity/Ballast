#!/usr/bin/env bash
# Builds the backend and web images. Usage: scripts/docker_build.sh [tag]
set -euo pipefail
# shellcheck source=lib.sh
source "$(dirname "$0")/lib.sh"
cd "$REPO_ROOT"
require docker "Install Docker"
tag="${1:-latest}"
docker build -f infrastructure/docker/Dockerfile.backend -t "ghcr.io/quantsingularity/ballast-backend:${tag}" .
docker build -f infrastructure/docker/Dockerfile.ai -t "ghcr.io/quantsingularity/ballast-ai:${tag}" .
docker build -f infrastructure/docker/Dockerfile.frontend -t "ghcr.io/quantsingularity/ballast-web:${tag}" .
ok "built ballast-backend, ballast-ai and ballast-web:${tag}"
