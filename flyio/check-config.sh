#!/usr/bin/env bash
# Static guards over flyio/*.fly.toml and flyio/postgres.fly.toml. No network, no flyctl, no secrets.
# Run by the `test` job of .github/workflows/flyio.yml before anything is built or deployed, and runnable locally:
#   bash flyio/check-config.sh
# Exit 0 when every guard holds, 1 otherwise (all violations are listed first).
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

failed=0
err() { echo "::error::flyio/check-config: $*" >&2; failed=1; }

# Non-comment lines of a file (comments in the tomls name secrets on purpose and must not trip the guards).
active() { grep -Ev '^[[:space:]]*#' "$1" || true; }

shopt -s nullglob
tomls=(flyio/*.fly.toml)
if [ "${#tomls[@]}" -eq 0 ]; then
  err "no flyio/*.fly.toml found"
fi

for f in "${tomls[@]}"; do
  name="$(basename "$f" .fly.toml)"
  body="$(active "$f")"

  # 1. [env] is public: no key that looks like a secret may be assigned in a fly.toml.
  if grep -Eiq '^[[:space:]]*[A-Za-z0-9_]*(SECRET|PASSWORD|PEPPER|PRIVATE|API_KEY)[A-Za-z0-9_]*[[:space:]]*=|^[[:space:]]*[A-Za-z0-9_]*TOKEN[[:space:]]*=' <<< "$body"; then
    err "$f assigns a secret-looking key. Secrets are Fly secrets set by CI (flyio/SECRETS.md), never [env]."
  fi

  # 2. Seed switches never exist in a deployed environment (the Production guard refuses them anyway).
  if grep -Eq 'DevSeed__|DevSeed:|SCHOOLSEARCH_DEMO_SEED' <<< "$body"; then
    err "$f sets a DevSeed or demo-seed switch. Seeding is for local and non-Production demo environments only."
  fi

  # 3. Every .NET service runs as Production.
  case "$name" in
    api|authservice|reviews|billing|mcp)
      grep -Eq '^[[:space:]]*ASPNETCORE_ENVIRONMENT[[:space:]]*=[[:space:]]*"Production"' <<< "$body" \
        || err "$f must set ASPNETCORE_ENVIRONMENT = \"Production\"."
      ;;
  esac

  # 4. Public listeners always force HTTPS; the database has no listener at all.
  if [ "$name" = "postgres" ]; then
    if grep -Eq '^\[\[?(http_service|services)\b' <<< "$body"; then
      err "$f declares a listener. Postgres is reachable over 6PN only."
    fi
    grep -Eq '^\[\[mounts\]\]' <<< "$body" || err "$f has no [[mounts]]: the database needs its volume."
    grep -Eq '^[[:space:]]*image[[:space:]]*=[[:space:]]*"postgres:[0-9]+[A-Za-z0-9._-]*"' <<< "$body" \
      || err "$f must pin a vanilla postgres:<major> image (never latest, never postgres-flex)."
    grep -Eq '^[[:space:]]*PGDATA[[:space:]]*=[[:space:]]*"[^"]+/[^"]+/[^"]+"' <<< "$body" \
      || err "$f must point PGDATA at a subdirectory of the mount."
  else
    grep -Eq '^[[:space:]]*force_https[[:space:]]*=[[:space:]]*true' <<< "$body" || err "$f must set force_https = true."
  fi

  # 5. The app name is the one the workflow deploys.
  app="$(sed -n 's/^app[[:space:]]*=[[:space:]]*"\([^"]*\)".*/\1/p' "$f" | head -n 1)"
  if [ -z "$app" ]; then
    err "$f has no app = \"...\" line."
  elif ! grep -Fq "$app" .github/workflows/flyio.yml; then
    err "$f names app '$app', which .github/workflows/flyio.yml does not mention."
  fi
done

# 6. Billing: mock mode is explicitly off in the deployed environment.
if [ -f flyio/billing.fly.toml ]; then
  active flyio/billing.fly.toml | grep -Eq '^[[:space:]]*Billing__MockMode[[:space:]]*=[[:space:]]*"false"' \
    || err "flyio/billing.fly.toml must state Billing__MockMode = \"false\"."
fi

# 7. AI: the kill switch is stated in git for every service that has an AI feature. Reviews, Api and Billing default to
#    ON in code, so an absent line plus a pushed key would switch a feature on without a reviewed change.
for svc in api reviews billing mcp; do
  f="flyio/${svc}.fly.toml"
  [ -f "$f" ] || continue
  active "$f" | grep -Eq '^[[:space:]]*AI_ENABLED[[:space:]]*=[[:space:]]*"(true|false)"' \
    || err "$f must state AI_ENABLED explicitly (\"false\" until the feature's eval gate and open items are closed)."
done

# 8. The web app must carry no secret and no map key in [env] (checked by rule 1) and must name its upstreams.
if [ -f flyio/web.fly.toml ]; then
  for key in CATALOG_API_URL AUTH_BASE_URL REVIEWS_BASE_URL BILLING_BASE_URL AGENT_BASE_URL; do
    active flyio/web.fly.toml | grep -Eq "^[[:space:]]*${key}[[:space:]]*=[[:space:]]*\"https://" \
      || err "flyio/web.fly.toml must set ${key} to an https URL (public URL, not .internal: Fly section 6)."
  done
fi

# 9. Service-to-service HTTP uses the public URL, never .internal (the callee may be scaled to zero).
for f in "${tomls[@]}"; do
  [ "$f" = "flyio/postgres.fly.toml" ] && continue
  if active "$f" | grep -Eq '\.internal'; then
    err "$f refers to a .internal address. Use the public https URL (AGENTS.md, Fly section 6)."
  fi
done

if [ "$failed" -ne 0 ]; then
  exit 1
fi
echo "flyio/check-config: ${#tomls[@]} fly.toml files pass every guard"
