#!/usr/bin/env bash
# Post-deploy assertions for the authservice identity provider (AUTHSERVICE-BRIEF section 5).
# Health cannot see an empty signing key set: a deploy that publishes no JWKS keys would look healthy
# while every consumer rejects every token. This script fails the deploy in that case.
#
#   scripts/assert-authservice.sh <public-base-url> <expected-issuer>
#   e.g. scripts/assert-authservice.sh https://school-search-authservice-dev.fly.dev school-search
#
# Exit 0 when every assertion passes, 1 on the first failure, 2 on bad arguments.
# Each request is retried, because the first boot runs migrations after the listener starts (brief section 4).
set -euo pipefail

if [[ $# -ne 2 || -z "$1" || -z "$2" ]]; then
  echo "usage: assert-authservice.sh <public-base-url> <expected-issuer>" >&2
  exit 2
fi

BASE="${1%/}"
EXPECTED_ISSUER="$2"
RETRY_ARGS=(--retry 20 --retry-delay 5 --retry-all-errors --max-time 15)

fail() { echo "::error::assert-authservice: $*" >&2; exit 1; }
pass() { echo "ok: $*"; }

case "$BASE" in
  https://*) ;;
  *) fail "base URL must be https (got '$BASE')" ;;
esac

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

# 1. Liveness and readiness: /health/ready is 503 until the schema is ready and the database answers.
curl --fail --silent --show-error "${RETRY_ARGS[@]}" "$BASE/health" -o "$WORK_DIR/health.json" \
  || fail "GET /health did not answer 200"
jq -e '.status == "Healthy"' "$WORK_DIR/health.json" >/dev/null || fail "/health status is not Healthy"
pass "/health is Healthy"

curl --fail --silent --show-error "${RETRY_ARGS[@]}" "$BASE/health/ready" -o "$WORK_DIR/ready.json" \
  || fail "GET /health/ready did not answer 200 (schema or database not ready)"
jq -e '.status == "Healthy"' "$WORK_DIR/ready.json" >/dev/null || fail "/health/ready status is not Healthy"
pass "/health/ready is Healthy"

# 2. Discovery: issuer, RS256 only, and a jwks_uri on the public origin (a relative or internal URI means
#    Jwt__PublicBaseUrl is missing, brief section 1).
curl --fail --silent --show-error "${RETRY_ARGS[@]}" "$BASE/.well-known/openid-configuration" -o "$WORK_DIR/discovery.json" \
  || fail "GET /.well-known/openid-configuration did not answer 200"
jq -e --arg iss "$EXPECTED_ISSUER" --arg base "$BASE" '
  .issuer == $iss
  and (.id_token_signing_alg_values_supported == ["RS256"])
  and (.jwks_uri | startswith($base + "/"))' "$WORK_DIR/discovery.json" >/dev/null \
  || fail "discovery document: issuer must be '$EXPECTED_ISSUER', alg must be [RS256], jwks_uri must start with $BASE/"
pass "discovery issuer, algorithm and jwks_uri"

# 3. JWKS: non-empty. An empty keys array means HS256 is active (no Jwt__PrivateKeyPem), and every consumer
#    would reject every token.
curl --fail --silent --show-error "${RETRY_ARGS[@]}" "$BASE/.well-known/jwks.json" -o "$WORK_DIR/jwks.json" \
  || fail "GET /.well-known/jwks.json did not answer 200"
jq -e '(.keys // []) | length >= 1
  and all(.kty == "RSA" and .alg == "RS256" and .use == "sig" and ((.kid // "") | length > 0))' \
  "$WORK_DIR/jwks.json" >/dev/null \
  || fail "JWKS is empty or malformed: every consumer would reject every token (check Jwt__PrivateKeyPem)"
pass "JWKS publishes $(jq '.keys | length' "$WORK_DIR/jwks.json") RS256 key(s)"

# 4. Enforcement: the account endpoint refuses an anonymous call (401). Any other answer means the API is not
#    enforcing authentication.
STATUS="$(curl --silent --output /dev/null --write-out '%{http_code}' "${RETRY_ARGS[@]}" "$BASE/api/v1/auth/me")"
[[ "$STATUS" == "401" ]] || fail "GET /api/v1/auth/me without a token answered $STATUS, expected 401"
pass "GET /api/v1/auth/me answers 401 without a token"

echo "assert-authservice: all assertions passed for $BASE"
