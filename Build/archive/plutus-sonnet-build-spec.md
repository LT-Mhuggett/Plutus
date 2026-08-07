> **📦 ARCHIVED — fully implemented.** Phases 0–2 (foundations, sale contract T1.3, web-POS
> cutover onto `/api/v1/sales`, device enrolment) are COMPLETE & LIVE — see `HANDOVER.md`
> §5a/§5b for the phase records. Kept for the reasoning behind the money/sale invariants, which
> are still enforced by `SaleV2.Validate()` and `tests/Plutus.Tests.Unit/SalesV2Tests.cs`.

# Plutus Build Specification — Phases 0–2 (for Claude Sonnet)

**You are implementing the Plutus platform foundations.** This document is your task list and your contract. It is subordinate to:

1. `plutus-platform-architecture.md` (v3) — the design; if this spec is silent, the architecture doc decides.
2. `plutus-implementation-plan.md` — the full phase map; this spec expands its Phases 0–2 to code level.
3. `kapow-db-gap-analysis.md` — source-database facts for the migration task.

Read all three before writing any code.

---

## 0. Session protocol

- Work **one task (T-numbered) at a time**, in order. Announce which task you are starting.
- A task is complete only when its **Verify** block passes. Run the tests; paste the output summary.
- **Stop conditions** — stop and report instead of improvising if: a migration would drop or alter existing data columns not listed here; a contract field is missing from this spec; a test in the standing suites (T1.7) fails after your change; you need a new NuGet/npm dependency not on the allow-list.
- Never modify: the OpenAPI contract after T1.6, the standing test suites (except to add tests), anything under `frontends/` while working backend tasks (and vice versa).
- Commit per task: `feat(wp0.1): retarget to net8.0` style, one task = at least one commit.

