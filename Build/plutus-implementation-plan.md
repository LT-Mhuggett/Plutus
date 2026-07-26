# Plutus Implementation Plan

**For execution by Claude Sonnet, one work package (WP) at a time.**
**Authority:** `plutus-platform-architecture.md` (v3) and `kapow-db-gap-analysis.md`. If this plan and the architecture doc conflict, the architecture doc wins; stop and flag the conflict.

---

## How to use this plan

- Execute WPs in order within a phase. Phases 2 and 4 are independent of each other; everything else follows the listed order.
- Each WP has a **Definition of Done (DoD)**. Do not start the next WP until the current DoD passes, including tests.
- Do not expand scope. If a WP reveals missing groundwork, stop and report rather than improvising schema or contract changes.
- Never invent API fields or rename contract properties — the OpenAPI spec (WP1.6) is the single source of truth after it exists.

## Global engineering rules (apply to every WP)

1. **Isolation rule (D20):** the backend never serves frontend assets, never renders HTML, and exposes only `/api/v1/*`. Frontends are static SPAs that call the API with a Bearer token — they never touch the DB, broker, or file system of the backend. No exceptions, including "temporary" ones.
2. **Tenancy (D2):** every tenant-owned entity has `TenantId` (GUID). EF Core global query filter on every such entity. `TenantId` always resolved from the JWT `tid` claim — never from request body/query. Every new table's indexes lead with `TenantId`.
3. **Money:** integer pence, `long`/`BIGINT` everywhere. No `decimal`/`double` for money in any layer. Formatting is a display concern (`Intl` in frontends).
4. **Time:** persist UTC `datetime(6)`. Device-local context travels as `businessDay` (date) + `occurredAt` (UTC). Never store zoneless local times.
5. **IDs:** UUIDv7 for new entity PKs (`BINARY(16)` in MySQL). Sales carry client-minted `saleId` (D4).
6. **Immutability (D5):** no UPDATE or DELETE on `Sales`, `SaleLines`, `SaleTenders`, `StockMovements`, `CreditEntries`. Corrections are new event rows. Enforce in code review and with DB permissions where practical.
7. **Minimal dependencies:** backend — no MassTransit (D8: broker-less start), no MediatR, no AutoMapper; plain ASP.NET Core + EF Core/Pomelo. Frontends — `react` + `react-dom` only; owned code instead of libraries; dev-time-only tooling is fine.
8. **Testing floor per WP:** unit tests for domain logic; integration tests (real MySQL via testcontainer or the dev instance) for anything touching EF; and the cross-cutting suites of WP1.7 must stay green.
9. **API versioning (D18):** everything under `/api/v1/`. Additive changes only within v1; breaking changes require `/api/v2` (expect never, this project).
10. **Observability:** structured logging (JSON) with `TenantId`, `DeviceId`, `SaleId` as scoped properties on every request; OpenTelemetry traces on ingest and consumers.

## Solution layout (target)

```
Plutus.sln
├─ src/
│  ├─ Plutus.Api/                 (single deployable host; module wiring, auth, versioned routes)
│  ├─ Plutus.Identity/            ├─ Plutus.Tenancy/        ├─ Plutus.Sales/
│  ├─ Plutus.Catalogue/           ├─ Plutus.Pricing/        ├─ Plutus.Stock/
│  ├─ Plutus.Cash/                ├─ Plutus.Customers/      ├─ Plutus.Reporting/
│  ├─ Plutus.Fleet/               ├─ Plutus.Integrations.Woo/  (add-on, isolated)
│  ├─ Plutus.Payments/            (add-on, isolated)
│  └─ Plutus.SharedKernel/        (TenantId plumbing, UUIDv7, pence type, outbox, event bus abstraction)
├─ tools/Plutus.SeedMigrator/     (extended: Kapow import + tenant provisioning backfill)
├─ tests/                          (per-module unit + integration; cross-cutting suites)
└─ frontends/                      (separate repos or top-level dirs; never referenced by backend)
   ├─ plutus-pos-web/              (existing React 19 web POS)
   └─ plutus-portal/               (new React management portal)
```

Modules communicate in-process via the `SharedKernel` event bus abstraction (interface now, MySQL-outbox implementation per D8, broker implementation later). Modules never reference each other's internals — events and public module interfaces only.

---

## Phase 0 — Baseline

**WP0.1 — .NET 8 upgrade.** Retarget `Plutus.DBService` to .NET 8 LTS; bump EF Core + Pomelo to matching versions; fix breaks; self-contained publish for the Mac still works.
*DoD:* solution builds on .NET 8; existing endpoints respond identically (snapshot-test Sale/Summary, Sale/Detail, ItemParameters.Search responses before/after); deployed to the Mac mini staging.

**WP0.2 — Solution restructure.** Create the module project layout above; move existing code (`Auth` → Identity, `Sale/*` → Sales, `ItemParameters` → Catalogue) without behaviour change; introduce `SharedKernel` (pence `Money` type, UUIDv7 generator, `ITenantContext`, `IEventBus` interface + no-op impl).
*DoD:* builds + all existing tests pass; no endpoint contract changed; module reference rules enforced (no cross-module project references except SharedKernel — verify with an architecture test).

**WP0.3 — CI + OpenAPI.** CI pipeline (build, test, publish artifacts); Swashbuckle generates `openapi.json` as a build artifact; a `frontends/` codegen script produces TypeScript types from it (dev-time only).
*DoD:* CI green; `openapi.json` committed/artifacted; generated TS types compile in the web POS repo without being imported yet.

---

## Phase 1 — Foundations (multi-tenant core + ingest pipeline)

**WP1.1 — Tenancy schema.** Add `Tenants` (id, name, status, plan, entitlements JSON, connectionRef), `Companies`, `Stores`, `Tills` (deviceId, credentialHash, storeId, status, lastSeenSeq). Add `TenantId` to all existing tenant-owned tables with composite indexes. Global query filters + `ITenantContext` populated from JWT `tid`.
*DoD:* migration applies to a copy of staging DB; every query in the app is filter-covered (integration test: seed two tenants, assert zero cross-reads on every repository method).

**WP1.2 — Provisioning API.** `POST /api/v1/tenants` (platform-admin only): creates tenant, first admin user, default Company + Store. `POST /api/v1/tills` + one-time enrolment code flow; `POST /api/v1/tills/enrol` redeems code → device credential (per-device client-credentials, token carries `tid` + `deviceId`).
*DoD:* end-to-end test: provision tenant → enrol till → till token authenticates against a protected endpoint; enrolment code single-use and expiring; revocation endpoint kills the device token.

**WP1.3 — Sales schema v2.** New `Sales`/`SaleLines`/`SaleTenders`/`SaleAdjustments` per architecture §4.1/§5: UUID PKs, `LegacyRef`, `DeviceId`, `DeviceSeq`, `BusinessDay`, `OccurredAt`, `ReceivedAt`, pence columns, per-line `VatRate`+`VatAmountPence`, `OverriddenFromPence`, line discounts. Unique index `(TenantId, SaleId)`.
*DoD:* migration + EF model; property-based test: any generated sale round-trips with `SUM(lines+vat) == totals` invariant enforced.

**WP1.4 — Idempotent ingest endpoint.** `POST /api/v1/sales` per contract §4.1. Duplicate `(TenantId, SaleId)` → `200` + original result. VAT-integrity validation (port existing guardrail); failures → `SaleQuarantine` table + `202` response (accepted, held). Transactional outbox row written in the same transaction (`OutboxEvents`: id, type, payload, createdAt).
*DoD:* tests — duplicate POST returns identical body; concurrent duplicate POSTs (race) yield one row; quarantine path; outbox row present; ingest p95 < 100 ms locally at 50 rps.

**WP1.5 — Broker-less event dispatch (D8).** `OutboxEvents` polling dispatcher in-process: per-consumer cursor table (`ConsumerOffsets`), ordered delivery per `DeviceId`, at-least-once with consumer-side idempotency helper in SharedKernel. Consumers register against `IEventBus`.
*DoD:* two dummy consumers at different cursor positions both converge; kill/restart mid-batch loses nothing and re-delivers safely; lag metric exposed.

**WP1.6 — Contract freeze.** OpenAPI spec reviewed against architecture §4.1; TS types regenerated; spec version tagged. From here, the spec is authoritative.
*DoD:* spec diff empty against architecture contract; CI fails on uncommitted spec drift.

**WP1.7 — Cross-cutting test suites (permanent).**
(a) *Tenant isolation:* for every API endpoint, authenticated as tenant A, attempt to read/mutate tenant B's data → 404/empty, never 403-with-existence-leak.
(b) *Money reconciliation:* random sale generator asserting pence invariants through ingest → DB → summary projection.
(c) *Idempotency:* replay harness re-POSTing captured traffic → zero duplicates.
*DoD:* suites run in CI on every commit; documented as untouchable.

