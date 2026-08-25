# Cutover — the till gets its own subdomain, the bare host becomes the landing page

> **Matt, 2026-08-25:** *"Can you make the plutus.huggett.dscloud.me default page the new landing
> page … It needs a 'Login' in the top right hand corner that takes you to the 'Portal' login. This
> needs a 'Switch to till' option that takes you to the till login. On the till login screen, it
> needs a 'Switch to portal' option."* → *"New till subdomain please."* → *"Landing page needs to
> wait for tonight."*
>
> **Do this when the shop is shut.** It requires re-enrolling both web tills.

## ⏱ PROGRESS — updated 2026-08-25 evening, mid-cutover

| | |
|---|---|
| ✅ **Step 1 applied** | `till.plutus.huggett.dscloud.me` is live. Certificate issued (`CN=till.plutus…`, valid to 23 Nov 2026), serving till **1.39.0**, API proxy answering **401** on the device probe — the answer that proves the DB path, not a 404 or 502. |
| ✅ **Till 1.39.0 deployed** | Carries *Switch to portal*. Both hosts serve it — nothing has been taken away. |
| ✅ **Portal 1.27.0 deployed** | ⚠⚠ **THIS STEP WAS MISSING FROM THIS RUNBOOK AND MATT CAUGHT IT**: *"The switch to till link in the portal needs updating."* The portal **derived** the till's URL by stripping `admin.`, which yields the **bare host** — the very host about to become the landing page. Left alone, "Switch to Till" would have sent staff to a marketing page. See §"the portal" below. |
| ✅ **Landing 1.2.0 built and on disk** | In `/srv/apps/PLUTUS/landing/current`, with the tombstone `sw.js`. **Served by nothing yet** — that is Step 2. |
| ⬜ **Re-enrol both web tills** | On `https://till.plutus.huggett.dscloud.me`, then ring a test sale on each. |
| ⬜ **Step 2** | The one-line Caddy swap. |

## ⚠ The portal — the surface this runbook forgot

Any surface that **links to** the till has to move with it, not just the till itself. There was one,
and only one: the portal's *Switch to Till* button.

`auth.ts tillUrl()` derived the till host from the portal's own (`admin.X` → `X`). That convention
died the moment the bare host became the landing page. Fixed two ways, deliberately:

- **`VITE_TILL_URL=https://till.plutus.huggett.dscloud.me` is now set in the portal build** — that is
  the authority.
- **The fallback derivation now yields `till.X`, not `X`**, so a future build that forgets the
  variable degrades to the right host instead of the marketing page.

⚠ The portal build also needs its two OIDC variables, whose absence is invisible to every gate:
`grep -c 'realms/plutus"' dist/assets/index-*.js` must be **1**, not 0.

## ⚠⚠ Everything that REFERENCES the till host — run this grep, do not trust the list

Moving a host means moving every surface that points at it. The portal was found by Matt using the
thing; the rest were found by finally running the obvious command:

```bash
grep -rn "plutus\.huggett\.dscloud\.me" --include=*.ts --include=*.tsx --include=*.cs \
  --include=*.json --include=*.xaml . | grep -v "admin\.\|login\.\|status\.\|till\."
```

| Surface | Impact | State |
|---|---|---|
| **Portal** `auth.ts tillUrl()` | "Switch to Till" → the landing page | ✅ fixed, portal 1.27.0 live |
| ⚠⚠ **Till Agent** `AgentConfig.AllowedOrigin` | **NO RECEIPT PRINTING AND NO CASH DRAWER.** It is a CORS allow-list matched with a single exact `string.Equals`, defaulting to the old host. The moment the till serves from `till.plutus…` the browser's `Origin` stops matching and the agent refuses it — on a shop counter, with nothing on screen naming the cause. **Both web tills run agent 1.4.0 with a Star TSP143 online.** | ⚠ **NEEDS A MANUAL STEP ON EACH TILL — see below.** Code now accepts a LIST and defaults to the new host, but installed agents keep their saved `agent.json`. |
| **MAUI** `TillConnection.DefaultServerUrl`, `Settings.ServerUrlSetting` | MAUI tills call `/api/*` on the **bare host** | ✅ unaffected — **and this is why the `/api/*` proxy must stay on the bare host.** ⚠⚠ It is there for the landing page's signup calls *and* for every MAUI till. Removing it as "landing pages don't need an API" would take the whole MAUI estate offline. |
| **Keycloak** `plutus-webpos` client (`rootUrl`, redirect URIs, web origins) | Still the bare host | ⬜ latent. The web till runs in **password mode**, so nothing breaks today. It bites the day the web till moves to OIDC — recorded in `Platform Gaps.md`. |

