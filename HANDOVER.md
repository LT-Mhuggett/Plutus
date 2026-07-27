# Handover — Plutus platform build

**Date:** 2026-07-27 (late night) — Phases 0–3, 5, 7(cash), 8, 9, 10, 11.1–11.4, **and ALL of
Phase 6 (WooCommerce connector)** complete; Kapow history loaded; everything pushed (head `cba7bea`)
**Branch:** `Matt's-Horror` · remote `origin` = LT-Mhuggett/Plutus
**Hard rule:** **DO NOT TOUCH ETRIE** — it shares the Mac mini but is a separate product. Every Plutus change keeps ETRIE's ports/processes/paths/Caddy blocks untouched; verify ETRIE health (`https://10.1.1.40/health`, `https://huggett.dscloud.me/health` → 200) after any Mac change.

---

## ⏰ RESUME HERE (written 2026-07-27 night — Matt went to bed with Phase 6 just finished)

**State right now, all live on the test env (test suite: 167 unit + 5 arch green):**
- **Phase 6 inbound LIVE**: kapow-comics.co.uk webhooks (order.created/updated) + a 20-min
  self-healing reconciliation poll ingest real web orders into SalesV2 (channel WebStore, virtual
  till "Kapow Web"); refunds → idempotent SaleAdjustments (closed 2026-07-27); unmatched SKUs park
  in the portal review queue (bind/ignore/create-item + Retry heals parked orders); pick-from-floor
  till banner on every web sale; product cache (745 = whole site) + catalogue view + alignment
  report (+CSV) in portal → Webstore.
- **Phase 6 outbound in DRY-RUN**: journaling to portal → Webstore → Outbound what it WOULD
  send (stock fast lane on every sale + slow-lane diff ≤100/cycle + WP6.5 draft scan). ZERO writes
  to the site — mode = dry-run, and only the read-only REST key exists.
- Two real orders ingested end-to-end as proof: #8505 £4.80, #8503 £67.49 (penny-exact).

**Waiting on MATT (in order of value):**
1. **Review the outbound dry-run journal** (portal → Webstore → Outbound) over ~a week of
   trading. It currently shows web-vs-till stock disagreement (web was stocked independently) —
   going live makes the TILL's ledger the truth for web stock. When satisfied: mint a WRITE REST
   key on the kapow box (same `wp eval` as §secrets), swap `Webstore__RestKeys__<id>` in
   `~/PLUTUS/plutus-ecosystem.config.js`, `pm2 delete plutus-backend && pm2 start` the ecosystem
   (PATH needs `/opt/homebrew/bin`), then portal → Outbound → live. First live write: verify ONE
   item on the storefront.
2. **Test WP6.1 one-click onboarding** (portal → Webstore shows a Connect form when no
   connection): browser flow only Matt can click. ⚠ A second connection to the same site
   DOUBLE-INGESTS new orders — connect → verify (site gains 2 webhooks) → **Disconnect promptly**
   (button removes its own webhooks + disables itself).
3. **Rotate the MySQL `plutus` password at leisure** (it echoed into a session transcript —
   LAN-only behind SSH, low risk). Change in MySQL + `~/PLUTUS/secrets/mysql.env` + the
   ecosystem ConnectionString.
4. Standing sudo items: Keycloak `login.plutus` Caddy vhost (Phase 9); portal basic_auth gap.