**WP1.8 — Kapow migration (SeedMigrator v2).** Implement gap-analysis §5 order: tenant "Kapow" + hierarchy adoption; item UUID minting + `Barcodes` split + department keys; prices → `PriceList` (policy CENTRAL); sales/lines/tenders/refunds with pence conversion + per-sale `SUM(lines)=Total` validation → quarantine mismatches; VAT backfill flagged `VatReconstructed=1`; UTC conversion + `BusinessDay`; employee (minus dropped PII columns); `AuthActions` → role seeds; stock counters archived, not migrated.
*DoD:* reconciliation report (sale count, gross, VAT per year: SQLite vs MySQL) — differences zero or itemised in quarantine; runs idempotently (re-run = no-op); parameterised by tenant for future client onboarding.

---

## Phase 2 — Web POS onto the pipeline

> ✅ **COMPLETE & LIVE (2026-07-24)** — see HANDOVER.md §5 for the full record. Delivered with two
> documented additions: (a) the live `plutus` DB was graduated to the Phase-1 schema (Matt's
> decision; backup + idempotent script, legacy data byte-identical); (b) a TRANSITIONAL
> server-side `LegacySaleBridgeConsumer` (Plutus.Reporting, on the outbox dispatcher) projects
> each `SaleRecorded` into the legacy tables + stock so the existing reports keep working until
> WP3.3 rollups replace the legacy read model — the client itself has exactly one write path.
> Interim contracts to retire at WP3.3: projection metadata in `SaleLine.DiscountsJson`, legacy
> payId in `SaleTender.ProviderRef`, returns as negative-qty lines. Item ids are deterministic
> (SharedKernel.DeterministicGuid ⇔ pipeline.ts itemGuid).

**WP2.1 — Outbox retarget.** Point the web POS IndexedDB checkout outbox at `POST /api/v1/sales` with the v1 contract (UUIDv7 `saleId`, `deviceId` per enrolled web device, `deviceSeq` from a local monotonic counter, generated TS types). Remove any legacy direct-write path.
*DoD:* offline checkout → reconnect → sale lands once (dedupe verified); UI unchanged; no new runtime dependencies. ✅ *(checkout is outbox-FIRST; replay → 200 dedupe proven in the live smoke; permanent rejections park client-side so the queue never wedges)*

**WP2.2 — Web device enrolment.** Web POS device registers via the WP1.2 enrolment flow (admin generates code, browser stores device credential); Bearer flow unchanged for the operator.
*DoD:* two browsers = two deviceIds; revoking one blocks only that one. ✅ *(Settings → "Till device"; admins — legacy Admin/Management AuthAction → `portal.tills.enrol` scope at login — generate codes in-app; code reuse → 410 verified)*

---

## Phase 3 — Portal + Company view

> ✅ **COMPLETE & LIVE (2026-07-25)** — see HANDOVER.md §5 for the full record (commits
> `58a4b62`, `6447506`, `160628b`, `9e6294e`, `70e0461`). Deliveries match the WPs below with
> these notes: RBAC tables are `Rbac*`-prefixed (legacy `Role` survives evolve-in-place);
> opening hours live in server-only `StoreDetails`; rollups are till/day grain with
> query-time aggregation up the spine; period locks key on ReceivedAtUtc vs ClosedAtUtc so
> rebuild reproduces locked figures; the portal ships from
> `Plutus/Frontend/Plutus.Frontend.Portal` (admin.plutus Caddy vhost staged — one sudo step,
> HANDOVER §5). The Phase-2 legacy bridge still runs until the till UI reads /api/v1 reports.

**WP3.1 — RBAC.** `Permissions` (code-defined catalogue, portal + POS entries per §7.2), `Roles` (built-ins seeded: Owner, Company Admin, Store Manager, Supervisor, Cashier, Auditor), `RoleAssignments` (user, role, scope node, optional time window). Enforcement middleware: effective permissions = union at-or-above the resource node; time windows checked at token issue. Endpoint: `GET /api/v1/users/{id}/effective-permissions?scope=` (also consumed by tills for offline enforcement).
*DoD:* matrix test covering scope inheritance, till-scoped cashier can't see store financials, time-window expiry; Kapow `AuthActions` seeds map to POS permissions with amount ceilings (`pos.refund.max:{pence}`).

**WP3.2 — Admin APIs.** CRUD (versioned, permission-gated): companies, stores (incl. opening hours), tills (enrol/revoke per WP1.2), users, role assignments. All actions audit-logged.
*DoD:* isolation + RBAC suites pass over new endpoints; audit rows written.

**WP3.3 — Reporting projections.** `SaleRecorded` consumer folds into rollup tables: `SalesRollup(TenantId, CompanyId, StoreId, TillId, BusinessDay, grossPence, vatPence, txnCount, avgBasketPence)` + `VatRollup(per rate, per period)`. Rebuild command (replay from `Sales`). Query endpoints: `GET /api/v1/reports/summary?level=&id=&from=&to=&granularity=`, `GET /api/v1/reports/vat?...`, `GET /api/v1/sales/{saleId}` (full drill-down detail).
*DoD:* projections match direct SQL aggregation on the migrated Kapow data to the penny; rebuild-from-scratch equals incremental result; late-arriving sale (old `businessDay`) lands in the right day.

**WP3.4 — Financial periods.** `FinancialPeriods` with close action: snapshot rollups, lock period; post-close events auto-post to next open period and are flagged. CSV export endpoints.
*DoD:* closing a year then ingesting a late sale leaves the closed year's figures byte-identical; export totals match rollups.

**WP3.5 — Portal frontend (new React app `plutus-portal`).** Same discipline as the web POS (React + react-dom only, one CSS file, hand-rolled SVG charts, generated TS types). Screens, in order: login; company dashboard (rollups + drill to transaction); VAT view; users & roles; stores/tills admin (enrolment codes); price editor (arrives WP5.4); fleet dashboard (arrives WP4.3).
*DoD:* deployed as static files behind Caddy on its own path/host; talks only to `/api/v1/*`; drill-down from year → single Kapow transaction works end-to-end.

---

## Phase 4 — MAUI till sync + fleet

> ⏸️ **PAUSED / ON HOLD (2026-07-24) — DO NOT BUILD YET.**
> New MAUI till code is coming from the **upstream repo** (`github.com/seank842/Plutus`) that will
> **replace** this work. Building WP4.1–4.3 (and the `Build/plutus-maui-build-spec.md` M0–M4) now
> would be thrown away. Hold until that code lands, then re-baseline this phase against it —
> the outbox/heartbeat/fleet *contracts* below still describe the target behaviour and remain the
> acceptance criteria, but the client-side implementation will build on the upstream MAUI code
> rather than a fresh port. The server-side pieces these depend on (`/api/v1/heartbeat`, fleet
> endpoints) can still be planned, but the MAUI client work is parked.
>
> _Server-side heartbeat/fleet endpoints (WP4.2/4.3 server halves) may be pulled forward
> independently if needed; the **MAUI client** (WP4.1 + the client halves) is what's on hold._

**WP4.1 — Till outbox + pusher (MAUI).** Local SQLite: sale write + outbox row in one transaction; background pusher drains in `deviceSeq` order, exponential backoff, survives app restarts; v1 contract via generated C# client from OpenAPI.
*DoD:* soak test — 1,000 sales offline, reconnect, all land exactly once in order; kill the app mid-drain, no loss/dupes.

**WP4.2 — Heartbeat.** `POST /api/v1/heartbeat` per §10.2 (60 s from MAUI + web POS SW). Server: Redis (or in-memory + periodic persist behind an interface) storing lastSeen + payload; status derivation ONLINE/STALE/OFFLINE; heartbeat *response* carries pull signals (`catalogueVersion`, `syncNow`, `lock`).
*DoD:* status transitions at 2/5 min boundaries verified with a fake clock; MySQL receives no per-heartbeat writes; response signals honoured by the till client.

**WP4.3 — Fleet dashboard + alerting.** Portal screen: company → store → till with status, last seen, appVersion, outboxDepth, clock skew; server-side `deviceSeq` gap detection cross-check. Alerts (email/webhook) only during store opening hours. Compliance-monitor framework (D19) lands here: `Monitors(rule, tenantId, storeId?, thresholds JSON, channels)`; rule #1 = offline-during-trading-hours; rule #2 = overdue cash-up (activated in WP7.2).
*DoD:* till silent 6 min during opening hours → alert; same outside hours → none; "3 of N offline" rollup correct; monitor thresholds editable per tenant/store via API.

---

## Phase 5 — Stock + pricing

