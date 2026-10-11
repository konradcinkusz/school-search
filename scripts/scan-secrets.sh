#!/usr/bin/env bash
# Scans for committed secrets with gitleaks.
#
#   scripts/scan-secrets.sh --staged   what is about to be committed (pre-commit hook)
#   scripts/scan-secrets.sh --all      every file in the working tree (CI and manual)
#
# Fails closed: if gitleaks is missing the scan cannot run, and a commit is refused
# rather than passed unscanned. Config: .gitleaks.toml. Requires gitleaks >= 8.19.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

MODE="${1:---staged}"

if ! command -v gitleaks >/dev/null 2>&1; then
  cat >&2 <<'EOF'
scan-secrets: gitleaks is not installed, so the secret scan cannot run.
  Install (single binary): https://github.com/gitleaks/gitleaks#installing
  macOS:   brew install gitleaks
  Windows: winget install gitleaks.gitleaks
The commit is refused on purpose: a scan that silently does not run is the
failure this script exists to prevent. Run scripts/setup.sh --check to see
every missing tool.
EOF
  exit 1
fi

case "$MODE" in
  --staged)
    exec gitleaks git --pre-commit --staged --redact --no-banner --config .gitleaks.toml
    ;;
  --all)
    exec gitleaks dir --redact --no-banner --config .gitleaks.toml .
    ;;
  -h|--help)
    echo "usage: scripts/scan-secrets.sh [--staged | --all]"
    ;;
  *)
    echo "scan-secrets: unknown option '$MODE' (use --staged or --all)" >&2
    exit 2
    ;;
esac
