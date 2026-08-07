> **📦 SUPERSEDED — folded into [`Build/To do/MAUI-Retrofit-Plan-2026-08-07.md`](../To%20do/MAUI-Retrofit-Plan-2026-08-07.md).**
> Almost all of this document survives there: the no-broker architecture decision, the work
> packages with their exact DTO and endpoint names, the six-area parity analysis, and the risk
> register. Kept for the three-way option scoring and the review notes behind them.
>
> ⚠ **Two things here are now WRONG — do not act on them:**
> 1. It says AppClient already has an outbox (`DBAction` / `SaveDBAction` / `LocalToServerSync` at
>    `RepositoryBase.cs:616`). **It does not** — that machinery is in *ClientUI*. AppClient has zero
>    hits. Building the till outbox is therefore real work, not a wiring job.
> 2. Its P0 / WP4.0 says to retarget `Plutus.Frontend.AppClient` to **.NET 8**. The branch
>    reconciliation it was waiting for happened (`4494a57`) and **both MAUI projects are on net10**.
>    Following that instruction now would downgrade them.

# MAUI POS ↔ Plutus Backend Connectivity Plan

**Date:** 2026-08-01
**Scope:** `Plutus.Frontend.AppClient` (the MAUI till/POS app, `seank842/Plutus` branch `feature/maui-pos-rework`) currently has no network connection to the Plutus backend at all. This document plans how to change that.
**Status:** Planning only — no code has been written or changed.

---

## Action for Matt: replicate key context on your other device

You're developing across two machines. Claude's memory is local per machine/session (`~/.claude/projects/.../memory/`) — it does **not** sync automatically between devices, so context saved on one machine is invisible to Claude on the other unless you repeat it there.

- [ ] On your other device, tell Claude: *"Plutus will become a multi-tenant SaaS product sold to other retailers, not just internal tooling for my own shop."* This was saved as project memory (`plutus-multitenant-saas-direction`) on this machine on 2026-08-01 — it explains the tenancy/operator-boundary design already in the codebase, and is worth having recognized on both machines.
- [ ] If you continue this specific plan from the other device, consider also repeating: the VAT-rate-change ingest compliance requirement (WP4.1b below), and that `feature/maui-pos-rework` is confirmed as the intended upstream MAUI replacement (resolved below) — both are decisions from this conversation, not things derivable from the code alone.
- [ ] More generally: any time you tell Claude something durable about this project's direction, correct its approach, or resolve an open question — on either machine — consider whether it's worth repeating on the other one too, since this doc (and the repo) sync between devices but Claude's memory doesn't.

---

## TL;DR

- **No dedicated broker product (RabbitMQ etc.) is needed** — but a durable queue absolutely still is. That queue already exists on both ends (till-side local outbox, backend-side Outbox table); the only question was which technology implements it, and a broker scored 2/10 across three independent architecture reviews versus extending what's already there.
- The right approach is to **extend the DB-backed Outbox pattern that already exists and is already live in production** for the React web POS — wiring the till's existing (but currently dead-ended) local outbox to it, not inventing something new.
- **Before any of this**: `feature/maui-pos-rework` is 183 commits behind the backend branch (`Matt's-Horror`) that this plan depends on — see "Branch reconciliation" below.

---

## Resolved: `feature/maui-pos-rework` is the intended upstream replacement

