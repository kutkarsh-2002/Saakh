#!/usr/bin/env bash
#
# Builds the two images and pushes them to a public registry that Container Apps
# can pull from without credentials.
#
# GitHub Container Registry is the default because it is free for public images
# and you already have a GitHub account for this repo. Azure Container Registry
# would be the native choice, but its cheapest tier is billed monthly and this
# deployment is meant to cost nothing.
#
#   REGISTRY=ghcr.io/youruser ./push-images.sh
#
# Log in first:
#   echo $GITHUB_TOKEN | docker login ghcr.io -u youruser --password-stdin
#
# The token needs write:packages. After the first push, make both packages
# public in GitHub (Packages -> saakh-api -> Package settings -> Change
# visibility), or Container Apps cannot pull them.

set -euo pipefail

REGISTRY="${REGISTRY:?set REGISTRY, e.g. ghcr.io/youruser}"
TAG="${TAG:-latest}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

# Container Apps runs x86_64. On an arm64 machine (Apple silicon) the default
# build would produce arm64 images that fail to start there with no useful error,
# so the platform is pinned rather than inferred.
PLATFORM="${PLATFORM:-linux/amd64}"

say "Building api for $PLATFORM"
docker build --platform "$PLATFORM" \
  -t "$REGISTRY/saakh-api:$TAG" "$REPO_ROOT/backend"

say "Building web for $PLATFORM"
docker build --platform "$PLATFORM" \
  -t "$REGISTRY/saakh-web:$TAG" "$REPO_ROOT/frontend"

say "Pushing"
docker push "$REGISTRY/saakh-api:$TAG"
docker push "$REGISTRY/saakh-web:$TAG"

cat <<EOF

$(say "Pushed")

Now provision with:

  API_IMAGE=$REGISTRY/saakh-api:$TAG \\
  WEB_IMAGE=$REGISTRY/saakh-web:$TAG \\
  ./provision.sh

If this is the first push, make both packages public on GitHub first.
EOF
