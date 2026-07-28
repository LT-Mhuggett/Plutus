# Plutus as a full webapp — rebuild plan

**Date:** 2026-07-23
**Status:** Design for review. No code changed by this document.
**Companion docs:** [HANDOVER.md](../HANDOVER.md), [Migration-2026-07-22-plan.md](Migration-2026-07-22-plan.md), [OfflineMode-2026-07-23-plan.md](OfflineMode-2026-07-23-plan.md).

---

## 1. Goal (TL;DR)

Rebuild the Plutus POS client as a browser-based webapp: till/checkout, inventory, statistics/reports and settings running in a browser, served from a web host, using the existing backend API. Native installs (MAUI/Xamarin) become optional rather than required; any device with a browser becomes a potential till or back-office terminal.

**The single most important framing:** Plutus already has a clean client/server split. `Plutus.DBService` is a complete ASP.NET Core Web API covering every entity. A webapp is *a third client* of that API — this is an additive frontend project, not a system rewrite.

---

## 2. What exists today (assets & liabilities)

### 2.1 Backend — the asset
`Plutus/Endpoints/Plutus.DBService` (net7, Docker-ready):
- 19 controllers (Business, Employee, Item, Category, Stock, Sale, Till, Store, Tax, Discount, Role, Refund, Note, PaymentMethod, SavedTransaction, Transaction, CheckoutItemChange, AuthAction) built on shared CRUD controller bases (`Controllers/Bases/ApiControllerBase{R,CR,CRU,CRUD}.cs` + composite variants).
- Auth: JWT bearer via Azure AD B2C (`Microsoft.Identity.Web`), plus `Plutus.Authentication` project.
- Swagger already wired (`Swashbuckle`) → contract-first client generation is available for free.
- Shared domain: `Plutus.Entities` (models), `Plutus.Repository` (EF Core), `Plutus.Reports`.

### 2.2 Abandoned WebUI — mine it, don't revive it
`Plutus/Frontend/Plutus.Frontend.WebUI` (~2021): netcoreapp3.1 SPA host (EOL Dec 2022) + Create-React-App ClientApp — React 16, react-scripts 3 (CRA is dead), TypeScript 3.6, MSAL-browser v2, Redux + class-era patterns. Contents are placeholders (`BusinessHome/Employee/Store/Till.tsx`, a Counter/WeatherForecast template leftover).
**Verdict: do not resurrect.** Salvage: `authConfig.js` (B2C SPA settings), `config/routes.ts` (intended page map), and the implicit decision that WebUI = B2C via MSAL redirect.

### 2.3 Existing clients — the functional spec
- NatApp (Xamarin, frozen) and MAUI ClientUI define the required screens and flows: Login, Till (basket, adjust/return/alteration popups, checkout with split payment + cashback), Inventory (view-all, add/edit, categories), Statistics (sales report, stock outtake), Settings (DB, printer, till/checkout options).
- The MAUI ViewModels are the best behavioural reference (e.g. `TillViewModel.cs` checkout flow) — port the *logic*, not the code.

### 2.4 Seed data — live till backup (added 2026-07-23)

`Kapow Comics ltd - Database - 23_07_2026 15_57_23.db` (`Build/seed-data/`, 35MB, **gitignored — real business data, never commit**) is the latest backup from the live NatApp till. Inspected read-only 2026-07-23; healthy SQLite, 22 tables:

| Table | Rows | Table | Rows |
|---|---|---|---|
| Items (incl. image BLOBs) | 20,372 | Sales | 21,653 |
| Trans (sale lines) | 74,830 | PaySales | 21,784 |
| Transaction_Discounts | 15,414 | Stocks | 7,868 |
| CheckoutItemChangeModel | 5,723 | Notes / NotesSales | 4,672 each |
| Refunds | 56 | Category | 9 |
| Discounts | 6 | PayMethods | 4 |
| Vats | 3 | AuthActions / EmpAuthActions | 11 each |
| Employees | 1 | Stores | 1 |

