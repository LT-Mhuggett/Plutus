# Keycloak — Plutus test-env IdP (Phase 9, WP9.2)

One of the two switchable identity providers behind the Phase-9 seam (the other is Entra
External ID — see `ops/entra/`). Keycloak runs self-hosted on the Mac so the IdP swap can be
proven end-to-end without a cloud tenant. Selected by `IdP:Provider=keycloak` on the backend.

## What runs where

- **Container:** `plutus-keycloak` (Docker, image `quay.io/keycloak/keycloak:26.0`), bound to
  `127.0.0.1:8089`, `--restart unless-stopped`. Runs alongside ETRIE and the `caddymanager`
  stack without touching them (own name/port/volume). Started by `run-keycloak.sh`.
- **Realm:** `plutus`, defined by the committed `plutus-realm.json` (imported on boot). Two public
  SPA clients — `plutus-portal` and `plutus-webpos` — both auth-code + PKCE (S256); a
  `plutus-api-audience` client scope stamps `aud=plutus-api` so the backend accepts the token.
- **Public URL:** `https://login.plutus.huggett.dscloud.me` (issuer). Requires the Caddy vhost
  (`caddy-login-vhost.caddy`) — **needs Matt's sudo to apply** (see below).

## Backend config to switch to Keycloak

In `~/PLUTUS/plutus-ecosystem.config.js` (or appsettings):

```
IdP__Provider = keycloak
IdP__Keycloak__Authority = https://login.plutus.huggett.dscloud.me/realms/plutus
IdP__Keycloak__Audience  = plutus-api
```

Then restart `plutus-backend`. Leave `IdP__Provider` empty (or `test`) to keep the current
HMAC login. Device/till enrolment tokens keep working under every provider (the router scheme
sends 2-segment HMAC tokens to the HMAC handler, 3-segment JWTs to Keycloak).

## Identity mapping

Keycloak only authenticates. `RbacClaimsTransformation` matches the token's verified **email**
to a `WebCredentials` row → resolves the user's RBAC scopes server-side. For a live test, the
Keycloak user's email must match a `WebCredentials.Email` (either edit the realm user or add a
`WebCredentials` row via `/api/Auth/SetPassword`). The seeded demo user is `ada@shop.test`.

## Apply the Caddy vhost (Matt — sudo)

```bash
# 1. Back up and stage
sudo cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.pre-keycloak.bak
sudo sh -c 'cat ~/PLUTUS/ops/keycloak/caddy-login-vhost.caddy >> /etc/caddy/Caddyfile'
# 2. Validate + graceful reload
caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo systemctl reload caddy    # or: sudo caddy reload --config /etc/caddy/Caddyfile
# 3. Verify (Plutus login up, ETRIE untouched)
curl -s -o /dev/null -w "keycloak: %{http_code}\n" https://login.plutus.huggett.dscloud.me/realms/plutus/.well-known/openid-configuration
curl -s -o /dev/null -w "etrie:    %{http_code}\n" https://huggett.dscloud.me/health
```

Rollback: `sudo cp /etc/caddy/Caddyfile.pre-keycloak.bak /etc/caddy/Caddyfile && sudo systemctl reload caddy`.

## Lifecycle

- Restart / re-import from the committed realm: `./run-keycloak.sh` (recreates the container).
- Logs: `docker logs -f plutus-keycloak`. Stop: `docker stop plutus-keycloak`.
- Admin console (local only): `ssh -L 8089:127.0.0.1:8089 …` then `http://127.0.0.1:8089/admin`
  (bootstrap admin from `KC_ADMIN_USER`/`KC_ADMIN_PASS` at first run). The edge blocks `/admin*`.

## WP18.1 — Operator MFA/SSO (staged; activation gated on the `login.plutus` vhost)

The realm export now carries a `platform-admin` realm role, an `operators` group that grants it,
and an `operator` user forced to enrol TOTP (`requiredActions: [CONFIGURE_TOTP]`, realm
`otpPolicyType: totp`). Backend side, `PlutusTokenAuthHandler` gained a flag
**`OPERATOR_SSO_ENFORCED`** (default off): when on, HMAC ("test") logins are stripped of the
`platform-admin` scope, so operator access is only obtainable via the Keycloak/OIDC path.

**Do NOT set `OPERATOR_SSO_ENFORCED=true` until all of these are true**, or operators lose the
Platform tab on the live test env:
1. Matt has applied the `login.plutus` Caddy vhost (`ops/keycloak/caddy-login-vhost.caddy`, sudo).
2. Keycloak is reachable and the portal's Keycloak login path is verified end-to-end.
3. A real operator account is in the `operators` group and has enrolled TOTP.
4. The OIDC claims-transformation emits `scope=platform-admin` for `operators`-group members.

Then set `OPERATOR_SSO_ENFORCED=true` in the backend pm2 env and restart. Verify: an HMAC
platform-admin token gets 403 on `/api/v1/platform/*`; a Keycloak operator login (post-TOTP) sees
the Platform section. Group-conditional "force OTP at every login" (not just first-enrolment) is
applied by cloning the browser flow with a Conditional-OTP sub-flow bound to the `operators` group.