> ✅ **COMPLETE & LIVE (2026-07-25)** — see HANDOVER.md §5 (commits `4e8d0f3`, `c0e87ff`, `8247ae9`, `c76a893`). All DoDs test-covered: level==ledger property, replay-idempotent sale consumer, in-transit no-double-count, scheduled-reprice boundary, 9-case policy matrix. Portal gained Stock + Prices tabs. Note: the web POS till still reads legacy prices — /api/v1/prices/effective adoption is a catalogue-sync follow-up.

**WP5.1 — Stock ledger.** `StockLocations` (STORE/WAREHOUSE), typed `StockMovements` (RECEIPT, TRANSFER_OUT/IN, SALE, RETURN, ADJUSTMENT, WRITE_OFF), materialised `StockLevels` maintained by a `SaleRecorded` consumer + direct movement APIs. Rebuild command.
*DoD:* level = ledger sum always (property test); sale consumer idempotent under replay.

**WP5.2 — Transfers + stock takes.** Paired movements with in-transit state; stock-take endpoint posting counted-vs-expected adjustments with reason codes. Portal screens: central view, per-store view, movements drill.
**WP5.3 — Goods-in.** `Suppliers`, `PurchaseOrders`, `POLines`; receiving posts RECEIPT movements (partials supported, cost captured).
**WP5.4 — Pricing.** `PriceLists` (effective-dated), `PriceOverrides`, `PricePolicy` (CENTRAL / CENTRAL_WITH_OVERRIDE / LOCAL) per §7.4; effective-price resolution endpoint consumed by till catalogue sync; audit on every change; portal price editor + variance view.
*DoD (5.2–5.4):* transfer never double-counts (in-transit test); scheduled price change activates at the boundary; policy matrix test (9 cases: 3 policies × HQ-change/store-override/force-reset).

---

## Phase 6 — WooCommerce connector (add-on)

> **Re-planned 2026-07-26 against the LIVE store.** Target is **kapow-comics.co.uk** — the shop's
> real production site on a **low-resource DreamHost VPS** (`ssh kapow`, WordPress 7.0.2 /
> WooCommerce 10.9.4 / PHP 8.2.30, wp-cli available; 739 published products, **648 (88%) carry
> SKUs that are barcodes** — same shape as Plutus `ItemIdOne`, so SKU⇔barcode is the item join).
> There is no test store; the live site is also under active development by the shop. Ground rules:
>
> 1. **Read-only by default.** Inbound (orders→Plutus) ships first and never writes to Woo.
>    Outbound writes (WP6.3) are OFF until Matt explicitly enables them, and even then dry-run first.
> 2. **Nothing installed on the Woo side — no plugin, deliberately.** Core Woo REST API + core
>    webhooks cover everything this phase needs: orders, products, stock, webhook management, and
>    key issuance via the built-in `/wc-auth/v1/authorize` browser flow (how future tenants'
>    stores onboard without SSH — Matt's 2026-07-26 plugin assumption addressed: not required).
>    A companion plugin would need maintaining against every WP/Woo upgrade on a live,
>    low-resource site. Revisit ONLY if a concrete need appears that the core API can't meet.
> 3. **Be gentle with the VPS.** Webhooks (push, near-zero cost to Woo) are the primary transport;
>    polling is a slow reconciliation net, not the mechanism: small pages (≤25), `modified_after`
>    cursor, generous intervals (≥15 min; full sweep nightly off-peak), back-off on any 429/5xx/slow
>    response, and a per-tenant request budget logged so we can prove we're not the load.
> 4. **Develop against fixtures, not the live site.** WP6.0 captures real order/product JSON once;
>    unit/integration tests run on those fixtures. The live site is used for read-only smoke checks
>    and the final DoD proof only — never as a dev loop.
> 5. Two keys, minted via wp-cli: a **`read`-permission REST key now**; a separate **`write` key
>    only when WP6.3 is approved** (so a leaked/buggy inbound path physically cannot write).
>    UpdraftPlus backups exist on the site; confirm one is fresh before any write phase.

**WP6.0 — Live-site recon, fixtures, read-only key (NEW).** ✅ **DONE (2026-07-26).**
Mint the `read` REST key via wp-cli (recorded in HANDOVER secrets, never committed); capture
fixtures: ~20 real orders (incl. a refund, a multi-line, a discounted line, guest + account
customer), product pages, and a webhook sample payload. Audit item matching: how many of the 648
Woo SKUs match a Plutus `Items.IdOne`? Produce the unmatched list (+ the 91 SKU-less products)
as the seed for WP6.2's mapping table. Note VAT shape (Woo tax lines → per-line `VatRate`) and
currency (site runs *price-based-on-countries* — decide GBP-only ingest, quarantine the rest).
*DoD:* fixtures committed (PII scrubbed); SKU match-rate report; read key proven with a paged
`orders?modified_after=` pull that stays inside the request budget.
> **Delivered:** read-only key minted (`Build/secrets.local.md`, gitignored); **10 PII-scrubbed
> fixtures** in `tests/Fixtures/Woo/` (6 orders incl. guest + 2 refunded, 2 refunds, 2 products)
> + README of structural facts; **SKU audit** `Build/woo-sku-audit-2026-07-26.md` — **596/648
> published SKUs (92.0%) match** a Plutus barcode, 52 unmatched + 91 SKU-less = 143 products
> seeding the WP6.2 queue. REST transport proven: full product sweep ≈2 min (75 pages), the
> incremental `modified_after` sweep is ~1 page/~1.6 s (near-free). **Findings that change later
> WPs:** (a) **HPOS is OFF** — orders in `wp_posts`, ignore the inert `wc_orders` table;
> (b) line `total`/`subtotal` are **net**, `total_tax` separate, order `total` gross — trust the
> fields, don't recompute (per-line `taxes[]` round a penny off); (c) **currency is GBP-only in
> practice** despite the price-based-on-countries plugin — no multi-currency orders seen, so
> GBP-ingest + quarantine-others holds; (d) the 26-digit composite SKUs + one whitespace SKU are
> a data-quality nudge for the shopkeeper.

**WP6.1 — Entitlements gate + connection config.**
Consumes the **WP11.7 `WebStoreDetails` shell** (name, URL, `StoreId` for stock fulfilment) —
one config surface, not two: WP11.7's card gains the connection fields (REST key ref, webhook
secret, enabled flag, oversell buffer). Secrets stored server-side only (config/user-secrets
pattern, never in the row). Entitlement check (`woo-connector`, already live from WP10.1)
enforced at the module boundary. Provision the **virtual webstore till/device** per connection
(the ingest path requires a `deviceId`/`tillId`; a webstore is `SaleChannel.WebStore` on its own
till so reports can slice channel × store cleanly).
- **One-click onboarding (Matt, 2026-07-26 — "user friendly, authenticate from within
  WordPress").** The portal's + Webstore card gets a **Connect** button driving WooCommerce's
  native **`/wc-auth/v1/authorize`** flow: browser → the store's own WordPress login (their
  existing wp-admin credentials — auth happens *inside* WordPress) → Woo's built-in approval
  screen ("Plutus would like read/write access — Approve/Deny") → Woo generates API keys and
  **POSTs them server-to-server to Plutus's HTTPS callback** (keys never shown on screen, nothing
  to copy) → browser returns to the portal, card shows Connected ✓. Plutus then auto-provisions:
  creates its webhooks via the API (this is why the flow requests `read_write` — webhook creation
  needs write; Plutus-side WP6.3 gates + kill switch govern actual writes), mints the
  per-connection webhook HMAC secret, runs the first product sweep. Card shows connection health:
  key valid, webhook delivery OK, last sweep time.
- **Fallback (manual keys):** wp-admin → WooCommerce → Settings → Advanced → REST API → create
  key → paste consumer key/secret into the card — for hosts whose security plugins break the
  redirect flow. **Kapow itself** stays on the stricter wp-cli-minted read-only key (rule 5)
  until WP6.3; it becomes the first live test of the one-click flow when outbound is approved.
*DoD:* unentitled tenant → clean 403 + portal upsell state; entitled tenant connects a store via
the wc-auth flow end-to-end without touching wp-admin settings (keys stored, webhooks created,
first sweep populated) AND via manual key paste; virtual till appears in Locations, excluded
from enrolment-code flows; disconnect revokes cleanly (webhooks deleted, secrets purged).