**Critical caveat: this is the *old NatApp schema*** (`Plutus.Database` models — `Vats` not Taxes, `Trans`, `CheckoutItemChangeModel`, no `Business`/`Till` tables, TEXT GUID ids, single-store), **not** the new `Plutus.Entities` schema the DBService/webapp use. It cannot be dropped in as-is; it needs a one-off **ETL** (old schema → `Plutus.Entities`):
- Synthesise the `Business` row ("Kapow Comics ltd") the old single-tenant schema never had; re-parent everything to it.
- Id-type mappings (e.g. old `Stores.Id` TEXT GUID → new `Store : Address<int>` int key; item/category composite keys gain `BusinessId`).
- `Vats` → `Taxes`, `Trans` → `Transactions`, `CheckoutItemChangeModel` → `CheckoutItemChange`, etc.
- 20k items with image BLOBs is also a realistic **performance test** for the webapp's item search/virtualised lists — treat that as a feature of this dataset, not a problem.

**Bonus discovery:** the old `Employees` table carries **`HashedPassword` + `Salt`** — the NatApp local-login credentials. This confirms Option B of [OfflineMode-2026-07-23-plan.md](OfflineMode-2026-07-23-plan.md) §4.3 (sync credentials down) matches the original design, and gives the webapp's test environment a real credentialed user to exercise auth flows against.

**Handling rules:** stays gitignored; only ever loaded into the LAN-locked test environment (§3.8); the raw file never deploys anywhere public.

### 2.5 Known backend liabilities the webapp will surface
- **net7 (EOL)** — Migration plan Workstream B (backend → net10) becomes a prerequisite-ish (see Phase 0).
- **B2C tenant is the original developer's** (`plutusdevenv`, backend `plutusbackend.seanknox.net`, reply URL points at jwt.ms) — a webapp login is impossible until the tenant/redirect situation is resolved (same blocker as MAUI, see HANDOVER §5).
- **Authorisation is client-enforced today** (client checks `EmpAuths`; `Authorisation.RequestAuthorisedUserInput` unimplemented; Employee has no credential fields client-side). A browser client makes server-side enforcement mandatory — you cannot trust the browser.
- API replay/permission gaps found during the offline-mode review (e.g. composite deletes unsupported) may need endpoint work as screens hit them.

---

## 3. Architecture decisions

### 3.1 Frontend framework — the big fork

Candidates, honestly assessed for a POS (long-lived till SPA + ad-hoc back office + offline PWA ambitions):

| | **A: Blazor Web App (.NET 10, `Auto` render mode)** | **B: React + TypeScript + Vite** | **C: SvelteKit** | **D: htmx / Razor Pages** |
|---|---|---|---|---|
| Language / toolchain | C# end-to-end | TS; second toolchain | TS; second toolchain | C#, minimal JS |
| Model reuse | **Reference `Plutus.Entities` directly**; validation attributes come along | TS types *generated* from Swagger (`openapi-typescript`/NSwag-TS) — models stay defined once in C#, derived not retyped | Same as React | Direct |
| First-load / payload | `Auto` mode: instant server-side first render, then cached WASM takes over — kills the classic WASM startup penalty | Smallest, fastest first paint | Smaller still than React | Trivial |
| PWA / offline till (Phase 5) | **Weakest ground** — service worker + IndexedDB is hand-rolled JS interop you own | **Paved road** — Workbox, Dexie, TanStack Query cache/mutation-queue ≈ a free outbox | Excellent, smaller ecosystem | Effectively impossible |
| Ecosystem (virtualised lists, dialogs, charts, scanner input) | MudBlazor/Radzen/Telerik — adequate, thin | Deepest; multiple battle-tested options per need | Good, thinner than React | n/a |
| Team fit today | Matches the all-.NET codebase & skills | Requires web-ecosystem fluency | Ditto, smaller talent pool | Matches |
| Longevity / hiring | Niche adoption; .NET devs | Safest 10-year bet; biggest pool | Healthy but smaller | Fine for CRUD |
| Auth | `Microsoft.Authentication.WebAssembly.Msal` | `@azure/msal-react` | MSAL-browser (hand-wired) | Cookie/OIDC server-side |
| Till suitability | Good (client-side after WASM activation) | Good | Good | **Bad** — stateful basket/checkout needs a real client app |

