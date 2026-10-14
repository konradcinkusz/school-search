#!/usr/bin/env bash
# Post-deploy smoke test: assertions the Fly health check cannot make.
#   flyio/smoke.sh <service> <public-base-url>
#   service: api | reviews | billing | mcp | web
#   e.g. flyio/smoke.sh billing https://school-search-billing-dev.fly.dev
# Exit 0 when every assertion passes, 1 on the first failure, 2 on bad arguments.
# Only anonymous, read-only requests plus requests that must be REFUSED. Nothing is written, no secret is needed.
# Each request is retried for a cold start. Requests that must be refused are sent once the service is answering.
set -euo pipefail

if [[ $# -ne 2 || -z "$1" || -z "$2" ]]; then
  echo "usage: smoke.sh <api|reviews|billing|mcp|web> <public-base-url>" >&2
  exit 2
fi

SERVICE="$1"
BASE="${2%/}"
case "$BASE" in
  https://*) ;;
  *) echo "::error::smoke: base URL must be https (got '$BASE')" >&2; exit 2 ;;
esac

RETRY_ARGS=(--retry 20 --retry-delay 5 --retry-all-errors --max-time 20)
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

fail() { echo "::error::smoke($SERVICE): $*" >&2; exit 1; }
pass() { echo "ok: $*"; }

# get_ok <path> <outfile>: GET, retried, must answer 2xx.
get_ok() {
  curl --fail --silent --show-error "${RETRY_ARGS[@]}" "$BASE$1" -o "$2" || fail "GET $1 did not answer 2xx"
}

# status_of <method> <path>: prints the HTTP status, one attempt after the service is known to answer.
status_of() {
  curl --silent --show-error --max-time 20 -X "$1" -H 'Content-Type: application/json' -d '{}' \
    -o /dev/null -w '%{http_code}' "$BASE$2" || echo "000"
}

readiness() {
  get_ok /health "$WORK_DIR/health.json"
  jq -e '.status == "ok" or .status == "degraded"' "$WORK_DIR/health.json" >/dev/null \
    || fail "/health status is not ok or degraded: $(jq -c '{status, checks}' "$WORK_DIR/health.json" 2>/dev/null || echo unreadable)"
  pass "/health answers ($(jq -r .status "$WORK_DIR/health.json")); degraded integrations are listed in the body, not failures (P8)"
  get_ok /alive "$WORK_DIR/alive.txt"
  pass "/alive answers"
}

# must_be <method> <path> <expected status regex> <why>
must_be() {
  local code
  code="$(status_of "$1" "$2")"
  if [[ "$code" =~ ^($3)$ ]]; then
    pass "$1 $2 -> $code ($4)"
  else
    fail "$1 $2 answered $code, expected $3 ($4)"
  fi
}

case "$SERVICE" in
  api)
    readiness
    get_ok /api/v1/meta "$WORK_DIR/meta.json"
    jq -e 'type == "object"' "$WORK_DIR/meta.json" >/dev/null || fail "/api/v1/meta is not a JSON object"
    pass "/api/v1/meta returns JSON (the catalog read path works)"
    ;;
  reviews)
    readiness
    # The seed route is not mapped in Production (DevSeedOptions.IsEnabled), so it must not exist here.
    must_be POST /dev/seed '404' "dev seed is off in Production"
    ;;
  billing)
    readiness
    must_be POST /dev/seed '404' "dev seed is off in Production"
    # Simulate needs a verified user (401) and answers 403 unconditionally when MockMode is false. Never 2xx.
    must_be POST /api/v1/billing/simulate '401|403' "mock mode is off; simulate is never open"
    # An unsigned webhook is refused in Production (401), whether or not a secret is configured yet.
    must_be POST /api/v1/billing/webhook '400|401|403' "unsigned webhooks are refused"
    ;;
  mcp)
    readiness
    ;;
  web)
    get_ok /healthz "$WORK_DIR/healthz.json"
    jq -e '.status == "ok"' "$WORK_DIR/healthz.json" >/dev/null || fail "/healthz status is not ok"
    pass "/healthz is ok"
    get_ok / "$WORK_DIR/index.html"
    grep -qi '<html' "$WORK_DIR/index.html" || fail "/ did not return an HTML page"
    pass "/ renders HTML"
    get_ok /api/config "$WORK_DIR/config.json"
    jq -e '.catalogApiSource == "CATALOG_API_URL"' "$WORK_DIR/config.json" >/dev/null \
      || fail "/api/config does not read CATALOG_API_URL (source: $(jq -r .catalogApiSource "$WORK_DIR/config.json" 2>/dev/null || echo unreadable))"
    pass "/api/config reads the runtime CATALOG_API_URL"
    # The BFF reaches the catalog API through its allowlisted proxy: proves web -> api over the public URL.
    get_ok /api/proxy/api/v1/meta "$WORK_DIR/proxy-meta.json"
    jq -e 'type == "object"' "$WORK_DIR/proxy-meta.json" >/dev/null || fail "/api/proxy/api/v1/meta is not a JSON object"
    pass "BFF proxy reaches the catalog API"
    ;;
  *)
    echo "::error::smoke: unknown service '$SERVICE'" >&2
    exit 2
    ;;
esac

echo "smoke($SERVICE): all assertions passed"
