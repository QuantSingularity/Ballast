#!/usr/bin/env bash
# Validates infrastructure definitions with whatever tooling is available.
set -euo pipefail
cd "$(dirname "$0")"

fail=0
echo "Checking YAML syntax"
python3 - <<'PY' || fail=1
import glob, sys
try:
    import yaml
except ImportError:
    print("  PyYAML not installed, skipping (pip install pyyaml)")
    sys.exit(0)
bad = 0
for f in sorted(glob.glob("**/*.yaml", recursive=True) + glob.glob("*.yml")):
    try:
        list(yaml.safe_load_all(open(f)))
        print(f"  ok   {f}")
    except Exception as e:
        bad += 1
        print(f"  FAIL {f}: {e}")
sys.exit(1 if bad else 0)
PY

if command -v docker >/dev/null 2>&1; then
  echo "Checking docker compose"
  BALLAST_SECRET_KEY=validate-only-validate-only-validate docker compose -f docker-compose.yml config -q && echo "  ok compose" || fail=1
else
  echo "docker not installed, skipping compose validation"
fi

if command -v kubectl >/dev/null 2>&1; then
  echo "Checking kustomize overlays"
  for env in dev prod; do
    kubectl kustomize "kubernetes/overlays/$env" >/dev/null && echo "  ok $env" || fail=1
  done
else
  echo "kubectl not installed, skipping kustomize validation"
fi

exit "$fail"