**The decision hinges on one question: who maintains this in year 3?**

- **A .NET team (current reality)** → **Option A: Blazor Web App (.NET 10, `Auto` render mode) + MudBlazor.** One language, direct `Plutus.Entities`/`Plutus.Contracts` reuse, and the till/checkout logic ports from the MAUI ViewModels almost mechanically. Do **not** use plain standalone WASM — the `Auto` render mode is what removes the startup-payload objection. Accept that Phase 5 (offline PWA) will be the hardest phase on this stack.
- **Web developers, ever — or the offline-PWA till is a must-have core feature** → **Option B: React + TS + Vite + TanStack Query**, full stop. Swagger-generated types neutralise most of Blazor's model-reuse advantage, and the riskiest phase (offline) lands on that ecosystem's strongest ground. Don't choose Blazor and then hire web devs into it.
- **Option C (SvelteKit)** only on strong preference — leaner than React but a smaller ecosystem/pool; it wins nothing decisive here.
- **Option D (htmx/Razor)** rejected for the till (mentioned to dismiss): great for CRUD back office, wrong for a stateful basket/checkout screen, and offline is a non-starter.

**Default recommendation for this repo as it stands: Option A** — with the explicit tripwire that if either condition in the second bullet becomes true before Phase 3 starts, switch to Option B while the sunk cost is still one skeleton and some CRUD pages.

> **✅ DECIDED 2026-07-23: Option B — React + TypeScript + Vite** (signed off by Matt, weighing ecosystem strength and the §3.5.1 minimal-dependency discipline against the standards-first option). The dependency manifest in §3.5.1 is binding: no UI kit, no Redux, no CSS-in-JS; churn-exposed packages stay behind thin adapters.

