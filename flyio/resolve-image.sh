#!/usr/bin/env bash
# Print the image reference `flyctl deploy --image` should use.
#   flyio/resolve-image.sh <fly-app> <ghcr-image-ref>
# Default (FLY_REGISTRY_MIRROR unset or not "true"): prints the GHCR reference unchanged. Fly pulls it itself, which works
# when the GHCR package is public (docs/runbooks/FLY-DEPLOY.md, section 2).
# FLY_REGISTRY_MIRROR=true: copies the image from GHCR to registry.fly.io/<fly-app>:<tag> and prints that reference. Use it
# when the GHCR packages stay private: Fly authenticates to its own registry natively, so no pull credential has to exist.
# The runner must already be logged in to ghcr.io (docker/login-action) and have FLY_API_TOKEN set. Only the reference is
# written to stdout; all progress goes to stderr, so `IMG="$(flyio/resolve-image.sh ...)"` is safe.
set -euo pipefail

if [[ $# -ne 2 || -z "$1" || -z "$2" ]]; then
  echo "usage: resolve-image.sh <fly-app> <ghcr-image-ref>" >&2
  exit 2
fi

APP="$1"
SRC="$2"

if [ "${FLY_REGISTRY_MIRROR:-}" != "true" ]; then
  printf '%s\n' "$SRC"
  exit 0
fi

: "${FLY_API_TOKEN:?FLY_API_TOKEN is required to push to registry.fly.io}"
TAG="${SRC##*:}"
DEST="registry.fly.io/${APP}:${TAG}"

{
  echo "Mirroring $SRC -> $DEST"
  docker pull "$SRC"
  docker tag "$SRC" "$DEST"
  flyctl auth docker
  docker push "$DEST"
} >&2

printf '%s\n' "$DEST"
