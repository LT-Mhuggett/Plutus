# Plutus incident-response runbook (WP18.3)

The first place to look when something is on fire. Terse by design. Everything here uses levers
this codebase already has — no new tooling.

> **ETRIE rule:** ETRIE shares the Mac mini but is a separate product. Never touch its ports,
> pm2 processes (`etrie-*`), web root (`/srv/apps/ETRIE`) or Caddy block. After any Mac change,
> re-check ETRIE: `curl --resolve huggett.dscloud.me:443:127.0.0.1 https://huggett.dscloud.me/health` → 200.

## Severity ladder

| Sev | Meaning | Examples | Comms |
|-----|---------|----------|-------|
| **SEV1** | Platform-wide outage or suspected breach | backend down for all tenants, DB down, credential leak | status page + Incident announcement + notify Matt immediately |
| **SEV2** | One subsystem degraded; selling continues | connector storm, elevated 5xx on one route, one tenant down | status page (if broad) + Maintenance announcement |
| **SEV3** | Contained / cosmetic | single stuck job, one tenant's connector silent | operator dashboard only; fix in-hours |

## First 15 minutes — by failure class

### Backend down (SEV1)
1. Confirm: `curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5100/swagger/v1/swagger.json` (on the Mac) — non-200 or refused.
2. The **status page stays up on its own** (`https://status.plutus.…` — static Caddy vhost, independent of the app); within 2 min it flags **"Status unknown (stale)"** automatically.
3. `pm2 logs plutus-backend --lines 100` → find the throw. If it's a bad deploy, **roll back**: `pm2 stop plutus-backend; rm -rf ~/PLUTUS/backend; mv ~/PLUTUS/backend.pre-phase<N> ~/PLUTUS/backend; pm2 restart plutus-backend --update-env` (rollback dirs are kept per phase).
4. Poll swagger → 200. Then post an **all-clear** (delete the Incident announcement / post a resolved one).

### Database down (SEV1)
1. Confirm: `mysqladmin -uplutus -p… ping` (creds in `~/PLUTUS/secrets/mysql.env`).
2. `brew services list | grep mysql`; restart via `brew services restart mysql` (Matt — needs the login session). Backend self-recovers once the DB answers (pooled reconnects).
3. If data is suspect, DO NOT improvise: nightly dumps are in `~/PLUTUS/backups/nightly/` (03:30 daily). For a single tenant use **`tools/Plutus.TenantRestore`** (see its RUNBOOK) — never a full restore over live to fix one tenant.

### Connector storm (SEV2)
1. Operator dashboard → **Platform → Health → Connector health**: which connector/tenant is red (silent or error-streak).
2. Alerts feed shows the keyed `connector-silent` / `connector-error` alert (auto-raised by `ConnectorMonitor`).
3. If a webstore is hammering us, set its `OutboundMode` to `off` (kill switch) or disable the connection on the Webstore tab. Selling continues regardless (webstore is downstream of the till).
4. A tenant-wide feature can be killed instantly via **Platform → Flags** (`PlatformFlag` kill switch — beats plan + overrides).

### Suspected breach (SEV1)
1. **Rotate secrets** (LAN-only test env, but treat seriously):
   - ⚠ **Rotating the MySQL password will break any plain-TCP client — read this before you do it.** The `plutus` account is `caching_sha2_password`: the server caches the password digest, only the cheap "fast auth" path works from that cache, and **changing the password empties it**. Full authentication then becomes necessary, and MySQL allows it only over a channel it deems secure (unix socket, TLS, or an RSA exchange). On 2026-08-09 a successful rotation crash-looped `plutus-backend` for ~15 minutes because its connection string was plain `Server=127.0.0.1;Port=3306`. **Rolling the password back does not fix it** — the cache stays cold for the old value too. The backend now connects over `/tmp/mysql.sock`; the rotation script refuses to run against an unsafe connection string, and [`ops/recover-mysql-auth.sh`](recover-mysql-auth.sh) is the recovery if it recurs.
   - MySQL `plutus` password → **run [`ops/rotate-mysql-password.sh`](rotate-mysql-password.sh) on the Mac.** It does all four places (MySQL, `~/PLUTUS/secrets/mysql.env`, the pm2 ecosystem `ConnectionString`, `~/.pm2/dump.pm2` via `pm2 save`), backs both files up `.pre-rotate-*` first, verifies the new password authenticates BEFORE restarting, and checks ETRIE afterwards. ⚠ It generates the password on the Mac and never prints it — this script exists because the old one was echoed into a session transcript twice (2026-07-26, 2026-08-09). ⚠ It restarts from the ecosystem FILE, not the process name: `pm2 restart plutus-backend --update-env` does not re-read new keys from the file. ⚠ `appsettings*.json` hold stale placeholders and are deliberately not touched.
   - `TEST_TOKEN_SECRET` / `JOBS_REPORT_SECRET` in the pm2 env — rotating invalidates all issued HMAC tokens (forces re-login), which is the point.
   - Webstore per-store secrets/rest-keys in the pm2 env.
2. Revoke operator access: with WP18.1 SSO enforced, disabling the Keycloak operator account is the single lever; until then, rotating `TEST_TOKEN_SECRET` invalidates HMAC operator tokens.
3. Preserve evidence: `AuditLogs` (per-tenant) + `OperatorAlerts` + pm2 logs before rotating. Snapshot the DB (`mysqldump … --no-tablespaces --skip-lock-tables --set-gtid-purged=OFF`).
4. Notify Matt; post a Maintenance/Incident announcement scoped appropriately.

## Who / what to notify

- **Status page** (`status.plutus.…`) — the always-up channel; reflects open **Incident** announcements + last-hour error rate automatically.
- **Announcements** (Platform → Comms, or `POST /api/v1/platform/announcements`): templates —
  - *Incident:* `severity=2, title="Service disruption", body="We're investigating an issue affecting <scope>. Updates here."`
  - *Maintenance:* `severity=1, title="Scheduled maintenance", body="<system> is briefly unavailable while we deploy a fix."`
  - Target all tenants (`tenantIds=null`) or a specific tenant. The till shows Maintenance/Incident banners; the portal shows all severities.
- **Matt** for anything SEV1 or any secret rotation.

## Rollback levers (what this codebase already gives you)

- **Bad backend deploy** → `backend.pre-phase<N>` rollback dirs + `pm2 restart`.
- **Bad feature** → `PlatformFlag` kill switch (Platform → Flags) — instant, no deploy.
- **Bad entitlement/limit** → per-tenant override (Platform → tenant detail) — takes effect on next read.
- **Bad Caddy edit** → `/etc/caddy/Caddyfile.pre-*.bak` backups + `sudo caddy reload` (Matt).
- **Bad data (one tenant)** → nightly dump + `tools/Plutus.TenantRestore` (`--verify` then `--apply`).
- **Bad config** → pm2 ecosystem backups (`plutus-ecosystem.config.js.pre-*`).

## Rehearsal log

- **2026-07-28 (SEV1 backend-down tabletop):** posted an Incident announcement → status page flipped to **incident**; `pm2 stop plutus-backend` → status page stayed reachable (static vhost) and `status.json` stopped advancing (page flags stale within 2 min); `pm2 restart` → `status.json` resumed + swagger 200; deleted the announcement → status **operational**. ETRIE unaffected (200 throughout). Recorded in HANDOVER.