*(Pure Blazor Server is rejected for the till: a dropped circuit freezes the till mid-sale; POS needs client-side resilience. The `Auto` mode's server-rendered first visit is a progressive-enhancement step, not the steady state.)*

**Perspective on total risk:** the two hardest parts of this project — the hardware agent (§3.5) and server-side authorisation (§3.4) — are identical under every option above. The framework choice moves less of the total risk than it appears to.

### 3.2 Hosting shape
- **Option A (Blazor `Auto`)**: a thin ASP.NET Core host project (required for the server-side first render) + the WASM client project; deployable alongside or in front of DBService. **Option B (React)**: pure static files, served by anything (DBService via `MapFallbackToFile`, or a CDN/static host). Either way, frontend deploys stay independent of API deploys.
- CORS enabled on DBService for the webapp origin.
- One new project: `Plutus/Frontend/Plutus.Frontend.WebApp` (+ optionally `Plutus.Frontend.WebApp.Shared` for client-side services/state). Delete `Plutus.Frontend.WebUI` once salvaged.

### 3.3 API access layer
- Generate a typed client from the existing Swagger (NSwag) *or* hand-write a thin `ApiClient` over `HttpClient` with a `DelegatingHandler` that injects the MSAL token (fixes, by construction, the "token captured once at login, never refreshed" fragility the MAUI client has).
- Server remains source of truth; client state is per-session (current business/store/till, basket) in a scoped state container (`AppState` equivalent).

### 3.4 AuthN/AuthZ
- **AuthN:** Azure AD B2C, MSAL redirect flow (SPA app registration with the webapp's redirect URIs). Requires tenant access or a new tenant — same decision the MAUI app is already blocked on; do it once for both.
- **AuthZ:** move enforcement server-side. Map `EmpAuths`/roles into token claims (B2C custom attributes or an enrichment endpoint) and apply `[Authorize(Policy=...)]` on DBService controllers. The client only *hides* buttons; the server *refuses* actions. This retires the never-implemented client-side authorisation prompt pattern for good.
- Till supervisor-override flow ("another admin authorises"): a proper server endpoint (`POST /api/auth/elevate` with the supervisor's credentials/token) — the design the native clients never got.

### 3.5 Receipt printing & cash drawer — the genuinely hard part
Browsers cannot talk OPOS/ESC-POS directly. Options, in order of recommendation:

1. **Local hardware agent (recommended):** a tiny .NET worker/tray app installed on till PCs, exposing `http://localhost:port` (or WebSocket) with endpoints like `/print`, `/opendrawer`, `/status`. The webapp calls it; the agent reuses the **existing `CommonPOSLibrary`/OPOS printing code** from the native apps. This is the industry-standard web-POS pattern; it also gives the webapp a place for other hardware later (scales, customer displays). Non-till machines (back office) simply have no agent and get PDF fallback.
2. **PDF receipts server-side:** `Plutus.Reports` already exists; render receipt → browser print dialog / auto-download. Zero install, but no silent printing and no cash-drawer kick — acceptable fallback, not primary for a till.
3. **WebSerial/WebUSB:** Chromium-only, per-device permission prompts, ESC/POS byte-level work — rejected as primary, viable as a no-install fallback (Chromium is fine on tills you control; the cost is re-implementing `CommonPOSLibrary`'s receipt formatting in TS and handling USB-vs-serial variance per printer model).

**Domain detail that simplifies everything:** the cash drawer almost always plugs into the receipt printer's RJ11/RJ12 "drawer kick" port — "open drawer" is just an ESC/POS kick-pulse command (`ESC p`) sent to the printer. Solve printing and the drawer comes free.

**The agent contract is framework-neutral** — the client side is plain `fetch`, identical under React, Blazor, or vanilla JS, and adds **zero** packages/dependencies:

```ts
// till client → agent (no library needed)
await fetch("http://localhost:9123/print", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(receipt),
});
await fetch("http://localhost:9123/drawer/open", { method: "POST" });
const healthy = await fetch("http://localhost:9123/status").then(r => r.ok).catch(() => false);
```

The till screen polls `/status` and shows a printer-health indicator; agentless machines automatically fall back to PDF (option 2).

**npm caveat (trips people up):** the ESC/POS packages on npm (`escpos`, `node-thermal-printer`, …) are **Node.js-only** — they cannot run in a browser. Browser-side direct printing means the native `navigator.serial`/`navigator.usb` APIs and hand-written ESC/POS bytes; there is no package that makes the sandbox go away.

Barcode scanners are keyboard-wedge — they already work in a browser input; keep the same scan-parsing logic as `TillViewModel.AutoScan`.

### 3.5.1 Option B dependency manifest (React path)

If §3.1 lands on Option B, the runtime dependency set is deliberately small and boring — split by churn exposure, since long-term stability is a stated priority:

| Package | Job | Churn risk |
|---|---|---|
| `react`, `react-dom` | UI | Low — API stable since hooks (2019) |
| `@tanstack/react-query` | server-state cache, retries, **offline mutation queue** (≈ the `DBAction` outbox, client-side) | Medium — wrap behind a thin adapter so it's replaceable |
| `react-router` | ~10 routes | Medium (churns majors) — or hand-roll; a POS barely needs a router |
| `@azure/msal-browser` + `@azure/msal-react` | B2C login | Low — and **never hand-roll auth** |
| `openapi-fetch` | typed `fetch` client over the generated API types | Low — thin over `fetch` |
| `dexie` | IndexedDB wrapper (offline till working-set) | Low — thin over a browser standard |
| `@sentry/react` | crash reporting (AppCenter replacement, aligns with the MAUI Sentry swap) | Low |

Dev-time only (disposable — never appears in runtime code, swappable without touching the app): `typescript`, `vite` + `@vitejs/plugin-react`, `openapi-typescript` (generates API types from the existing DBService Swagger — models stay defined once, in C#), `vite-plugin-pwa` (Workbox service worker), `vitest`, `@testing-library/react`, `playwright`.

**Deliberately excluded** (this is where web churn actually lives):
- **No heavy UI kit** (MUI/Ant/Mantine). A POS needs buttons, inputs, dialogs and one virtualised list: plain CSS / CSS Modules (zero runtime), at most **Radix primitives** (headless, tiny) for dialog/focus behaviour.
- **No Redux/state library** — the basket is one object; `useReducer` + context suffices.
- **No CSS-in-JS, no date library** (`Intl` is built in), **no component framework**.

Net: ~8 runtime dependencies, roughly half of them thin wrappers over browser standards — about as churn-resistant as a React app can be. Note the hardware integration (§3.5) contributes zero of them.

### 3.6 Offline strategy (PWA)
A webapp *raises* the offline bar vs. MAUI (no local SQLite). Mirror the design in [OfflineMode-2026-07-23-plan.md](OfflineMode-2026-07-23-plan.md), translated to web:
- **PWA** (installable, service worker caching the app shell).
- **IndexedDB** cache of the working set (items, categories, taxes, payment methods, current employees) refreshed on login/interval.
- **Outbox** in IndexedDB for sales/stock movements made offline; background replay on reconnect — the same `DBAction` concept, client-side.
- Token caveat: B2C tokens expire (~1h); design offline sales to be *queued locally and replayed under the next valid token*, with the till usable for checkout while auth is stale. Reports/admin can simply require connectivity.
- Scope control: **offline = till checkout only.** Inventory management, reports, settings are online-only. This keeps the sync surface small and matches how tills fail in the real world.

### 3.7 What is explicitly out of scope for the webapp
- Local SQLite restore/backup (a native-client concern; the webapp's "database" is the server).
- AppCenter (dead) — use Sentry (browser SDK) from day one, aligning with the planned MAUI Sentry swap.

### 3.8 Test hosting environment (Mac mini, `10.1.1.40`)

The test deployment target is the existing macOS host at `10.1.1.40`, which already runs **Caddy** as its TLS edge for the ETRIE installation (see [Environment_Setup_Runbook.md](Environment_Setup_Runbook.md)). **Hard constraint: the ETRIE installation must not be touched.**

**DNS — already solved.** Synology DDNS serves a **wildcard**: `*.huggett.dscloud.me` (verified 2026-07-23: `plutus.huggett.dscloud.me` and a random-label probe both resolve to the same public IP, `94.6.166.54`). Router 80/443 → Caddy forwarding is already live (runbook, TLS section). So the entire job is one **additive sibling site block** in `/etc/caddy/Caddyfile` — no DNS work, no router work.

```caddyfile
# ── PLUTUS test site — fully separate from ETRIE ─────────────────
# Own hostname, own auto-issued Let's Encrypt cert, own web root,
# own ports, own logs. LAN-only via IP allowlist.
plutus.huggett.dscloud.me {
	# IP allowlist: LAN only. Public internet gets 403.
	@outsiders not remote_ip 10.1.1.0/24 127.0.0.1 ::1
	respond @outsiders 403

	root * /srv/apps/PLUTUS/web/current
	encode zstd gzip
	try_files {path} /index.html
	file_server

	# later, when the Plutus API/dev process exists:
	# handle /api/* { reverse_proxy 127.0.0.1:5100 }

	log {
		output file /var/log/caddy/plutus-access.log
		format console
	}
}
```

**Separation guarantees:**

| Concern | ETRIE (untouched) | Plutus (new) |
|---|---|---|
| Hostname / certificate | `huggett.dscloud.me` (own LE cert) | `plutus.huggett.dscloud.me` (own LE cert, auto-issued) |
| Web root | `/srv/apps/ETRIE/web/current` | `/srv/apps/PLUTUS/web/current` |
| Backend ports | 3001 / 5173 / 8443 | 5100+ only (never ETRIE's) |
| PM2 process names | `etrie-*` | `plutus-*` |
| Access log | `etrie-access.log` | `plutus-access.log` |

**The only shared touch-points** (both covered by the runbook's own safety pattern):
1. `/etc/caddy/Caddyfile` — additive sibling block, zero ETRIE lines changed. Protocol: back up to `Caddyfile.pre-plutus.bak` (continuing the existing backup convention) → `caddy validate --config /etc/caddy/Caddyfile` → apply.
2. One **graceful** config reload via `caddy reload` (admin API, zero-downtime; in-flight ETRIE requests unaffected) — *not* the runbook's `launchctl kickstart`, which restarts the process. Afterwards, re-run the runbook's four ETRIE verification curls **plus** the new Plutus checks; ETRIE verification is part of this change's definition of done.

**Access-control notes (VERIFIED on first deploy, 2026-07-23):**
- ACME/Let's Encrypt issuance worked immediately (cert issued 2026-07-23 15:19 UTC, renews automatically).
- **The `remote_ip` allowlist does NOT work on this network and was replaced with `basic_auth`.** Verified empirically: an external probe (Anthropic infra) reached the site straight through the `10.1.1.0/24` allowlist. The router **source-NATs forwarded WAN traffic to a LAN address**, so every external request reaches Caddy with an in-range source IP — the mirror image of the hairpin caveat. Consequence: on this router, Caddy-level IP filtering cannot distinguish external from internal clients. The effective gate is HTTP `basic_auth` (user `plutus`; credentials in `~/PLUTUS/secrets/site-basicauth.txt` on the Mac, chmod 600).
- Hairpin NAT itself works fine — LAN clients reach the public hostname without any DNS override.

**First-deploy checklist:** create `/srv/apps/PLUTUS/web/current` with a placeholder `index.html` → add the site block → backup + validate → graceful reload → confirm cert issued → confirm a LAN browser gets the placeholder with a green padlock → confirm an external connection gets 403 → re-run the ETRIE verification curls.

---

## 4. Phased plan

### Phase 0 — Unblock & modernise foundations (prereqs, mostly existing plans)
1. **Resolve the B2C tenant** (own tenant or credentials to the dev tenant) + create a **SPA app registration** with webapp redirect URIs. *Blocks all real login; already blocks MAUI (HANDOVER §5) — solve once.*
2. **Backend → net10** (Migration plan Workstream B) and enable CORS. Not strictly blocking (a net7 API serves a webapp fine) but do it before building on top.
3. Decide framework (§3.1) — sign-off needed.

### Phase 1 — Skeleton: auth + shell + read-only screens (small)

> **STATUS 2026-07-23 — Phase 1 exit criterion MET (except MSAL):** `https://plutus.huggett.dscloud.me` serves the React app behind basic_auth; `/api/*` proxies to `plutus-backend` (self-contained DBService under pm2, port 5100, `DISABLE_AUTH_DEV_ONLY` flag active because of the B2C blocker); inventory screen lists the real seeded Kapow catalogue. Remaining from Phase 1: MSAL login (blocked on tenant), typed-client generation, nav shell, Business/Store/Till bootstrap (BusinessId currently a constant in `src/api.ts`).
- New `Plutus.Frontend.WebApp` (Blazor Web App, `Auto` render mode, + MudBlazor — or Vite+React if §3.1 lands on Option B), CI build.
- Test hosting live per §3.8: `plutus.huggett.dscloud.me` on the Mac mini (Caddy sibling site, LAN-only allowlist), starting with a placeholder page and then serving each Phase 1 increment.
- MSAL login, token handler, typed API client (NSwag from Swagger).
- App shell (nav parity with MAUI flyout: Till / Inventory / Statistics / Settings gear).
- Read-only **Inventory list** + **Business/Store/Till bootstrap** (port of `LoginViewModel.LoadDataIn`).
- **Seed ETL** (§2.4): one-off tool migrating the Kapow live-till backup (old NatApp schema) into `Plutus.Entities` shape and loading it into the test backend's database — so every screen from day one runs against Kapow's real 20k-item catalogue and sales history rather than fabricated data.
- **Exit criterion:** sign in in a browser, see **Kapow's actual inventory** from the API.

### Phase 2 — Back office (medium)
- Inventory add/edit + category create (port `AddEditInventoryViewModel`, `CategoryCreateViewModel`).
- Statistics: sales report + stock outtake (API already produces reports; render/download in browser).
- Settings page (till/checkout toggles — per-user/till server-stored instead of device Preferences).
- Server-side authorisation policies on the touched endpoints (§3.4).
- *Rationale for ordering: back office is pure CRUD — fastest value, no hardware, exercises the whole stack before the till.*

### Phase 3 — The till (large; the core)
- Basket screen (port `TillViewModel`: scan/manual add, quantity, adjust price, returns, alterations/discounts, saved transactions).
- Checkout flow: split payment, cashback, change calculation.
- Popups become dialogs (MudBlazor `DialogService` — same result-returning pattern as the MAUI `ShowPopupAsync` rework).
- Receipts: **PDF fallback first** (server-rendered), so checkout is complete end-to-end without hardware.
- Supervisor-override endpoint + UI (§3.4).
- **Exit criterion:** a full sale, browser-only, receipt as PDF.

### Phase 4 — Hardware agent (medium, parallelisable with Phase 3 tail)
- .NET worker/tray app wrapping the existing `CommonPOSLibrary` OPOS code; localhost API; pairing/config UI in webapp Settings ("Change printer" talks to the agent).
- Cash-drawer kick + silent receipt printing from the till screen; agent auto-discovery + health indicator.

### Phase 5 — PWA/offline till (medium–large, optional but recommended)
- Service worker, IndexedDB working set, offline checkout outbox + replay (§3.6).
- Chaos-test: kill network mid-sale, expire token, replay on reconnect.

### Phase 6 — Parity, pilot, cutover
- Interactive-test matrix mirroring the six MAUI popups + checkout permutations.
- Pilot on one till alongside the native app; then decide the native clients' fate (NatApp freeze is already planned; MAUI may remain for tablets/offline-heavy sites or retire).

---

## 5. Effort shape (relative, not calendar)

| Phase | Size | Risk |
|---|---|---|
| 0 Foundations | S (mostly admin/agreed plans) | B2C tenant access is the wildcard |
| 1 Skeleton | S–M | Low |
| 2 Back office | M | Low |
| 3 Till | L | Medium — domain logic fidelity |
| 4 Hardware agent | M | Medium — OPOS quirks (known code helps) |
| 5 Offline PWA | M–L | Medium–high — sync edge cases |
| 6 Cutover | S–M | Low |

---

## 6. Risks & open questions

1. **B2C tenant** — hard blocker for any real login (native *and* web). Decide: recover access vs. stand up own tenant (own tenant also fixes the jwt.ms reply-URL weirdness).
2. **Server-side authorisation is not optional** on the web; today's model trusts the client. Budget backend work in Phases 2–3, not as an afterthought.
3. **Hardware agent = an installed component** on till PCs — the webapp isn't 100% install-free for tills (it is for back office). Accepted trade-off; PDF fallback covers agent-less machines.
4. **Offline web till** is strictly weaker than native local-SQLite offline. If a site needs long-running full offline, keep MAUI there; the webapp targets connected/briefly-disconnected tills.
5. **Framework sign-off** (§3.1) — Blazor Web App (`Auto`) is the default for the current .NET-only team, with a named tripwire: if web devs are hired or the offline-PWA till becomes core *before Phase 3*, switch to React while sunk cost is one skeleton + CRUD pages. Reversing after Phase 3 is expensive.
6. Entity serialisation quirks (Newtonsoft on the API, `TypeNameHandling` in saved transactions) may need DTO tidying as the typed client is generated.

---

## 7. Recommended first step

Phase 0.1 (B2C tenant decision) in parallel with a **Phase 1 spike**: stand up the Blazor Web App (`Auto` render mode) skeleton against the dev backend with auth stubbed, render the inventory list, and validate the typed-client + entity-reuse story end-to-end. If the §3.1 tripwire conditions look likely, run the same spike in Vite+React instead (or both — each is a day-scale exercise) and compare with running code before any larger commitment.
