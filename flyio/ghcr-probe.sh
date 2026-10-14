#!/usr/bin/env bash
# Warn (never fail) when a GHCR image cannot be pulled anonymously.
#   flyio/ghcr-probe.sh <ghcr.io/owner/name:tag>
# Fly pulls `--image ghcr.io/...` without any credential from this repository, so a private package fails the deploy with
# an image-pull error. This probe turns that into an early, readable warning. It uses the public registry API only.
set -euo pipefail

if [[ $# -ne 1 || "$1" != ghcr.io/*:* ]]; then
  echo "usage: ghcr-probe.sh ghcr.io/<owner>/<name>:<tag>" >&2
  exit 2
fi

REF="${1#ghcr.io/}"
REPO="${REF%:*}"
TAG="${REF##*:}"

TOKEN="$(curl --silent --max-time 15 "https://ghcr.io/token?service=ghcr.io&scope=repository:${REPO}:pull" | jq -r '.token // empty' || true)"
if [ -z "$TOKEN" ]; then
  echo "::warning::ghcr-probe: could not obtain an anonymous pull token for ghcr.io/${REPO}; the package is probably private or the registry is unreachable"
  exit 0
fi

CODE="$(curl --silent --max-time 15 -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer ${TOKEN}" \
  -H 'Accept: application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json, application/vnd.docker.distribution.manifest.list.v2+json' \
  "https://ghcr.io/v2/${REPO}/manifests/${TAG}" || echo 000)"

if [ "$CODE" = "200" ]; then
  echo "ghcr-probe: ghcr.io/${REPO}:${TAG} is anonymously pullable"
else
  echo "::warning::ghcr-probe: ghcr.io/${REPO}:${TAG} answered ${CODE} to an anonymous pull. If the package is private, Fly cannot pull it: make it public or set the repository variable FLY_REGISTRY_MIRROR=true (docs/runbooks/FLY-DEPLOY.md, section 2)."
fi
