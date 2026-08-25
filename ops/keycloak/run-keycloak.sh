#!/usr/bin/env bash
# Phase 9 (WP9.2): stand up Keycloak for the Plutus test env as a Docker container.
# Runs alongside ETRIE + the caddymanager stack without touching them (own name, port, volume).
# Bound to 127.0.0.1:8089; Caddy terminates TLS for login.plutus.huggett.dscloud.me and proxies in.
#
# Idempotent: re-running recreates the container. ⚠ Since 2026-08-25 that NO LONGER destroys user
# state — H2 lives in the named volume below. See the note above the `docker run`.
set -euo pipefail

NAME=plutus-keycloak
PORT=8089
# ⚠⚠ THE DATA VOLUME, ADDED 2026-08-25 — the whole point of this line.
# Until it existed, Keycloak's H2 database sat in the container's WRITABLE LAYER, so `docker rm`,
# an image bump, or a re-run of this very script erased every operator account, group membership
# and TOTP enrolment, with no undo. `docker volume` survives all three.
VOL=plutus-keycloak-h2
IMAGE=quay.io/keycloak/keycloak:26.0
PUBLIC_URL="https://login.plutus.huggett.dscloud.me"
HERE="$(cd "$(dirname "$0")" && pwd)"

# Admin bootstrap creds — dev only; the admin console is NOT exposed publicly by the Caddy vhost.
ADMIN_USER="${KC_ADMIN_USER:-admin}"
ADMIN_PASS="${KC_ADMIN_PASS:-admin-change-me}"

docker rm -f "$NAME" >/dev/null 2>&1 || true

docker volume create "$VOL" >/dev/null
# ⚠⚠ A FRESH NAMED VOLUME IS ROOT-OWNED AND KEYCLOAK RUNS AS uid 1000. `/opt/keycloak/data/h2` does
# not exist in the image, so Docker has no ownership to copy up and creates the volume owned by
# root:root — the container then cannot write H2 and dies on startup. chown is idempotent and
# matches the live container exactly (1000:0), so it is safe on an already-populated volume.
docker run --rm -v "$VOL":/data alpine:3 chown -R 1000:0 /data

docker run -d --name "$NAME" --restart unless-stopped \
  -p 127.0.0.1:${PORT}:8080 \
  -v "$VOL":/opt/keycloak/data/h2 \
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

# ⚠ THIS USED TO SAY recreating the container WIPES all user state back to the committed realm
# seed, "stateless by design on the test env". That stopped being acceptable the moment real
# operator MFA existed: by 2026-08-25 `matt` had a password + TOTP enrolment that nothing else held,
# and the only documented protection was a hard rule in the runbook saying never run this script.
# The volume above replaces that rule with a mechanism.
#
# ⚠ STILL TRUE, and the trap to understand: an EMPTY volume plus `--import-realm` seeds the realm
# from the COMMITTED json — which is the stripped 2026-07-29 state (~7-9 KB, against 63 KB live) and
# carries NO users. So a first run on a fresh volume gives you a skeleton realm and no accounts.
# ⚠ Do NOT "fix" that by committing a full realm export: a real export embeds password hashes and
# TOTP secrets, and that must not go into git. The recovery path for accounts is the backup
# (`~/PLUTUS/backups/keycloak-*/realm-export/`), not this repo.
#
# ⚠ The themes bind mount below is load-bearing: the realm sets `loginTheme: plutus`. The live
# container built before 2026-08-25 had those files sitting in its writable layer with NO mount, so
# recreating it would have silently unbranded the login page as well as wiping the accounts.

echo "Started $NAME on 127.0.0.1:${PORT}. Waiting for realm to import..."
for i in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:${PORT}/realms/plutus/.well-known/openid-configuration" >/dev/null 2>&1; then
    echo "Keycloak up; realm 'plutus' OIDC metadata reachable on :${PORT}."
    exit 0
  fi
  sleep 2
done
echo "Timed out waiting for Keycloak. Logs:"; docker logs --tail 40 "$NAME"; exit 1
