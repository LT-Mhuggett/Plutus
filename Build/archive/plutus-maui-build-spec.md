> **📦 SUPERSEDED — pause lifted, folded into [`Build/To do/MAUI-Retrofit-Plan-2026-08-07.md`](../To%20do/MAUI-Retrofit-Plan-2026-08-07.md).**
> The upstream MAUI code this spec was waiting for **has landed** (`Plutus.Frontend.AppClient`,
> merged at `4494a57`), so the ⏸ hold below is void. Its M0–M4 acceptance criteria and the local
> schema v2 design were carried forward — they were the best-specified part of it and stand as
> written. The M-numbering is retired; use the new plan's WP numbers.

# Plutus MAUI Build Specification — extending the till onto the platform (for Claude Sonnet)

> ⏸️ **PAUSED / ON HOLD (2026-07-24) — DO NOT BUILD M0–M4 YET.**
> New MAUI till code is coming from the **upstream repo** (`github.com/seank842/Plutus`) that will
> **replace** the port this spec assumes. Do not start the M0–M4 work — it would be discarded when
> the upstream code lands. When it does, re-baseline this spec against the actual upstream MAUI
> project (the target behaviours — integer-pence money, VAT integrity, sale contract, outbox sync,
> enrolment, offline RBAC, heartbeat — still stand as the acceptance criteria). See
> `plutus-implementation-plan.md` → Phase 4 for the same hold.

**Context.** The MAUI app is the in-progress port of the NatApp POS. It currently runs on the **same SQLite schema as the Kapow database** analysed in `kapow-db-gap-analysis.md`. The extensions built for the webapp/backend (integer-pence money, VAT integrity, band validation, sale contract, auth) must be **rolled back into MAUI** — and the till must join the platform (outbox sync, enrolment, offline RBAC, heartbeat).

Subordinate to `plutus-platform-architecture.md` (v3). Companion to `plutus-sonnet-build-spec.md` (backend Phases 0–2) — **backend T0.2 (SharedKernel) and T1.x (endpoints) must exist before M2 onward can be verified against a real API;** M0–M1 can proceed in parallel.

**Session protocol, allow-list discipline, and commit style: identical to the backend spec §0.** MAUI-specific allow-list additions: `Microsoft.Data.Sqlite`, `sqlite-net-pcl` *or* EF Core Sqlite (pick whichever the port already uses — do not switch mid-port), `CommunityToolkit.Mvvm` if already in use. Banned additions: anything else without asking.

---

## The one design rule that replaces "porting"

**MAUI is C#. It does not re-implement the webapp's extensions — it references the same code the backend runs.**

| Webapp/backend extension | MAUI equivalent | How |
|---|---|---|
| Integer-pence arithmetic module (TS) | `Pence` struct | Reference `Plutus.SharedKernel` |
| UUIDv7 (TS `uuid7.ts`) | `Uuid7.New()` | SharedKernel |
| Sale contract (`POST /api/v1/sales` payload) | Same DTOs | `Plutus.Contracts` (see M0.2) |
| VAT-integrity validation (backend guardrail) | Pre-commit validation at the till | Shared validator class (M0.2) |
| Item band-validation guardrail | Same class, run against local catalogue | Shared validator class |
| `ItemParameters.Search` | Local search over the cached catalogue, same matching rules | Shared matcher, local data |
| Auth (PBKDF2 verify, token client) | Operator offline login + device token client | SharedKernel KDF + `Plutus.Client.Core` |
| IndexedDB checkout outbox + SW pusher | SQLite outbox table + background pusher | `Plutus.Client.Core` (M2) |
| `Sale/Summary`, `Sale/Detail` | Not ported — these are portal/API features. The till keeps only a local rolling window for reprint/X-report | — |

**New shared project:** `src/Plutus.Client.Core` (net8.0, no MAUI references) — outbox engine, pusher, sync cursors, heartbeat client, enrolment/token client, offline permission evaluator. Unit-testable without a device; MAUI hosts it. `src/Plutus.Contracts` — the request/response DTOs for `/api/v1/*`, referenced by backend controllers **and** Plutus.Client.Core, so the contract cannot drift between server and till (the OpenAPI spec remains the outward truth for non-.NET clients).

---

## Phase M0 — Shared foundations into MAUI

### M0.1 — Solution stitching

Bring the MAUI project into (or reference from) the Plutus solution. Add references: MAUI app → `Plutus.Client.Core` → `Plutus.Contracts` + `Plutus.SharedKernel`. Architecture test (extend backend T0.4): the MAUI project never references backend modules (`Plutus.Sales` etc.) — only SharedKernel, Contracts, Client.Core.

**Verify:** builds for `net8.0-windows10.0.19041.0` (and any other current targets); architecture test fails if a backend module reference is added.

