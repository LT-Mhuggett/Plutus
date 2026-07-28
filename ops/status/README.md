# Plutus status page (WP15.2)

A public, backend-independent status page for the Plutus platform.

## How it works

- **`StatusPageWriter`** (a hosted service in `Plutus.Tenancy`) writes `status.json` every 30s to
  the path in `STATUS_JSON_PATH`. It is **inert** unless that env var is set, so dev/test never
  touch the disk. The snapshot carries overall `status` (`operational` / `degraded` / `incident`),
  the last-hour aggregate 5xx error rate, request volume, and any open **Incident** announcements.
  Status derivation: an open Incident announcement ⇒ `incident`; otherwise a last-hour 5xx rate
  over 5% ⇒ `degraded`; otherwise `operational`.
- **`status.html`** is served by a **separate static Caddy vhost** (`caddy-status-vhost.caddy`),
  *not* the app. Because the file server is independent of the backend, the status page stays
  reachable when the backend is down. The page polls `status.json` every 30s and, if
  `generatedAtUtc` is more than **2 minutes** behind, shows **"Status unknown (stale)"** — which is
  exactly what a dead backend produces (the writer stops, the file goes stale).

## SLA

`GET /api/v1/platform/sla?tenantId=<guid>&month=YYYY-MM` (platform-admin) returns advisory monthly
availability: `minutesWithTraffic`, `goodMinutes`, and `availabilityPct` = minutes whose 5xx-rate
is under 1% ÷ minutes with traffic, from `TenantRequestStats`. Single-box, advisory-grade (flagged
`advisory: true`).

## Deploy (Matt — needs sudo for the Caddyfile)

See the header of `caddy-status-vhost.caddy`. In short: create the docroot, copy `status.html` →
`index.html`, set `STATUS_JSON_PATH` in the backend's pm2 env and restart, append the vhost block
to `/etc/caddy/Caddyfile`, `sudo caddy reload`, then verify `status.json` refreshes.

## DoD rehearsal

Stop the backend (`pm2 stop plutus-backend`) and confirm the status page is still reachable and
flips to "stale/unknown" within 2 minutes; restart and confirm it returns to operational.
