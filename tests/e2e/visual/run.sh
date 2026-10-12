#!/usr/bin/env bash
# Visual harness: mock catalog/reviews/billing/agent -> build and start the web app -> collect screenshots and findings.
#
# Usage:
#   tests/e2e/visual/run.sh              build, start everything, collect, stop
#   SKIP_BUILD=1 tests/e2e/visual/run.sh reuse the existing web/app/.next build
#
# Output: tests/e2e/visual/out/<viewport>/<route>.png and tests/e2e/visual/out/report.json
# Background processes (mock, next start) are stopped on exit, including after a failure.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
WEB_DIR="$REPO_ROOT/web"
E2E_DIR="$REPO_ROOT/tests/e2e"
OUT_DIR="$SCRIPT_DIR/out"
LOG_DIR="$OUT_DIR/logs"

MOCK_PORT="${MOCK_PORT:-8080}"
REVIEWS_PORT="${MOCK_REVIEWS_PORT:-8082}"
BILLING_PORT="${MOCK_BILLING_PORT:-8083}"
AGENT_PORT="${MOCK_AGENT_PORT:-8084}"
WEB_PORT="${WEB_PORT:-3000}"
SKIP_BUILD="${SKIP_BUILD:-0}"

MOCK_PID=""
WEB_PID=""

log() { printf '[visual] %s\n' "$*"; }

kill_tree() {
  local pid="$1" child
  for child in $(pgrep -P "$pid" 2>/dev/null || true); do
    kill_tree "$child"
  done
  kill "$pid" 2>/dev/null || true
}

cleanup() {
  local status=$?
  set +e
  if [[ -n "$WEB_PID" ]]; then kill_tree "$WEB_PID"; fi
  if [[ -n "$MOCK_PID" ]]; then kill_tree "$MOCK_PID"; fi
  wait 2>/dev/null
  log "stopped background processes (exit $status)"
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

wait_for() {
  # wait_for <url> <seconds> <label> [grep-pattern]
  local url="$1" timeout="$2" label="$3" pattern="${4:-}" waited=0
  until { if [[ -n "$pattern" ]]; then curl -fsS --max-time 2 "$url" 2>/dev/null | grep -q "$pattern"; else curl -fsS --max-time 2 "$url" >/dev/null 2>&1; fi; }; do
    if (( waited >= timeout )); then
      log "ERROR: $label not ready at $url after ${timeout}s"
      return 1
    fi
    sleep 1
    waited=$((waited + 1))
  done
  log "$label ready ($url)"
}

mkdir -p "$LOG_DIR"
rm -rf "$OUT_DIR/results" "$OUT_DIR/test-results" "$OUT_DIR/report.json"
find "$OUT_DIR" -maxdepth 2 -name '*.png' -delete 2>/dev/null || true

# 1. Mock services. Refuse to run against a foreign process already on the port.
if curl -fsS --max-time 2 "http://localhost:$MOCK_PORT/alive" >/dev/null 2>&1; then
  log "port $MOCK_PORT already answers; stop that process first (it is not assumed to be the mock)"
  exit 1
fi
log "starting mock services (catalog :$MOCK_PORT, reviews :$REVIEWS_PORT, billing :$BILLING_PORT, agent :$AGENT_PORT)"
MOCK_PORT="$MOCK_PORT" MOCK_REVIEWS_PORT="$REVIEWS_PORT" MOCK_BILLING_PORT="$BILLING_PORT" MOCK_AGENT_PORT="$AGENT_PORT" \
  node "$SCRIPT_DIR/mock-api.mjs" >"$LOG_DIR/mock-api.log" 2>&1 &
MOCK_PID=$!
wait_for "http://localhost:$MOCK_PORT/alive" 20 "mock catalog" "mock-catalog"
wait_for "http://localhost:$REVIEWS_PORT/alive" 10 "mock reviews" "mock-reviews"
wait_for "http://localhost:$BILLING_PORT/alive" 10 "mock billing" "mock-billing"
wait_for "http://localhost:$AGENT_PORT/alive" 10 "mock agent" "mock-agent"

# 2. Web app: build (unless SKIP_BUILD=1), then start on $WEB_PORT with the mock as its backends.
if [[ "$SKIP_BUILD" == "1" ]]; then
  log "SKIP_BUILD=1: reusing existing build"
else
  log "building web app"
  if ! (cd "$WEB_DIR" && pnpm --filter ./app run build) >"$LOG_DIR/build.log" 2>&1; then
    log "ERROR: build failed, see $LOG_DIR/build.log (tail below)"
    tail -n 40 "$LOG_DIR/build.log" || true
    exit 1
  fi
  log "build finished"
fi

log "starting web app on :$WEB_PORT"
(
  cd "$WEB_DIR"
  export CATALOG_API_URL="http://localhost:$MOCK_PORT"
  export REVIEWS_BASE_URL="http://localhost:$REVIEWS_PORT"
  export BILLING_BASE_URL="http://localhost:$BILLING_PORT"
  export AGENT_BASE_URL="http://localhost:$AGENT_PORT"
  export PORT="$WEB_PORT"
  exec pnpm --filter ./app exec next start -p "$WEB_PORT"
) >"$LOG_DIR/web.log" 2>&1 &
WEB_PID=$!
wait_for "http://localhost:$WEB_PORT/healthz" 90 "web app" "school-search-web"

# 3. Collect screenshots and findings. Playwright exit code decides the run (only 5xx or load failures fail).
log "running visual collector"
set +e
(
  cd "$E2E_DIR"
  VISUAL_BASE_URL="http://localhost:$WEB_PORT" pnpm exec playwright test --config "$SCRIPT_DIR/playwright.visual.config.ts"
)
PW_STATUS=$?
set -e

if [[ -f "$OUT_DIR/report.json" ]]; then
  log "report: $OUT_DIR/report.json"
else
  log "no report written"
fi
log "logs: $LOG_DIR (mock-api.log, web.log, build.log)"
exit "$PW_STATUS"