**WP6.2 — Inbound orders (read-only on Woo).**
Core-Woo **webhook receiver** (`order.created/updated`, `refund.created`; HMAC-SHA256
`X-WC-Webhook-Signature` verified against the per-connection secret) → map to a v1 sale
(`channel: WebStore`, **deterministic `saleId` from the Woo order id** via
SharedKernel.DeterministicGuid, so replays/duplicate webhook deliveries dedupe through the
existing idempotent ingest) → internal ingest call. Item lines join by SKU⇔`ItemIdOne`; misses
land in a **`WebstoreSkuMap`** review table (portal screen: bind SKU→item or ignore) and the
order quarantines until resolved. Refunds map to the platform's return shape. The **reconciliation
poll** (rule 3 cadence) sweeps `modified_after` ≥ cursor to catch dropped webhooks; cursor
persists so kill/restart resumes. Woo stock is NOT adjusted by inbound sales in this WP —
Plutus-side stock moves only (the webstore till's fulfilment `StoreId`).
- **Pick-from-floor notification (Matt, 2026-07-26).** A web sale sells stock that is physically
  on the shop floor — staff must be told to pull it. Each ingested web order raises a
  notification: *"Item X sold online — check if it needs removing from the shop floor"* delivered
  per connection config to **till pop-up** (the fulfilment store's tills; a
  `GET /api/v1/notifications` feed the web POS polls on its existing sync cadence, acknowledge
  to dismiss — acked-by/at audited) **and/or email** (per-connection address list) — both
  configurable on the WP11.7 card. Unacknowledged notifications persist across till restarts.
*DoD:* fixture suite green (multi-line, discount, refund, guest); duplicate webhook delivery →
one sale; a webhook outage window is fully healed by one poll pass; a live-site smoke ingest of
recent real orders matches Woo totals to the penny; request-budget log shows poll traffic within
limits; a web order pops the notification on the store's till within one sync interval and the
same order emails the configured address; ack on one till clears it on all.
> **Mapper core ✅ DONE (2026-07-26).** New isolated module `src/Plutus.Webstore` (references only
> SharedKernel + Entities — arch tests green, module stays off the core's reference graph):
> `WooOrderMapper.MapOrder` maps a Woo order → validated `SaleV2` (channel WebStore) through
> `SaleV2.Create`, so a mis-map **quarantines** rather than writing a wrong sale. Money model
> handled: Woo's NET line total + separate tax → platform VAT-**inclusive** unit/line prices
> (unit rounded up, residual+discount into `DiscountPence` so invariant-1 is exact); shipping/fees
> become non-catalogue lines (null `ItemIdOne`, excluded from items-sold) so Σ gross == order
> total == tender; **deterministic saleId** from the Woo order id (`DeterministicGuid.ForName`,
> new general overload) → re-delivered webhooks dedupe through the idempotent ingest; unknown SKU
> → `NeedsMapping` (WP6.2 queue) not a sale; non-GBP → quarantine; refund → `SaleAdjustment`.
> **8 new unit tests against the real scrubbed fixtures pass** (127 unit + 5 arch green).
>
> **Inbound decision pipeline ✅ DONE (2026-07-26).** `WooWebhookVerifier` (constant-time
> HMAC-SHA256 of the RAW body vs `X-WC-Webhook-Signature`; forged/garbled → rejected before any
> parsing) + `WebstoreWebhookProcessor` (verify → parse → map → route) over two connector-owned
> ports — `IWebstoreSaleSink` (idempotent submit; host adapts to `SalesIngestService`, so the
> connector never references the Sales module) and `IWebstoreSkuMapQueue` (review queue). Routes to
> Recorded / Duplicate / NeedsMapping / Quarantined / Rejected. **+6 unit tests (133 unit + 5 arch
> green).** Still to build: the HTTP webhook controller + the real sink adapter, the review
> screen, the reconciliation poll + cursor, and the pick-from-floor notification.
>
> **DB layer ✅ DONE (2026-07-26).** Two tenant-owned tables in the shared model: **`WebStores`**
> (WP6.1 connection config — name/url/enabled/storeId/virtual till+device/oversell buffer; secrets
> stay in server config keyed by Id, never on the row) and **`WebstoreSkuMaps`** (WP6.2 review
> queue; one row per tenant×webstore×SKU). Registered on `MySqlDbContext` (DbSets, `TenantOwned`,
> unique indexes). DB-backed **`CatalogueSkuResolver`** (connector module; depends only on the
> shared context) resolves SKU⇔`Items.IdOne` → the web-POS deterministic ItemId. **EF migration
> `AddWebstoreConnector` generated** (creates only the two tables + indexes — scope verified) but
> **NOT yet applied to any DB** — that's the rehearse-on-`plutus_t1`-then-`plutus` ops step.
> **+2 SQLite tests (persistence + tenant isolation + resolver); 135 unit + 5 arch green.**
>
> **Inbound wired end-to-end (in test) + connector-side DI complete ✅ DONE (2026-07-26).**
> Real `WebstoreSkuMapQueue` (upsert per tenant×webstore×SKU, bumps SeenCount, leaves Bound/Ignored
> alone); `WebstoreModule.AddPlutusWebstore` registers the resolver, queue, and processor (host
> supplies `IWebstoreSaleSink`). **Keystone e2e test:** a signed order webhook flows verify → map →
> the **real `SalesIngestService`** → SalesV2 + outbox, and a re-delivery dedupes on the
> deterministic saleId. `WebStoreId` threaded through the connection context. **137 unit + 5 arch
> green.** REMAINING host wiring (compile-only locally — needs the Mac MySQL to runtime-test): the
> `WebstoresController` (CRUD + `POST …/{id}/webhook` reading the raw body for HMAC), the host
> `WebstoreSaleSink` adapter over `SalesIngestService` (mirrors the e2e test's sink), `Startup`
> registration, and virtual-till provisioning. **Design note to resolve there:** the webhook is
> anonymous (HMAC-authed, tenant from the URL's webstore id), but `HttpTenantContext` derives the
> tenant from JWT claims (falls back to Kapow) and has no setter — so the handler must run under a
> **per-webhook tenant scope** (`FixedTenantContext(resolvedTenant)` / child DI scope) or the SKU
> resolver/queue would query the wrong tenant once multi-tenant. Then apply the migration
> (rehearse `plutus_t1` → `plutus`). **→ Resolved by the WP6.2a design below (2026-07-26).**

**WP6.2a — Anonymous-webhook security & tenant-scoping design (decided 2026-07-26).**

*The problem, precisely.* A Woo webhook delivery carries no JWT — its only credential is
`X-WC-Webhook-Signature` = base64(HMAC-SHA256(raw body, per-connection secret)). Our tenant
scoping (`HttpTenantContext`) reads `tid` from JWT claims and falls back to Kapow on anonymous
requests — so an unscoped webhook handler would run resolver/queue/ingest against the wrong
tenant the day a second tenant exists. Two sub-problems: (1) authenticate the caller; (2) run
the pipeline under the right tenant. Neither requires touching core auth plumbing.

*Decision — NO WordPress plugin (re-confirmed).* Matt asked again whether a small WP plugin
should carry authentication. Honest analysis: a plugin *could* inject extra headers into
webhook deliveries (`woocommerce_webhook_http_args` filter), e.g. a bearer token — but that is
just a second shared secret in a different pocket. HMAC over the raw body IS the industry
standard for webhook auth (Stripe `Stripe-Signature`, GitHub `X-Hub-Signature-256`, Woo — all
identical model): it proves knowledge of the secret AND payload integrity, which a bearer
header alone does not. And the actual hard part — mapping a delivery to a tenant scope — is
OUR internal plumbing; no plugin can solve it. A plugin would reintroduce exactly the burdens
rule 2 exists to avoid (per-site install, WP/Woo upgrade maintenance, friction for future
non-SSH tenants). Rule 2 stands; the revisit clause remains for needs the core API truly
cannot meet (none here).

*Design (host-side, ~3 small classes, no core changes):*
1. **Connection resolution.** `POST /api/v1/webstores/{id}/webhook` (`[AllowAnonymous]`,
   rate-limited). Look up `WebStores` by `{id}` with **`IgnoreQueryFilters()`** — the one and
   only unscoped read (same established pattern as ingest's idempotency re-read); the id is an
   unguessable UUIDv7 and HMAC still gates everything. Unknown id → 404; row disabled or
   `woo-connector` unentitled → 410 Gone (tells Woo to stop retrying).
2. **Secret resolution.** Connector-owned port `IWebstoreSecretProvider.GetWebhookSecret(id)`;
   host impl reads server config keyed by connection id (`Webstore:{id}:WebhookSecret` — the
   TEST_TOKEN_SECRET pattern; pm2 env / user-secrets on the Mac). Secret absent → 500 +
   error log (misconfiguration, never silent). Secrets never touch the DB row or the repo.
3. **Verify BEFORE parse.** Read the RAW body once (`StreamReader(Request.Body)`, as the
   billing webhook does); `WooWebhookVerifier.Verify` (constant-time, already built+tested);
   bad/missing signature → 401 with no side effects. Handle Woo's **activation ping**
   (form-encoded body `webhook_id=N`, sent when the webhook is created — and sent UNSIGNED by
   Woo, so it must be answered before signature checking, with zero side effects) → 200, no
   pipeline — webhook creation FAILS if the ping isn't 2xx, so this is required for WP6.1's
   auto-provisioning. *(Corrected during build 2026-07-26: the ping carries no signature.)*
4. **Per-delivery tenant scope.** Construct the pipeline against a tenant-fixed context:
   `new MySqlDbContext(sp.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
   new FixedTenantContext(row.TenantId))` — `FixedTenantContext` already exists in
   Plutus.Entities.Tenancy (what every unit test uses), and `AddDbContext<RepositoryContext,
   MySqlDbContext>` already registers the options in DI. Build `CatalogueSkuResolver`,
   `WebstoreSkuMapQueue`, the `WebstoreSaleSink` (over `SalesIngestService` on the SAME scoped
   context — mirrors the proven e2e test sink), and `WebstoreWebhookProcessor` on top. One
   host factory class (`WebstoreWebhookPipelineFactory`) owns this composition.
5. **Outcome → HTTP status, chosen for Woo's retry/auto-disable behaviour.** Woo retries
   non-2xx deliveries and **auto-disables a webhook after repeated failures** — so anything we
   have durably recorded or parked MUST return 2xx: Recorded → 200, Duplicate → 200,
   NeedsMapping → 202 (parked in review queue), Quarantined → 202 (parked in SaleQuarantine).
   Non-2xx is reserved for: 401 bad signature, 404/410 unknown/disabled, 500 genuine transient
   failure (where a Woo retry actually helps). Log `X-WC-Webhook-Delivery-ID` per delivery.
6. **Replay safety.** Woo signatures aren't timestamped; a captured delivery can be replayed —
   harmlessly: the deterministic saleId dedupes to a 200/Duplicate. No nonce store needed.

*Tests.* Unit (offline, fake ports): signature pass/fail/missing; ping → 200 without pipeline;
unknown id → 404; disabled/unentitled → 410; outcome→status table; tenant-scope proof — two
webstores under two tenants on one SQLite DB, a delivery to tenant B's URL writes rows ONLY
under tenant B (the multi-tenant regression this design exists to prevent). Integration (Mac
MySQL, after migration rehearsal): real webhook POST end-to-end.
> ✅ **BUILT & TESTED (2026-07-26).** Connector: `WebstoreWebhookHandler` (framework-free flow:
> lookup → ping → HMAC → tenant-fixed pipeline → outcome→HTTP, quarantines PARKED into
> SaleQuarantine idempotently by deterministic saleId) + `WebstoreWebhookPipelineFactory`
> (per-delivery `FixedTenantContext`); `WooOrderId` threaded through the inbound result. Host:
> `WebstoreWebhookController` (thin — raw body in, status out), `WebstoreIngestSink` (adapter
> over `SalesIngestService`), `ConfigWebstoreSecretProvider` (`Webstore:Secrets:{id}`), Startup
> registration + csproj ref. **10 new handler tests incl. the tenant-scope proof (delivery under
> a deliberately WRONG ambient tenant lands every row under the webstore's tenant, and the
> virtual Device row derives the sale's TillId) — 147 unit + 5 arch green; host builds.**
> Committed `45914a6` + pushed. **Migration APPLIED 2026-07-26** — rehearsed on `plutus_t1`
> (idempotency re-run = no-op) then live `plutus`; SalesV2 untouched (21,648); till/portal/ETRIE
> all 200.
>
> 🎉 **INBOUND LIVE (2026-07-26 evening, `cbd66fb`).** Backend deployed; Kapow connection
> provisioned (WebStores row + virtual till "Kapow Web" + device + `woo-connector` entitlement);
> webhook secret in pm2 ecosystem env; **order.created + order.updated webhooks ACTIVE on
> kapow-comics.co.uk**. E2E smoke from the DreamHost box over the public internet: real order
> #8505 → `recorded` 0.85 s, SalesV2 penny-exact (£1.50 zero-rated + £3.30 shipping = £4.80,
> PayPal ref, virtual till), re-delivery → `duplicate`, one row. Go-live fixes en route (all
> committed): paid-status gate (Skipped→200), DI lifetimes, form-encoded unsigned ping handling.
> Real web orders now flow into Plutus automatically. Remaining in WP6.2: review screen,
> reconciliation poll + cursor, pick-from-floor notification.
*DoD:* the tenant-scope proof test passes; a forged delivery leaves zero rows; Woo's activation
ping succeeds during WP6.1 auto-provisioning; quarantined/parked deliveries do NOT cause Woo to
disable the webhook (2xx verified); secret rotation = config change + webhook update, no deploy.

**WP6.3 — Outbound stock/price (WRITE — gated, off by default).**
**Stock immediacy is the point of this WP (Matt, 2026-07-26):** a comic shop holds single-copy
items — an in-store sale must reach the site *near-immediately* or the sold copy stays buyable
online. Two lanes on the `StockLevelChanged`/`ItemUpdated` consumer:
- **Fast lane — sale/return-driven stock changes:** push per-item as soon as the outbox delivers
  the event (coalesced per item, no batching delay). **Target: till sale → Woo stock updated
  p95 ≤ 60 s**, and a level hitting 0 marks the product out-of-stock in the same call. A few
  dozen single-PUT calls a day is negligible VPS load — immediacy and gentleness don't conflict
  here.
- **Slow lane — bulk operations** (stock takes, goods-in, price-list changes, reprices): Woo REST
  **batch** endpoint, debounced (coalesce per item; flush ≤1 batch/5 min, ≤100 items/batch — the
  VPS budget, not ours). Prices stay slow-lane always: web prices may legitimately differ (see
  WP6.4), so price pushes apply only to items explicitly marked web-price-follows-Plutus.
Per-tenant **oversell buffer** (list `max(0, level − buffer)`) and a hard **kill switch**
(connection flag, checked per push) cover both lanes. Ships behind THREE gates: Matt's explicit
go-ahead, the separate `write` REST key minted only then, and a **dry-run mode that logs exactly
what it would send** — dry-run runs in production for ≥ a week of real trading before the first
live write. First live write is a single agreed item, verified on the storefront, before the
consumer is opened up.
*DoD:* dry-run log matches expected deltas over a real trading week; sell the last unit of an
item in store → storefront shows out-of-stock within 60 s; kill switch halts mid-stream cleanly;
bulk stock take flows through the slow lane within the debounce window without starving the fast
lane; oversell buffer respected; write key absent → outbound refuses to start (fails safe).

**WP6.4 — Webstore catalogue view + alignment report (Matt, 2026-07-26).**
Both read the same **`WebstoreProducts` cache** — Plutus's copy of the webstore catalogue,
maintained by the reconciliation poll's product sweep. **Cadence:** incremental
(`modified_after` cursor — usually zero pages) every **15–30 min**; **full sweep nightly
off-peak** (the only pass that can detect webstore-side *deletions*). Nothing queries the live
site at render time; every screen shows "last refreshed". **Manual "Refresh now" button** on
both screens for the just-edited-it-in-wp-admin case: triggers the *incremental* sweep only,
rate-limited (min 5 min between manual runs per connection, counted in the request budget),
button shows in-progress state and the resulting new timestamp.
- **Webstore catalogue view (portal):** a browsable list of *what is on the webstore* — name,
  SKU, web price, web stock status, Woo status (published/draft — WP6.5's drafts visible here),
  and the linked Plutus item (or "unlinked"). Sortable/filterable (published/draft, in/out of
  stock, linked/unlinked), paged, with the show-25/50/100 limit pattern from the Stock report.
  Lives under the WP11.7 webstore card + a link from Stock. Product images stay on the webstore
  (hotlinking thumbnails would put image traffic on the VPS) — link out to the product page
  instead.
- **Alignment report:** the comparison lens over the same cache, joined SKU⇔`ItemIdOne`:
  **name drift** (both names shown), **prices on BOTH platforms side-by-side** — differing
  prices are legitimate (web ≠ shelf), so this is *display*, not an error, with an optional
  variance filter — **missing-on-either-side** lists (Woo products with no Plutus item incl.
  the ~91 SKU-less; Plutus items not on the web), and stock disagreement once WP6.3 is live.
  CSV export. WP6.0's one-off audit is the seed that becomes this report.
*DoD:* the catalogue view lists every product the sweep saw with correct status/link flags and
paging; renaming a product on either side surfaces in the next sweep; a price difference
displays both values without flagging an error; the missing-on-either-side counts reconcile
with WP6.0's audit; neither screen causes any live Woo request at render.

**WP6.5 — Two-way item creation, webstore side as DRAFT (Matt, 2026-07-26).**
Creating an item on either platform creates its counterpart on the other — **asymmetrically**,
because the till needs less than the webstore (e.g. images: not needed on the till, required
for the web):
- **Plutus → Woo:** new Plutus item (with barcode) → Woo product created in **`draft` status**
  (name, SKU=barcode, price as the default web price) — never published by Plutus. A human adds
  images/description/categories and publishes from wp-admin. Portal shows "draft awaiting
  publish" on the alignment report (WP6.4). Requires the WP6.3 write key + gates; per-connection
  toggle, default OFF until enabled.
- **Woo → Plutus:** new published Woo product (from the reconciliation poll's product sweep) with
  an unknown SKU → lands in the WP6.2 `WebstoreSkuMap` review queue with a one-click **"create as
  new Plutus item"** (name, barcode=SKU, web price as starting price) alongside the existing
  bind/ignore actions — reviewed, not silent, so a typo'd SKU on the web can't mint a phantom
  till item.
*DoD:* new Plutus item → draft Woo product with matching SKU, invisible on the storefront until
manually published; new Woo product → appears in the review queue and one click creates the till
item; neither direction ever auto-publishes to shoppers; toggles independently disableable.

*DoD (phase):* runs against the LIVE store within the request budget; kills/restarts resume from
cursor; unentitled tenant gets clean 403 + portal upsell; core platform has **zero** references
to the connector (architecture test); site performance unchanged for shoppers (spot-check
storefront latency before/after enabling the connector); the oversell scenario — last copy sold
in store while in a shopper's web basket — is demonstrated blocked at checkout.

---

## Phase 7 — Payments + cash (can pull forward after Phase 1)

> 🟡 **WP7.2 cash sessions COMPLETE & LIVE (2026-07-25, `67b344e`)** — X/Z with server-computed expected+variance, one-Z-per-day, banking view; till Cash tab + portal Banking tab. **WP7.1 payments = provider-agnostic SEAM ONLY** (capture events + orphaned-payment reconciliation queue live; the first concrete adapter is BLOCKED on the commercial provider choice). The cash-up overdue monitor is deferred (needs the paused WP4.3 fleet framework). See HANDOVER §5.

**WP7.1 — Payment provider adapter.** `IPaymentProvider` (authorise/capture/refund/settlement-report); first concrete provider per commercial choice; terminal ref written into pending sale pre-capture (D13); unresolved-payments queue + portal review screen; daily settlement reconciliation job.
**WP7.2 — Cash sessions.** `CashSessions` as events through ingest (float, paid-in/out, X snapshot, Z close — one Z per business day enforced at till); portal banking view; activate overdue-cash-up monitor (defaults 3/7/14 trading days, per-tenant/store config) in the WP4.3 framework.
*DoD:* orphaned-payment simulation (capture success, sale POST dropped) surfaces in the queue; Z-report day matches rollups to the penny; cash-up monitor fires per config, respects opening hours.

---

## Phase 8 — Customers, credit, loyalty

> 2705 **COMPLETE & LIVE (2026-07-25, `f4ebfeb`)** — Customer + append-only CreditEntry ledger (balance = 03a3 entries, overdraw-guarded, idempotent redeem) + renewal-dated Membership auto-discount; period close records the outstanding-credit liability; portal Customers tab. DoD tests: balance==sum property, issue2192redeem, no-overdraw, liability==entry-sum. Follow-up: wire credit-as-tender + members auto-discount into the checkout basket (endpoints live). See HANDOVER 00a75.

`Customers` (optional on sale, synced to tills), `CreditAccounts` + append-only `CreditEntries` (D15), credit-redemption tender type, `Memberships` with renewal dates feeding auto-discounts. Outstanding-credit figure on period close.
*DoD:* credit issued at till redeemable in web POS after sync; balance = entry sum (property test); period close includes liability line.

## Phase 9 — IdP swap

> ✅ **CODE COMPLETE & LIVE (2026-07-25)** — see HANDOVER.md. Seam + both providers built
> full-stack; Keycloak runs on the Mac (Docker `plutus-keycloak`, 127.0.0.1:8089) with the realm
> export committed. **Default stays `IdP:Provider=test`** so live behaviour is unchanged until
> flipped. Remaining: the `login.plutus` Caddy vhost is staged for **Matt's sudo**; Entra is
> config-ready only (WP9.3 — needs Matt's Azure tenant, no live proof possible).
>
> **Decision (2026-07-25, Matt):** implement **BOTH** Entra External ID **and** Keycloak,
> switchable by config; go full-stack (backend + both React frontends); map an IdP identity
> to a Plutus user **by verified email**. Order: 9.1 seam → 9.2 live Keycloak → 9.3 Entra
> config-ready → 9.4 frontends → 9.5 tests.

**Design invariant — authorization is untouched.** Today the login endpoint already resolves a
user's scopes from RBAC and bakes them into the token; every policy (`perm:*`, `platform-admin`,
the legacy `[RequiredScope]` `scp` filters) reads a fixed claim set. Phase 9 keeps that claim set
identical. A real IdP only **authenticates** (proves identity via `sub`/`email`); a shared
claims-transformation resolves the same RBAC scopes server-side and injects the same claims the
HMAC handler emits today. So swapping IdP changes the token *source*, never the API contract.

**WP9.1 — Provider-agnostic auth seam (backend).**
Replace the implicit "B2C unless `DISABLE_AUTH_DEV_ONLY`" branch with one explicit selector
**`IdP:Provider` ∈ {`test`, `entra`, `keycloak`}** (default `test` — nothing changes until flipped).
- `test` → today's `PlutusTokenAuthHandler` (HMAC) + `/api/Auth/Login`, unchanged.
- `entra` / `keycloak` → metadata-driven `AddJwtBearer` (Authority + Audience from config; JWKS,
  issuer auto-discovered). Both are standard OIDC; the only per-provider differences (authority
  shape, audience, native scope/role claim) live in a small `IIdpProfile` (entra: `oid`/`scp`;
  keycloak: `sub`/`realm_access.roles`).
- **Device/till tokens stay independent.** A default **policy-scheme** inspects the bearer and
  forwards our compact HMAC device tokens (`did`/`tid`/`scope:device`) to the HMAC handler and
  3-part JWTs to the configured IdP — so **client-credentials/enrolment are unaffected** by the
  swap (DoD).
- Shared **`RbacClaimsTransformation`** (runs for `entra`/`keycloak`): read verified email from
  the validated token → find the Plutus user (`People`/`WebCredentials`, by email) → resolve
  effective RBAC scopes (same logic as `AuthController.Login`) → add `NameIdentifier`,
  `objectidentifier`, `scope` claims, and the `scp` API scopes. Unknown email → authenticated but
  unauthorized (no scopes) + audit. Provider-agnostic, so `entra` and `keycloak` behave identically.
*DoD:* `IdP:Provider=keycloak` validates a Keycloak JWT and the caller gets their RBAC scopes; a
till device token still authenticates under every provider; `test` is byte-for-byte today's behaviour.

**WP9.2 — Keycloak live on the test env.**
Stand up Keycloak as a user process on the Mac (own high port + pm2, ETRIE untouched), realm
`plutus`, an SPA public client per frontend (auth-code + PKCE) and the API audience. Seed a test
user whose email matches a Plutus user. **Realm export committed** (`ops/keycloak/plutus-realm.json`)
so it's reproducible. Caddy vhost (`login.plutus.huggett.dscloud.me`) **staged for Matt's sudo**.
*DoD:* browser logs in via Keycloak → portal loads with the user's real permissions; kill/restart
Keycloak, login still works from its persisted realm.

**WP9.3 — Entra External ID config-ready.**
The code path is already live from 9.1; this WP is the config templates + runbook (tenant, user-flow,
app registrations, API scope exposure, redirect URIs) so a flip to `IdP:Provider=entra` works once
Matt provisions the Azure tenant. No live proof possible without his tenant — documented as such.

**WP9.4 — Frontend OIDC (portal + web POS).**
Provider-agnostic SPA login: auth-code **+ PKCE** against `OIDC:Authority`/`OIDC:ClientId` (config
per deployment), **access token in memory + refresh via cookie** (plan). Silent renew; logout hits
the IdP end-session endpoint. Under `IdP:Provider=test` the existing email/password login stays the
default, so the test env keeps working with no IdP. Device enrolment on the till is unchanged.
*DoD:* both apps complete a real Keycloak login; access token never touches localStorage; refresh
survives a reload; `test` mode still logs in with a password.

**WP9.5 — Tests + DoD.**
Unit: selector picks the right scheme per `IdP:Provider`; `RbacClaimsTransformation` maps
email→scopes and denies unknown emails; a self-signed JWT validates through the JwtBearer path
(fake issuer). Integration: device token authenticates under `keycloak`. Live: end-to-end browser
login through the stood-up Keycloak. Arch test still green (no module→module leak).
*DoD (phase):* flag flip swaps IdP with **zero API contract change**; till client-credentials
unaffected; `openapi.json` unchanged by the swap.

## Phase 10 — Platform billing & offboarding

> ✅ **COMPLETE & LIVE (2026-07-25)** — see HANDOVER.md. Entitlements + `IBillingProvider` seam
> (`NullBillingProvider`, HMAC webhook), tenant lifecycle (Suspended = portal refused, tills keep
> syncing — D16), tenant export ZIP, `DeletionSchedule` + `RetentionSweeper`. **The concrete
> Stripe adapter remains DEFERRED** on Matt's billing-provider choice (same pattern as WP7.1).
>
> **Started 2026-07-25 (Matt).** Build the buildable halves now; the Stripe concrete adapter stays
> a seam until Matt picks a billing provider (same pattern as WP7.1 payments). Order: 10.1 → 10.2 →
> 10.3 → 10.4.

**WP10.1 — Entitlements + billing seam.**
Per-tenant `Entitlements` (feature flags — e.g. `woo-connector` — + numeric limits like seat/till
caps), read at module boundaries (`IEntitlementService.IsEnabled(tenant, feature)` → clean 403 +
portal upsell state when off). `IBillingProvider` seam (create-checkout / webhook-verify /
subscription-status) with a `NullBillingProvider` default; a webhook receiver (HMAC-verified)
translates provider events → entitlement writes. The **concrete Stripe adapter is DEFERRED** to
Matt's provider choice; the model + enforcement + webhook plumbing land now.
*DoD:* toggling an entitlement flips a gated endpoint 200↔403; webhook payload writes the tenant's
entitlements; core has zero reference to any concrete provider (arch test).

**WP10.2 — Tenant lifecycle states.**
`TenantStatus` (Active | Suspended | Closed) on the tenant record. **Suspended = portal login
refused (402/403) but tills keep syncing sales** (decision D16 — a shop mid-day is never cut off
for a billing lapse). Closed = read-only pending export/delete. Enforced in the auth path for
portal scopes only; the `sales.ingest`/device path is exempt. Portal banner + a platform-admin
control to set status.
*DoD:* a suspended tenant's till POSTs a sale 200 while portal login returns the locked state.

**WP10.3 — Tenant data export.**
`GET /api/v1/admin/export` (platform-admin/company-admin) streaming a ZIP of every tenant-owned
class as CSV **and** a single JSON document (sales, lines, tenders, stock, customers, credit,
rollups, RBAC, audit…). Deterministic, resumable-friendly (paged internally), tenant-filtered by
the same query filters so no cross-tenant leak. This is the portability half of offboarding.
*DoD:* export a tenant → the JSON round-trips into a fresh tenant (sanity import) with matching
row counts + penny-exact sales totals.

**WP10.4 — Scheduled deletion + retention.**
Soft-delete request (`DeletionSchedule`: requestedAt, executeAfter grace window) + a retention
sweeper (the outbox dispatcher's timer host) that: purges device **heartbeats**/telemetry past a
short window, **retains sales 6+ years** (legal), and executes due tenant deletions (hard-delete
tenant-owned rows after export confirmation). All destructive actions audited; a closed tenant can
cancel before the grace window elapses.
*DoD:* a scheduled deletion past its window removes only that tenant's rows (others untouched);
sales inside the 6-year window are never purged; heartbeats past the window are.

---

## Phase 11 — Operability & shopkeeper UX (Matt's punch list, 2026-07-25)

> Requirements gathered from live use of the test environment. Planned 2026-07-25; **no code
> yet**. Order chosen so the data-model WPs (11.1, 11.3) land before the surfaces that display
> their output (11.2, 11.4). All web-till items should be mirrored into
> `Build/till-retrofit-2026-07-25.md` for MAUI when Phase 4 re-baselines.

**WP11.1 — Till naming (unique per tenant).**
The legacy `Till` table has **no Name column** — the name typed at till-creation is currently
dropped (`EnrolmentService.CreateTillAsync` accepts it, persists nothing). Fix WITHOUT touching
the shared legacy POCO (MAUI Sqlite maps `Till`): a server-only **`TillDetails`** table
(`TillId` PK, `TenantId`, `Name`) — the same evolve-in-place pattern as `StoreDetails`.
- `PUT /api/v1/tills/{id}/name` — gated `portal.tills.enrol`, audited. **Uniqueness enforced
  per tenant** (case-insensitive) server-side: duplicate → `409` with the clashing till's id;
  plus a unique index `(TenantId, Name)` as the backstop.
- Set from the **portal** (Stores & Tills: rename inline) and from the **till itself**
  (Settings → Till device: "Name this till" — calls the same endpoint, so the same 409 surfaces
  to the operator if the name is taken).
- Backfill: `CreateTillAsync` writes the name it already receives; existing tills get a
  migration-time default ("Till {short-id}") they can rename.
- Display everywhere a bare till GUID shows today: portal tills list, Banking view, Stock
  movements drill, report drill-downs, and the till's own Settings page.
*DoD:* rename from portal AND from till; duplicate name (either surface) → clear 409 message,
nothing saved; names visible in Banking + Stores & Tills; audit rows for every rename.

**WP11.2 — Receipts: template editor, viewer, saleId barcode.**
- **Barcode:** render the sale's unique code (`saleId`) on the receipt as a **hand-rolled
  Code 39 SVG** (A–Z/0–9/'-' covers a UUID; no library — plan rule 7). Shown under the printed
  saleId so a returned item can be scanned straight into the till's Return flow (extend Return
  lookup to accept a scanned saleId).
- **Template editor (portal):** per-store receipt config stored server-side alongside
  `StoreDetails` — header lines (shop name/address auto-fill from Store, editable), footer
  message (e.g. returns policy), toggles: show VAT number, show operator name, show barcode.
  `GET/PUT /api/v1/stores/{id}/receipt-template`, gated `portal.company.manage`, audited. The
  till caches the template with its catalogue sync and applies it to printed receipts.
- **Receipt viewer:** recall any past sale as a rendered receipt — from the till (existing
  Reporting sale-recall gains a "View receipt" that renders the stored sale through the current
  template, reprintable) and from the portal (Dashboard sale drill-down gains the same).
*DoD:* edit footer in portal → next till receipt shows it (after sync); scan a receipt barcode
into the Return flow → original sale found; view+reprint a week-old sale's receipt from both
till and portal.

**WP11.3 — Stores & stock locations: create + friendlier hours.**
- **Add store (portal):** `POST /api/v1/stores` exists — the portal lacks the button. Add
  "New store" (address + phone), auto-creating its STORE stock location on first use as today.
- **Warehouse locations:** `StockLocationType.Warehouse` exists in the model but nothing can
  create one. Add `POST /api/v1/stock/locations` {storeId, type, name} + rename, gated
  `portal.stock.adjust`, audited; portal Stock tab gains "New location". Transfers/stock-takes
  already work per-location, so a warehouse is immediately usable once creatable.
- **Opening hours editor:** replace the raw-JSON textarea with a structured control — a
  tick-box per weekday (open/closed) and open/close times in **24-hour** inputs
  (`<input type="time">`), writing the SAME JSON shape the API already stores
  (`{"mon":[{"open":"09:00","close":"17:30"}],…}`) so no backend change. Pre-populate from the
  stored JSON; keep an "advanced" JSON view for multi-interval days (e.g. lunch closing).
*DoD:* create a store and a warehouse from the portal, transfer stock into the warehouse; set
Mon–Sat 09:00–17:30 closed-Sunday via tick-boxes only; stored JSON round-trips unchanged.

**WP11.4 — Items-sold report (NatApp "Stock Outtake Report" parity).**
One line per item sold: **date sold, item (barcode + name), location sold (store/till name from
WP11.1), qty, unit price, discount (if any), line gross** — with quick ranges **last day / last
7 days / last 30 days** plus **pick a month / quarter / year**, and CSV export.
- Backend: `GET /api/v1/reports/items-sold?from=&to=[&storeId=][&tillId=][&itemIdOne=]`, gated
  `portal.reports.view`, capped + paged, plus a `.csv` variant (same pattern as the existing
  exports). **Sourcing decision:** read from the LEGACY `Trans`+`Sales` tables for now — they
  uniformly cover 2019→today (the bridge keeps writing them for new sales) and carry
  `ItemIdOne` for name joins, whereas platform `SaleLines` only carry an item reference for
  post-Phase-2 sales. Re-point to `SaleLines` when the legacy tables retire (the endpoint
  contract doesn't change).
- Surfaces: a **Reports → Items sold** view in the portal AND in the till's Reporting tab
  (shopkeeper habit from NatApp); quick-range buttons render the same endpoint.
*DoD:* the four quick ranges + month/quarter/year picker return correct rows (spot-check
against a known day's sales to the penny incl. a discounted line); CSV totals match on-screen;
a 30-day query on the full Kapow dataset returns in acceptable time.

### Phase 11 (cont.) — Portal information architecture (Matt's follow-up, 2026-07-26)

> Feedback from live use: the "Stores & Tills" tab conflates two backend concepts and the
> top-level navigation is missing an obvious way home and a home for company/period settings.
> **No code yet.** Frontend-only re-layout plus one small model addition (webstore channel row);
> no change to how stores, stock locations, tills, or sales are stored. Ordered so the nav
> shell (11.5) lands before the page it reveals (11.6), and the webstore card (11.7) last since
> its behaviour depends on Phase 6.

**The Stores-vs-Locations confusion (root cause).** Two distinct records overlap in the UI:
- **`StoreDetails`** — the *shop*: name, address, phone, opening hours, receipt template, tills.
- **`StockLocation`** (`Store` | `Warehouse`) — an *inventory bucket*. Every store
  **auto-creates** a `Store`-type stock location (`StockLedger.cs`, `"Store {storeId}"`). That
  auto-row is exactly the "Store 1" the user sees duplicated under "Physical locations".
A **webstore** is neither: it is `SaleChannel.WebStore`, a *sales channel* (Phase 6 Woo), not a
physical place. The fix is presentational — group by kind and nest the store's own stock bucket
inside its store card instead of listing it flat — with **no schema change to stores/locations**.

**WP11.5 — Portal navigation: Dashboard + Company tabs; Periods moves in.**
Current tabs: `Reporting, Banking, Stock, Prices, Customers, Loyalty, Users & Roles, Stores &
Tills, Periods`. Target order:
`Dashboard, Reporting, Banking, Stock, Prices, Customers, Loyalty, Users & Roles, Locations,
Company`.
- **Dashboard tab (new, default landing):** the summary/analytics view currently reached via
  Reporting → Summary becomes its own first tab so "Dashboard" always takes you home. Reporting
  keeps its Summary sub-tab (or points at the same component) — decide during build whether to
  de-duplicate; no data change either way.
- **Company tab (new):** holds the company record (name, VAT — moved out of the top of the
  Stores page) and **absorbs the Periods page** as a sub-section (Company details │ Financial
  periods). Removes the standalone "Periods" tab.
- Pure `App.tsx` tab-list + routing change plus moving `CompanyRow` and `PeriodsPage` under a new
  `CompanyPage`. No API changes.
*DoD:* Dashboard tab lands on the analytics view from any other tab; Company tab edits company
details and creates/closes periods; no standalone Periods tab; every RBAC gate that applied to
Periods still applies inside Company.

**WP11.6 — "Locations" page: Company out, grouped collapsibles in.**
Rename the "Stores & Tills" tab to **Locations** and restructure the page top-down:
1. **Physical locations** heading with a one-line **summary** (e.g. "1 store · 1 warehouse · 0
   webstores") and the action buttons in one row: **`+ New store`**, **`+ Warehouse / location`**,
   **`+ Webstore`** (11.7).
2. **Stores** — collapsible, **closed by default**. Each store card is the existing
   `StoreCard` (address/phone, opening hours, tills, receipt template). The store's auto
   `Store`-type stock location is shown **inside** its card (as its inventory bucket), not in a
   separate flat table.
3. **Warehouses** — collapsible, closed by default: the `Warehouse`-type `StockLocation` rows,
   with the existing create/rename.
4. **Webstores** — collapsible, closed by default (11.7).
The current flat "Physical locations" `StockLocation` table is retired in favour of these three
grouped sections. Company details are gone from this page (now in the Company tab, 11.5).
- Frontend-only: re-compose `StoresPage.tsx` (`LocationsSection` folds into the Warehouses group;
  the store-stock-bucket row is filtered by `storeId` into each `StoreCard`). Existing endpoints
  (`fetchStores`, `fetchStockLocations`, `createStore`, `createStockLocation`) unchanged.
*DoD:* opening the tab shows Company-free page: summary + three buttons at top, all three groups
collapsed; expanding Stores shows store cards with their tills/receipt and their own stock bucket
inline; a store no longer appears as a separate top-level "physical location"; adding a warehouse
lands it in the Warehouses group.

**WP11.7 — Webstore (online sales channel).**
Add a webstore entry alongside stores and warehouses. A webstore is **`SaleChannel.WebStore`, a
channel — not a `StockLocationType`**; do **not** add it to the stock-location enum. Model a
minimal server-side **`WebStoreDetails`** row (`Id`, `TenantId`, `Name`, `Url`, `Enabled`,
optional `StoreId` for stock fulfilment) — the same evolve-in-place pattern as `StoreDetails`.
- `GET/POST/PUT /api/v1/webstores`, gated `portal.company.manage`, audited. Uniqueness on
  `(TenantId, Name)`.
- Portal: the **Webstores** group (11.6) lists them; `+ Webstore` captures name + URL. Editing is
  a card like a store card.
- **Sync is out of scope here** and gated behind **Phase 6 (WooCommerce connector)** plus the
  `woo-connector` entitlement — this WP delivers the *configuration shell* only; when Phase 6
  lands, the connector reads these rows. Show a clear "Connect via WooCommerce (Phase 6)" note on
  the card so the shopkeeper knows sync isn't live yet.
*DoD:* create/edit/list a webstore from the Locations page; row persists tenant-scoped and unique;
card shows the not-yet-connected note; no impact on stock-location transfers or existing sales.

---

## Phase 12 — Legacy retirement & ops hardening (added 2026-07-26)

> Collects every **known-interim contract** currently running (previously scattered as asides in
> Phase 2/5/8/11 notes — easy to forget there) plus the ops debt of a now-live test environment.
> Nothing here is urgent; all of it is deliberate debt that must not become permanent by default.
> Sequencing: 12.1 (repoint the last legacy readers) is the prerequisite for 12.2 (turn the
> bridge off) — do not attempt 12.2 first.

**WP12.1 — Repoint the last legacy readers to /api/v1.**
Three readers still consume legacy data:
- **Till "Custom" report** still reads legacy `/api/Sale/Index` (near-empty legacy tables — it
  shows 2 sales). Repoint to the v1 reports endpoints like Summary/VAT/Items-sold already were.
- **Till catalogue prices** still read legacy prices (Phase 5 note): adopt
  `/api/v1/prices/effective` in the catalogue sync.
- **Checkout basket** doesn't yet apply Phase 8's credit-as-tender + membership auto-discount
  (endpoints live, basket not wired).
*DoD:* till Custom report matches v1 Summary for the same range to the penny; a WP5.4 scheduled
price change reaches the till at the boundary; a member's auto-discount and a credit redemption
both flow through a live checkout and land correctly in SalesV2 tenders/adjustments.

**WP12.2 — Retire the legacy sale bridge + interim contracts.**
Once 12.1 leaves zero readers on legacy tables: switch off `LegacySaleBridgeConsumer` (config
flag first, delete later) and retire the interim encodings it required — projection metadata in
`SaleLine.DiscountsJson`, legacy payId in `SaleTender.ProviderRef`, returns as negative-qty
lines (give returns a first-class shape). Legacy `Sales`/`Trans` become frozen read-only archive
(kept for the 6-year retention window, excluded from new writes).
*DoD:* a day of live trading with the bridge OFF produces penny-identical v1 reports; nothing
writes legacy sales tables (assert with a trigger or audit query); reconciliation report re-run
clean; rollback = one config flag.

**WP12.3 — Test-env ops hardening.**
The Mac mini is now a de-facto staging environment with real (migrated) data and no safety net:
- **Scheduled MySQL dumps** (launchd/cron, nightly, N-day retention, `plutus` + `plutus_t1`),
  plus a **restore rehearsal** — a backup that's never been restored is a hope, not a backup.
- **pm2 log rotation** (the DBService logs grow unbounded) and `pm2 save` verified so a Mac
  reboot brings everything back.
- **Uptime checks**: a tiny health-check script (plutus API, portal, web till, Keycloak — and
  **ETRIE, alert-only, never touched**) so we learn about outages before Matt does.
- Housekeeping: close the long-lived dev-box SSH tunnel when idle; prune `backend.pre-*`
  rollback dirs older than N deploys; document the runbook in HANDOVER.
*DoD:* kill the DBService → alerted; restore last night's dump into `plutus_t1` and row-counts
match; reboot the Mac → all Plutus processes return without manual steps; ETRIE untouched
throughout (200 before/after).

---

## Standing verification (every phase)

1. WP1.7 suites (isolation, money, idempotency) green.
2. Architecture tests: no cross-module internal references; no frontend→backend coupling beyond `/api/v1`; no `decimal` money types; no UPDATE on immutable tables.
3. OpenAPI drift check.
4. Migrated Kapow reconciliation report re-run after any schema migration touching sales.