**Dependency allow-list.** Backend: `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*` 8.x, `Pomelo.EntityFrameworkCore.MySql` 8.x, `Swashbuckle.AspNetCore`, test: `xunit`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MySql`. Frontend: `react`, `react-dom`; dev-only: `vite`, `typescript`, `openapi-typescript`. **Anything else: stop and ask.** Explicitly banned: MediatR, AutoMapper, MassTransit, Dapper, Newtonsoft.Json (use System.Text.Json), Dexie, axios, react-router.

---

## 1. Conventions (apply everywhere, tested by T0.4)

| Concern | Rule |
|---|---|
| Money | `long` pence in C#, `BIGINT` in MySQL. The domain type is `Pence` (below). `decimal`/`double` for money = build failure |
| Time | Store UTC `DATETIME(6)`. Domain properties are `DateTime` with `Kind=Utc` enforced in converters. Device-local context = `BusinessDay` (`DATE`) |
| IDs | UUIDv7 via `Uuid7.New()` (below), stored `BINARY(16)` |
| Tenancy | Every tenant-owned entity implements `ITenantOwned { Guid TenantId }`; a single global query filter is applied by convention in `OnModelCreating` — never per-entity by hand |
| JSON | System.Text.Json, camelCase, `JsonStringEnumConverter`, unknown fields ignored on read, never emitted null-suppressed money fields |
| API | Everything under `/api/v1/`. Additive changes only. Errors use RFC 7807 `application/problem+json` |
| Naming | Tables plural PascalCase; columns PascalCase; FK columns `{Entity}Id` |
| Immutable tables | `Sales`, `SaleLines`, `SaleTenders`, `SaleAdjustments`, `OutboxEvents`: no `UPDATE`/`DELETE` statements in application code (T0.4 architecture test greps EF change-tracking usage; also revoke UPDATE at the DB user level for these tables in production config) |

---

## Phase 0 — Baseline

### T0.1 — Retarget to .NET 8

1. All csproj: `<TargetFramework>net8.0</TargetFramework>`.
2. Packages: `Microsoft.EntityFrameworkCore` → 8.x, `Pomelo.EntityFrameworkCore.MySql` → 8.0.x, `Swashbuckle.AspNetCore` → latest 6.x.
3. Fix compile breaks. Known Pomelo 6→8 items: `ServerVersion.AutoDetect` unchanged; check `HasCharSet` calls; `DateOnly` now maps natively (you will use this for `BusinessDay`).
4. Before touching anything, capture snapshot responses of `GET Sale/Summary`, `GET Sale/Detail/{id}`, `GET ItemParameters.Search?q=batman` against the current build; re-run after.

**Verify:** solution builds `-warnaserror` on net8.0; snapshot responses byte-identical (allow reordered JSON properties); self-contained `osx-arm64` publish succeeds.

### T0.2 — Solution restructure

Create this layout (class libraries; only `Plutus.Api` is executable):

```
src/Plutus.SharedKernel      — no project references
src/Plutus.Identity          — refs SharedKernel
src/Plutus.Tenancy           — refs SharedKernel
src/Plutus.Sales             — refs SharedKernel
src/Plutus.Catalogue         — refs SharedKernel
src/Plutus.Reporting         — refs SharedKernel
src/Plutus.Api               — refs all modules (composition root only)
tools/Plutus.SeedMigrator    — refs SharedKernel, Tenancy, Sales, Catalogue
tests/Plutus.Tests.Unit
tests/Plutus.Tests.Integration
tests/Plutus.Tests.Architecture
```

Move existing code without behaviour change: auth → Identity; `Sale/*` endpoints/services → Sales; `ItemParameters` + band validation → Catalogue; `Sale/Summary` → Reporting. Controllers stay thin; module services registered via one `AddPlutusModule()` extension per module, called from `Plutus.Api/Program.cs`.

**SharedKernel contents (implement exactly):**

```csharp
// Money
public readonly record struct Pence(long Value)
{
    public static Pence Zero => new(0);
    public static Pence operator +(Pence a, Pence b) => new(a.Value + b.Value);
    public static Pence operator -(Pence a, Pence b) => new(a.Value - b.Value);
    public override string ToString() => Value.ToString(); // formatting is a client concern
}
// EF: ValueConverter<Pence, long> registered by convention for all Pence properties.

// UUIDv7 (RFC 9562) — own implementation, ~30 lines, no package:
// 48-bit unix-ms timestamp, ver=7, variant=10, rest crypto-random.
public static class Uuid7 { public static Guid New(); }
// Test: 1,000 sequential calls are strictly ordered by the first 6 bytes; version nibble == 7.

// Tenancy
public interface ITenantOwned { Guid TenantId { get; } }
public interface ITenantContext { Guid TenantId { get; } Guid? DeviceId { get; } bool IsPlatformAdmin { get; } }
// Implementation reads claims: tid (Guid, required unless IsPlatformAdmin), did (optional).

// Events
public abstract record DomainEvent(Guid EventId, Guid TenantId, DateTime OccurredAtUtc);
public sealed record SaleRecorded(Guid EventId, Guid TenantId, DateTime OccurredAtUtc,
    Guid SaleId, Guid DeviceId, long DeviceSeq, DateOnly BusinessDay) : DomainEvent(EventId, TenantId, OccurredAtUtc);
public interface IEventBus { Task PublishAsync(DomainEvent e, CancellationToken ct); }   // T1.5 implements
public interface IEventConsumer { string Name { get; } Task HandleAsync(DomainEvent e, CancellationToken ct); }
```

**Verify:** build + all existing tests pass; endpoint snapshots from T0.1 still identical.

### T0.3 — CI + OpenAPI artifact

1. GitHub Actions (or equivalent): restore → build `-warnaserror` → unit + architecture tests → integration tests (Testcontainers MySQL 8) → publish `openapi.json` artifact (generated via Swashbuckle CLI at build, not runtime scrape).
2. `frontends/codegen.sh`: `npx openapi-typescript openapi.json -o src/api/types.gen.ts` (dev-time only).
3. CI fails if `openapi.json` differs from the committed copy (drift gate — active from T1.6).

**Verify:** CI green end-to-end on a clean clone.

### T0.4 — Architecture tests (permanent)

In `Plutus.Tests.Architecture`, enforce by reflection over loaded assemblies + csproj parsing:

1. No module references another module (SharedKernel excepted); `Plutus.Api` may reference all.
2. No public type in any module uses `decimal` or `double` in a property/parameter whose name matches `(?i)(price|cost|amount|total|pence|charge|fee)`.
3. All entities implementing `ITenantOwned` have the global query filter applied (assert via `IModel` metadata).
4. No type outside SharedKernel calls `Guid.NewGuid()` for entity IDs (grep-based; allow in tests).
5. Frontend isolation: `Plutus.Api` has zero references to anything under `frontends/`; no Razor/Blazor/SpaServices packages anywhere.

**Verify:** suite passes; deliberately breaking each rule in a scratch commit makes it fail.

---

## Phase 1 — Foundations

### T1.1 — Tenancy schema + enforcement

**New tables (one EF migration, `AddTenancy`):**

```sql
Tenants    (Id BINARY(16) PK, Name VARCHAR(200), Status TINYINT,        -- 0 Trial,1 Active,2 PastDue,3 Suspended,4 Closed
            Plan VARCHAR(50), Entitlements JSON, ConnectionRef VARCHAR(100) NULL,
            CreatedAtUtc DATETIME(6))
Companies  (Id BINARY(16) PK, TenantId BINARY(16), Name VARCHAR(200), CreatedAtUtc DATETIME(6),
            INDEX IX_Companies_Tenant (TenantId))
Stores     (Id BINARY(16) PK, TenantId BINARY(16), CompanyId BINARY(16), Name VARCHAR(200),
            Abbr VARCHAR(10), VatNumber VARCHAR(20) NULL, AddressJson JSON NULL,
            OpeningHoursJson JSON NULL,                                  -- {"mon":["09:00","17:30"],...,"sun":null}
            CreatedAtUtc DATETIME(6), INDEX IX_Stores_Tenant (TenantId, CompanyId))
Tills      (Id BINARY(16) PK, TenantId BINARY(16), StoreId BINARY(16), Name VARCHAR(100),
            DeviceId BINARY(16) UNIQUE, CredentialHash VARBINARY(64) NULL, CredentialSalt VARBINARY(32) NULL,
            Status TINYINT,                                              -- 0 PendingEnrolment,1 Active,2 Revoked
            LastSeenSeq BIGINT DEFAULT 0, CreatedAtUtc DATETIME(6),
            INDEX IX_Tills_Tenant (TenantId, StoreId))
EnrolmentCodes (Id BINARY(16) PK, TenantId BINARY(16), TillId BINARY(16), CodeHash VARBINARY(64),
            ExpiresAtUtc DATETIME(6), UsedAtUtc DATETIME(6) NULL)
```

**Existing tables:** add `TenantId BINARY(16) NOT NULL` to every tenant-owned table (Items, Categories, Vats, Discounts + children, Sales legacy tables if retained pre-T1.3, Employees/Users, Stocks). Backfill in the same migration with the single existing tenant's ID (create tenant row "Kapow" first, deterministic ID from config). Add `(TenantId, <old PK/lookup>)` composite indexes.

**Enforcement:** convention in the DbContext — every `ITenantOwned` entity gets `HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)`. `SaveChanges` override stamps `TenantId` from context on added entities and **throws** if an entity's TenantId differs from context (defence in depth).

**Verify:** migration applies to a copy of the staging DB; integration test seeds tenants A and B with data, then for every DbSet asserts a query in A's context returns zero B rows; SaveChanges cross-tenant write attempt throws.

### T1.2 — Provisioning + device enrolment

**Endpoints (Tenancy module):**

| Method/route | Auth | Body → Response |
|---|---|---|
| `POST /api/v1/tenants` | platform-admin | `{name, plan, adminEmail, adminPassword}` → `201 {tenantId, companyId, storeId, adminUserId}` — creates tenant + default Company (same name) + default Store ("Main") + admin user |
| `POST /api/v1/tills` | `portal.tills.enrol` | `{storeId, name}` → `201 {tillId, enrolmentCode, expiresAtUtc}` — code = 8 chars Crockford base32, stored SHA-256, TTL 48 h, single-use |
| `POST /api/v1/tills/enrol` | anonymous | `{enrolmentCode}` → `200 {deviceId, clientSecret, tillId, tenantId}` — marks code used, till Active; secret returned **once**, stored PBKDF2 (reuse existing KDF params from TestTokenAuth) |
| `POST /api/v1/tokens/device` | anonymous | `{deviceId, clientSecret}` → `200 {accessToken, expiresInSeconds}` — HMAC-signed JWT (existing TestTokenAuth machinery), claims `tid`, `did`, `scope:"device"`, 12 h |
| `POST /api/v1/tills/{id}/revoke` | `portal.tills.enrol` | → `204`; till Status=Revoked; token issuance refuses |

Rate-limit `enrol` and `tokens/device` (5/min/IP). Failed enrol of a used/expired code → `410 Gone`.

**Verify:** e2e test: provision → create till → enrol → device token → call a `[Authorize(Policy="Device")]` echo endpoint → revoke → token issuance now 401. Code reuse → 410. Expired → 410.

### T1.3 — Sales schema v2

**New tables (migration `AddSalesV2`; legacy sale tables untouched — the migrator (T1.8) moves data, then legacy tables are dropped in a later, separate migration after reconciliation sign-off):**

```sql
Sales (Id BINARY(16) PK,                          -- the client-minted UUIDv7 saleId
       TenantId BINARY(16), TillId BINARY(16), DeviceId BINARY(16), DeviceSeq BIGINT,
       Channel TINYINT,                            -- 0 Till,1 WebPos,2 WebStore
       BusinessDay DATE, OccurredAtUtc DATETIME(6), ReceivedAtUtc DATETIME(6),
       GrossPence BIGINT, VatPence BIGINT, LegacyRef VARCHAR(32) NULL,
       OperatorUserId BINARY(16) NULL, Note VARCHAR(1000) NULL, VatReconstructed TINYINT(1) DEFAULT 0,
       UNIQUE KEY UX_Sales_Tenant_Sale (TenantId, Id),
       UNIQUE KEY UX_Sales_Device_Seq (TenantId, DeviceId, DeviceSeq),
       INDEX IX_Sales_Tenant_Day (TenantId, BusinessDay),
       INDEX IX_Sales_Tenant_Till_Day (TenantId, TillId, BusinessDay))

SaleLines (Id BINARY(16) PK, TenantId BINARY(16), SaleId BINARY(16) FK→Sales,
       LineNo INT, ItemId BINARY(16), Qty INT,
       UnitPricePence BIGINT, LineGrossPence BIGINT,
       VatRateBp INT,                              -- basis points: 2000 = 20%
       VatAmountPence BIGINT,
       OverriddenFromPence BIGINT NULL, DiscountsJson JSON NULL,
       INDEX IX_SaleLines_Tenant_Sale (TenantId, SaleId),
       INDEX IX_SaleLines_Tenant_Item (TenantId, ItemId))

SaleTenders (Id BINARY(16) PK, TenantId BINARY(16), SaleId BINARY(16) FK→Sales,
       TenderType TINYINT,                         -- 0 Cash,1 Card,2 Online,3 Credit
       AmountPence BIGINT, ChangePence BIGINT DEFAULT 0, ProviderRef VARCHAR(100) NULL,
       INDEX IX_SaleTenders_Tenant_Sale (TenantId, SaleId))

SaleAdjustments (Id BINARY(16) PK, TenantId BINARY(16),
       Type TINYINT,                               -- 0 Refund,1 Void
       OriginalSaleId BINARY(16), AdjustmentSaleId BINARY(16) NULL,
       ItemId BINARY(16) NULL, Qty INT NULL, AmountPence BIGINT,
       Reason VARCHAR(500), AuthoriserUserId BINARY(16) NULL, CreatedAtUtc DATETIME(6),
       INDEX IX_Adj_Tenant_Orig (TenantId, OriginalSaleId))

SaleQuarantine (Id BINARY(16) PK, TenantId BINARY(16), PayloadJson JSON,
       Reason VARCHAR(500), ReceivedAtUtc DATETIME(6), ResolvedAtUtc DATETIME(6) NULL)

OutboxEvents (Id BIGINT AUTO_INCREMENT PK, EventId BINARY(16) UNIQUE, TenantId BINARY(16),
       EventType VARCHAR(100), PayloadJson JSON, CreatedAtUtc DATETIME(6),
       INDEX IX_Outbox_Id (Id))

ConsumerOffsets (ConsumerName VARCHAR(100) PK, LastOutboxId BIGINT, UpdatedAtUtc DATETIME(6))
```

**Domain invariant (constructor-enforced, unit-tested):** `GrossPence == Σ LineGrossPence`; per line `LineGrossPence == UnitPricePence * Qty − Σ discounts`; `VatPence == Σ VatAmountPence`; `Σ tender AmountPence − Σ ChangePence == GrossPence`.

**Verify:** migration applies; property-based test (1,000 random valid sales) round-trips through EF with invariants intact; constructing an inconsistent sale throws.

### T1.4 — Idempotent ingest endpoint

`POST /api/v1/sales` — auth: device token (`scope:"device"`) or operator token with `pos.sell`.

Request body: exactly architecture §4.1. `tenantId` and `deviceId` come from the **token**, never the body; a body `deviceId` mismatching the token's `did` → `403`.

**Semantics (in one DB transaction):**

1. Validate shape + invariants (T1.3). Malformed → `400` problem+json, field-level errors.
2. Invariant/VAT-integrity failure that a till can't fix (totals disagree, unknown VAT band) → insert `SaleQuarantine`, return **`202 {status:"quarantined", saleId}`**. (Quarantine is still idempotent: same saleId re-POST → same 202.)
3. Insert Sale + lines + tenders; insert `OutboxEvents` row (`SaleRecorded`); update `Tills.LastSeenSeq = GREATEST(current, deviceSeq)`.
4. Commit → **`201 {status:"recorded", saleId, receivedAtUtc}`**.
5. Duplicate `(TenantId, Id)` (catch unique-violation, don't pre-check) → re-read stored result → **`200`** with the same body shape as the original outcome.

`Idempotency-Key` header must equal body `saleId` when present; mismatch → `400`.

**Verify (integration):** duplicate POST → 200 + identical body; 20 concurrent identical POSTs → exactly 1 row, 1 outbox event, 19×200; quarantine flow idempotent; token/body deviceId mismatch → 403; `LastSeenSeq` monotonic under out-of-order arrivals; throughput smoke: 50 rps sustained 60 s locally, p95 < 100 ms.

### T1.5 — Broker-less event dispatch

Hosted service `OutboxDispatcher` in `Plutus.Api`:

- Loop: every 500 ms (configurable), for each registered `IEventConsumer`: read `ConsumerOffsets[name]`, fetch next batch (≤100) of `OutboxEvents` with `Id > LastOutboxId` **ordered by Id**, deserialise, `HandleAsync` sequentially, advance offset in the same transaction as the last successful handle. Failure → log, retry with backoff (1 s, 5 s, 25 s, then park: write to `ConsumerDeadLetters` and advance — a stuck consumer must not halt the others' progress; dead letters surface in logs and a `GET /api/v1/ops/deadletters` platform-admin endpoint).
- Consumers must be idempotent; provide `ProcessedEvents(ConsumerName, EventId PK pair)` helper table + `IIdempotentConsumer` base class in SharedKernel that checks/records EventId.
- Expose lag metric per consumer (max OutboxEvents.Id − offset) via `/metrics`-style endpoint or logs.

**Verify:** two dummy consumers at different offsets converge; kill the host mid-batch (test harness cancellation) → restart → no lost or double-applied events (idempotency table proves it); poison event parks after 3 retries and later events still flow.

### T1.6 — Contract freeze

Generate `openapi.json`; review field-by-field against architecture §4.1 and this spec's endpoint tables; commit; enable the CI drift gate from T0.3.

**Verify:** CI fails when a controller signature changes without regenerating the spec; TS types regenerate cleanly.

### T1.7 — Standing test suites (permanent, untouchable)

1. **Tenant isolation:** discover all `/api/v1` routes via `ApiExplorer`; for each GET/POST, authenticated as tenant A, attempt access to tenant-B-owned resource IDs → expect 404 or empty collection — never 200-with-data, never 403 (existence leak).
2. **Money reconciliation:** generator produces random valid sale batches → ingest → assert `Σ DB == Σ submitted` in pence, and (after T3.3 exists) projections match to the penny.
3. **Idempotency replay:** capture a run of 200 ingests, replay the full log twice → row counts unchanged.

**Verify:** all green in CI; a deliberately-introduced filter omission on a scratch entity is caught by suite 1.

### T1.8 — Kapow migration (SeedMigrator v2)

Input: the Kapow SQLite file. Target: the new schema. Follow `kapow-db-gap-analysis.md` §5 exactly.

**Structure requirement:** implement the Kapow→v2 mapping logic (ID remap, pence parsing, VAT backfill, timezone conversion) as a reusable library `tools/Plutus.Migration.Kapow` with the SeedMigrator as a thin runner — the MAUI till uses the same Kapow schema locally, and its cutover tooling (see `plutus-maui-build-spec.md`) reuses this library.

Concrete mapping decisions (source facts are in the gap analysis — do not re-derive):

1. Tenant "Kapow" (deterministic ID) + Company; adopt `Stores` row → new `Stores`; create one `Tills` row (the historical device), Status=Active, synthetic `DeviceId`.
2. Items: `Uuid7.New()` per item; old text PK → `Barcodes(ItemId, Code, IsPrimary)` when it looks like EAN/ISBN (`^\d{8,14}$`), else create as **department key** (`Items.Kind=Department`); build in-memory remap `oldId→newId` used by every FK below.
3. Prices: `Items.Price` (decimal text) → pence → `PriceLists` entry, policy CENTRAL, effective from migration date. *(Create minimal `PriceLists` table now: Id, TenantId, ItemId, PricePence, EffectiveFromUtc — Phase 5 extends it.)*
4. Sales: per sale — `Uuid7.New()`, `LegacyRef=old Id`, `DeviceSeq` = row number ordered by `Created`, `Channel=Till`, `BusinessDay` from `DateOfSale` (Europe/London → date), timestamps Europe/London → UTC. Money: parse decimal text → pence; **validate `Σ lines == Sales.Total` per sale**; mismatch → row goes to `SaleQuarantine` with reason, not into `Sales`.
5. Lines: `Trans` → `SaleLines` (`Amount`→Qty, prices→pence); VAT backfill: rate from `Items.VatId→Vats.Rate` (multiplier→basis points: 1.2→2000, 1.05→500, 1.0→0), `VatAmountPence = LineGross − round(LineGross / multiplier)`, set `VatReconstructed=1` on the sale. Fold `CheckoutItemChangeModel` → `OverriddenFromPence`; `Transaction_Discounts` → `DiscountsJson`.
6. Tenders: `PaySales` → `SaleTenders` (PayMethods 1→Card, 2→Cash, 3→Online, 4→Credit).
7. Refunds → `SaleAdjustments` (Type=Refund, map both sale refs through remap, `AuthoriserUserId` via employee map).
8. Employee → `Users` (name, email, PBKDF2 hash+salt carried over; **skip** Wage/ContractedHours/NIN/address/mobile — confirmed unused, dropped).
9. `AuthActions`/`EmpAuthActions` → seed `Permissions`/`Roles` mapping: Till→`pos.sell`, Refund20→`pos.refund.max:2000`, Refund100→`pos.refund.max:10000`, Refund Unlimited→`pos.refund.max:*`, Staff/Item/Report/Admin/Management → matching portal permissions (create the minimal RBAC tables now if Phase 3 hasn't run: Permissions, Roles, RoleAssignments per architecture §7.2).
10. Stocks: **do not migrate quantities.** Write `LegacyStockCounters` archive table verbatim for reference.
11. Idempotent: keyed on `LegacyRef`/natural keys — re-running produces zero new rows.

**Reconciliation report (the migrator prints and saves it):** per calendar year — sale count, gross pence, VAT pence: SQLite vs MySQL, plus quarantine count with reasons. Expected: equal or itemised.

**Verify:** run against the real backup file; report differences = quarantined items only; re-run = no-op; spot-check 5 known sales end-to-end (pick from the report).

---

## Phase 2 — Web POS onto the pipeline

### T2.1 — Contract types + device identity

In `plutus-pos-web`: add generated `types.gen.ts`; implement `uuid7.ts` (same RFC 9562 layout, `crypto.getRandomValues`); `deviceSeq` = monotonic counter persisted in IndexedDB meta store (single writer via the existing outbox transaction).

**Verify:** unit tests for uuid7 ordering/version bits; seq survives reload and increments across outbox entries.

### T2.2 — Outbox retarget

Change the checkout outbox flush to `POST /api/v1/sales` with the v1 payload (map basket → lines with pence + VAT from the cached catalogue). Handle: `201/200` → remove from outbox; `202` → remove + log warning (quarantined, server holds it); `400` → park entry in a `failed` store, surface a till-side banner (do **not** retry 400s); network/5xx → keep + backoff retry (existing SW behaviour). Remove every legacy write path.

**Verify:** offline checkout → reconnect → exactly one sale server-side; forced duplicate flush (double-tab race) → server 200-dedupes, outbox clears; a 400-parked entry doesn't block the queue behind it.

### T2.3 — Web device enrolment

Enrolment screen (admin enters code from portal/API) → `POST /api/v1/tills/enrol` → store `{deviceId, clientSecret}` in IndexedDB → device token fetched/refreshed via `POST /api/v1/tokens/device` and used for `/sales` + heartbeat; operator login (existing Bearer flow) unchanged for UI auth.

**Verify:** two browser profiles = two deviceIds with independent seq streams; revoking one device 401s only that device's sale pushes.

---

## Done = Phase gate

Phases 0–2 are complete when: CI green including standing suites; Kapow reconciliation signed off; the web POS records sales through the new pipeline in staging (Mac mini) with the legacy path deleted; `openapi.json` frozen. **Then stop and request the Phase 3–4 spec** (portal, RBAC enforcement, projections, MAUI sync, heartbeat) — do not begin Phase 3 from the summary plan alone.
