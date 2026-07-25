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

**WP6.1 — Entitlements gate + connection config.** `WebstoreConnections` CRUD (portal), entitlement check (`woo-connector`) enforced at module boundary.
**WP6.2 — Inbound orders.** Webhook receiver (HMAC), order → v1 sale event (`channel: WEB_STORE`) → internal `POST /sales`; reconciliation poll with sync cursor.
**WP6.3 — Outbound stock/price.** `StockLevelChanged`/`ItemUpdated` consumer → Woo REST batch, debounced; per-tenant oversell buffer config.
*DoD:* connector runs against a real Woo test store; kills/restarts resume from cursor; unentitled tenant gets clean 403 + portal upsell state; core platform has **zero** references to the connector (architecture test).

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

---

## Standing verification (every phase)

1. WP1.7 suites (isolation, money, idempotency) green.
2. Architecture tests: no cross-module internal references; no frontend→backend coupling beyond `/api/v1`; no `decimal` money types; no UPDATE on immutable tables.
3. OpenAPI drift check.
4. Migrated Kapow reconciliation report re-run after any schema migration touching sales.