**✅ Phase 11 (11.5–11.7) + Phase 12 (12.1–12.3) DONE & LIVE 2026-07-27** (head after this
session's commits). Portal: Dashboard + Company tabs (Periods absorbed), Locations page with
collapsible Stores/Warehouses/Webstores groups. Ops HARDENED: **nightly MySQL backups (launchd
03:30, restore-rehearsed, row-counts match), pm2 save + resurrect LaunchAgent (backend now
survives a Mac reboot), logrotate.** Till Custom report + export repointed to v1 (last
`/api/Sale/Index`/`SaleReport` readers gone); legacy bridge behind `LegacyBridge:Enabled` (still
ON). Prices/credit/membership were already wired.

**WP12.2 progress (2026-07-27):** `/api/v1/sales/{id}` is now ENRICHED (additive) with per-line
`itemIdOne`+`itemName` (barcode from SaleLine.ItemIdOne OR DiscountsJson for web-till sales),
`operatorName`, and refund `adjustments` — verified live (names resolve). Portal drill-down shows
product names now. **Still to do to flip the bridge off:** (1) repoint the till's `fetchSaleDetail`
(used by BOTH the view dialog AND ReturnDialog) to `/api/v1/sales/{id}` — but that endpoint is
gated `PortalFinancialsView` while RETURNS are done by supervisors holding `pos.refund` not
financials, so returns need either a pos-gated sale-lookup or a re-gate, PLUS a live return test
(trading-critical — do with Matt present); (2) then `LegacyBridge:Enabled=false` + delete the
bridge/legacy tables. `VatIntegrity` (`/api/Sale/VatIntegrity`) is a CATALOGUE check, not
bridge-fed — it can stay or move independently. After that, only externally-gated items remain
(Phase 4 MAUI upstream, Phase 7 payments provider, Phase 10 Stripe, Phase 6 outbound go-live).

**Key session learnings live in:** §Phase-6 records below (deploy gotchas: pm2 PATH, ecosystem
env restarts, form-encoded ping, DI lifetimes) + `Build/secrets.local.md` (gitignored: all
webstore ids/secrets/keys + rotation steps) + the memory files (ETRIE health = bare
`huggett.dscloud.me/health`; build loop = `C:\Program Files\dotnet\dotnet.exe`).

This supersedes the earlier MAUI-only handover. Companion docs: `Build/` (platform architecture v3 + implementation plan + Sonnet/MAUI build specs + Kapow gap analysis), `WebApp-2026-07-23-plan.md`, `OfflineMode-2026-07-23-plan.md`, `VAT-Investigation-2026-07-23-plan.md`, `VAT-FixLater-Report-2026-07-23.md`.

---

## 1. What exists and is LIVE (test environment)

A complete **React web POS** + **management portal** trading against the **multi-tenant .NET platform + MySQL**, all on the Mac mini, seeded with the real Kapow database.

- **Till:** `https://plutus.huggett.dscloud.me` — login-first (real token auth). Logins: `dev@plutus.local` / `PlutusDev2026`, or `kapow_comics@outlook.com` / (the till's real password). Features: scan/search, basket (qty/price-adjust/reorder), discounts, returns (by receipt id **or by date**), park/retrieve, split-payment checkout, browser receipts + copy-reprint, offline/PWA (IndexedDB catalogue + checkout outbox), employee management, item add/edit, **Reporting** (Summary dashboard w/ SVG charts, Custom + Excel + sale recall, VAT calc + off-band integrity banner), editable Store Information, Settings. **Since Phase 2 the till is an enrolled DEVICE**: checkout goes outbox-first through `POST /api/v1/sales` (Settings → Till device to enrol a browser).
- **Portal (Phase 3):** management back office — dashboard (year→month→day→transaction drill), VAT view, Users & Roles (RBAC), Stores & Tills (enrolment codes), Financial Periods. Live at **https://admin.plutus.huggett.dscloud.me** (Caddy vhost applied by Matt 2026-07-25; LAN preview retired).
- **Backend:** `Plutus.DBService` (**.NET 8** modular monolith: SharedKernel/Identity/Catalogue/Sales/Reporting/Tenancy), self-contained `osx-arm64`, under **pm2** as `plutus-backend` on `127.0.0.1:5100`. Auth = HMAC bearer (`PlutusTokenAuthHandler`, 12h) with REAL scope + RBAC (`perm:*`) policies — the flag swaps B2C out, it does NOT bypass auth. Every `/api` endpoint 401s without a token.
- **DB:** MySQL 9.6 (Homebrew), schema `plutus` — **graduated to the full platform schema in Phase 2/3** (tenancy, devices, sales-v2, outbox, RBAC, audit, rollups, periods; all also on staging `plutus_t1`). Seeded from the Kapow backup (20,340 items / 21,657 legacy sales). Credentials in `~/PLUTUS/secrets/mysql.env` (also holds `TEST_TOKEN_SECRET`). Rollback dump: `~/PLUTUS/backups/plutus-pre-phase2-20260724.sql.gz`.
- **Edge:** Caddy serves the static till at `plutus.huggett.dscloud.me` and reverse-proxies `/api/*` → 5100. LE cert auto-renews. (Router SNATs WAN→LAN, so Caddy IP allowlists don't work — auth is the gate, not IP. ⚠ No basic_auth on the till host — flagged in §5, Matt to decide.)

Full environment detail is in memory (`plutus-test-environment.md`) and `Environment_Setup_Runbook.md` is **ETRIE's** runbook (left uncommitted deliberately — not ours to commit).

## 2. Subdomain scheme (decided, DNS-verified; recorded in architecture doc §6.2)

| Host | Serves | Status |
|---|---|---|
| `plutus.huggett.dscloud.me` | Web POS / till | live |
| `admin.plutus.huggett.dscloud.me` | Management portal (React app #2) | **LIVE** (vhost applied 2026-07-25) |
| `api.plutus.huggett.dscloud.me` | Backend API — single isolated surface | later cutover |

`*.huggett.dscloud.me` wildcard resolves any depth to 94.6.166.54. Until the portal lands the till keeps using `/api` on its own host.

## 3. Git state

Branch `Matt's-Horror`, in sync with `origin/Matt's-Horror`; **GitHub Actions CI green on
every Phase-2/3 commit** (build-test + openapi-drift). Recent commits (newest first):

```
8eb3073 docs: Phase 3 COMPLETE — HANDOVER §5 record, plan banner, Caddy sudo block   [CI ✓]
a890080 chore: drop stray openapi.regen.json committed with the portal
70e0461 WP3.5 plutus-portal: management back office (React, /api/v1-only)
9e6294e WP3.4 financial periods: close/snapshot/lock, late-post redirect, CSV export [CI ✓]
160628b WP3.3 reporting projections: rollups + consumer + rebuild + report API      [CI ✓]
6447506 WP3.2 admin APIs: companies/stores/tills/users/role-assignments + audit
58a4b62 WP3.1 RBAC: permission catalogue, roles/assignments, effective permissions
b2413e8 docs: Phase 2 COMPLETE (+ itemGuid parity vector)
38fd0f8 WP2.1+WP2.2 web POS: checkout onto the v1 pipeline + device enrolment
45dee4f WP2.1 backend: legacy sale bridge + deterministic item ids + admin scope
```

**Remote (added 2026-07-24):** `origin` = `https://github.com/LT-Mhuggett/Plutus.git` (Matt's, private) · `upstream` = `github.com/seank842/Plutus.git` (Sean's original). All work is **pushed to origin/Matt's-Horror**. NOTE: the push was rebuilt into a **single squashed commit `3d2837a`** on top of upstream/master ("remove secret-bearing history") — the granular per-task commits are NOT on GitHub (content intact); new commits from here are granular again. GitHub Credential Manager (browser) — Matt authenticates.

**Synced to upstream (2026-07-24):** our platform line was **merged into `upstream/Matt's-Horror`** (`seank842/Plutus`) as merge commit `c2732e3` (fast-forward from `aa667db`, no force). Resolution: platform/backend/Commons/tests → ours (net8); MAUI + frontends + publishing binaries → upstream's. ⚠ The upstream MAUI ClientUI is still **net7** and won't build against the now-net8 `Commons` until the incoming upstream MAUI code (Phase 4, paused) lands and is retargeted — intended/transitional. The platform `Plutus.slnx` builds independently (63 tests green). `origin/Matt's-Horror` (LT-Mhuggett) is unchanged at `d12ae9a`; upstream and origin are now separate lineages.

**Deferred hygiene (Matt's call, left as-is):** tracked `appsettings*.json` carry cleartext MySQL passwords (Sean's old dockerised-dev creds, NOT the live Mac DB) — pushed to the private repo. Options when revisited: move to env/user-secrets (forward), or `git filter-repo` purge (if repo goes public).

Git identity is set **repo-locally** (`Matt Huggett` / `mhuggett@leadingtalent.co.uk`).

## 4. Platform build progress (Build/ specs — executed one T-task at a time)

**Goal:** evolve the single-tenant DBService into the multi-tenant modular-monolith platform in `Build/plutus-platform-architecture.md` (v3). Authority order: architecture doc > implementation plan > build specs.

**Phase 0 — COMPLETE (all verified live; ETRIE untouched).**
- ✅ **T0.1 — .NET 8 retarget.** 7 projects net7→net8; EF/Pomelo 6→8, Identity.Web 1→2; dropped unused AzureAD.UI + PlatformAbstractions. Endpoints byte-identical (snapshots in `Build/snapshots/`, gitignored).
- ✅ **T0.2 — module carve-up (Option A).** `src/`: `SharedKernel` (Pence, Uuid7, tenancy, events), `Web.Infrastructure` (generic controller bases + APIConventions), `Identity` (auth), `Catalogue` (ItemController + band guardrail), `Sales` (SaleController incl. transitional Summary/VatIntegrity/SaleReport), `Reporting` + `Tenancy` scaffolds. Host (`Plutus.DBService`) = composition root, registers module controllers via `AddApplicationPart` + `AddPlutus<Module>()`. Namespaces kept stable (zero concrete-controller edits). Every endpoint byte-identical at each step.
- ✅ **T0.3 — OpenAPI + codegen + CI.** `openapi.json` (64 paths) from the live Swagger; `frontends/codegen.sh` → `WebApp/src/api/types.gen.ts` (generated, tsc-clean, committed, not yet imported — wired at T2.1); `.github/workflows/ci.yml` (needs a 9.0.x SDK for .slnx; **untested until Actions enabled**).
- ✅ **T0.4 — architecture tests** (`tests/Plutus.Tests.Architecture`, 5 pass + 1 Phase-1 skip): no cross-module refs; no decimal/double money in modules; no `Guid.NewGuid()` for IDs; frontend isolation. (Query-filter rule skipped until tenant entities exist.)

Test totals: **Unit 5 + Architecture 6 (1 skip)** green. Whole `Plutus.slnx` builds clean.

Phases 2–10 not started.

## 5. Phase records (historical — the live RESUME HERE is at the top of this file)

**Phase 3 (WP3.1–WP3.5) is COMPLETE and LIVE (2026-07-25)** — RBAC, admin APIs, reporting
projections, financial periods, and the **management portal**. **79 unit + 5 integration +
5 arch = 89 tests green.** Commits: `58a4b62` (WP3.1), `6447506` (WP3.2), `160628b` (WP3.3),
`9e6294e` (WP3.4), `70e0461` (WP3.5). All migrations applied to BOTH `plutus_t1` and live
`plutus`; backend redeployed per WP; ETRIE untouched throughout (health 200 re-checked).

**What's live:**
- **WP3.1 RBAC** — code-defined `PermissionCatalogue` (portal+POS, one catalogue);
  `RbacRoles/Grants/Assignments` (scope = tenant|company|store|till, optional day/time
  windows checked at token issue); `EffectivePermissionsService` (union at-or-above the
  spine; ceilings: unlimited beats all, else highest — `pos.refund.max:{pence}`);
  `GET /api/v1/users/{id}/effective-permissions`; dynamic `perm:<code>` policies;
  login scopes derive from RBAC (pre-seed fallback kept). Seeds: 8 built-ins + 3 refund-
  ceiling roles from Kapow AuthActions (`SeedMigrator rbac --mysql`); re-seed ADDS new
  template grants to built-ins (never removes tenant custom grants).
- **WP3.2 Admin APIs** — /api/v1 companies, stores (opening hours in server-only
  `StoreDetails` — shared Store POCO untouched for MAUI), tills (fleet list, create+code,
  revoke), users (create incl. login, deactivate), roles, role-assignments, audit trail.
  Every mutation writes `AuditLogs` in the SAME SaveChanges. New catalogue code
  `portal.company.manage`.
- **WP3.3 Reporting projections** — `SalesRollups` (till/day grain; store/company =
  SUM at query time) + `VatRollups` (store/day/rate) folded by a second outbox consumer
  (`reporting-rollups`); `RollupRebuilder` (single-snapshot-txn wipe+rescan, advances the
  consumer offset → rebuild==incremental); endpoints `/api/v1/reports/summary|vat`,
  `/api/v1/sales` (day-range list) + `/api/v1/sales/{saleId}` drill-down,
  `POST /api/v1/reports/rebuild` [platform-admin]; `SeedMigrator rollups-rebuild` for
  cutover (migrated LegacyRef rows carry no outbox events — REBUILD AFTER THE KAPOW ETL).
- **WP3.4 Financial periods** — create/close (snapshot totals into `SnapshotJson`, lock);
  late sales (RECEIVED after close) post to the first open day + `period.late-post` audit
  flag; the received-vs-closed distinction is what keeps rebuild == locked figures.
  CSV export `/api/v1/reports/export.csv?type=summary|vat`.
  ⚠ **Incident (fixed 2026-07-25):** the WP3.4 live-verification period ("H1 2026",
  2026-01-01→06-30, closed 24 Jul 23:22 with an EMPTY snapshot) was left closed on live
  `plutus`. The Kapow history ETL ran AFTER that close, so every Jan–Jun-2026 sale had
  `ReceivedAtUtc` > close and the late-post rule lumped £44,060.53 onto 2026-07-01 in the
  rollups (Matt spotted it in the portal). Fix: deleted the test period (row saved at
  `~/PLUTUS/backups/financialperiod-h1-2026-removed-20260725.txt` — no reopen/delete
  endpoint exists yet) + `POST /api/v1/reports/rebuild` → 1,955 day-rows, rollup total ==
  SalesV2 total penny-exact (£556,859.41). LESSONS: (1) bulk history ETL must run BEFORE
  any period close — or drop the closes first; (2) period reopen/delete endpoint is a
  real gap (candidate for Phase 11+); (3) never leave verification artifacts on live.
- **WP3.5 Portal** — new React app `Plutus/Frontend/Plutus.Frontend.Portal` (react+react-dom
  only, one CSS file, hand-rolled SVG chart). Login → dashboard (year→month→day→sale
  drill), VAT view, Users & Roles, Stores & Tills (enrolment codes), Periods.
  **Deployed:** dist at `/srv/apps/PLUTUS/portal/current`, served at
  **https://admin.plutus.huggett.dscloud.me**.

**✅ admin.plutus vhost APPLIED (Matt, 2026-07-25 00:46):** the staged Caddyfile went live
(rollback copy at `/etc/caddy/Caddyfile.pre-wp35.bak`); portal + /api proxy verified 200 over
the vhost, ETRIE health 200. The temporary LAN preview (pm2 `plutus-portal-preview`,
:5274) has been deleted — the vhost is the only portal entry point.

**Phase-3 notes / debts:**
- The **legacy sale bridge** (Phase 2) still runs alongside the rollup consumer — the till's
  Reporting page reads legacy tables. Retire it when the till UI moves to /api/v1 reports.
- **Kapow historic sales are IN the test pipeline (2026-07-25):** `sales-v2` ETL ran against
  live `plutus` (21,646 recorded / 8,114 reconciled / 7 quarantined / 100.0% gross) followed
  by `rollups-rebuild` (1,825 SalesRollups + 3,333 VatRollups from 21,647 sales). Penny
  parity verified on the REAL data: rollups == direct SQL aggregation exactly
  (£556,851.91 gross / £25,548.46 VAT / 21,647 txns); portal drills year→…→a single 2023
  transaction; legacy tables untouched (bridge skips LegacyRef rows); 0 dead letters.
  ⚠ The ETL is ONE-SHOT (fresh UUIDv7 ids per run — re-running would duplicate). The
  PRODUCTION cutover (retiring the physical till) remains a separate future op: re-run the
  ETL from a FINAL till backup at that point (drop SalesV2 rows with LegacyRef first, or
  restore the pre-phase2 dump, then ETL + rollups-rebuild).
- Force-logout AuthActions unmapped (no platform session-kill yet).
- Time windows evaluate in SERVER local time (= store's tz for this deployment).

**Phase 5 progress (2026-07-25):**
- **WP5.1 stock ledger — COMPLETE & LIVE** (`4e8d0f3` + order-proofing fix): append-only
  typed `StockMovements` + materialised `StockLevels` (== ledger sum, property-tested,
  rebuildable), `StockLocations` per store; third outbox consumer (`stock-ledger`) folds
  SALE/RETURN movements per sale line (RefId = saleId); APIs `/api/v1/stock/levels|
  movements|locations` [portal.reports.view] + manual `POST /api/v1/stock/movements`
  (Receipt/Adjustment/WriteOff, audited) [portal.stock.adjust] + rebuild [platform-admin].
  Opening balances seeded from legacy `Stocks` (3,193 items; `SeedMigrator stock-open`,
  idempotent + adoption-order-proof: heals pre-seed history replay, fences unprocessed
  history). Live parity verified: ledger == legacy Stocks and both move in lockstep per
  sale (the Phase-2 bridge keeps decrementing legacy in parallel until legacy retires).
- **WP5.2 transfers + stock takes — COMPLETE & LIVE** (`8247ae9`): StockTransfers with a
  real in-transit state (dispatch = TRANSFER_OUT at source; goods in NEITHER level while
  travelling — structurally impossible to double-count; receive = TRANSFER_IN at
  destination; cancel returns to source; 409 on double actions; all audited).
  `POST /api/v1/stock/takes` posts counted-vs-expected ADJUSTMENTs with reason codes and
  returns the variance report. Portal gained a **Stock** tab: central/per-store views,
  in-transit receive/cancel, per-item movements drill, adjust/count/dispatch dialogs.
  Live smoke: a count of 46 vs expected 44 posted a +2 reasoned adjustment.
- **WP5.3 goods-in — COMPLETE & LIVE** (`c76a893`): Suppliers → PurchaseOrders → POLines;
  receiving posts RECEIPT ledger movements (RefId = PO id; partials Open→Partially→Received;
  door cost overrides ordered cost; over-receive 400; state conflicts 409; audited).
- **WP5.4 pricing — COMPLETE & LIVE** (`c76a893`, architecture §7.4): per-item PricePolicy
  (CENTRAL default / CENTRAL_WITH_OVERRIDE / LOCAL), append-only effective-dated
  PriceListEntries, store PriceOverrides (survive HQ repricing; force-reset revokes —
  409 under LOCAL; overrides 409 under CENTRAL). Resolution: store price → price list →
  legacy Items price (evolve-in-place baseline). `GET /api/v1/prices/effective` is the
  till-catalogue-sync feed (any authenticated principal). Portal: Prices tab (policy,
  HQ price incl. scheduling, store price, force-reset, history, variance-vs-HQ).
  FULL 9-case policy matrix + boundary-activation tests. ⚠ The web POS till still reads
  legacy Item prices — wiring it to /prices/effective is the catalogue-sync follow-up.
- **Phase 5 COMPLETE.** 105 tests green (95 unit + 5 integration + 5 arch).

**Phase 7 progress (2026-07-25):**
- **WP7.2 cash sessions — COMPLETE & LIVE** (`67b344e`): `CashEvents` (append-only,
  idempotent by client eventId) — OpenFloat/PaidIn/PaidOut/XSnapshot/ZClose through the
  sales-ingest discipline; X/Z compute the expected drawer server-side
  (float + cash takings net of change + paid-ins − paid-outs) and freeze counted/expected/
  variance; ONE Z per till per business day (409 on a second Z or any post-Z event).
  `POST /api/v1/cash-events`, `GET /api/v1/cash-events`, `GET /api/v1/cash/banking`.
  Till gained a **Cash** tab, portal a **Banking** tab. Live smoke: Z variance + banking
  view + one-Z-per-day 409 all correct.
- **WP7.1 payments — SEAM ONLY (deliberate)**: `IPaymentProvider` + `NullPaymentProvider`,
  `PaymentEvents` capture + reconciliation (orphaned-payment queue that auto-resolves when
  the sale's outbox drains), `POST /api/v1/payments/events`, `/unresolved`, `/reconcile`;
  portal Banking surfaces the queue. ⚠ **The first concrete provider adapter (Dojo/Stripe
  Terminal/SumUp/etc) is BLOCKED on Matt's commercial choice** — nothing downstream depends
  on which. WP7.1's cash-up overdue monitor is deferred (it builds on the paused WP4.3
  fleet framework).
- 108 tests green (98 unit + 5 integration + 5 arch).

**Phase 8 — COMPLETE & LIVE (2026-07-25, `f4ebfeb`):** customers (optional on sale, till
lookup), store credit as an append-only liability ledger (D15 — `CreditEntry` Issue/Redeem/
Expire, balance = Σ entries, overdraw-guarded, idempotent-by-entry-id redeem), memberships
(renewal-dated auto-discount rate). `Plutus.Customers` module + `CreditLedgerService`. APIs:
customers CRUD + at-sale lookup, `credit/issue|redeem`, `membership`, credit history. Period
close now records `outstandingCreditLiabilityPence` (§7.1). Portal **Customers** tab. Live
smoke: issue £20 → redeem £7.50 → overdraw 400 → membership — all correct. 102+5+5 green.
**Till retrofit — WEB DONE, MAUI documented (2026-07-25, `9a047c3`):** the web POS now
consumes the Phase 5/8 backend it had drifted behind — effective pricing (WP5.4) at
basket-add, a customer bar (attach/search), members' auto-discount, and store credit as a
checkout tender. Full gap list + per-item web(done)/MAUI(to-do) in
**`Build/till-retrofit-2026-07-25.md`**. Still open there: WP7.1 card-capture events
(⏸ blocked on the provider choice, both tills) and CustomerId-on-the-sale (small additive
backend follow-up — credit entries already carry the saleId, so the credit flow doesn't need it).

**▶ NEXT (all need Matt's input):** Phase 4 (MAUI) remains **paused for upstream code**;
the Kapow PRODUCTION cutover (final till backup → re-ETL → rollups+stock rebuild) is a deliberate
op; and the follow-ups: till reads /prices/effective, retire the legacy bridge when the till UI
moves to /api/v1 reports, and the Caddy sudo block below.

**Phase 6 (WooCommerce connector) — STARTED 2026-07-26. WP6.0 (recon/fixtures/read-key) ✅ DONE.**
Re-planned against the **LIVE** Kapow store (there is no test store) — `ssh kapow` →
kapow-comics.co.uk, WooCommerce 10.9.4 on a low-resource DreamHost VPS. Ground rules + WP6.0–6.5
are in `Build/plutus-implementation-plan.md` (Phase 6); the full WP6.0 audit is
`Build/woo-sku-audit-2026-07-26.md`.
- **Read-only REST key** minted via wp-cli → `Build/secrets.local.md` (**gitignored**). A write
  key is deferred to WP6.3 approval.
- **10 PII-scrubbed fixtures** in `tests/Fixtures/Woo/` (+ README) so the connector builds/tests
  offline — the live site is read-only smoke/DoD only.
- **SKU audit:** 596/648 published SKUs (**92.0%**) match a Plutus barcode; 52 unmatched + 91
  SKU-less = 143 products seeding the WP6.2 review queue.
- **Facts that shape later WPs:** HPOS is OFF (orders in `wp_posts`, ignore `wc_orders`); line
  totals are **net** with separate `total_tax` (trust the fields, don't recompute); GBP-only in
  practice; incremental `modified_after` product sweep is ~1 page/~1.6 s (near-free), full sweep
  ~2 min. **Nothing on the Woo side was changed except adding one read-only API key.**
- **Next:** WP6.1 (connection config on the WP11.7 webstore card + virtual webstore till) — all
  buildable offline against fixtures; then WP6.2 inbound (webhooks) is the first live read path.

**WP6.2 mapper core ✅ DONE (2026-07-26).** New isolated module **`src/Plutus.Webstore`** (in the
slnx; references only SharedKernel + Entities, so the arch module-boundary test stays green and
core never references the connector). `WooOrderMapper.MapOrder` → validated `SaleV2` (channel
WebStore) via `SaleV2.Create`; deterministic saleId from the Woo order id (added a general
`DeterministicGuid.ForName`); Woo net+tax → platform VAT-inclusive money, invariant-exact;
shipping/fees as null-`ItemIdOne` lines; unknown SKU → needs-mapping queue; non-GBP/mismatch →
quarantine; refund → `SaleAdjustment`. **8 new unit tests on the real scrubbed fixtures.** Then the **inbound decision
pipeline** (`WooWebhookVerifier` constant-time HMAC + `WebstoreWebhookProcessor` verify→parse→map→
route over ports `IWebstoreSaleSink`/`IWebstoreSkuMapQueue`; +6 tests). **133 unit + 5 arch green.**
Local build/test loop: **no SDK on PATH or on the Mac — use `& "C:\Program Files\dotnet\dotnet.exe"`**
(x64, SDK 10.0 builds net8; the x86 shim on PATH has no SDK). **DB layer ✅ DONE (2026-07-26):** two tenant-owned tables
(`WebStores` config + `WebstoreSkuMaps` review queue) on `MySqlDbContext`, DB-backed
`CatalogueSkuResolver` (SKU⇔Items.IdOne → web-POS deterministic ItemId), +2 SQLite tests
(**135 unit + 5 arch green**). **EF migration `AddWebstoreConnector` generated but NOT applied** —
rehearse on `plutus_t1` then `plutus`. To generate/apply migrations locally: **prepend
`C:\Program Files\dotnet` to PATH** (else `dotnet ef`'s inner `dotnet msbuild` hits the SDK-less
x86 shim) then `dotnet ef … --project Plutus\Commons\Plutus.Entities --startup-project
Plutus\Database.Migrations.Startup --context MySqlDbContext -o Migrations/MySql`. **Connector-side DI complete + inbound proven e2e ✅ (2026-07-26):**
real `WebstoreSkuMapQueue` (upsert/bump), `WebstoreModule.AddPlutusWebstore` (registers resolver +
queue + processor; host supplies `IWebstoreSaleSink`), and a **keystone e2e test** — signed webhook
→ verify → map → REAL `SalesIngestService` → SalesV2 + outbox → dedupe on redelivery. `WebStoreId`
threaded through the context. **137 unit + 5 arch green.**

**Remaining Phase 6 (host wiring — compile-only locally, needs Mac MySQL to runtime-test):**
`WebstoresController` (CRUD + `POST api/v1/webstores/{id}/webhook` reading the RAW body for HMAC —
mirror the billing webhook in `PlatformController`), the host `WebstoreSaleSink` adapter over
`SalesIngestService` (mirror the e2e test's `IngestSink`), `Startup` `AddPlutusWebstore()` +
`AddScoped<IWebstoreSaleSink, WebstoreSaleSink>()`, and virtual-till provisioning.
**✅ PHASE 6 FINAL CLOSURES (2026-07-27 late, Matt's decisions).** (1) **Refund gap closed:**
`WebstoreRefunds.ApplyAsync` — Woo refunds → idempotent SaleAdjustments (Id from Woo refund id;
full via status "refunded"/Skipped, partial via Recorded/Duplicate; no-op when the sale predates
the connector); wired into webhook + poll; smoke: #8347 → 200 skipped/0 adjustments (correct).
(2) **WP6.1 onboarding built** — portal Connect form → `/wc-auth/v1/authorize` on the store's own
WordPress → anonymous ONE-SHOT callback stores keys in `~/PLUTUS/secrets/webstore-secrets.json`
(config wins for Kapow's hand-provisioned creds) → auto-creates both webhooks; Disconnect button
removes the site's webhooks + disables the row. Env `Webstore__PublicBaseUrl` added to the pm2
ecosystem. **MATT TO TEST** the browser flow — ⚠ a second connection to the same site
DOUBLE-INGESTS new orders (separate deviceId): connect → verify → Disconnect promptly.
(3) **Email channel closed as won't-do** (till banner is the mechanism). (4) **Outbound: dry-run
continues** pending Matt's journal review (portal → Webstore → Outbound). **167 unit + 5 arch
green.**

**✅ PHASE 6 CODE-COMPLETE (2026-07-27): OUTBOUND BUILT, DEPLOYED IN DRY-RUN.** WP6.3+WP6.5
drafts implemented: fast lane (`WebstoreStockOutboundConsumer` on SaleRecorded — caught up, outbox
head 6), slow lane (poll diff, LINKED products only, ≤100/cycle), draft scan (48 h lookback,
journal-idempotent), oversell buffer, kill switch (`WebStores.OutboundMode` off|dry-run|live,
re-read per item). Journal = `WebstoreOutboundLogs` (dry-run review artefact / live send audit);
portal → Webstore → **Outbound** tab (mode switch: live REFUSED without a dry-run journal;
audited) + alignment CSV export. Migration `AddWebstoreOutbound` rehearsed t1 → live. **Kapow is
in DRY-RUN now**: 180 journaled / 0 sent — the journal shows web-vs-till stock disagreement (the
web was stocked independently of the till; review before live). ⚠ First dry-run caught a real
bug: the diff included UNLINKED web products (26-digit SKUs overflowed ItemIdOne → cycle failed;
and live would have zeroed them) — fixed (join to Items), regression-tested, polluted journal
rows purged. **160 unit + 5 arch green.** **GO-LIVE RUNBOOK:** review ≥1 wk of journal → mint
write key on kapow (`wp eval` like the read key) → swap `Webstore__RestKeys__<id>` in pm2
ecosystem (ck|cs) → pm2 delete+start → portal Outbound → live (confirm dialog) → verify ONE item
on the storefront. Kill switch = set mode off (portal or SQL). Still open: WP6.1 one-click
wc-auth onboarding UI (tenant #2), notification email (SMTP choice).

**✅ PHASE 6 INBOUND COMPLETE (2026-07-26 night, `60c6867`).** On top of the go-live below:
**review queue** live (portal → Webstore tab: pending SKUs w/ webstore name/price from the cache,
bind / ignore / **create-item** (WP6.5 Woo→Plutus: name+price from cache, tenant-default tax/
category) / **Retry parked orders** — needs-mapping orders now PARK their payload in SaleQuarantine
so the bind→retry loop heals them into real sales); **pick-from-floor notification** live (one per
web order, idempotent; till polls 60 s → amber banner → "Done — acknowledged" clears for all
tills, audited; EMAIL channel pending an SMTP choice — no mail infra on the env); **WP6.4 product
cache** live (**745 products cached** on first full sweep = the whole site incl. drafts; 31
requests one-off, then incremental ~2/cycle; nightly full sweep 02:00–06:00 UTC stamps deletions
Status="deleted"); **catalogue view** (paged/filterable, Refresh-now rate-limited 5 min, links out
to the site — no image hotlinking) + **alignment report** (name drift, prices both sides as
display, web-only list) render from cache — zero live-site requests. Migration
`AddWebstorePhase6Completion` rehearsed t1 → live. Live smoke: order #8503 → £67.49 recorded +
notification minted. **153 unit + 5 arch green.** Remaining (gated/deferred): WP6.3 outbound +
WP6.5 Plutus→Woo drafts (Matt's gates + write key); WP6.1 one-click wc-auth onboarding UI (needed
for tenant #2); notification email channel (SMTP); WP6.4 CSV export.

**🎉 Phase 6 INBOUND IS LIVE (2026-07-26 evening).** Backend deployed (4 iterations — see gotchas
below), Kapow connection provisioned (`WebStores` row + virtual till "Kapow Web" + device;
`woo-connector` entitlement granted to the Kapow tenant — was `[]`), webhook secret in the pm2
ecosystem env (`Webstore__Secrets__<id>`, ids+secret in gitignored `Build/secrets.local.md`), and
**two webhooks live on kapow-comics.co.uk** (order.created + order.updated, both active — their
activation pings got 200s). **End-to-end smoke PASSED from the DreamHost box over the public
internet:** real order #8505 signed+POSTed → `recorded` in 0.85 s → SalesV2 penny-exact (£1.50
zero-rated comic + £3.30 shipping line = £4.80, PayPal ref on the tender, virtual TillId,
BusinessDay = paid date); re-delivery → `duplicate`, same saleId, still ONE row. Real web orders
now flow into Plutus automatically. **Deploy gotchas (cost 3 redeploys):** (1) pm2 needs
`export PATH=/opt/homebrew/bin:$PATH` in non-interactive ssh — bare `pm2` silently no-ops;
(2) the backend was running UNMANAGED (pm2 daemon had no apps) — always verify with
`lsof -nP -iTCP:5100` + process start time, not pm2's word; (3) restart from the ecosystem FILE
(`pm2 delete` + `pm2 start ecosystem.config.js`) when env changes; (4) status gate added: only
`processing`/`completed` orders ingest (Skipped→200 otherwise); (5) Woo's activation ping is
FORM-encoded and the form feature drains Request.Body — controller reconstructs `webhook_id=N`
from Request.Form. ⚠ MySQL `plutus` password echoed into a transcript while debugging — rotate at
leisure (note in secrets.local.md). **Reconciliation poll LIVE too (2026-07-26 late,
`0ba6223`):** `WebstoreReconciler` + BackgroundService — every 20 min, orders `modified_after`
cursor−5min-overlap pulled via the REST read key (`Webstore__RestKeys__<id>` = "ck|cs" in the pm2
ecosystem env) and routed through the SAME core as webhooks (status gate/SKU queue/quarantine/
dedupe); ≤25/page, ≤4 pages/cycle, 24h initial lookback; cursor = max date_modified seen
(`WebStores.OrdersCursorUtc`, migration `AddWebstoreOrdersCursor` rehearsed t1 → live). First live
cycle verified: `webstores=1 requests=1 orders=0`, cursor persisted — 3 req/hour steady-state.
150 unit + 5 arch green. Remaining Phase 6: WP6.2 review screen + pick-from-floor notification;
WP6.4 catalogue view/report; WP6.5 draft creation; WP6.3 outbound (gated on Matt).

**WP6.2a webhook receiver ✅ BUILT & TESTED (2026-07-26)** — design AND implementation done.
Connector: `WebstoreWebhookHandler` (framework-free: unscoped lookup by URL id → Woo's UNSIGNED
activation ping `webhook_id=N` → 200 before signature checks → HMAC over the raw body → pipeline
on `FixedTenantContext(row.TenantId)` via `WebstoreWebhookPipelineFactory` → outcome→HTTP with
parked-=-2xx for Woo's retry/auto-disable; mapper quarantines PARKED into SaleQuarantine
idempotently). Host: thin `WebstoreWebhookController` (`POST api/v1/webstores/{id}/webhook`),
`WebstoreIngestSink` over `SalesIngestService`, `ConfigWebstoreSecretProvider`
(`Webstore:Secrets:{id}` from config), Startup wiring + csproj ref. **10 handler tests incl. the
tenant-scope proof (wrong ambient tenant → rows still land under the webstore's tenant; virtual
Device row derives TillId) — 147 unit + 5 arch green; host builds.** **No WP plugin**
(re-confirmed — HMAC is the standard; a plugin can't solve tenant scoping). **Committed `45914a6` + pushed;
migration APPLIED (2026-07-26):** rehearsed on `plutus_t1` (+ idempotency re-run = no-op), then
live `plutus` — `WebStores` + `WebstoreSkuMaps` created, SalesV2 untouched (21,648), till 200 /
portal 200 / **ETRIE 200** (note: ETRIE's vhost is the BARE `huggett.dscloud.me` — checking
`etrie.…` gets 000 and means nothing). Script kept at `~/PLUTUS/webstore-migration.sql`.
Remaining before live inbound: deploy the new backend (webhook endpoint + tables in model), WP6.1
provisioning (WebStores row + virtual till/device + secret in config + webhooks on the site via
wp-cli), Mac integration smoke.

---

## 5a. (historical) Phase 2 record — COMPLETE ✅

**Phase 2 (WP2.1 + WP2.2) is COMPLETE and LIVE (2026-07-24 evening)** — the web POS trades through
`POST /api/v1/sales` end-to-end in the test environment. **63 unit + 5 arch + 4 integration = 72 tests green.**
Commits: `45dee4f` (backend bridge), `38fd0f8` (web POS pipeline), plus docs/vector commits after.

**Decisions made this session (and why):**
1. **Live `plutus` DB GRADUATED to the Phase-1 schema** (Matt's explicit call, ending the
   "plutus_t1 only" rule): backup first (`~/PLUTUS/backups/plutus-pre-phase2-20260724.sql.gz`,
   5.2MB — note: `plutus` MySQL user lacks RELOAD, so dumps need `--no-tablespaces
   --skip-lock-tables`, taken with the backend stopped), then the idempotent
   `dotnet ef migrations script --idempotent` output. Verified: 7 new migrations in
   `__EFMigrationsHistory`, legacy counts byte-identical (21,657 sales / 74,826 trans /
   20,341 items / 7,841 stocks), every row tenant-stamped, Kapow `Tenants` row seeded.
2. **Legacy read model stays alive via a server-side bridge** (Matt's call):
   `Plutus.Reporting.LegacySaleBridgeConsumer` (outbox consumer, TRANSITIONAL — delete at WP3.3)
   projects each `SaleRecorded` into legacy `Sales/Trans/PaySales/Transaction_Discounts/
   CheckoutItemChange/Refunds` + a `Stocks` adjustment. The client has exactly ONE write path;
   reports/recall/stock keep working. Its writes commit atomically with the consumer offset
   (same scoped DbContext as the drainer). Sales without projection metadata (MAUI/ETL) are skipped.
3. **Deterministic item ids**: v1 `SaleLine.ItemId` = SHA-256 name-derived GUID of
   `plutus:item:{businessId}:{idOne}` (`SharedKernel.DeterministicGuid` ⇔ `pipeline.ts itemGuid`,
   parity vector `4abfb7bf-50d6-8990-9a56-b4ebfafbe22e` pinned in the unit test). No mapping table;
   Phase 5 unifies with the WP1.8 random-minted historic ids via `LegacyRef`.
4. **Projection metadata rides in `SaleLine.DiscountsJson`**
   (`{"itemIdOne","exUnitPence","discounts":[{id,rate}],"return":{originSaleId}}`) and the legacy
   PayMethod id in `SaleTender.ProviderRef` (`{"payId":N}`) — documented interim contract between
   the web POS and the bridge. **Returns = negative-qty lines** (arithmetically valid under the
   T1.3 invariants; bridge → legacy `Refunds` + restock).
5. **Checkout is outbox-FIRST** (WP2.1): every sale is durably queued in IndexedDB before any
   network attempt, then drained (online = immediate). Permanent 4xx rejections go to a local
   "parked" store (client dead-letter, visible in Settings) so they never block the queue.
   deviceSeq = atomic IndexedDB counter (safe across tabs). Legacy `POST /api/Sale` + client-side
   stock patches REMOVED from the client.
6. **Admin scopes**: login also grants `portal.tills.enrol` to employees holding the legacy
   `Admin`/`Management` AuthAction (minimal AuthActions→scope slice; WP3.1 RBAC replaces it).
7. **Checkout change-attribution bug fixed**: the old proportional split could drift 1p across two
   changeable methods (harmless to the legacy API, but the v1 net-tender invariant would
   quarantine it). Last changeable method now absorbs the rounding remainder.

**Deployed to the Mac (ETRIE verified untouched, `etrie-*` pm2 uptimes preserved):**
new backend publish (osx-arm64, whole folder swap; rollback at `~/PLUTUS/backend.pre-phase2`),
webapp dist → `/srv/apps/PLUTUS/web/current/`, `types.gen.ts` regenerated (8,248 lines,
openapi-typescript 7.13.0 — includes all `/api/v1/*`).

**Smoke test (all passed, scripted at `~/PLUTUS/staging/smoke-phase2.sh`):** create till+code →
enrol (reuse → 410) → device token → ingest 201 → replay 200 (dedupe) → invariant-breaking sale
202 quarantined → bridge projected legacy Sale (£15.00/£12.52) + Trans + PaySales, stock 47→45,
0 dead letters, `Device.LastSeenSeq` bumped → legacy `/api/Sale/Detail` returns the sale with the
item name resolved. WP2.2 DoD (two browsers = two deviceIds) holds: credential is per-browser
localStorage; revoke blocks only that device.

**⚠ First-use note for Matt:** the deployed till now REQUIRES device enrolment — Settings →
"Till device" → "Generate a code" (your login carries the Admin AuthAction) → "Enrol this browser
with it". Until then checkout errors with "not enrolled". Hard-refresh once so the service worker
picks up the new build.

**⚠ Finding (pre-existing, not changed):** the Caddy `plutus.huggett.dscloud.me` block has **no
`basic_auth`** — the static site is world-reachable (the API still 401s without a token). Memory
said basic_auth gated it; it isn't there (likely dropped when the PWA/service-worker work landed,
since basic_auth breaks SW caching). Matt to decide whether to re-add it (Caddy is shared with
ETRIE — edit carefully per §6) or accept login-as-the-gate.

**▶ NEXT — Phase 3** (portal + company view: RBAC, admin APIs, reporting projections that
REPLACE the bridge, financial periods, `plutus-portal` React app). The bridge consumer and the
`DiscountsJson`/`ProviderRef` metadata contract are the first things WP3.3 retires.

---

## 5b. (historical) Phase 1 record — COMPLETE ✅

**Phase 1 (T1.1–T1.8) is COMPLETE** — all pushed to `Matt's-Horror`. **54 unit + 5 arch + 4 integration = 63 tests green.** Every schema migration applied to staging **`plutus_t1` only**; live `plutus` + ETRIE untouched throughout.

| Task | Commit(s) | Delivered |
|---|---|---|
| T1.1 | `26f03e0`/`7fe8e78`/`55c8588` | Tenancy schema + shadow-TenantId query filters + SaveChanges stamp/guard (19 entities) |
| T1.2 | `a786c46`→`97b4528` | Provisioning + enrolment + device tokens + scope-based auth + rate limiting |
| T1.3 | `4b6c8cb` | Sales schema v2 (7 tables) + constructor-enforced money invariants |
| T1.4 | `7fd12db` | Idempotent ingest `POST /api/v1/sales` (quarantine, dup→200, outbox, monotonic seq) |
| T1.5 | `9aab9af` | Broker-less `OutboxDispatcher` (offsets, retry→dead-letter, effectively-once) |
| T1.6 | `d2915fd` | Contract freeze — `openapi.json` (71 paths) + `[ProducesResponseType]` + CI drift gate |
| T1.7 | `ea9c839`/`5129d4e` | Standing suites (reconciliation, replay) + `WebApplicationFactory` HTTP e2e + route surface |
| T1.8 | `12a4a51` | `Plutus.Migration.Kapow` library + SeedMigrator `sales-v2` runner |

**Follow-up status — ALL FOUR RESOLVED (2026-07-24 pm):**
- **#4 DONE** (`e337359`): login stamps `Scope="pos.sell"`.
- **#2 DONE** (`cf575d4`): Actions was enabled but **red on every run** — root-caused to a Swagger config-less 500 (B2C oauth2 scope keys collapsing to duplicate `https:///`), fixed by emitting the oauth2 scheme only when B2C is configured; `openapi.json` regenerated config-independent + LF-pinned; **CI now green**.
- **#1 DONE** (`43776da`, decision **B — trust `Sales.Total`**): mismatched legacy sales keep their lines + one reconciling adjustment line (sentinel `ReconciliationItemId`), so gross == `Sales.Total`; flagged `VatReconstructed` + noted. **Real run: 21,646/21,653 recorded (8,114 reconciled), 7 quarantined (3 tender-mismatch, 4 empty), 100.0% gross recovered.** VAT on reconciled sales is Σ reconstructed line VAT (delta band unknown → approximate, flagged).
- **#3 CLOSED (deferred)**: 20-way concurrent ingest → a CI job with a MySQL service container (no Docker locally; the DB unique key is the guarantee, single-thread-proven).

**Kapow cutover note:** the real data move runs `Plutus.SeedMigrator <kapow.db> sales-v2 --mysql "<conn>"` against the target at cutover (a deliberate op — not done yet). 8,114 sales carry a `legacy-reconciled` note; the 7 quarantined need manual review.

**⚠ Phase 1 follow-ups before production cutover (NOT blockers for Phase 2):**
1. **Kapow discount handling** — the real-data run (`SeedMigrator … sales-v2 --sqlite`) mapped 21,653 sales → **13,401 recorded / 8,252 quarantined** (£306k/£557k, 55%). The ~38% quarantine is DISCOUNTED sales: `Transaction_Discounts`/`DiscountRate` aren't folded into per-line `DiscountPence`, so line-sum ≠ `Sales.Total`. Add that to `KapowSalesReader`/`KapowSaleMapper` to recover them. (Quarantine-on-mismatch is the *designed* safety net — see gap-analysis §5.4.)
2. **Enable GitHub Actions** — `build-test` + `openapi-drift` jobs are written but untested until Actions is on.
3. **20-way concurrent ingest** — proven single-threaded; true concurrency needs a real MySQL/Testcontainers (SQLite is single-writer).
4. **JWT-backed operator scopes at login** — `AuthController` login still issues scopeless operator tokens; wire real scopes when the portal lands.

**Local-boot recipe** (spec/HTTP work): `DOTNET_ROLL_FORWARD=LatestMajor ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 DISABLE_AUTH_DEV_ONLY=true TEST_TOKEN_SECRET=x dotnet <host.dll>` then curl `/swagger/v1/swagger.json` (this box has AspNetCore.App 10 x64 only → roll-forward).

**▶ NEXT — Phase 2** (`Build/plutus-implementation-plan.md`): the till/web-POS frontend cutover onto the new `/api/v1` contract (T2.1 wires the generated TS types), then subsequent phases. Confirm scope from the implementation plan before starting.

**⏸️ Phase 4 (MAUI till) is ON HOLD (2026-07-24):** new MAUI code is coming from **upstream** (`github.com/seank842/Plutus`) that will **replace** the planned port — do NOT build Phase 4 / `plutus-maui-build-spec.md` M0–M4; re-baseline against the upstream code when it lands. Pause banners are in both `plutus-implementation-plan.md` (Phase 4) and `plutus-maui-build-spec.md`. Target behaviours there still stand as acceptance criteria. (Server-side heartbeat/fleet halves could be pulled forward independently if needed.)

---
### (historical) T1.7 resume notes — superseded by the above

**✅ T1.5 COMPLETE (2026-07-24)** — commit `9aab9af`. `OutboxDispatcher` (BackgroundService, Web.Infrastructure, `Plutus.Infrastructure.Outbox`) polls 500ms and drains `OutboxEvents` into each registered `IEventConsumer` via the testable `OutboxDrainer`. Per event: dedupe vs `ProcessedEvents`, handle with bounded retry (1s/5s/25s then park to `ConsumerDeadLetters`), advance `ConsumerOffsets` in the SAME SaveChanges → effectively-once across restarts; poison parks + later events flow; consumers independent. Idempotency centralised in the drainer (not a per-consumer base). Lag = max(Id)−offset; `GET /api/v1/ops/deadletters` (platform-admin). New tables `ProcessedEvents`+`ConsumerDeadLetters` (migration `AddOutboxConsumerTables` → `plutus_t1`). Registered via `AddPlutusOutbox()`; inert on SQLite host. Tests: converge / offset-reset dedupe / poison-parks-then-flows / lag. **33/33 unit, 5/5 arch.**

**▶ NEXT — T1.6 contract freeze** (spec §T1.6): boot an instance, regenerate `openapi.json`, review field-by-field vs architecture §4.1 + the T1.2/T1.4 endpoint tables, commit; enable the CI drift gate from T0.3 (the commented `openapi-drift` job in `.github/workflows/ci.yml`) so a controller signature change without regen fails CI, and TS types regenerate cleanly. NOTE: the new `/api/v1/*` endpoints (tenants, tills, tokens, sales, ops) are net-new since the 64-path `openapi.json` snapshot.

---
### (historical) T1.5 resume notes — superseded by the above

**✅ T1.4 COMPLETE (2026-07-24)** — commit `7fd12db`. `SalesIngestService` (Sales module) `POST /api/v1/sales`: one tx → validate T1.3 invariants → quarantine (202) if unfixable, else insert SaleV2+lines+tenders + `OutboxEvents(SaleRecorded)` + bump `Device.LastSeenSeq=max(cur,seq)` → 201; duplicate saleId re-reads → 200; quarantine idempotent via unique `(TenantId,SaleId)` on SaleQuarantine (migration `AddQuarantineSaleId`, applied to `plutus_t1`). Provider-agnostic idempotency (re-read on conflict, no vendor error codes). Controller: tenant/device from token not body (mismatch→403), Idempotency-Key must equal saleId (else 400), TillId server-derived from device. Policy `sales.ingest` = device OR `pos.sell`. Tests: record+outbox+seq, duplicate→200 (1 row/1 event), quarantine idempotent, monotonic seq. **29/29 unit, 5/5 arch.** 20-way concurrent test deferred to T1.7 (needs MySQL; DB unique constraint is the guarantee).

**▶ NEXT — T1.5 broker-less dispatch** (spec §T1.5): `OutboxDispatcher` hosted service — per registered `IEventConsumer`, read `ConsumerOffsets[name]`, fetch next ≤100 `OutboxEvents` with Id>offset ordered by Id, `HandleAsync` sequentially, advance offset in the same tx as the last success; retry 1s/5s/25s then park to `ConsumerDeadLetters` + advance (a stuck consumer must not block others). `IIdempotentConsumer` base + `ProcessedEvents` table in SharedKernel. Lag metric + `GET /api/v1/ops/deadletters` (platform-admin). Runs in the host (`Plutus.Api`/DBService).

---
### (historical) T1.4 resume notes — superseded by the above

**✅ T1.3 COMPLETE (2026-07-24)** — commit `4b6c8cb`. Seven server-only tables on `plutus_t1`: `SalesV2` (header, named V2 to avoid the legacy `Sales` collision — renamed at T1.8 cutover), `SaleLines`, `SaleTenders`, `SaleAdjustments`, `SaleQuarantine`, `OutboxEvents`, `ConsumerOffsets`. Integer pence, UUIDv7 (char(36)). `SaleV2.Create` enforces the four money invariants (throws `InvalidSaleException`); `Validate()` re-runnable post-EF. The 4 queryable sale tables are tenant-scoped; Outbox/Offsets/Quarantine unscoped (infra). Tests: inconsistent-throws + 1000-sale property round-trip. **25/25 unit, 5/5 arch.** Legacy `Sales` (21,654 rows) + live + ETRIE untouched.

**▶ NEXT — T1.4 idempotent ingest** `POST /api/v1/sales` (spec §T1.4): device/operator token; tenantId+deviceId from token not body; validate invariants → quarantine (202) on unfixable; insert Sale+lines+tenders + OutboxEvents(SaleRecorded) + bump Device.LastSeenSeq in one tx → 201; duplicate `(TenantId,Id)` → re-read → 200. Idempotency-Key must equal body saleId. Ingest lives in the Sales module; add an `OutboxEvent` write here (dispatch is T1.5).

---
### (historical) T1.3 resume notes — superseded by the above

**✅ T1.2 COMPLETE (2026-07-24)** — commits `a786c46`,`2d33cc2`,`1b98f07`,`bb69da5`,`a4a60ef`,`97b4528`:
- **Auth:** `HttpTenantContext` resolves tid/did/scope from claims (null-safe→Kapow, platform-admin→unscoped). `PlutusTokenAuthHandler` (test-env) validates operator + device tokens → scope/tid/did claims; real scope policies `platform-admin`/`portal.tills.enrol`/`device` (names in `SharedKernel.PlutusPolicies`) replace the old all-or-nothing DevAuthBypass. Gated behind `DISABLE_AUTH_DEV_ONLY`.
- **Schema:** `EnrolmentCode` + `Device` (server-only, global/unscoped) on `plutus_t1`; `WebCredential` mapped to the existing table (guarded migration, no-op on existing DBs).
- **Endpoints (Tenancy module):** `POST /api/v1/tenants` (provision Tenant+Business+Store+admin), `POST /api/v1/tills`, `POST /api/v1/tills/enrol` (anon), `POST /api/v1/tokens/device` (anon), `POST /api/v1/tills/{id}/revoke`. Rate limiter 5/min/IP on the anon endpoints. Device tokens signed with `TEST_TOKEN_SECRET` (same secret the handler validates).
- **Crypto (SharedKernel):** Crockford32, Pbkdf2 (legacy KDF params), CompactToken (HMAC).
- **Tests:** enrolment lifecycle, wrong-secret 401, reused/expired 410, auth-handler claim emission/expiry/tamper, provisioning full-graph + admin-login verify + isolation. **23/23 unit, 5/5 arch green.**
- **DEBUG caveat:** the host runs SqliteDbContext in DEBUG (no tenancy tables) so tenancy endpoints are Release-only; service logic is covered by SQLite tests. Full HTTP e2e via the host deferred to T1.7.

**▶ NEXT — T1.3 Sales schema v2:** new `Sales`/`SaleLines`/`SaleTenders`/`SaleAdjustments`/`SaleQuarantine`/`OutboxEvents`/`ConsumerOffsets` (spec §T1.3), constructor-enforced money invariants, migration to `plutus_t1`, property-based round-trip test. Legacy sale tables stay untouched until T1.8 migrates data.

---
### (historical) T1.2 resume notes — superseded by the above

Phase 0 done; **Phase 1 authorised**. Phase 1 is specced in `Build/plutus-sonnet-build-spec.md` T1.1–T1.8.

**✅ T1.1 COMPLETE (2026-07-24)** — commits `26f03e0`, `7fe8e78`, `55c8588` on `Matt's-Horror`:
- `Tenant` entity + `Tenants` table on **MySqlDbContext only** (shared model + MAUI Sqlite untouched). Scaffold's EF6→8 spurious `AlterColumn` noise was trimmed away and **proven clean** (a probe migration scaffolds an empty `Up()`).
- Shadow `TenantId` + index + global query filter on **19 tenant-owned entities** (by convention). `Person` is scoped as the TPT root of `Employee`. Global/shared (Role, PaymentMethod, Person-as-reference→no, AuthActions*, mapping tables) stay unscoped. **Decision 2026-07-24:** Role/PaymentMethod/Person-hierarchy classification confirmed with Matt.
- `SaveChanges` stamps `TenantId` from context and throws on cross-tenant writes. `ITenantContext` (SharedKernel) via optional ctor; **null-safe default = Kapow** so every non-DI call site keeps working.
- Migration `AddTenantIdToTenantOwned` (19 ADD COLUMN + 19 indexes + in-migration Kapow backfill) **applied to `plutus_t1` only** — 21,654 Sales / 74,823 Trans backfilled, 0 rows left `Guid.Empty`. **Live `plutus` has 0 TenantId columns (untouched); ETRIE untouched.**
- Tests: `Plutus.Tests.Unit.TenancyTests` — model-metadata (right entities scoped) + SQLite two-tenant isolation + cross-tenant-write guard. **8/8 unit, 5/5 arch green.** T0.4 rule-3 skip removed.
- Kapow tenant id (stable): `0192b8a0-1a6f-7000-8000-000000000001` (`Plutus.Entities.Tenancy.KnownTenants.Kapow`).

**▶ NEXT — T1.2 (provisioning + enrolment + real ITenantContext):** fill the `Plutus.Tenancy` scaffold; `POST /api/v1/tenants` (platform-admin) and `POST /api/v1/tills/enrol` (anon) per spec §T1.2; implement the **request-scoped JWT-backed `ITenantContext`** reading `tid`/`did` claims and register it (scoped) in the DBService DI so EF picks the `(options, ITenantContext)` ctor. Until then the Kapow default drives single-tenant.

**Superseded groundwork notes (kept for context):**
- ✅ **DB copy** `plutus_t1` on the Mac (dump of live `plutus`, `--set-gtid-purged=OFF`; 20,340 items / 21,654 sales). `plutus` user granted. **All T1.1 migration work targets `plutus_t1`; live `plutus` is untouched until proven.**
- ✅ **Migration toolchain on net8**: `Database.Migrations.Startup` retargeted net7→net8 (EF Tools 8); `dotnet-ef` 8.0.10 installed global; `dotnet ef dbcontext list` discovers MySqlDbContext/SqliteDbContext. **PATH gotcha:** the ef tool needs the x64 SDK first on PATH — run with `export PATH="/c/Program Files/dotnet:$HOME/.dotnet/tools:$PATH"` or it fails "Unable to retrieve project metadata" (x86 shadow).

**KEY DECISION (Matt, 2026-07-24): evolve the schema IN PLACE** (not greenfield). Add `Tenants` above the existing hierarchy; existing **`Business` plays the Company role** (add a separate `Companies` table only if a real multi-company-per-tenant need appears); **keep existing `Stores`/`Till`**; add `TenantId` to tenant-owned tables; backfill one tenant "Kapow" (deterministic id). The live webapp keeps working throughout.

**Two complications to handle in the next increment:**
1. **Name overlap already resolved by the decision:** the spec's `Companies/Stores/Tills` map to existing `Business/Stores/Till` — do NOT create parallel tables.
2. **`RepositoryContext` is bi-modal** — MySqlDbContext (server) AND SqliteDbContext (MAUI till) share it. Tenancy is server-side; generate/apply the migration for **MySqlDbContext only** (`dotnet ef migrations add … -c MySqlDbContext -o Migrations/MySql`), and keep the model change tolerable for the Sqlite/MAUI side (columns nullable/unused locally, or guarded). Verify the MAUI Sqlite path still builds.

**Next concrete steps:** create `Tenant` entity + DbSet + config in `Plutus.Entities`; add `TenantId` (Guid, char(36) to match existing GUID mapping) to tenant-owned entities implementing `ITenantOwned`; global query filter in `RepositoryContext` reading an injected `ITenantContext` (null-safe for MAUI); `AddTenants`/`AddTenantId` migration → `dotnet ef migrations script` → apply to `plutus_t1` → verify + backfill Kapow → isolation integration test (two tenants). Then remaining T1.1–T1.8 below.

---
**Full Phase 1 task list** (spec authority):

Order (each with a DoD in the spec; commit per task; deploy the FULL publish folder):
1. **T1.1 Tenancy schema** — new `Tenants/Companies/Stores/Tills/EnrolmentCodes` (fill the `Plutus.Tenancy` scaffold); add `TenantId` to every tenant-owned table with composite indexes; EF global query filter by convention; `ITenantContext` from JWT `tid`; `SaveChanges` stamps/guards TenantId. **This is the flip-the-query-filter-test-on point** (un-skip the T0.4 rule-3 test). ⚠️ migrate a DB copy first; the live env has real Kapow data.
2. **T1.2 Provisioning + device enrolment** (Tenancy module): `POST /tenants`, `/tills`, `/tills/enrol`, `/tokens/device`, revoke.
3. **T1.3 Sales schema v2** — `Sales/SaleLines/SaleTenders/SaleAdjustments/SaleQuarantine/OutboxEvents/ConsumerOffsets`, pence + per-line VAT, UUIDv7 PKs, `(TenantId,SaleId)` unique. Legacy sale tables stay until reconciliation sign-off.
4. **T1.4 idempotent ingest** `POST /api/v1/sales` (Plutus.Sales) + transactional outbox.
5. **T1.5 broker-less dispatcher** (OutboxEvents polling + ConsumerOffsets).
6. **T1.6 contract freeze** (regenerate `openapi.json`, enable drift gate).
7. **T1.7 standing suites** (tenant isolation, money reconciliation, idempotency).
8. **T1.8 Kapow migration v2** (extend SeedMigrator per `kapow-db-gap-analysis.md` §5).

Note: the transitional `Sale/Summary`/`VatIntegrity`/`SaleReport` on Plutus.Sales migrate to Plutus.Reporting in **Phase 3** (projections), not Phase 1.

## 6. Operational how-to (for the next session)

- **SSH:** `ssh -i ~/.ssh/plutus_mac_ed25519 admin@10.1.1.40`. sudo needs Matt (password prompt) — hand sudo blocks to him.
- **Node/dotnet:** Windows dev box has **.NET 10 SDK** (`"C:\Program Files\dotnet\dotnet.exe"`, builds net8 fine) but **no Node** (by choice). The **Mac** has Node 26 (Homebrew) — the webapp is built there.
- **Backend deploy:** publish `-r osx-arm64 --self-contained`; **⚠️ when new module assemblies are added, copy the WHOLE publish folder** (a cherry-picked DLL missing `Plutus.SharedKernel.dll`/`Plutus.Identity.dll` crashed the process to 000 this session). Procedure: tar the publish dir → scp → on Mac stop pm2, swap `~/PLUTUS/backend` (keep the old as a `*_bak`), `chmod +x Plutus.DBService`, `pm2 restart plutus-backend`. Rollback layers currently on the Mac: `~/PLUTUS/backend_net7_bak`, `~/PLUTUS/backend_prev`.
- **Webapp deploy:** build on Mac (`npm run build` in `~/PLUTUS/Plutus.Frontend.WebApp`), copy `dist/.` → `/srv/apps/PLUTUS/web/current/`.
- **Verify token flow:** `POST /api/Auth/Login {email,password}` → `{token}`; use `Authorization: Bearer <token>` + `BusinessId: d5a31aac-159e-9a30-706b-02f9eb935600`.
- **Caddy edits:** stage in `~/PLUTUS/staging/`, `caddy validate`, back up, graceful reload; re-run ETRIE health checks. (Not needed for backend-only work.)

## 7. Known debts / open decisions

- **Newtonsoft in `TestTokenAuth`** — spec bans Newtonsoft; kept for now (token (de)serialisation). Migrate to System.Text.Json as a later cleanup (safe: validation works on the raw string; only issue-time JSON changes).
- **No git remote** — decide (GitHub private repo?) so work is backed up off-machine and CI can run. Currently one disk = single point of failure.
- **B2C tenant** — the real auth blocker (architecture §11); `TestTokenAuth` is the stand-in seam. Deferred by Matt.
- **VAT legacy data** — 47 off-band items + NatApp `DiscountRate=0` regression documented in `VAT-FixLater-Report-2026-07-23.md`; guardrails live, legacy data intentionally not repaired.
- **NatApp bug fixes are code-only, NOT build-verified** (no Xamarin toolchain here) — build in Visual Studio before shipping to the shop.
- **No test suite beyond SharedKernel unit tests** — the spec's standing suites (tenant isolation, money reconciliation, idempotency) arrive with Phase 1.
- **MAUI ClientUI** (net10-windows) still references the shared libs (now net8) — fine (net10 consumes net8); not re-verified this session.

## 8. One-line status

### Phase 10 record (Billing & offboarding) — COMPLETE & LIVE 2026-07-25

All four WPs built, deployed, verified live. Migration `AddDeletionSchedule` (t1 → plutus).
Backend rollback `~/PLUTUS/backend.pre-phase10b`. 119 unit + 5 arch green. Platform-admin surface
is **API-only** (like tenant provisioning) — no portal tab.
- **WP10.1 entitlements + billing seam**: `IEntitlementService` (SharedKernel) + `EntitlementService`
  reads `Tenant.Entitlements`; `IBillingProvider` seam + `NullBillingProvider` (HMAC-verifies
  `BILLING_WEBHOOK_SECRET` — **Stripe adapter deferred to Matt's provider choice**); billing webhook
  applies changes. Verified: set/read entitlements 204/200; unsigned webhook → 400.
- **WP10.2 lifecycle** (D16): `TenantStatus`; portal login refused when Suspended/Closed
  (AuthController checks the user's tenant via People.TenantId) **while device tokens keep syncing**;
  platform-admin `PUT /api/v1/tenants/{id}/status|entitlements`, `GET /api/v1/tenants`.
- **WP10.3 export**: `GET /api/v1/tenants/{id}/export` streams a ZIP — a CSV per tenant table
  (discovered from information_schema) + `manifest.json` (row counts + sales gross). **Verified
  penny-exact**: 53 tables, 21,648 SalesV2, salesGrossPence 55,685,941 == DB SUM.
- **WP10.4 deletion + retention**: `DeletionSchedule` + grace window; `RetentionSweeper`
  (BackgroundService, runs on startup then hourly) executes due deletions via a schema-discovered
  hard-delete (founding tenant guarded) + purges expired enrolment codes; sales never purged.
  **Verified**: a throwaway tenant scheduled grace=0 was hard-deleted (52 tables) on the next
  sweep, schedule→Executed, **Kapow untouched** (Business 1, SalesV2 21,648, TillDetails 4).
- **Only Stripe left**: the concrete billing adapter (replace `NullBillingProvider`, set
  `BILLING_WEBHOOK_SECRET`) — awaits Matt's provider choice. Everything else is live.

---


### Phase 11 record (Operability & shopkeeper UX) — COMPLETE & LIVE 2026-07-25

All four WPs built, deployed, and verified live (till names, items-sold 210 rows/7d, receipt-template
GET, warehouse create — all 200/201; ETRIE 200). Migrations `AddTillDetails` + `AddReceiptTemplate`
rehearsed on plutus_t1 then applied to plutus; both frontends rebuilt+deployed. Backend rollback
`~/PLUTUS/backend.pre-phase11`. 114 unit + 5 arch green.
- **WP11.1 till naming** (`…`): server-only `TillDetails` (legacy Till has no Name), tenant-unique
  (case-insensitive guard + unique index), `PUT /api/v1/tills/{id}/name` (portal admin OR device),
  backfill "Till {short-id}", rename from portal (inline) + till Settings. Names shown in the fleet list.
- **WP11.2 receipts**: `saleId` as hand-rolled Code39 SVG on the receipt; per-store template in
  `StoreDetails.ReceiptTemplateJson` (GET sales.ingest so the till caches+applies; PUT company.manage);
  portal template editor + receipt viewer/reprint. Follow-ups: scan-into-Returns, operator/VAT lines.
- **WP11.3**: `POST /api/v1/stock/locations` (warehouse creation); portal add-store button, new-location
  control, tick-box/24h opening-hours editor (same JSON, advanced-JSON fallback).
- **WP11.4 items-sold report**: `GET /api/v1/reports/items-sold(.csv)` from legacy Trans+Sales;
  portal "Items Sold" tab (today/7/30-day + month/quarter/year, totals, auth-correct CSV). Follow-up:
  till Reporting mirror.

**Phase 10 (billing & offboarding): PLANNED (4 WPs in the impl plan), not yet built.** WP10.1 entitlements
+ IBillingProvider seam (Stripe deferred), WP10.2 lifecycle states (suspended locks portal, tills keep
syncing — D16), WP10.3 tenant data export, WP10.4 scheduled deletion + retention.

---

**Phases 0–3, 5, 7(cash), 8, 10, 11 COMPLETE; Phase 9 IdP-swap BUILT & DEPLOYED (dormant)** — the web POS
trades through the idempotent v1 pipeline; RBAC, admin APIs, rollup reporting (penny-parity),
financial periods, stock, pricing, cash sessions, customers/credit are live; portal at
**https://admin.plutus.huggett.dscloud.me**. **Phase 9 (2026-07-25):** provider-agnostic auth seam
(`IdP:Provider = test|entra|keycloak|b2c`) deployed — defaults to `test`, so live behaviour is
unchanged (verified: legacy+v1 endpoints 200, 401 enforced, ETRIE 200). Live Keycloak running in
Docker on the Mac (`plutus-keycloak`, :8089, realm `plutus`); both frontends have OIDC PKCE login
behind `VITE_AUTH_MODE=oidc` (default password). **To flip to Keycloak (Matt):** apply the staged
Caddy vhost (`ops/keycloak/README.md`, needs sudo), set `IdP__Provider=keycloak` +
`IdP__Keycloak__Authority/Audience`, rebuild the frontends with the OIDC env, and ensure the
Keycloak user's email matches a `WebCredentials` row. Entra is config-ready (`ops/entra/`, needs
his Azure tenant). 112 unit + 5 arch green. Phase 4 (MAUI) paused; open moves: Phase 6 (Woo — needs
a test store), Phase 10 billing, Phase 11 (Matt's punch list, planned), production cutover.

### Phase 9 record (IdP swap) — BUILT 2026-07-25

- **WP9.1 seam** (`be88494`): `ConfigureAuthentication` selector replaces the implicit B2C branch.
  `entra`/`keycloak` = metadata-driven `AddJwtBearer` (`MapInboundClaims=false`); a policy-scheme
  routes 2-segment HMAC device tokens → `PlutusTokenAuthHandler`, 3-segment JWTs → the IdP (till
  client-credentials unaffected). `RbacClaimsTransformation` maps the token's verified email →
  `WebCredentials` user → injects the SAME claims the HMAC login emits (EmployeeId + RBAC scopes +
  legacy `scp`). Login endpoint + transformation share `EffectivePermissionsService.ResolveLoginScopesAsync`.
- **WP9.2 Keycloak** (`…`): Dockerised KC26 (`plutus-keycloak`, 127.0.0.1:8089, `restart unless-stopped`),
  realm export `ops/keycloak/plutus-realm.json` (2 public PKCE SPA clients, `aud=plutus-api`, demo
  user `ada@shop.test`), `run-keycloak.sh`. Issuer `https://login.plutus.huggett.dscloud.me/realms/plutus`.
  Caddy vhost staged (`ops/keycloak/caddy-login-vhost.caddy`, admin surface blocked) — **needs Matt's sudo**.
- **WP9.3 Entra** (`ops/entra/README.md`): setup runbook; code path already live from 9.1.
- **WP9.4 frontends**: hand-rolled OIDC PKCE (`oidc.ts` + `auth.ts` in both apps); access token in
  memory only, IdP SSO cookie backs reload, refresh_token renews in-tab. Default password mode.
- **WP9.5 tests**: `AuthSchemeRouter` (device-vs-JWT) + `RbacClaimsTransformation` unit-tested.
- **Rollback:** backend `~/PLUTUS/backend.pre-phase9`. Keycloak: `docker stop plutus-keycloak`.

Resume at §5 for earlier phases.