### The Till Agent step — do this on each till, tonight

No rebuild and no reinstall. On the till machine: **Plutus Till Agent tray icon → Settings →
"Till address allowed to connect"** → set it to:

```
https://till.plutus.huggett.dscloud.me
```

⚠ Or, to cover both while you are mid-move, the field now accepts a comma-separated list:
`https://till.plutus.huggett.dscloud.me, https://plutus.huggett.dscloud.me`

⚠ **Then prove it**: ring a sale and print a receipt. "No agent found" is a *different* fault; a
CORS refusal looks like the agent being absent, which is exactly why this is worth testing rather
than assuming.

## Why this is not a one-step change

`plutus.huggett.dscloud.me` currently serves the **till**, and the till is an installed PWA:
`manifest.json` has `start_url`/`scope`/`id` = `/`, and `sw.js` is **network-first on navigation and
re-caches whatever `/` returns as its offline shell**.

⚠⚠ **So simply pointing `/` at the landing page would make each till cache the marketing page as its
offline shell** — and the next time that till lost its network it would show the marketing page
instead of the till. That is the one property this app exists to guarantee, so the sequence below
never lets it happen.

## What breaks, honestly

| | |
|---|---|
| **Both web tills must re-enrol and sign in again** | `till.plutus…` is a **new origin**, and `localStorage` is origin-scoped. The device credential, the session and the device preferences do not travel. |
| **The offline catalogue re-syncs** | IndexedDB is origin-scoped too. First sign-in pulls it again; then press **Plutus → "Re-download the whole catalogue"** to be certain (the catalogue feed only sends items changed since the till's cursor, and a fresh till has none). |
| **MAUI tills: unaffected** | They talk to `/api/...` on the host in their config, not to the web till's origin. |
| **The portal, Keycloak, the status page: unaffected** | Separate hosts already. |
| **Queued offline sales on a web till** | ⚠⚠ **Drain them BEFORE you start.** They live in the old origin's IndexedDB, and after the move that origin is a marketing page. Check each till has nothing waiting to send. |

## Pre-flight — already verified 2026-08-25, no action needed

- **DNS**: the Synology DDNS wildcard resolves *any* depth — `till.plutus.huggett.dscloud.me` already
  answers `94.6.166.54`. Nothing to add; Caddy gets its own certificate on first request.
- **The code is in the tree**: the till's *Switch to portal* link, the landing's *Login* / *Switch to
  till* header, and the landing's tombstone `sw.js`.

## ⚠ One decision before you start

Putting the landing page on `plutus.huggett.dscloud.me` makes it **publicly reachable by anyone who
knows the URL** — that host has no basic_auth, and the router's SNAT means an IP allowlist cannot
work. On 2026-08-24 you said *"not have it externally facing for now"*.

**The `noindex` stays on** (it is in the dist today), so it will not be *found* — search engines are
told to skip it. Reachable-but-unlisted, in other words. If you want it genuinely closed, say so and
it goes behind basic_auth in the same Caddy block.

---

# Step 1 — the till's new home, while the old one still works

Nothing is taken away in this step. The till answers on **both** hosts, so it is safe to stop here
and finish another night.

**1.1 Build the till with the portal link.** On the Mac:

```bash
cd ~/PLUTUS/Plutus.Frontend.WebApp
PLUTUS_APP_VERSION=1.39.0 \
  VITE_PORTAL_URL=https://admin.plutus.huggett.dscloud.me \
  npm run build
```

⚠ `PLUTUS_APP_VERSION` is not optional — the Mac tree has no `versions/` above it and omitting it
ships `0.0.0`. ⚠ Check the artefact carries the link before deploying:
`grep -c "admin.plutus.huggett.dscloud.me" dist/assets/index-*.js` must be **1**.

**1.2 Deploy it** to `/srv/apps/PLUTUS/web/current` (back up `current` → `current.pre-1.39.0` first).
Both hosts serve from that directory, so this changes the till in place — it is still the till.

**1.3 Add the `till.` vhost.** Stage `ops/caddy/till-subdomain-2026-08-25.caddy` **STEP 1** block into
`/etc/caddy/Caddyfile`, keeping the existing `plutus.huggett.dscloud.me` block untouched:

```bash
sudo cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.pre-till-subdomain.bak
# append the STEP 1 block
sudo caddy validate --config /etc/caddy/Caddyfile
sudo caddy reload --config /etc/caddy/Caddyfile
```

**1.4 Verify** — a 200 proves nothing on its own, both hosts have an SPA fallback:

```bash
H=till.plutus.huggett.dscloud.me
curl -s --resolve $H:443:127.0.0.1 https://$H/ | grep -o "index-[A-Za-z0-9_-]*\.js"   # matches the build
curl -s -o /dev/null -w "%{http_code}\n" -L --resolve huggett.dscloud.me:443:127.0.0.1 \
  https://huggett.dscloud.me/health                                                    # ETRIE, must be 200
```

**1.5 Re-enrol both tills.** Portal → **Locations & Tills** → create an enrolment code for each, then
on each till open `https://till.plutus.huggett.dscloud.me`, enrol, sign in, and
**Plutus → Re-download the whole catalogue**.

**1.6 Prove each till actually trades** before going further: ring a small sale, take cash, print or
preview the receipt, and confirm it appears in Reporting.

# Step 2 — hand the bare host to the landing page

Only once step 1.6 has passed on **both** tills.

**2.1 Build the landing page with both links:**

```bash
cd ~/PLUTUS/Plutus.Frontend.Landing
PLUTUS_APP_VERSION=1.2.0 \
  VITE_TILL_URL=https://till.plutus.huggett.dscloud.me \
  VITE_PORTAL_URL=https://admin.plutus.huggett.dscloud.me \
  npm run build
```

⚠ Check the artefact: both hosts present, `noindex` still in `index.html`, and **`sw.js` is in the
dist** (`ls dist/sw.js`) — that file is what retires the till's old service worker.

**2.2 Deploy** to `/srv/apps/PLUTUS/landing/current` (back up first).

**2.3 Swap the vhost.** Replace the `plutus.huggett.dscloud.me` block with **STEP 2** from the staged
file — keeping the `/api/*` proxy, which the signup form needs. Validate, reload, re-check ETRIE.

**2.4 Verify:**

```bash
H=plutus.huggett.dscloud.me
curl -s --resolve $H:443:127.0.0.1 https://$H/ | grep -c "Switch to till"    # the landing page
curl -s --resolve $H:443:127.0.0.1 https://$H/sw.js | head -3                # the TOMBSTONE, not HTML
```

⚠⚠ **That second check is the one that matters.** If `/sw.js` returns HTML, the SPA fallback is
shadowing the tombstone, the old till service worker will never retire, and it will keep caching this
page. It must return JavaScript beginning with the tombstone comment.

**2.5 On each till machine**, visit `https://plutus.huggett.dscloud.me/` once. That is what lets the
tombstone install and unregister the old worker. The tab will reload itself; you should land on the
landing page with the till's old worker gone.

## Rollback

| After | To undo |
|---|---|
| Step 1 | Remove the `till.` block, reload Caddy. Nothing else changed. |
| Step 2 | `sudo cp /etc/caddy/Caddyfile.pre-till-subdomain.bak /etc/caddy/Caddyfile`, validate, reload. The till returns to the bare host. ⚠ Tills already re-enrolled on `till.plutus…` keep working there — both hosts serve the same directory. |

⚠ The one thing rollback does **not** undo is the tombstone, on any browser that reached step 2.5.
That is harmless: those browsers simply have no service worker until they load the till again on the
new origin and it registers a fresh one.
