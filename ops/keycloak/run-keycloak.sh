#!/usr/bin/env bash
# Phase 9 (WP9.2): stand up Keycloak for the Plutus test env as a Docker container.
# Runs alongside ETRIE + the caddymanager stack without touching them (own name, port, volume).
# Bound to 127.0.0.1:8089; Caddy terminates TLS for login.plutus.huggett.dscloud.me and proxies in.
#
# Idempotent: re-running recreates the container from the committed realm export.
set -euo pipefail

NAME=plutus-keycloak
PORT=8089
IMAGE=quay.io/keycloak/keycloak:26.0
PUBLIC_URL="https://login.plutus.huggett.dscloud.me"
HERE="$(cd "$(dirname "$0")" && pwd)"

# Admin bootstrap creds — dev only; the admin console is NOT exposed publicly by the Caddy vhost.
ADMIN_USER="${KC_ADMIN_USER:-admin}"
ADMIN_PASS="${KC_ADMIN_PASS:-admin-change-me}"

docker rm -f "$NAME" >/dev/null 2>&1 || true

docker run -d --name "$NAME" --restart unless-stopped \
  -p 127.0.0.1:${PORT}:8080 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME="$ADMIN_USER" \
  -e KC_BOOTSTRAP_ADMIN_PASSWORD="$ADMIN_PASS" \
  -e KC_HOSTNAME="$PUBLIC_URL" \
  -e KC_HOSTNAME_ADMIN="$PUBLIC_URL" \
  -e KC_HTTP_ENABLED=true \
  -e KC_PROXY_HEADERS=xforwarded \
  -e KC_HEALTH_ENABLED=true \
  -v "$HERE/plutus-realm.json":/opt/keycloak/data/import/plutus-realm.json:ro \
  -v "$HERE/themes/plutus":/opt/keycloak/themes/plutus:ro \
  "$IMAGE" \
  start-dev --import-realm

# ⚠ Recreating this container WIPES all user state (passwords changed since import, TOTP
# enrolments) back to the committed realm seed — the container is stateless by design on the
# test env. Re-onboard operators (or kcadm set-password) after any re-run.

echo "Started $NAME on 127.0.0.1:${PORT}. Waiting for realm to import..."
for i in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${PORT}/realms/plutus/.well-known/openid-configuration" >/dev/null 2>&1; then
    echo "Keycloak up; realm 'plutus' OIDC metadata reachable on :${PORT}."
    exit 0
  fi
  sleep 2
done
echo "Timed out waiting for Keycloak. Logs:"; docker logs --tail 40 "$NAME"; exit 1