### M0.2 — Extract contracts + validators

1. Move/create the sale payload DTOs (exactly backend T1.4's shape) into `Plutus.Contracts`; backend controllers switch to consuming them (coordinate with backend task ordering — this is the only cross-repo edit).
2. Extract VAT-integrity + band-validation logic from the backend into `Plutus.SharedKernel` (pure functions over the contract types); backend ingest and MAUI pre-commit both call it.

**Verify:** backend integration tests still green (same validation behaviour, relocated); unit tests for the validators run in the MAUI test project referencing only SharedKernel/Contracts.

### M0.3 — Money and ID sweep

Replace all money handling in the MAUI port with `Pence` end-to-end (basket, discounts, tender, change calculation — the port currently inherits NatApp's decimal-string habits). Replace any `Guid.NewGuid()` entity IDs with `Uuid7.New()`.

**Verify:** compile-time: no `decimal`/`double` money properties (same regex rule as backend T0.4, applied to the MAUI assemblies); basket totalling property test (random baskets: total == Σ lines − discounts, change == tendered − total, all in pence).

---

## Phase M1 — Local store v2

**Decision (mirrors architecture):** the till's local DB is an **operational cache, not an archive.** Historical sales live centrally (migrated once per client by the SeedMigrator). The till keeps: catalogue cache, price cache, permission cache, sync cursors, the outbox, saved baskets, and a **rolling window of recent sales** (default 14 days, config) for reprint and X/Z. Seven years of history does not ride along on every till.

### M1.1 — Schema v2 (new local SQLite)

```sql
Meta            (Key TEXT PK, Value TEXT)                       -- deviceId, tenantId, tillId, storeId,
                                                                -- deviceSeq, catalogueVersion, schemaVersion
CatalogueItems  (Id BLOB PK, Name TEXT, Kind INTEGER,           -- 0 Product,1 Department
                 PricePence INTEGER, VatRateBp INTEGER, CategoryId BLOB,
                 BandData TEXT NULL, UpdatedAtUtc TEXT)
Barcodes        (Code TEXT PK, ItemId BLOB)
Operators       (UserId BLOB PK, DisplayName TEXT, CredentialHash BLOB, CredentialSalt BLOB,
                 PermissionsJson TEXT, TimeWindowsJson TEXT NULL, UpdatedAtUtc TEXT)
LocalSales      (SaleId BLOB PK, DeviceSeq INTEGER UNIQUE, BusinessDay TEXT, OccurredAtUtc TEXT,
                 PayloadJson TEXT,                              -- the exact contract payload
                 Status INTEGER,                                -- 0 Pending,1 Pushed,2 Quarantined,3 Failed
                 PushedAtUtc TEXT NULL, ServerResponseJson TEXT NULL)
SavedBaskets    (Id BLOB PK, Name TEXT NULL, ContractJson TEXT, CreatedAtUtc TEXT)
```

Notes: `LocalSales` **is** the outbox (Status=Pending) *and* the rolling window (Status=Pushed, pruned past the window) — one table, one transaction, no dual-write. `PayloadJson` is the contract itself, so the pusher never re-serialises from entities. No legacy tables in v2.

### M1.2 — Cutover tool

A till-side command (dev/deploy tooling, not end-user UI): archive the old Kapow-schema file (timestamped copy → designated upload location for the central SeedMigrator run), create the v2 store, seed `CatalogueItems`/`Barcodes` from the old file **reusing `Plutus.Migration.Kapow`** (same ID remap the server used — the remap must be deterministic or exported/imported so till item IDs equal central item IDs; confirm with the backend implementation, stop if it isn't deterministic), set `Meta.schemaVersion=2`.

**Verify:** cutover on a copy of the real Kapow file: catalogue count matches (20,372 + department keys), spot-check 10 barcodes resolve to the same UUIDs the central migration produced; old file untouched.

### M1.3 — SavedTransactions fix

Parked baskets serialise as **contract JSON** (`SavedBaskets.ContractJson`) — no .NET `$type` metadata (gap analysis: `NatApp.Plutus` type names break on the namespace change). One-off import of any existing parked baskets during cutover, tolerant of failure (they're transient by nature — log and continue).

**Verify:** park → kill app → restore basket; serialised form contains no `$type`.

---

## Phase M2 — Sale recording + sync (the outbox)

### M2.1 — Local commit path

Checkout completes → build the contract payload (validators from M0.2 run first; a validation failure blocks completion at the till — the operator fixes it now, unlike the server quarantine which handles what tills *couldn't* fix) → **one SQLite transaction:** insert `LocalSales` (Status=Pending, `DeviceSeq` = ++`Meta.deviceSeq`) → receipt prints from the committed payload.

**Verify:** kill the app between checkout tap and print → on restart the sale exists exactly once with a seq; seq strictly monotonic across restarts; checkout latency unaffected by network state (airplane-mode test).

### M2.2 — Pusher

Background service in `Plutus.Client.Core`: drain Pending in `DeviceSeq` order → `POST /api/v1/sales` (device token). Responses: `201/200` → Status=Pushed + store response; `202` → Status=Quarantined + store response (do not retry); `400` → Status=Failed, **skip and continue** (a poison sale must not block the queue), surface count in UI status bar; network/5xx/timeout → stay Pending, exponential backoff (5 s → 5 min cap), retry forever. Prune Pushed rows older than the rolling window (never prune Pending/Failed).

**Verify:** soak — 1,000 sales offline, reconnect: all land exactly once, in order (server `DeviceSeq` gaps = none); kill mid-drain → no loss/dupes (server dedupe count confirms); one Failed sale doesn't halt those behind it; simulated 48 h offline then reconnect drains clean.

### M2.3 — Enrolment + device identity

First-run screen: enter enrolment code → `POST /api/v1/tills/enrol` → store `deviceId` in `Meta`, `clientSecret` in **platform secure storage** (DPAPI/`SecureStorage`, never the SQLite file) → token client (`POST /api/v1/tokens/device`) with refresh-before-expiry and 401→re-auth-once logic in Client.Core.

**Verify:** e2e against staging: enrol → sell → sale lands attributed to the right till/store; revocation server-side → pusher parks with a clear "device revoked" state (sales keep committing locally); secret absent from any file copied off the machine.

---

## Phase M3 — Operator auth + offline RBAC

### M3.1 — Operator sync + offline login

Sync `Operators` (users assigned to this till's scope, per architecture §7.2): `GET /api/v1/tills/{id}/operators` → cached with credential hashes + **effective POS permission sets** + time windows. Login verifies locally (SharedKernel KDF) — works fully offline; refreshed on each sync; server-side revocation removes the operator on next sync *and* kills their central token immediately.

### M3.2 — Permission enforcement at the till

`IPermissionGate` in Client.Core: `Can(operator, "pos.refund", amountPence)` — handles plain permissions, `pos.*.max:{pence}` ceilings (a `Refund20`-style ceiling check), and time windows against local clock. Every gated action embeds `{operatorId, permission}` into the sale/adjustment payload (audit, per architecture §7.2). UI: gated buttons disabled with reason tooltip; supervisor-override flow = second operator authenticates for one action.

**Verify:** matrix test — cashier can sell but not refund; supervisor refund ≤ ceiling passes, > ceiling requires override; Saturday-only operator rejected on Sunday (fake clock); all offline. Audit fields present in the pushed payload.

---

## Phase M4 — Heartbeat, catalogue sync, fleet citizenship

### M4.1 — Heartbeat client

Every 60 s (config): `POST /api/v1/heartbeat` with `{deviceId, appVersion, outboxDepth (count Pending), oldestUnsyncedAge, deviceClock}`. Process response pull signals: `catalogueVersion` newer than `Meta` → trigger M4.2 sync; `syncNow` → kick the pusher; `lock` → lock UI to a "contact your administrator" screen (local sales data untouched). Heartbeat failures are silent (never disturb selling); resumes on success.

### M4.2 — Catalogue/price/permission sync

Cursor-based pull: `GET /api/v1/catalogue/changes?since={version}` → upsert `CatalogueItems`/`Barcodes` (effective-dated prices apply by comparing `EffectiveFromUtc` at lookup time, so a scheduled price change activates offline at the right moment); same pattern for operators (M3.1). Full-resync command for recovery. Sync runs on: app start, heartbeat signal, and a 15-min timer — never during an open basket transaction.

**Verify:** price scheduled for 02:00 activates at 02:00 with the till offline; 20k-item full resync < 60 s locally; a sale mid-sync sees a consistent snapshot (price read at basket-add is what's charged and what's in the payload).

### M4.3 — App update citizenship

Handle `426 Upgrade Required` from any endpoint: banner + grace behaviour per architecture §10.2 (selling continues, sync parked until updated, if the server says so). Report `appVersion` accurately from assembly metadata.

**Verify:** staging minimum-version bump → till surfaces the banner; sales still commit locally.

---

## Phase gate

MAUI spec complete when: a fresh machine can enrol with a code and sell offline within minutes; the 1,000-sale offline soak passes against staging; RBAC matrix green; cutover tool verified against the real Kapow file; heartbeat visible on the fleet dashboard (needs backend Phase 4 — coordinate). **Cash sessions/X/Z (architecture §9.2) and payment terminals (§9.1) are deliberately not in this spec** — they arrive with backend Phases 7's contracts; the local commit path (M2.1) is where they'll plug in.