`HANDOVER.md` (from `Matt's-Horror`, dated 2026-07-27) had flagged Phase 4 (MAUI till sync) as paused pending "new MAUI code... coming from upstream (`github.com/seank842/Plutus`) that will replace the planned port." **Confirmed (2026-08-01): `feature/maui-pos-rework` is that replacement** — the Phase 4 re-baseline should proceed against this branch. The branch reconciliation work below (merging onto `Matt's-Horror`'s current tip, retargeting to net8) is the re-baselining step the handover doc anticipated.

---

## Current state (verified against the actual code, not just docs)

### Backend
Already built and, per `HANDOVER.md`, live in production for the React web POS since 2026-07-24:

- **Tenancy**: `Tenant`, `Store`, `Till`, `TillDetails` — pooled multi-tenant model, `TenantId` on every row, resolved from the JWT, never from the request.
- **Device enrolment**: `Device`, `EnrolmentCode`, `EnrolmentService`, `TillsController` (`POST /api/v1/tills/enrol`), `TokensController` (`POST /api/v1/tokens/device`) — issues a one-time enrolment code, redeemed for a device credential.
- **Sale ingest**: `POST /api/v1/sales` — idempotent on `(TenantId, SaleId)`, `SaleId` is a client-minted UUIDv7, `deviceSeq` used for gap detection.
- **Sync primitive**: `OutboxDispatcher` / `OutboxDrainer` (`src/Plutus.Web.Infrastructure/Outbox/`) — a DB-backed, at-least-once, per-consumer-offset, dead-letter-on-poison outbox. Already drives the WooCommerce/Webstore integration and the web POS's sale ingest.
- **No broker, no SignalR, no MQTT anywhere** — verified by grepping every `.csproj` in the branch and the full file tree.

A duplicate/legacy `TillController` also exists in the old `Plutus.DBService` endpoint project — it bypasses tenant scoping and should be deprecated or retired once MAUI is wired to the new `TillsController`.

### MAUI till app (`Plutus.Frontend.AppClient`)
Confirmed via direct code inspection:

- Local storage is **SQLite via EF Core** (`Database.db`, per-device file). A `DatabaseProvider.Cloud` enum value exists but its constructor throws `NotImplementedException` — scaffolded, never built.
- `LoginViewModel.cs` authenticates entirely against the **local** database — no network call anywhere in the login path.
- `TillViewModel.cs` (checkout, returns, discounts, held baskets, payment, stock decrement, refund authorization tiers) — searched for `HttpClient`/`Refit`/`RestSharp`/any HTTP call: **zero hits**. Everything is local CRUD.
- A rudimentary outbox **already exists**: a `DBAction` table (`SqliteDbContext`) is enqueued by `SaveDBAction()` (`RepositoryBase.cs:616`), and each repository subscribes to `Connectivity.ConnectivityChanged` to call `LocalToServerSync()` — but there is currently nothing on the other end for it to sync to.
- Known bugs already catalogued in the branch's own `OfflineMode-2026-07-23-plan.md`: `ItemRepository` id-converter throws `NotImplementedException`; composite-key deletes throw `NotSupportedException`; a `SaveChangesAsync` overload bug makes sync-state toggling a no-op; no cross-type ordering/retry in `LocalToServerSync()`.

This confirms the till app is a full offline-first, local-database application — almost certainly inherited from the legacy "Kapow" till software it's replacing (there's a one-off `Plutus.Migration.Kapow` tool for importing old Kapow data into the new central schema, but that's a one-time migration, not live sync).

---

## Recommended architecture: extend the existing Outbox + plain REST

**No broker. No SignalR (yet).**

Three independent architecture reviews scored the options as follows, and all three reached the same #1 choice:

| Option | Score | Why |
|---|---|---|
| **A — Extend DB-backed Outbox + REST (poll/push)** | **9/10** | Already built, already tested, already live for the web POS. This is a wiring job, not a design job. |
| D — Pure client-driven pull/sync, no broker | 7–8/10 | Functionally very close to A, but re-derives what A already gets for free (idempotent ingest, dead-lettering). |
| C — SignalR hub + REST fallback | 4–5/10 | No existing precedent; solves a problem (sub-second push) the till doesn't have; still needs the full REST/outbox path underneath anyway. |
| B — Message broker (RabbitMQ/NATS/etc.) | 2/10 | Explicitly rejected by the team's own architecture doc: *"Exposing a broker to hundreds of retail-site networks means broker credentials on every till and firewall fights."* Adds an always-on stateful service to operate on a single self-hosted Mac mini, for a problem (till survives the network being down) a broker doesn't solve. |

The backend's own architecture document already reached this conclusion independently:

> "Per D8, start broker-less (MySQL outbox + polling consumers with per-consumer cursors, ~200 lines of owned code); adopt RabbitMQ/Service Bus only when volume demands — consumers depend only on 'events arrive', so the swap is plumbing, not redesign."

> "The local outbox table is the till's queue (durable, ordered, survives crashes). The wire is plain HTTPS to the idempotent ingest API."

**Clarifying note:** "no broker" does not mean "no synchronization mechanism." A durable queue is still required and still exists on both sides — it's implemented as database rows (a local outbox table on the till, an `Outbox` table on the backend) rather than as messages inside a dedicated broker product like RabbitMQ or Kafka. The recommendation above is about *which technology implements the queue*, not whether one is needed.

**Future consideration (not in scope now):** if Plutus ever moves off the single self-hosted Mac mini onto managed cloud infrastructure, and the number of *internal* consumers of the sale-ingest event grows (reporting rollups, loyalty triggers, webhook delivery, inventory ledger propagation, analytics), a managed broker (AWS SNS+SQS/EventBridge, Google Pub/Sub, Azure Service Bus) becomes worth reconsidering — purely for fanning the ingested event out to many internal consumers without each one polling the same Outbox table. This would not change the till/device edge, which should stay on HTTPS + local durable queue regardless of scale. Not blocking or relevant to the current plan.

---

## Prerequisite: branch reconciliation

`feature/maui-pos-rework` is only 3 commits ahead of `Development`, but **183 commits behind `Matt's-Horror`** — it predates the tenancy module, the `Device`/`EnrolmentCode`/`Till` schema, `TillsController`, `TokensController`, and the Outbox infrastructure this whole plan depends on.

Worse: `Matt's-Horror`'s 2026-07-24 merge deliberately kept MAUI on **.NET 7** while the rest of the backend moved to **.NET 8** — as it stands today, `Plutus.Frontend.AppClient` will not build against the tenancy/auth code it needs to call.

**Do this before writing any client-side networking code:**
1. Merge/rebase `feature/maui-pos-rework` onto `Matt's-Horror`'s current tip.
2. Retarget `Plutus.Frontend.AppClient` to .NET 8.
3. Resolve the resulting `Commons` breakage and get a green build.

---

## Backend-side work

Mostly reuse, not new build:

- **Enrolment/auth**: no new modeling — MAUI consumes the existing `POST /api/v1/tills/enrol` and `POST /api/v1/tokens/device`, exactly as the web POS does.
- **Retire the legacy `TillController`** (the one in `Plutus.DBService`, not `Plutus.Tenancy`) or explicitly document it as deprecated CRUD-only — it bypasses tenant scoping, and having two till-shaped endpoints once MAUI is wired up is a foot-gun.
- **Sale ingest**: `POST /api/v1/sales` already exists, idempotent, no change needed for the base contract — MAUI is just a new caller. **New requirement (confirmed with Matt, 2026-08-01): VAT-rate-change compliance.** If a government VAT rate change happens while a till is offline, that till may apply a now-stale cached rate to sales it queues locally. Ingest must revalidate every incoming sale's declared VAT against the rate that was **actually in effect at the sale's `occurredAtUtc`** (not just "is this a currently-valid rate") — this requires the backend to hold VAT rates as an effective-dated history (rate + effective-from date), not a single flat current value, if it doesn't already. Verify whether `Plutus.Entities`' tax/VAT model already supports this before assuming it needs building. On a mismatch, do not silently accept or silently auto-correct the customer-facing total — route it into the existing quarantine/reconciliation pattern (mirroring how the Kapow migration already flags reconstructed VAT via `VatReconstructed=1`) so it surfaces for review rather than passing as compliant.
- **Catalogue/price sync**: build or confirm `GET /api/v1/catalogue/changes?since={version}` (cursor-based). Check whether the web POS's existing `/prices/effective` endpoint can be reused as-is or needs a MAUI-specific cursor shape.
- **Heartbeat**: `POST /api/v1/heartbeat` (`{deviceId, appVersion, outboxDepth, oldestUnsyncedAge, deviceClock}`), server derives ONLINE/STALE/OFFLINE status.
- **Data migration**: run `Plutus.Migration.Kapow` so MAUI tills enrol against the new schema (UUID item PKs, integer pence, per-line VAT), not the frozen legacy tables.
- **Deployment**: nothing new to containerize — `OutboxDispatcher` already runs in-process as a hosted service inside the existing API host.

## MAUI client-side work

- **New shared projects**: adopt `Plutus.Client.Core` (net8, no MAUI references — outbox engine, pusher, sync cursors, heartbeat client, enrolment/token client) and a new DTO-sharing project for the `/api/v1/*` request/response shapes, so wire drift is structurally impossible. **Naming note (confirmed 2026-08-01):** a project literally named `Plutus.Contracts` already exists (`Plutus/Commons/Plutus.Contracts`) — it's the legacy repository-interface layer (`IItemRepository`, `ISaleRepository`, etc.), unrelated to DTO sharing. Do not reuse that name; call the new project `Plutus.Contracts.Client` (or similar) to avoid an assembly/namespace collision. Reference it from `AppClient` rather than hand-rolling HTTP calls into `TillViewModel.cs`.
- **Wire up the existing outbox**: fix the cataloged `DBAction`/`LocalToServerSync` bugs (composite-delete exception, the `SaveChangesAsync` no-op bug, missing retry ordering), then point the drain at `POST /api/v1/sales` with device-token auth. This is centralizing an existing mechanism, not building a new one.
- **Enrolment/login**: replace the dead `DatabaseProvider.Cloud` branch with a real first-run flow — enrolment code → `POST /api/v1/tills/enrol` → `deviceId` stored locally, `clientSecret` in platform secure storage (never the SQLite file) → token client with refresh-before-expiry and 401-retry-once. Operator login stays local/offline: sync operator credential hashes down and verify locally, no network round-trip per till login. **Correction (confirmed 2026-08-01):** the endpoint this needs (`GET /api/v1/tills/{id}/operators`) does **not** exist yet — only `GET /api/v1/users/{id}/effective-permissions` does. This is small additive backend work (see WP4.11), not pure consumption as originally assumed.
- **Conflict resolution**: no merge logic needed for sales themselves — each is a new immutable record with a client-minted UUID, deduped on `SaleId`+`deviceSeq`, so there's nothing to merge, only accept-or-already-seen. Catalogue/price is one-directional and server-always-wins, so staleness self-resolves on the next successful pull. **Stock is the one genuine shared-mutable-state conflict**: if two tills sell the last unit of the same item while both offline, both decrement events are individually valid and both must apply. This is why stock must be modeled as a movement ledger rather than a mutable counter (per the Kapow gap analysis, finding F5 — the legacy system's mutable stock counter already goes deeply negative for exactly this reason) — each sale appends a stock-decrement event, so concurrent offline sales simply both append correctly (the running total may go briefly negative, which is expected/normal for any offline-capable POS and should raise an alert, not be prevented with a lock or a broker). **Verified: this already exists** — `src/Plutus.Catalogue/StockLedger.cs` implements exactly this (`StockLedgerService.ApplyAsync` appends an immutable `StockMovement` and updates a derived `StockLevel`; `StockProjectionConsumer` folds `SaleRecorded` events into movements via the same Outbox/drainer contract as every other consumer, so it's channel-agnostic — MAUI sales get correct stock handling automatically once they flow through `POST /api/v1/sales`, no new backend logic required). **One real requirement this surfaces for MAUI** (not a backend change): the consumer only attributes a movement if the sale line's JSON metadata carries `itemIdOne` matching how `Plutus.Catalogue` keys items — lines without it are silently skipped, no error — so MAUI's outbound payload must populate this the same way the web POS already does, and testing should explicitly assert stock moves correctly for a MAUI-originated sale, not just that the sale itself is accepted. **Worth confirming with Matt**: the legacy mutable `Stock.Quantity` model is still dual-written per the code's own comments ("the ledger becomes the single authority when the legacy tables retire"), but `HANDOVER.md` says the legacy sale bridge was already switched off as of 2026-07-27 — so the ledger may already be sole authority in practice.
- **`TillViewModel.cs` changes**: checkout finalization keeps writing locally first (unchanged); it additionally writes an outbox row in the same transaction, drained by a background pusher. The local database is not replaced — only supplemented.
- **Feature-parity retrofit**: bring MAUI up to what the web POS already has, per `Build/till-retrofit-2026-07-25.md` — effective pricing at basket-add (with offline fallback to cached price), customer attach/search, store-credit tender (online-only, disabled offline by design), `CustomerId` on the sale. Card-capture is blocked on both tills pending a provider decision — independent of this plan.

## Suggested phased sequencing

| Phase | Work |
|---|---|
| **P0** | Branch reconciliation: merge onto `Matt's-Horror`, retarget to net8, green build |
| **P1** | Stand up `Plutus.Contracts.Client` (name changed to avoid collision with the existing legacy `Plutus.Contracts` project — see MAUI client-side work above) + `Plutus.Client.Core`; generate/hand-write the `/api/v1/*` OpenAPI client |
| **P2** | Enrolment & auth: first-run screen, device token client, secure-storage secrets |
| **P3** | Outbox wiring & sale ingest: fix existing bugs, point the pusher at `/api/v1/sales`, verify idempotency end-to-end |
| **P4** | Catalogue/price sync + heartbeat, fleet dashboard visibility, `426 Upgrade Required` handling |
| **P5** | Feature-parity retrofit — see "Feature & UI Parity" below for the full 6-section breakdown and build order |
| **P6** | Operator RBAC sync, permission caching, local credential verification |

## Feature & UI parity: Cash, Inventory, Reporting, Loyalty, Store Information, Settings

The plan above covers *transport* — how MAUI talks to the backend. It does not by itself cover the fact that several whole sections of the till UI need to be reworked or built from scratch to match what the React WebTill (`Plutus.Frontend.WebApp`, live on `Matt's-Horror`) already does. This section covers that gap, verified against the actual WebTill and both MAUI projects (`Plutus.Frontend.AppClient`, the active branch, and its sibling `Plutus.Frontend.ClientUI`).

### Cash — build from scratch
MAUI has nothing today — both projects contain only `POSCashDrawer.cs`, a hardware driver for the drawer solenoid; no float/session/X/Z concept, no ViewModel, no backend calls. The WebTill's `CashPage.tsx` supports Open float, Paid in/out (amount + mandatory reason), X snapshot, and Z close (one per business day), all posted to `POST /api/v1/cash-events` (`src/Plutus.Cash/CashController.cs`), with expected/counted/variance computed server-side. **Work**: new CashPage/CashViewModel per till/business-day, wired to the same endpoint and payload shape, with a client-side guard against a second Z close per day.

### Inventory Management — rework existing screens (lower effort)
Both MAUI projects already have full local item CRUD (name/brand/desc/cost/price/tax/category) with a UI shape very close to the WebTill's `InventoryPage.tsx`. The real gap is structural, not cosmetic: MAUI's stock is a flat 1:1 quantity column, while the backend (`Plutus.Catalogue`) now runs a proper movement ledger (`api/v1/stock/movements` — Receipt/Adjustment/WriteOff with audit trail, multi-location) plus a VAT-band consistency guard (`|price − exPrice×rate| ≤ 2p`) neither MAUI project enforces. `Plutus.Frontend.ClientUI`'s repository layer (`IRepositoryWrapper`/`ItemRepository`, using the same `Plutus.Entities.Models` types as the backend) is architecturally the better starting point than AppClient's raw local `DbContext` calls, though neither is more feature-complete. **Work**: rewire stock writes through the ledger endpoint instead of the flat column (the one genuine rebuild here), surface the VAT-band guard, and swap category CRUD to `api/v1/categories`.

### Reporting — architectural rewrite, not a feature add
MAUI's three Statistics viewmodels (`SalesReportsViewModel`, `StatisticsViewModel`, `StockOuttakeViewModel`) query the till's own local SQLite sales history directly — zero HTTP calls. The WebTill's `ReportingPage.tsx` has 8 tabs (Summary w/ period deltas, VAT return aid + integrity check, Items sold, Category sales, Best sellers, Stock/negative-stock, plus a sale-level drill-down with reprint), all **cross-till, store-wide, server-aggregated** via `Plutus.Reporting` v1 endpoints. Critically, even a pixel-perfect copy of the WebTill's screens would only show one till's data if built on MAUI's current local-query approach — closing this gap means rewriting the viewmodels to call the backend rather than extending local-DB queries, then adding the missing report types entirely new. (`ClientUI`'s equivalent is thinner still — `StockOutakeViewModel` is an empty stub, and its sales report calls a legacy file-download endpoint rather than rendering data.)

### Loyalty — build from scratch, highest effort
Confirmed zero in both MAUI projects (grepped 157 + 259 files for "loyalty"/"membership"/"customer" — zero hits, sanity-checked against a working "cash" grep in the same tree). The WebTill has a full management page (member list, tier/discount/credit balance) plus an at-sale customer search/attach flow with auto-discount and a store-credit tender, backed by `src/Plutus.Customers/CustomersController.cs` and an **append-only signed credit ledger** (balance = Σ entries, never a mutable field — an explicit architectural rule, D15). No new backend work is needed — this is pure consumption of existing endpoints. The hard part is offline design: cache a bounded, periodically-refreshed snapshot of members/credit-holders for offline name/tier/discount lookup and auto-discount; treat cached credit balance as stale-display only; hard-block redemption offline (matching the WebTill, which is also online-only for this); require connectivity for create/edit/membership writes rather than trying to reconcile customer creation across offline devices.

### Store Information — rework (the two apps are actually inverted)
This one's a surprise: the WebTill made Store Information **deliberately read-only** — an explicit "WP6.1" architectural decision that the management portal is the single source of truth for store config, matching the "P6 till platform alignment: store-info read-only" work already in Matt's-Horror. MAUI's `StoreInformationViewModel` is the *opposite* model — a fully local, admin-gated editor writing straight to a local `StoreModel`, no API calls, no opening-hours concept, with address-editing already stubbed out empty. **Work**: replace the local-write commands with API reads against the same store-info endpoint the WebTill uses, add opening-hours display, and drop (or gate out) the local edit commands — this is a simplification, not a buildout, and among the lowest-effort items on this list.

### Settings — rework the local layer, build the networked half from scratch
Both MAUI projects only replicate the old NatApp-era local-preferences layer (DB backup/restore, printer config, checkout toggles like ask-for-receipt/cash-drawer-flag/barcode symbology) — all device-local `Preferences`, no networking. The WebTill's `SettingsPage.tsx` (its largest page) has all of that plus an entirely server-backed layer: **till device enrolment/identity** (codes, Active/PendingRemoval/Revoked lifecycle, manager-approved un-enrolment), server-validated till renaming, a live environment/diagnostics panel (signed-in user, API reachability, business ID), sync-queue/parked-count visibility, and role-based gating. The enrolment/identity piece is the single largest missing sub-area and has no local equivalent to extend — it should directly reuse whatever device-token/enrolment plumbing P2 above is already adding for sync, rather than being built twice.

### UI / Visual parity — feasible, no hard blocker
MAUI fully supports ResourceDictionary/StyleSheet theming, and there's already a proof of concept in-repo: `Plutus.Frontend.ClientUI/Resources/Styles/Colors.xaml` contains the **exact hex palette** the WebTill's CSS references by name (`Primary=#272643`, `Quinary=#2c698d`, etc.) — it just hasn't been copied into `AppClient`, which currently has zero theme resources at all (`App.xaml` registers only a value converter). Work required: port `Colors.xaml`/author a `Styles.xaml` into AppClient using the already-known values, then theme the Syncfusion.Maui control suite via its own theming APIs. The one genuine limit: native OS chrome (Windows title bar, system dialogs) will never pixel-match a browser — but content/branding can match fully. On NatApp specifically: the migration plan explicitly frames its retirement as feature-parity-driven with **no stated requirement to match its look** — but WebApp's own CSS comments (*"blue application band, as per the original NatApp till"*) confirm the current palette already carries NatApp's look forward, so matching it costs virtually nothing extra.

### Suggested build order for this track
1. **Settings (enrolment/identity first)** — other areas (Cash, offline sync generally) depend on the device-token/enrolment lifecycle; build it once, reuse everywhere.
2. **Store Information** — smallest lift (simplify an existing screen to read-only), and builds confidence in the "portal is source of truth" pattern before harder areas.
3. **Cash** — depends on the device-token auth from Settings; otherwise self-contained with a well-specified backend contract.
4. **Inventory Management** — rework of existing, functionally similar screens; mainly a stock-model rebuild plus endpoint swaps, not new UI.
5. **Reporting** — larger rewrite (local queries → backend calls) but no offline-consistency design problem; can reuse Inventory's endpoint-wiring patterns.
6. **Loyalty** — highest effort, the only true build-from-scratch surface with a genuine offline/credit-integrity design problem; sequenced last so it can reuse the enrolment and connectivity-detection groundwork from Settings and Cash.

## Testing strategy

- Run the existing Appium UI suite (PR #8) **unchanged** through P0–P2 as a regression gate — enrolment shouldn't alter existing basket/checkout flows.
- Extend it with an offline-mid-checkout fixture (toggle a mock connectivity gate) asserting the sale still completes and lands in the outbox, and an online-transition test asserting queued sales drain correctly (P3).
- Add unit tests for the outbox retry/backoff contract itself (mirroring `OutboxDrainer`'s dead-letter/retry semantics client-side): stay Pending, exponential backoff 5s→5min, retry forever on network failure.
- Add an integration test hitting a real `/api/v1/tills/enrol` + `/api/v1/sales` against a disposable test tenant.
- Explicitly test that store-credit and card-capture stay correctly disabled offline — a regression there is a silent data-integrity risk, not just a UX gap.

---

## Known gaps / risks (from an adversarial review of this plan)

These are real open problems this plan does not yet solve — flagging them now rather than discovering them mid-build:

1. **Existing local till databases are never migrated or reconciled.** Each till already has real sales/held-baskets data in its own local SQLite file. Nothing above says what happens to that data at enrolment time — does it stay orphaned locally, get one-time imported, or risk being lost/duplicated for a till enrolled mid-life?
2. **Local schema ↔ backend schema bridge is unscoped.** The till's local models use `int` IDs and decimal money; the new backend uses UUID surrogate keys and integer pence. `Plutus.Contracts.Client` solves *wire* drift, but nothing names the translation layer converting local models to/from contract DTOs — this touches every basket/checkout code path.
3. **Receipt printing vs. outbox-write ordering is undesigned.** If the printer fires before/after the outbox commit and one fails, a customer could be handed a receipt for a sale that was never queued. Given printing is physical and irreversible, this needs an explicit design, not silence.
4. **Stolen-hardware threat model is missing.** Local-only operator login (recommended for Phase 2) means a stolen till carries cached operator credentials with no server-side kill switch — an attacker has offline access up to the till's local refund-authorization tier.
5. **Fleet update/versioning mechanics are hand-waved.** `426 Upgrade Required` is named, but not what the till does on receiving it, nor how a binary update physically reaches till hardware across many separate shop networks.
6. **No mixed-version-in-field story.** What happens when some tills are on the new outbox-wired client and others aren't, both hitting the same backend simultaneously during rollout?
7. **Cross-till refund lookup is unaddressed.** `TillViewModel.cs`'s return flow validates a refund against a **locally-stored** prior `SaleModel` only. Once sales sync centrally (per this plan), a customer returning an item bought on a *different* till (or the same till, after a reinstall) has no sale to look up locally. This needs the same cross-till lookup WP4.8b built for reporting drill-down (`GET /api/v1/sales/{saleId}`), but for the actual money-handling refund path — a materially higher-risk gap than the reporting one, since it affects real transactions, not just a report view. Not currently in any WP above.
8. **GDPR/PII exposure via the new Loyalty cache, compounding the stolen-hardware gap (#4).** The backend already has a real data-retention mechanism — `src/Plutus.Tenancy/RetentionSweeper.cs` + a `DeletionSchedule` model/migration — presumably handling right-to-be-forgotten/anonymization requests centrally. WP4.9b introduces a local `LoyaltyCache` on the till holding customer name/tier/discount (PII) for offline lookup. Nothing above says whether a central deletion/anonymization event propagates to purge that till-side cache — if it doesn't, a customer's data-deletion request could be honoured centrally while a stale copy persists indefinitely on till hardware (worse still if that hardware is later stolen, per gap #4).
9. **Payment/card-terminal integration is entirely greenfield — confirmed, not just blocked on a decision.** Grepped the full backend + both MAUI frontends for any card-terminal/PDQ/payment-provider integration code (Stripe, SumUp, Worldpay, Adyen, Zettle, etc.) — zero hits anywhere. The "card-capture provider choice" open question below isn't just unresolved, there's no existing code to build on at all — whoever picks this up is starting from nothing, which changes its effort estimate relative to everything else in this plan.

---

## Open questions for Matt & Sean

- Does the web POS's `/prices/effective` endpoint suffice for MAUI's offline-cache model, or does it need a dedicated `changes?since=` shape?
- Legacy `TillController` disposition: deprecate, delete, or keep as read-only CRUD?
- Card-capture provider choice — blocks retrofit parity on both tills, independent of this sync plan. Confirmed 2026-08-01: zero existing integration code anywhere in the repo (checked for Stripe/SumUp/Worldpay/Adyen/Zettle/generic PDQ) — this is a from-scratch integration once a provider is picked, not a wire-up.
- Kapow migration timing relative to MAUI enrolment — does a till enrol before or after its store's Kapow data has been migrated to the new schema?
- Credentials fork (local PIN now vs. synced-hash local verification later, per `OfflineMode-2026-07-23-plan.md` §4.3, Option A vs B) — who decides when to move to B, and what triggers it?

---

## Implementation (work packages, for direct execution by Claude/an AI coding agent)

Everything above is the plan; this section operationalizes it. It's written in the exact house style already used by this branch's own `Build/plutus-implementation-plan.md` (`WP<phase>.<n>` bold title + description, italicized `*DoD:*` line, one work package at a time) so it can be dropped straight into that document as a continuation of its Phase 4, or executed standalone from here. Every contract/DTO/field name below was fetched from the actual current code (`Matt's-Horror` for backend, `feature/maui-pos-rework` for MAUI) — nothing here is invented, per this repo's own governing rule.

`WP4.1`–`4.3` already existed in `Build/plutus-implementation-plan.md` as paused target contracts; they're reproduced verbatim below, now unblocked. `WP4.0` and `WP4.4` onward are new, covering branch reconciliation plus the six feature-parity areas from the section above, in the recommended build order (Settings/enrolment → Store Info → Cash → Inventory → Reporting → Loyalty), plus theming.

### Phase 4 — MAUI till sync + fleet

> ✅ **UNPAUSED (2026-08-01).** `feature/maui-pos-rework` is confirmed as the upstream MAUI code the Phase 4 pause (2026-07-24) was waiting for. `WP4.1`–`4.3` (till outbox/pusher, heartbeat, fleet dashboard) already existed as target contracts describing the required behaviour and stand as originally written below, now unblocked for implementation. `WP4.0` and `WP4.4` onward are new work items covering branch reconciliation and the feature/UI parity build-out (Cash, Inventory, Reporting, Loyalty, Store Information, Settings, theming) identified as out of the original Phase 4 scope.

**WP4.0 — Branch reconciliation & shared contracts.** Merge `feature/maui-pos-rework` (183 commits behind) onto `Matt's-Horror`'s tip; retarget `Plutus.Frontend.AppClient`'s TFM to net8. Stand up `src/Plutus.Client.Core` (net8, no MAUI refs: outbox engine, pusher, sync cursors, heartbeat client, enrolment/token client). Note: a project literally named `Plutus.Contracts` already exists (`Plutus/Commons/Plutus.Contracts/Plutus.Contracts.csproj`, targets net10.0) — but it's the legacy repository-interface layer (`IItemRepository`, `ISaleRepository`, etc.), referencing `Plutus.Entities`, unrelated to DTO sharing. It must not be reused; pick a distinct name (e.g. `Plutus.Contracts.Client`) to avoid an assembly/namespace collision, then populate it against the OpenAPI spec per WP1.6's contract freeze. Target `IngestSaleRequest` (`src/Plutus.Sales/SalesIngestService.cs`) exactly: `SaleId, DeviceId, DeviceSeq, Channel(byte), BusinessDay(DateOnly), OccurredAtUtc, GrossPence, VatPence, Note, OperatorUserId, List<IngestLine>, List<IngestTender>` — `SalesV2Controller` derives tenant/device from the token only; a conflicting body `DeviceId` is a 403.
*DoD:* merged branch builds net8 across API + AppClient; a smoke `IngestSaleRequest` posted from `Plutus.Client.Core` round-trips 201 against a local `Matt's-Horror` instance; solution has no duplicate `Plutus.Contracts` name collision.

**WP4.1 — Till outbox + pusher (MAUI).** Local SQLite: sale write + outbox row in one transaction; background pusher drains in `deviceSeq` order, exponential backoff, survives app restarts; v1 contract via generated C# client from OpenAPI.
*DoD:* soak test — 1,000 sales offline, reconnect, all land exactly once in order; kill the app mid-drain, no loss/dupes.

**WP4.1b — VAT-rate-change ingest compliance (backend, extends WP1.4).** Confirmed requirement (Matt, 2026-08-01): a till offline across a government VAT-rate change may push a sale with a stale cached rate. Verify whether `Plutus.Entities`' tax model already stores VAT rates with an effective-from date (history), or only a flat current value — if the latter, add that history table first. Extend the existing WP1.4 VAT-integrity guardrail so `SalesIngestService` looks up the rate that was actually in effect at each incoming sale's `OccurredAtUtc` (not "is this *a* valid rate") and treats a mismatch the same way reconstructed/uncertain VAT is already handled elsewhere (flag into the quarantine/reconciliation path — mirroring the Kapow migration's own `VatReconstructed=1` pattern — rather than silently accepting or silently rewriting the customer-facing total).
*DoD:* a sale timestamped after a seeded rate-change boundary, submitted with the pre-change rate (simulating a till that was offline across the change), is flagged/quarantined rather than accepted as compliant; a sale correctly using the post-change rate ingests normally; a sale before the boundary using the pre-change rate also ingests normally (no false positives).

**WP4.2 — Heartbeat.** `POST /api/v1/heartbeat` per §10.2 (60 s from MAUI + web POS SW). Server: Redis (or in-memory + periodic persist behind an interface) storing lastSeen + payload; status derivation ONLINE/STALE/OFFLINE; heartbeat *response* carries pull signals (`catalogueVersion`, `syncNow`, `lock`).
*DoD:* status transitions at 2/5 min boundaries verified with a fake clock; MySQL receives no per-heartbeat writes; response signals honoured by the till client.

**WP4.3 — Fleet dashboard + alerting.** Portal screen: company → store → till with status, last seen, appVersion, outboxDepth, clock skew; server-side `deviceSeq` gap detection cross-check. Alerts (email/webhook) only during store opening hours. Compliance-monitor framework (D19) lands here: `Monitors(rule, tenantId, storeId?, thresholds JSON, channels)`; rule #1 = offline-during-trading-hours; rule #2 = overdue cash-up (activated in WP7.2).
*DoD:* till silent 6 min during opening hours → alert; same outside hours → none; "3 of N offline" rollup correct; monitor thresholds editable per tenant/store via API.

**WP4.4 — MAUI device enrolment (client-side).** Consume the existing flow verbatim: `POST /api/v1/tills/enrol {EnrolmentCode}` (`AllowAnonymous`, `enrol` rate-limit policy) → `EnrolResult{DeviceId, ClientSecret, TillId, TenantId}` or 410 Gone on a reused/expired/unknown code; `POST /api/v1/tokens/device {DeviceId, ClientSecret}` → `DeviceTokenResult{AccessToken, ExpiresInSeconds}` or 401 (both in `src/Plutus.Tenancy/Controllers`, backed by `EnrolmentService`). Replace the `DatabaseProvider.Cloud` throw in `Plutus.Frontend.AppClient/Helpers/Database/Database.cs` with a first-run screen: code entry → `EnrolAsync` → store `DeviceId/TillId/TenantId` in local Meta, `ClientSecret` in platform `SecureStorage` (never the SQLite file). Add a token client wrapping `/tokens/device` with refresh scheduled at `ExpiresInSeconds` minus a safety margin, plus 401-retry-once around the outbox pusher's calls (WP4.1).
*DoD:* fresh install with a valid code enrols, survives restart without re-prompting; expired/reused code surfaces the 410 as a retryable message, not a crash; app killed mid-refresh still has a valid token next launch; `ClientSecret` never appears in the `.db3` file (grep-verified).

**WP4.5 — Store Information (read-only rework).** Replace `StoreInformationViewModel.cs` (class `StoreInformationViewModel`, `Plutus.Frontend.AppClient/ViewModels/MainTill/StoreOptions/`) entirely: strip every local-write command (`StoreNameChangeCommand`, `StoreLogoChangeCommand`, `StoreContactNumberChangeCommand`, `VatINChangeCommand`, `StoreDefaultBagChangeCommand` — all currently mutate the local `StoreModel` via `Helpers.Database.Database`) and replace with a single load call to `GET /api/v1/stores/{id}/info` (`StoresController.GetInfo`, policy `sales.ingest`, works with the WP4.4 device token), binding its response (`storeId, name, businessName, vatNumber, adLine1, adLine2, city, postCode, country, contactNumber, openingHoursJson`) to display-only fields — mirror WebTill's `StoreInformationPage.tsx`/`OpeningHours` component, rendering the per-day `{"mon":[{"open","close"}],…}` JSON as a read-only weekly table. No PUT anywhere in MAUI; editing stays portal-only per WP6.1.
*DoD:* screen renders all fields incl. opening hours from a live API call with no local DB read/write; build has zero remaining references to `StoreModel`-mutating commands in this file; killing network shows a clear "unavailable" state, not stale local data.

**WP4.6 — Cash (till cash drawer).** New `CashPage`/`CashViewModel` in AppClient, replacing the current gap (only `POSCashDrawer.cs` hardware driver exists). Four actions — Open float, Paid in/out (reason mandatory), X snapshot, Z close — each POSTs `POST /api/v1/cash-events` (`CashController.Ingest`, `CashEventRequest`: `EventId` UUIDv7, `DeviceId`, `Type` string `OpenFloat|PaidIn|PaidOut|XSnapshot|ZClose`, `BusinessDay`, `OccurredAtUtc`, `AmountPence`, `CountedPence` (X/Z only), `Reason`, `OperatorUserId`) using the WP4.4 device token; render the 201/200 response (`eventId, tillId, businessDay, type, amountPence, countedPence, expectedPence, variancePence`) as expected/counted/variance. Client-side guard mirrors the server's: block a second Z for the same business day locally (server also 409s).
*DoD:* Z close after a Z close is blocked client-side before the call fires, and the corresponding replayed/duplicate case against the API returns 409 per `CashEventService.IngestAsync`; paid-in/out with blank reason is rejected client-side; variance shown matches `CountedPence − ExpectedPence` from the response.

**WP4.7a — Stock ledger rewire (MAUI).** Replace AppClient's flat `Item.Stock.Quantity` write in `ViewModels/MainTill/Inventory/Items/AddEditViewModel.cs` (`CreateUpdateStock()`) with calls to `POST /api/v1/stock/movements` (`StockController.cs`), body `{ stockLocationId?, storeId?, itemIdOne, type: Receipt|Adjustment|WriteOff, qty, reason }` — Adjustment/WriteOff require a non-empty reason (server 400s without one) and WriteOff must submit a negative qty (server 400s a positive one). `ViewAllViewModel.cs`'s quantity column becomes a `GET /api/v1/stock/levels` read (paginated, location-aware) instead of a local join; add a movement-history view backed by `GET /api/v1/stock/movements?itemIdOne=`. Port ClientUI's `IRepositoryWrapper`/`StockRepository` abstraction (already interface-based, closer to the server's ledger shape than AppClient's direct-EF `Stock` row) into AppClient as the HTTP client, extended with `PostMovementAsync` in place of `Create`/`Update` on a flat `Stock`.
*DoD:* creating an item with an opening quantity produces exactly one Receipt movement, never a bare stock row; adjusting without a reason is rejected client-side before the request is sent; two devices adjusting the same item concurrently both land as separate movements and `GET /api/v1/stock/levels` reflects the sum, never a last-write-wins overwrite.

**WP4.7b — Category management + VAT-band guard (MAUI).** Wire Inventory's category create (`AddEditViewModel.ExecuteCreateCategory`, currently local-only) to `POST /api/v1/categories`, and add rename/delete/reassign screens against `PUT /api/v1/categories/{id}`, `POST /api/v1/categories/{id}/reassign`, `DELETE /api/v1/categories/{id}` (`CategoriesController.cs`) — delete must surface the 409 "{n} item(s) are still in this category" / "last category" responses as a blocking reassign-first flow, since MAUI has no reassign UI today. Separately, mirror `ItemController`'s VAT-band guard (`|Price − ExPrice×Rate| > 0.02m` → 400, `isSync` writes exempt) client-side in `AddEditViewModel`/`AddEditInventoryViewModel` before submit, and surface the server's exact message on a 400 for cases the client missed (band changed server-side since last sync).
*DoD:* deleting a category with items attached shows the item count and blocks, offering reassign; reassigning then deleting succeeds; an item priced inconsistently with its tax band is rejected with the same "Price £x.xx does not match ex-VAT..." message the portal shows, both from client pre-check and from a forced server 400.

**WP4.8a — Reporting rewrite: core summary/VAT (MAUI).** Rip out `StatisticsViewModel`/`SalesReportsViewModel`'s local SQLite queries (`db.Get<SaleModel>()`, `PaymentMethod_SaleModel` joins, manual per-day/pay-method aggregation) and call the backend instead: `GET /api/v1/reports/summary-rich?from&to` (`ReportsController.SummaryRich`) for the till's Summary screen — returns `totalSales`, `totalSalesExTax`, `totalOrders`, `byDay[]`, `topItems[]`, `byPayMethod[]`, `byTaxRate[]`, already in pounds; `GET /api/v1/reports/summary?level&id&from&to&granularity` for the zero-filled bucket chart (`totals.grossPence/vatPence/txnCount/avgBasketPence`, `buckets[]`) replacing the hand-rolled `ChartSeriesCollection` build; `GET /api/v1/reports/vat?level&id&from&to&granularity` (bucketed by `vatRateBp`) for the VAT band table. All calls go through the WP4.4 device token's Bearer client (D20 — no direct DB), scoped implicitly by `tid` (D2). Local SQLite (`SaleModel`, `PaymentMethod_SaleModel`, `Transactions`) is no longer read for any report.
*DoD:* two tills with distinct `deviceId`/`tillId` each push one sale via the WP4.1 outbox; once both land, either till's Summary view for that `businessDay` shows the **combined** `totalSales`/`totalOrders`/`byDay` across both sales (not just its own prior local-SQLite rows) — the till is a thin client over `summary-rich`, not a second source of truth. VAT band totals shown match `/api/v1/reports/vat` bucket sums for the same range.

**WP4.8b — Reporting rewrite: drill-downs, best-sellers, category-sales (MAUI).** Replace `StockOuttakeViewModel`'s local `SaleModel.Transactions.Item` join with `GET /api/v1/reports/items-sold?from&to&storeId&tillId&operatorUserId&itemIdOne` (rows: `itemIdOne`, `itemName`, `category`, `qty`, `unitPricePence`, `discountPence`, `lineGrossPence`, resolved server-side incl. barcode→name→category); add two new screens MAUI has never had: `GET /api/v1/reports/category-sales` (`category`, `qty`, `grossPence`, `sharePct`) and `GET /api/v1/reports/best-sellers?by=qty|gross&take` (`rank`, `itemIdOne`, `itemName`, `category`, `qty`, `grossPence`, `sharePct`). Wire a sale-lookup flow onto `GET /api/v1/sales?from&to&tillId&take` (header list, capped 500) drilling into `GET /api/v1/sales/{saleId}` (full `lines[]`/`tenders[]`/`adjustments[]`, incl. cross-till sales) — this is the only path to another till's sale detail, since local SQLite never had it.
*DoD:* same two-till fixture as WP4.8a — `items-sold` and `best-sellers` totals on either till include **both** tills' lines for the shared `businessDay`; from till A, drilling into a `saleId` that was rung up on till B resolves via `/api/v1/sales/{saleId}` and renders identical lines/tenders/adjustments as viewed from till B.

**WP4.9a — Loyalty screens (MAUI).** Customer search/attach control on the till sale screen (`GET /api/v1/customers?search=&take=`, tap a hit → live lookup via `GET /api/v1/customers/{id}` for `creditBalancePence` + `membership{tier,autoDiscountRate,renewalDay,expired}`). Create/edit dialog posting `CustomerBody{Name,Email,Phone}` to `POST/PUT /api/v1/customers[/{id}]`, gated on `perm:CustomersManage`. Store-credit tender row in the checkout screen, mirroring WebTill's `CheckoutDialog.tsx` synthetic `CREDIT_PAYID` pattern: only offered when a customer is attached, `creditBalancePence > 0`, **and** the device is online; posts `POST /api/v1/customers/{id}/credit/redeem` with `CreditBody{AmountPence,Reason,SaleId,EntryId}` using a client-minted UUIDv7 `EntryId` as the idempotency anchor, surfacing the server's `InsufficientCreditException` 400 exactly as WebTill does. Loyalty management list page (gated on `perm:CustomersManage`): `GET /api/v1/loyalty?search=&take=` grid (name, tier, autoDiscountRate, renewalDay, expired, creditBalancePence) with row actions to `POST .../membership` (`MembershipBody{Tier,AutoDiscountRate,StartDay,RenewalDay}`) and `POST .../credit/issue`.
*DoD:* attach a customer mid-sale, redeem partial credit alongside a card tender in the same checkout — sale posts once with the correct `EntryId`; revoking `CustomersManage` hides create/edit/issue/membership controls but leaves search/attach/redeem intact.

**WP4.9b — Offline cache + redemption gate (MAUI).** Background job refreshes a bounded local SQLite `LoyaltyCache` (CustomerId, Name, Tier, AutoDiscountRate, CreditBalancePenceAsOf, RefreshedAtUtc) periodically from `GET /api/v1/loyalty`, capped at a configured row count. Used **only** for offline name/tier/discount lookup and a receipt-preview discount hint — never as an input to redemption math. The store-credit tender's eligibility is computed from the live per-sale check, not the cache: replicate WebTill's exact three-part gate (customer attached, live `creditBalancePence > 0` from `GET /api/v1/customers/{id}` this session, device online) — any session without a successful live fetch treats the customer as if offline and hides the tender, same as WebTill's `navigator.onLine` check.
*DoD:* airplane-mode test — cached row still shows correct name/tier/discount hint, but the store-credit tender option is absent; reconnect → live balance fetched → tender reappears with the correct balance; two devices race to redeem the last remaining credit for one customer → exactly one `POST .../credit/redeem` succeeds, the other gets the 400 `InsufficientCreditException`, confirming D15's ledger-sum invariant (never a mutable balance) prevents double-spend under concurrency.

**WP4.10 — UI/visual theming parity (AppClient).** `Plutus.Frontend.AppClient/App.xaml` (net10.0, `feature/maui-pos-rework`) has zero `ResourceDictionary`/`MergedDictionaries` today — only a `MaterialIconGlyphConverter`. Port `Plutus.Frontend.ClientUI/Resources/Styles/Colors.xaml` verbatim into AppClient: `Primary #272643`, `Secondary #ffffff`, `Tertiary #e3f6f5`, `Quaternary #bae8e8`, `Quinary #2c698d`, `White`/`Black`, `Error #FF9494`, plus the `Cyan100/200/300Accent` and `Blue100/200/300Accent` scale and matching `*Brush` `SolidColorBrush` keys — same `x:Key` names so existing bindings resolve unchanged. Author a new `Styles.xaml` (Button/Entry/Label/Frame implicit styles) referencing these brushes, and merge both into `App.xaml.Resources.MergedDictionaries`. AppClient's `.csproj` pins `Syncfusion.Maui.Core`/`Inputs`/`ListView`/`Picker`/`Popup`/`Calendar`/`Charts`/`SunburstChart` at `34.1.32` — theme the suite via that version's `Syncfusion.Maui.Themes.SyncfusionThemeResourceDictionary` (merged dictionary, `VisualStyle="Material"`), remapping its palette slots to the brushes above rather than Syncfusion defaults.
*DoD:* every AppClient screen (Inventory/Statistics/Settings/StoreOptions/Till) renders using the ported palette with no hard-coded hex left in XAML; a Syncfusion control (e.g. `SfListView`) visibly reflects `Primary`/`Quinary`, verified side-by-side against ClientUI's existing look.

**WP4.11 — Operator RBAC sync.** *(Naming note: "operator" here means a till employee/cashier — unrelated to the separate "platform operator" concept in `Build/operator-portal-plan.md`/`plutus-operator-platform-plan.md` and `OperatorBoundaryMiddleware`, which governs Matt-as-SaaS-provider's access to client tenant data. The two are unconnected; worth a less overloaded name if this WP is renamed later.)* No till-scoped operator/role-listing endpoint exists yet on `Matt's-Horror` — `src/Plutus.Tenancy/Controllers/TillsController.cs` has no `GET /api/v1/tills/{id}/operators`; the only related surface is `GET /api/v1/users/{id}/effective-permissions` in `src/Plutus.Identity/UsersController.cs`, backed by `EffectivePermissionsService` (Tenant→Company→Store→Till scope-chain union, `EffectivePermission(Code, MaxPence)`, unlimited beats ceiling, highest ceiling wins per D5-style append semantics) over the `PermissionCatalogue` in `Plutus.SharedKernel/Permissions.cs`. This WP must add the missing endpoint (new, additive, `/api/v1/tills/{id}/operators` under `PlutusPolicies.PortalTillsEnrol`) returning each operator's user id + effective permission set for that till's scope chain, then have MAUI pull and cache it per device. Verify login locally using `Plutus.SharedKernel.Pbkdf2` (`Rfc2898DeriveBytes.Pbkdf2`, SHA-1, 101,010 iterations, 64-byte hash, 32-byte salt, `Verify` via `CryptographicOperations.FixedTimeEquals`) — the same static class Tenancy already uses for device secrets (`ClientSecret`), kept identical so offline verification matches server-side hashing exactly.
*DoD:* new endpoint returns correct union-merged grants for a multi-level (company+store+till) assignment fixture; airplane-mode login succeeds against the last-synced hash/permission-set cache and denies a permission not in that cached set.
