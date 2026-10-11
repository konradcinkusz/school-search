#!/usr/bin/env bash
# One-command onboarding for school-search. Numbered steps; each step says what
# it did, what it skipped and why. Mirror of scripts/setup.ps1: keep them in step.
#
#   scripts/setup.sh           run every step
#   scripts/setup.sh --check   list missing tools by name and exit (no changes made)
#
# Exit status: 0 when every required tool is present; 1 when one is missing.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

APPHOST="src/SchoolSearch.AppHost/SchoolSearch.AppHost.csproj"
CHECK_ONLY=0

usage() {
  sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'
}

case "${1:-}" in
  "")        ;;
  --check)   CHECK_ONLY=1 ;;
  -h|--help) usage; exit 0 ;;
  *)         echo "setup.sh: unknown option '$1'" >&2; usage >&2; exit 2 ;;
esac

say()  { printf '\n== %s\n' "$*"; }
ok()   { printf '  [ok]      %s\n' "$*"; }
miss() { printf '  [missing] %s\n' "$*"; }
skip() { printf '  [skip]    %s\n' "$*"; }

have() { command -v "$1" >/dev/null 2>&1; }

# Values from the gitignored .env, empty when absent.
read_env() {
  [[ -f .env ]] || return 0
  grep -E "^$1=" .env | tail -n1 | cut -d= -f2- || true
}

REQUIRED_MISSING=""
OPTIONAL_MISSING=""

# check_tool <display name> <command> <required 1|0> <install pointer>
check_tool() {
  local name="$1" cmd="$2" required="$3" hint="$4"
  if have "$cmd"; then
    ok "$name"
  else
    miss "$name  ->  $hint"
    if [[ "$required" == 1 ]]; then
      REQUIRED_MISSING="${REQUIRED_MISSING}${name}; "
    else
      OPTIONAL_MISSING="${OPTIONAL_MISSING}${name}; "
    fi
  fi
}

# Step 1: prerequisites, with install pointers. Also what --check runs.
step_prerequisites() {
  say "1/5 Prerequisites"
  check_tool "git"                  git      1 "https://git-scm.com/downloads"
  check_tool ".NET 10 SDK (dotnet)" dotnet   1 "https://dotnet.microsoft.com/download/dotnet/10.0"
  if have dotnet && [[ "$(dotnet --version 2>/dev/null | cut -d. -f1)" != "10" ]]; then
    miss "dotnet is present but not a 10.x SDK; global.json pins the band  ->  https://dotnet.microsoft.com/download/dotnet/10.0"
    REQUIRED_MISSING="${REQUIRED_MISSING}.NET 10 SDK (wrong version); "
  fi
  check_tool "Node.js 20+ (node)"   node     1 "https://nodejs.org/en/download"
  check_tool "pnpm (pnpm)"          pnpm     1 "run 'corepack enable' (ships with Node) or https://pnpm.io/installation"
  check_tool "gitleaks 8.19+"       gitleaks 1 "https://github.com/gitleaks/gitleaks#installing (the pre-commit hook needs it)"
  check_tool "container engine (docker)" docker 0 "https://docs.docker.com/get-docker/ (needed only for image builds)"
  check_tool "GitHub CLI (gh)"      gh       0 "https://cli.github.com (optional, for PRs and workflow runs)"
}

summary_check() {
  echo
  if [[ -n "$REQUIRED_MISSING" ]]; then
    echo "Required tools missing: ${REQUIRED_MISSING%; }"
  else
    echo "Required tools: all present."
  fi
  if [[ -n "$OPTIONAL_MISSING" ]]; then
    echo "Optional tools missing: ${OPTIONAL_MISSING%; }"
  fi
}

# Step 2: the pre-commit secret scan. Fails closed without gitleaks.
step_hooks() {
  say "2/5 Secret-scan pre-commit hook"
  if ! git rev-parse --git-dir >/dev/null 2>&1; then
    skip "not a git checkout; hook not installed"
    return
  fi
  chmod +x scripts/hooks/pre-commit scripts/scan-secrets.sh
  git config core.hooksPath scripts/hooks
  ok "core.hooksPath = scripts/hooks (every commit runs scripts/scan-secrets.sh --staged)"
}

# Step 3: the local .env, gitignored, created from the committed template.
step_env() {
  say "3/5 Local .env (gitignored)"
  if [[ -f .env ]]; then
    ok ".env exists; left unchanged"
  else
    cp secrets.env.example .env
    ok "created .env from secrets.env.example; every value is optional"
  fi
}

# Step 4: optional integrations, each labelled with what it enables.
step_optional() {
  say "4/5 Optional integrations"
  local key
  key="$(read_env ANTHROPIC_API_KEY)"
  if [[ -z "$key" && -t 0 ]]; then
    printf '  ANTHROPIC_API_KEY (optional, needed for AI features; Enter to skip): '
    read -r -s key || true
    echo
  fi
  if [[ -z "$key" ]]; then
    skip "ANTHROPIC_API_KEY not set: AI features disabled, /health lists ai as disabled"
    return
  fi
  if [[ -f "$APPHOST" ]] && have dotnet; then
    dotnet user-secrets set "Parameters:anthropic-api-key" "$key" --project "$APPHOST" >/dev/null
    ok "stored ANTHROPIC_API_KEY in user-secrets"
  else
    skip "ANTHROPIC_API_KEY read but no AppHost or dotnet yet; run setup again once the solution is in place"
  fi
}

# Step 5: scan the tree once, then say what to run next.
step_scan_and_next() {
  say "5/5 Scan the working tree and next steps"
  if have gitleaks; then
    if ./scripts/scan-secrets.sh --all >/dev/null 2>&1; then
      ok "gitleaks: no secrets in the working tree"
    else
      miss "gitleaks reported findings; run scripts/scan-secrets.sh --all and fix them before committing"
    fi
  else
    miss "gitleaks not installed; the tree scan did not run"
  fi
  echo
  echo "Next: dotnet run --project $APPHOST"
  echo "      then open the URL the AppHost dashboard prints, and GET /health on the API."
  echo "Variables and their tiers: scripts/README.md"
}

main() {
  step_prerequisites
  if [[ "$CHECK_ONLY" == 1 ]]; then
    summary_check
    [[ -z "$REQUIRED_MISSING" ]] && exit 0 || exit 1
  fi
  step_hooks
  step_env
  step_optional
  step_scan_and_next
  summary_check
  [[ -z "$REQUIRED_MISSING" ]] && exit 0 || exit 1
}

main "$@"
