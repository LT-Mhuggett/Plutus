# MAUI Retrofit — the single plan

**Date:** 2026-08-07 · **Status:** plan; no code written from it yet.
**Goal:** take Sean's MAUI till (`Plutus.Frontend.AppClient`), which today is a fully offline,
local-SQLite app with **no network code at all**, and make it a first-class client of the Plutus
platform — enrolled, syncing, and at feature parity with the web till.

**This document replaces six earlier ones.** They are archived, banner-stamped, and still readable
for their reasoning; everything still live in them is carried forward here:

| Superseded | What was carried forward |
|---|---|
| `MAUI-Backend-Sync-Plan-2026-08-01.md` | Almost all of it — the architecture decision, the work packages, the parity analysis, the risk register. This is the backbone. |
| `plutus-maui-build-spec.md` | The M0–M4 acceptance criteria and the local schema v2 design (§6). Its pause is lifted — the code it was waiting for has landed. |
| `till-retrofit-2026-07-25.md` | The MAUI column of its gap list (§7.5). The web-POS column was closed in July. |
| `Migration-2026-07-22-plan.md` | Nothing outstanding for AppClient — see §3. Its ClientUI workstreams die with ClientUI. |
| `BugFix-2026-07-22-plan.md` | Nothing — all three bugs are **already fixed** in AppClient (§3). |
| `OfflineMode-2026-07-23-plan.md` | The §4.3 credential fork, which is still an open decision (§10). Its bug list belongs to ClientUI, not AppClient (§3). |

`NatApp-Translation-Agent-Plan-2026-08-05.md` stays standalone — it moves *legacy shop data* into
the new backend, which is a different job from making a till talk to it. The two meet at exactly
one point, recorded in §10.

---

## 0. Execution protocol — for an autonomous (Sonnet-grade) session, NO QUESTIONS

This plan is written to be executed one work package at a time by an implementing model with no
prior context. **Every decision it needs is already made**: §9's binding defaults and §3a's
rulings ARE the answers — do not re-ask them; Matt can veto any before the WP that uses it starts.

**Required reading before any code** (in this order):
1. [`Build/repo-runbook.md`](../repo-runbook.md) — build/test/migrate commands, the ten codebase
   pitfalls, hard rules. Everything there applies here.
2. [`Build/till-parity.md`](../till-parity.md) — the feature register this plan exists to close.
   Its rule binds you: **a capability isn't done until its row is updated in the same commit.**
3. `Build/plutus-platform-architecture.md` wins on any design conflict — stop and flag, don't improvise.

**Session shape** (how phases 0–18 were built): one WP per session, in §4 order. Announce the WP,
build it, run its DoD, paste the test output, commit with the `Co-Authored-By: Claude` trailer,
update HANDOVER's resume block. A WP is complete only when its DoD passes.

**Verification split.** Automated DoD elements (builds, unit/integration tests, soak harnesses)
gate the WP. DoD elements needing physical hardware or a human eye (enrolment on a real till,
printer output, drawer, visual checks) are **USER-VERIFY**: collect them into a checklist at the
end of each WP for Matt, and carry on — they block sign-off, not the next WP.

**No Mac deploys in this plan.** Verify backend-touching WPs (2b, 5, 8, 13) against a locally-run
`Plutus.DBService` + the integration factory. Matt deploys to the test env via the runbook when
he chooses. (Corollary: ETRIE is untouchable and untouched.)

**Package allow-list** (MAUI side): what AppClient already references — EF Core Sqlite,
`CommunityToolkit.Mvvm`/`.Maui`, Mapster, the Syncfusion `34.1.32` pins. `Plutus.Client.Core` and
`Plutus.Contracts.Client` are plain net10 class libraries: SharedKernel + BCL only. Anything else
= stop and report, don't add it.

**Stop-and-report conditions** (the ONLY reasons to halt): the WP0 gate fails; WP2's ID remap
turns out non-deterministic; an architecture-doc conflict; a package not on the allow-list seems
required. Everything else has an answer in this document.

**Toolchain facts (verified 2026-08-07 on this box):** SDK 10.0.302 with the `maui` workload
installed; `Plutus.Frontend.AppClient` builds for `net10.0-windows10.0.19041.0` (WP0 re-confirms).
⚠ The "Appium UI suite (PR #8)" the superseded sync plan told you to run is **NOT in this
branch** — `Plutus.Frontend.AppClient.Tests` is a small xunit/Moq unit project. Baseline is that
project + the three platform suites; UI regression is USER-VERIFY until an in-branch UI suite
exists.

---

## 1. The decision that shapes everything: AppClient, not ClientUI

There are two MAUI projects in this repo and they are **not** two versions of the same thing:

| | `Plutus.Frontend.AppClient` | `Plutus.Frontend.ClientUI` |
|---|---|---|
| What it is | Sean's MAUI rework of NatApp (`feature/maui-pos-rework`), merged in at `4494a57` | The earlier, abandoned in-house MAUI port |
| Data layer | `Plutus/Data/Database` — **the legacy NatApp schema** (`SaleModel`, `ItemModel`, decimal money, string IDs) | `Plutus.Entities.Models` — the **backend's** types |
| HTTP | **Zero.** No `HttpClient`, no Refit, nothing | 5 files with `HttpClient` |
| Sync machinery | **None** | `DBAction` outbox + `LocalToServerSync` (buggy, see §3) |
| Telemetry | Clean | 30 AppCenter references, 0 Sentry |
| Feature completeness | The working till | Missing Settings, StoreOptions, FTSU, add-user |
| Bugs from `BugFix-2026-07-22-plan` | All three **fixed** | All three **present** |
| `.cs` files | 125 | 193 |

**Recommendation: build on AppClient; harvest two things from ClientUI, then retire it.** AppClient
is the app that actually works and the one Matt is waiting on. ClientUI is bigger only because it
carries a half-built repository layer and dead telemetry.

⚠ **This is a real trade, not a formality.** AppClient's data layer is the *legacy* schema —
decimal money, string IDs, a flat stock column — which is precisely what the platform moved away
from. ClientUI already speaks `Plutus.Entities.Models`. Choosing AppClient means §6 (local store
v2) is genuine work rather than something inherited. It is still the right call: a working till
with a schema to migrate beats a schema-correct shell with no till in it.

**Harvest from ClientUI before it goes:**
1. `Resources/Styles/Colors.xaml` — the exact WebTill palette (`Primary #272643`, `Quinary
   #2c698d`, …). AppClient has *no* theme resources at all. → WP7.
2. `IRepositoryWrapper`/`ItemRepository` — the interface *shape* is closer to a networked
   repository than AppClient's direct-`DbContext` calls. Port the shape, **not** the
   implementation, which carries all seven bugs in §3.

Everything else in ClientUI is superseded. Once WP7 lands, delete it from `Plutus.slnx`.

> **This is binding default §9.1** — an autonomous build proceeds on it without asking. Veto it
> before WP1 starts if you disagree; every work package below assumes it.

---

## 2. Architecture: extend the existing outbox, no broker

Settled 2026-08-01 across three independent reviews; restated here because it's the load-bearing
decision and the reasoning shouldn't need an archived document.

| Option | Score | Why |
|---|---|---|
| **Extend the DB-backed outbox + plain REST** | **9/10** | Already built, tested and live for the web till. A wiring job, not a design job. |
| Client-driven pull/sync, no broker | 7–8/10 | Re-derives what the outbox gives free (idempotent ingest, dead-lettering). |
| SignalR hub + REST fallback | 4–5/10 | Solves sub-second push, which a till doesn't need; still needs the REST path underneath. |
| Message broker (RabbitMQ/NATS) | 2/10 | The architecture doc rejects it outright: *"broker credentials on every till and firewall fights."* An always-on stateful service on one Mac mini, for a problem it doesn't solve. |

**"No broker" is not "no queue."** A durable queue is still mandatory and exists on both ends — it's
just implemented as database rows (a local table on the till, the `Outbox` table on the server)
rather than a broker product. The wire between them is plain HTTPS to an idempotent ingest API.

*Future, not now:* if Plutus leaves the single Mac mini and the number of **internal** consumers of
sale-ingest grows (rollups, loyalty, webhooks, analytics), a managed broker becomes worth
revisiting — purely to fan one event out to many consumers. It would not change the till edge,
which stays HTTPS + local durable queue at any scale.

---

## 3. Verified current state (checked against code 2026-08-07)

Several claims in the superseded documents were **wrong or have gone stale**. Corrected here so
nobody builds on them.

### Already true — do not redo this work

- **Branch reconciliation is done.** `MAUI-Backend-Sync` P0/WP4.0 called for merging
  `feature/maui-pos-rework` onto `Matt's-Horror` and retargeting to net8. The merge landed at
  `4494a57`, and **both MAUI projects are already on `net10.0-android/ios/windows`.**
  ⚠ Following that instruction now would *downgrade* them.
- **AutoMapper → Mapster is done** in both projects (`9f65772`), which resolves `Migration`
  Workstream C item 4 differently from how it was planned (it said remove and hand-write).
- **AppCenter is not in AppClient** (0 references). `Migration` Workstream C item 3
  (AppCenter → Sentry) is therefore moot for the go-forward app; it only ever applied to ClientUI.
- **All three `BugFix-2026-07-22-plan` bugs are fixed in AppClient** — the add-user crash is a
  guarded dialog, `Basket` is a plain `ObservableCollection` with no reversing copy, and
  `Models/BasketAlteration.cs` has `[JsonConstructor]` plus setters.

### Wrong in the source documents — corrected

- ❌ *"A rudimentary outbox already exists in AppClient — `DBAction`, `SaveDBAction()`,
  `LocalToServerSync()` at `RepositoryBase.cs:616`."* → **It does not.** AppClient has **zero**
  hits for any of those. That machinery is in **ClientUI**. WP3 is therefore *build an outbox*,
  not *wire up the existing one* — a materially larger job than the source plan implied.
- ❌ *The seven replay bugs in `OfflineMode` §7* (id-converter `NotImplementedException`,
  composite-delete `NotSupportedException`, the `SaveChangesAsync` no-op, lazy repos never
  draining, …) are **ClientUI's bugs**. They matter only if ClientUI's repository implementation
  is ported — which §1 says not to do. Port the interface shape and leave the bugs behind.

### The starting point, plainly

AppClient is a **complete, working, entirely local till**: login authenticates against the local
SQLite file, checkout/returns/discounts/held-baskets/refund-tiers are all local CRUD, and
`Helpers/Database/Database.cs:39` still has `case DatabaseProvider.Cloud: throw new
NotImplementedException();` — the network path was scaffolded and never built. Money is `decimal`
across 11 model files; sale IDs are strings.

### Backend: three gaps, everything else exists

Verified endpoint by endpoint. **These exist and need no work** — MAUI is just a new caller:
`POST /api/v1/tills/enrol`, `POST /api/v1/tokens/device`, `POST /api/v1/sales`,
`POST /api/v1/cash-events`, `POST /api/v1/stock/movements`, `GET /api/v1/stock/levels`,
`GET /api/v1/categories`, `GET /api/v1/reports/{summary,summary-rich,vat,items-sold,category-sales,best-sellers}`,
`GET /api/v1/sales/{saleId}`, `GET /api/v1/customers`, `POST /api/v1/customers/{id}/credit/redeem`,
`GET /api/v1/loyalty`, `GET /api/v1/stores/{id}/info`, `GET /api/v1/prices/effective`.

**Genuinely missing — additive backend work, scheduled as WP2b/WP5/WP8:**

| Gap | Needed by | Note |
|---|---|---|
| `POST /api/v1/heartbeat` | WP5 | Copy the shape of `TillsController.ReportAgentStatus` + `GetDeviceStatus`, which already do exactly this for the Windows till agent. |
| `GET /api/v1/tills/{id}/operators` | WP8 | Only `GET /api/v1/users/{id}/effective-permissions` exists. Additive, small. |
| `GET /api/v1/catalogue/changes?since=` | WP5 | Check first whether `/prices/effective` can carry a cursor instead of building a second endpoint. |
| VAT rates as effective-dated history | WP2b | May already exist — **verify `Plutus.Entities`' tax model before assuming.** |

One free win: `GET /api/v1/stores/{id}/receipt-template` shipped on 2026-08-06. MAUI can consume it
and retire NatApp's hardcoded C# receipt layout — a debt that has outlived two rewrites.

---

## 3a. Scope found after this plan's sources were written

The parity analysis inherited from `MAUI-Backend-Sync` (2026-08-01) covered six areas — Cash,
Inventory, Reporting, Loyalty, Store Information, Settings — plus theming. An audit on 2026-08-07
against the *current* web till found it had missed the **Till screen itself** and everything
platform-level. Full register: **[`Build/till-parity.md`](../till-parity.md)**.

**Rulings (binding, veto-able): everything below is IN**, homed as follows — gift cards become
**WP13**; announcements + support tickets + pick-notes become **WP5b**; the receipt template folds
into **WP3**; the users screen folds into **WP8**; refund-only baskets into **WP3**; add-unknown-item,
the Bin and untracked stock into **WP10**; theming into **WP7**; cross-till refunds into **WP11**.

| Found | Size | Note |
|---|---|---|
| **Gift cards** (sell + redeem) | Large | Zero references in MAUI. ⚠ The per-tenant VAT-treatment gate means an unaware till gets **409s it can't explain**. |
| **Portal-controlled receipt template** | Small | `GET /api/v1/stores/{id}/receipt-template` shipped 2026-08-06. MAUI's hardcoded C# layout (`PosPrinterManager.cs:138-167`) *does* print the store's name/logo/address/VAT from the locally-synced StoreModel — but it ignores the portal template (header/footer lines, toggles) entirely, contradicting a stated requirement. |
| **Users / employee management** | Medium | A whole screen — but a **smaller one than it looks**: the web till only does employee list/create + set password (legacy `/api/Employee`); roles and effective permissions are portal-side. MAUI's add-user is a stopgap "not available yet" dialog. Overlaps WP8 but is distinct. |
| **Announcements banner** | Small | `GET /api/v1/announcements/active` — one poll, one banner. |
| **Help / support tickets** | Small | `/api/v1/support/tickets`. Closes the `support-heavy` churn signal. |
| **Pick-from-floor notifications** | Small | `GET /api/v1/notifications` + ack — a web sale sold stock that's physically on the shelf. |
| **Refund-only baskets**, **add-unknown-item**, **the Bin + untracked stock** | Small each | All shipped to the web till in the last week; fold into WP3/WP10. |
| **Portal-pushed theming** | Small | Shipped to the web till 2026-08-07 (`GET /api/v1/themes/effective`). Extends WP7: don't just port the palette — apply the pushed theme by mapping its eight slots onto the XAML resource keys, refreshed on the WP5 heartbeat/sync cadence. |

One behavioural trap the register also surfaces: the web till's word-matching search is a
**client-side device pref** (the server default is whole-phrase), so a MAUI till searching its
cached catalogue offline will return *different results for the same query* unless WP1's shared
matcher is genuinely shared.

## 3b. Progress board

Legend: ✅ done (DoD passed) · 🔨 in progress · ⬜ not started. Keep this current — it is the
resume point for the next session.

| WP | Status | Landed |
|---|---|---|
| **0** Toolchain + baseline gate | ✅ | 2026-08-07 · `27e91c5`. maui workload present; AppClient `net10.0-windows` head builds 0 errors; AppClient.Tests **295 pass / 3 skip**; Unit 347 · Arch 6 · Integration 67. |
| **1** Shared contracts + client core | ✅ | 2026-08-07 · `27e91c5`. `Plutus.Contracts.Client` + `Plutus.Client.Core` (outbox engine, pusher, API client, token provider). Backend: `stores/{id}/info` now returns `businessId`. Arch test keeps both MAUI-free and backend-module-free (mutation-checked). |
| **2b** VAT effective-dating | ✅ **corrected** | `3ec4eff` shipped exact-bp validation — wrong against the webtill's VAT decisions (Matt's catch). Corrected 2026-08-08 · `88e5c26`: pair-based (`VatRateHistory.Assess`), three verdicts, only StaleBand blocks. Tests rebuilt on real price pairs; gift-card exemption mutation-checked. Still inert live (0 rate rows) — **safe to seed bands now.** |
| **2** Local store v2 + cutover + money | ✅ (one VAT note) | 2026-08-07 · `6e46734`. New `Plutus.Client.Storage` (schema v2, SQLite). Cutover archives-never-merges and implements the §10 STOP. ⚠ The snapped catalogue band is a **label only** — at sale time lines derive `vatRateBp` from the price pair like the webtill (2026-08-08 correction; see the WP2 note + WP3). Money property test over 2,000 randomised baskets — its VAT arithmetic gets corrected with WP2b. |
| **3** Sale commit path + outbox | ✅ | 2026-08-07 · `6e46734`. `CommitSaleAsync` (one transaction, sequence allocated, **commit before print** — risk #2 decided in code). `TillStore` implements `IOutboxStore`, so the WP1 pusher drove it unchanged. Soak: 120 offline sales drain exactly once in order, no gaps; crash mid-drain records 20 of 20. |
| **4** Enrolment + device identity | ✅ | 2026-08-07 · `f90dac5`. Server URL + code; **archive gate refuses enrolment** while a legacy DB is un-archived; placement (storeId + legacy businessId) refreshed each start; secret asserted absent from the DB file. |
| **5** Heartbeat + catalogue sync · **5b** | ⬜ | **Next.** Needs the three missing backend endpoints (heartbeat, catalogue/changes, and `syncNow`/`lock` on Device). `TillStore.ApplyCatalogueChangesAsync` + `PriceSchedule` already exist and handle tombstones. |
| 6–13 | ⬜ | The parity WPs — these are where MAUI **UI** work begins (XAML + viewmodels), so they need a device to verify. |

**Two notes for whoever picks this up:**
1. **The transport spine is done.** WP1–WP4 mean a till can enrol, commit sales offline, and drain
   them exactly once — all provable headlessly. WP5 onward is the last backend gap, then the
   remaining WPs are screen work against endpoints that already exist.
2. **`IOutboxStore` did its job**: WP2's SQLite table implemented it and WP3's pusher needed no
   change at all. Keep new capability behind interfaces in `Client.Core` for the same reason —
   the rules stay testable without a till.

## 4. Build order

Each work package has a Definition of Done. Do them in order; **WP1 and WP2 gate everything else.**

| WP | Title | Why here |
|---|---|---|
| 0 | Toolchain + baseline gate | Re-confirm the §0 toolchain facts before anything else |
| 1 | Shared contracts + client core | Nothing can call the API until the DTOs exist |
| 2 | Local store v2 + money/ID sweep | The wire format demands integer pence and UUIDs |
| 2b | VAT effective-dating (backend) | Compliance; independent, can run in parallel |
| 3 | Outbox + sale ingest (+ portal receipt template, refund baskets) | The core of the whole retrofit |
| 4 | Enrolment, device identity, Settings | Everything after this needs a device token |
| 5 | Heartbeat + catalogue sync · **5b** announcements, tickets, pick-notes | Fleet citizenship |
| 6 | Store Information (read-only) | Smallest lift; proves the "portal is the truth" pattern |
| 7 | Theming (portal-pushed) | Cheap, visible, unblocks nothing — do it when you want a win |
| 8 | Operator RBAC + offline login + the Users screen | Needs the new endpoint; replaces local-only auth |
| 9 | Cash | Self-contained, well-specified contract |
| 10 | Inventory + stock ledger (+ Bin, untracked, add-unknown) | Rework of screens that already exist |
| 11 | Reporting + **cross-till refund lookup** | Rewrite local queries → backend calls; the refund path is money-handling, not a report |
| 12 | Loyalty | Highest effort, hardest offline design; reuses everything before it |
| 13 | Gift cards | Sell + redeem at the till; last — reuses WP12's customer plumbing |

**WP0 — Toolchain + baseline gate.** `dotnet workload list` shows `maui`; AppClient builds for
`net10.0-windows10.0.19041.0`; `Plutus.Frontend.AppClient.Tests` + the three platform suites run
green (baseline counts in HANDOVER). All four were verified passing on this box 2026-08-07 — this
WP is a re-confirmation, not exploration. *DoD:* all builds/suites green; failures reported
verbatim, not worked around.

---

## 5. Work packages — transport

**WP1 — Shared contracts + `Plutus.Client.Core`.**
Stand up `src/Plutus.Client.Core` (net10, **no MAUI references** — outbox engine, pusher, sync
cursors, heartbeat client, enrolment/token client, offline permission evaluator; unit-testable
without a device) and a DTO project for the `/api/v1/*` shapes.
⚠ **Do not name it `Plutus.Contracts`** — that name is taken by the legacy repository-interface
layer (`Plutus/Commons/Plutus.Contracts`, `IItemRepository` et al). Use `Plutus.Contracts.Client`.
Target `IngestSaleRequest` (`src/Plutus.Sales/SalesIngestService.cs`) exactly: `SaleId, DeviceId,
DeviceSeq, Channel(byte), BusinessDay(DateOnly), OccurredAtUtc, GrossPence, VatPence, Note,
OperatorUserId, List<IngestLine>, List<IngestTender>`. `SalesV2Controller` derives tenant and
device **from the token only** — a conflicting body `DeviceId` is a 403, not a merge.
⚠ **There is no first-class `ItemIdOne` field on the wire.** The barcode rides *inside*
`IngestLine.DiscountsJson`, a metadata envelope: `JSON.stringify({itemIdOne, exUnitPence,
discounts?, return?})` — exactly as the web till builds it (`api.ts:985-992`) and as
`SalesIngestService.ExtractItemIdOne` + `StockLedger` read it. Adding an `ItemIdOne` property to
the DTO would serialise to nothing and stock would silently stop moving.
**"A local backend" throughout this plan means `PlutusAppFactory`'s in-process `HttpClient`**
(shared in-memory SQLite, token helpers per runbook pitfalls 5–6) — `Plutus.Client.Core` must
accept an injected `HttpClient` precisely so the factory client satisfies every DoD. No local
MySQL is required, ever.
Extend the architecture test suite: MAUI may reference SharedKernel / Contracts.Client /
Client.Core and **never** a backend module (`Plutus.Sales` etc.).
*DoD:* solution builds with no duplicate-name collision; a smoke `IngestSaleRequest` posted from
`Plutus.Client.Core` round-trips 201 against a local backend; the architecture test fails if a
backend-module reference is added to the MAUI project.

**WP2 — Local store v2 + money/ID sweep.**
The till's local DB becomes an **operational cache, not an archive** — history lives centrally.
Keep: catalogue, prices, permissions, sync cursors, the outbox, saved baskets, and a rolling
window of recent sales (default 14 days) for reprint and X/Z. Seven years of history does not ride
on every till.

```sql
Meta            (Key TEXT PK, Value TEXT)              -- serverUrl, deviceId, tenantId, businessId,
                                                       -- tillId, storeId, deviceSeq,
                                                       -- catalogueVersion, schemaVersion
CatalogueItems  (Id BLOB PK,
                 IdOne TEXT NOT NULL UNIQUE,           -- the canonical legacy id / default barcode:
                                                       -- EVERY v1 stock/price/sale-line call keys on
                                                       -- this string, and it CANNOT be recovered from
                                                       -- Id (a one-way hash — see cutover note below)
                 Name TEXT, Kind INTEGER, PricePence INTEGER,
                 VatRateBp INTEGER, CategoryId BLOB, BandData TEXT NULL, UpdatedAtUtc TEXT)
PriceSchedule   (ItemId BLOB, EffectiveFromUtc TEXT, PricePence INTEGER,
                 PRIMARY KEY (ItemId, EffectiveFromUtc))  -- future-dated prices, applied by
                                                          -- comparing at lookup time so a scheduled
                                                          -- change activates offline (WP5 DoD needs
                                                          -- this — build the table NOW, not as a v3)
Barcodes        (Code TEXT PK, ItemId BLOB)            -- aliases only; IdOne is the default code
                                                       -- (there is no server Barcode entity —
                                                       -- Item.IdOne IS the barcode)
Operators       (UserId BLOB PK, DisplayName TEXT, CredentialHash BLOB, CredentialSalt BLOB,
                 PermissionsJson TEXT, TimeWindowsJson TEXT NULL, UpdatedAtUtc TEXT)
LocalSales      (SaleId BLOB PK, DeviceSeq INTEGER UNIQUE, BusinessDay TEXT, OccurredAtUtc TEXT,
                 PayloadJson TEXT,                     -- the exact contract payload
                 Status INTEGER,                       -- 0 Pending,1 Pushed,2 Quarantined,3 Failed
                 PushedAtUtc TEXT NULL, ServerResponseJson TEXT NULL)
SavedBaskets    (Id BLOB PK, Name TEXT NULL, ContractJson TEXT, CreatedAtUtc TEXT)
```

`LocalSales` **is** both the outbox (Pending) and the rolling window (Pushed) — one table, one
transaction, no dual-write. `PayloadJson` is the contract itself, so the pusher never re-serialises
from entities.

Alongside: replace every money `decimal` with `Pence` end-to-end (basket, discounts, tender,
change) and every entity id with `Uuid7.New()`, referencing `Plutus.SharedKernel` rather than
reimplementing. Parked baskets serialise as **contract JSON** with no .NET `$type` metadata —
NatApp's type names break on the namespace change.

A **cutover tool** (dev tooling, not end-user UI) archives the old Kapow-schema file, creates the
v2 store, and seeds `CatalogueItems` from it. **The catalogue ID mapping IS deterministic — but it
is NOT `Plutus.Migration.Kapow`'s `IdRemap`** (that mints *random* Uuid7s per run, applies only to
historic sale rows, and would wrongly trigger this plan's stop condition if you check against it).
The real mapping is `Plutus.SharedKernel.DeterministicGuid.ForItem(businessId, itemIdOne)` — the
twin of the web till's `pipeline.ts itemGuid`, unit-pinned in `LegacySaleBridgeTests`. The cutover
tool mints ids with that, and the DoD spot-check compares against it.
⚠ **`businessId` ≠ `tenantId`.** `ForItem` is keyed on the legacy *Business* id (Kapow:
`d5a31aac-159e-9a30-706b-02f9eb935600`, hardcoded as `BUSINESS_ID` in the web till's `api.ts:25`) —
NOT the `TenantId` that `EnrolResult` returns. Deriving item GUIDs from TenantId produces silently
wrong ids that nothing catches quickly (stock still moves, because lines key on `itemIdOne`).
Store the businessId in `Meta` at enrolment — small additive backend work: add `businessId` to the
`GET /api/v1/stores/{id}/info` response, which already joins the Business row for its name/VAT.
The Kapow source file for the cutover DoD is
`Build/seed-data/Kapow Comics ltd - Database - 23_07_2026 15_57_23.db` — **copy it; never write to
the original.**
**Where v2 lives:** a NEW EF Core Sqlite context homed in AppClient (or Client.Core). Drop the
`Plutus/Data/Database` project reference when the cutover lands — AppClient is its sole referencer,
so nothing else breaks. The no-decimal-money sweep gates AppClient + CommonPOSLibrary + CustomViews.

⚠ **The cutover's snapped VAT band is a LABEL, never a line rate** (corrected 2026-08-08 with
WP2b). `CatalogueItem.VatRateBp` is snapped to a clean band (2000, not the 2002 the legacy pair
derives) for display and pre-checks only. At SALE time, MAUI must derive the line's `vatRateBp`
from the price pair exactly as the webtill does (`api.ts:978`) — sending the snapped-clean rate
instead would make the two tills bucket the same item's VAT differently, which is precisely the
behavioural drift the parity register's §6 exists to prevent. See WP3.

*DoD:* no `decimal`/`double` money property survives in the MAUI assemblies (same regex rule the
backend arch test uses); a basket property test holds (total == Σ lines − discounts, change ==
tendered − total, all pence); cutover against a copy of the real Kapow file reproduces the
catalogue count and 10 spot-checked barcodes resolve to the **same UUIDs the central migration
produced**; park → kill → restore works and the serialised form contains no `$type`.

**WP2b — VAT-rate-change ingest compliance (backend).**

> ✅ **CORRECTED AND SHIPPED 2026-08-08 (Matt's catch).** The first implementation (`3ec4eff`)
> validated each line's `VatRateBp` by **exact membership** of the in-force set — which contradicts
> how the platform declares VAT and would have quarantined **ordinary webtill sales** the moment
> any tenant's bands were seeded. Replaced by the pair-based rule below (`VatRateHistory.Assess`,
> shipped in `88e5c26`). Still inert live (0 rate rows), and **now safe to seed bands.**

**The four standing VAT decisions this must respect** (all verified in code):
1. **Ordinary lines carry wobbled rates on the wire, by design.** The webtill derives per-line
   `vatRateBp` from the price pair (`api.ts:978`: `round((price/exPrice − 1) × 10000)`), so a
   £14.99/£12.49 item ships as **2002bp**. The FE7 comment says it plainly: *"round-tripping pence
   through the generic ratio would wobble the band to 1998–2002bp"*. Rollups and reports group by
   the RAW bp (`RollupProjection.cs:122`). Exact-bp checks are therefore wrong by construction.
2. **The canonical validity rule is on the price PAIR, not the rate**:
   `|price − round(exPrice × rate)| ≤ 2p` (`ItemController.BandInconsistency`, the WP1.4 guard).
   Bands live per tenant in the legacy `Tax` table; `VatRatePoints` adds only the TIME dimension
   the `Tax` table lacks — seed it FROM `Tax` (one band per row, effective-from epoch).
3. **Owner decision (VAT-FixLater report): legacy off-band damage is SURFACED, never blocked.**
   Off-band items still sell; the VatIntegrity report is where they are seen. Ingest must not
   quarantine them.
4. **Gift-card lines are pinned by the voucher treatment, not the catalogue** — activation 0/2000
   (`api.ts:976`), and single-purpose REDEMPTION is a negative 2000bp line (`api.ts:997-1019`).
   All `GIFT-CARD` lines stay exempt from this check.

**Corrected validation** (per line, using `UnitPricePence` + the meta's `exUnitPence` — unit
prices, so discounts don't disturb it; skip lines with no usable meta):
- *Explained by an in-force band* — some rate in force at `OccurredAtUtc` satisfies the 2p pair
  rule → **accept**.
- *Explained ONLY by a retired/not-yet-effective rate of the tenant's bands* → **quarantine**:
  this is the stale-band trading the WP exists for, stated in the reason ("priced at 20%, but the
  standard rate at time of sale was 17.5%").
- *Explained by neither* → **accept** — that is decision #3's off-band damage; the VatIntegrity
  report owns it.
- Empty history skips with a log line; `GIFT-CARD` lines exempt — both unchanged.

Also corrected alongside: `BasketMathTests` and `VatRateChangeE2eTests` currently pin
**rate-arithmetic VAT** (`gross × bp/(10000+bp)`), which is NOT the platform's derivation — the
webtill computes `vatAmountPence = lineGross − lineEx` from the price pair, always. The tests must
mirror the reference implementation, not a plausible alternative.

*DoD (corrected):* a stale-pair sale after the change (`£14.99/£12.49` after standard moves to
17.5%) is quarantined with a reason naming both rates; the same pair before the change ingests;
a **real webtill-shaped line at 2002bp** ingests with bands seeded (the regression Matt caught);
a deliberately off-band pair (`£11.00/£10.00`) ingests (decision #3) and appears in VatIntegrity;
gift-card activation AND single-purpose redemption lines ingest after a rate change; empty
history unchanged.

**WP3 — Outbox + sale ingest.**
⚠ Building, not wiring — AppClient has no outbox today (§3).
*Commit path:* checkout completes → validators run (a failure blocks completion at the till, where
the operator can fix it) → **one SQLite transaction** inserting `LocalSales` with Status=Pending
and `DeviceSeq = ++Meta.deviceSeq` → the receipt prints from the committed payload.
*Pusher* (in `Plutus.Client.Core`): drain Pending in `DeviceSeq` order → `POST /api/v1/sales` with
the device token. `201/200` → Pushed. `202` → Quarantined, do not retry. `400` → Failed, **skip and
continue** — one poison sale must never block the queue — and surface the count in the UI.
Network/5xx/timeout → stay Pending, exponential backoff 5s → 5min cap, retry forever. Prune Pushed
rows past the rolling window; never prune Pending or Failed.
The outbound payload **must** populate `itemIdOne` on each line the way the web till does —
`StockProjectionConsumer` silently skips lines without it, so stock would quietly stop moving with
no error anywhere.
⚠ **VAT on the payload: `api.ts:958-1019` is the REFERENCE IMPLEMENTATION** (added 2026-08-08,
Matt's catch — parity §6 in practice). Mirror it exactly, never "improve" it:
- `vatRateBp` = `round((unitInc/unitEx − 1) × 10000)` from the PRICE PAIR — wobbled values like
  2002bp are correct and expected; do NOT send the catalogue's snapped-clean band.
- `vatAmountPence` = `lineGross − lineEx`, where `lineEx` scales the discount by the ex/inc ratio
  (`api.ts:964-965`) — never rate arithmetic (`gross × bp/(10000+bp)` disagrees with the receipt).
- Returns: negative qty, discount dropped, `lineEx` negated; meta carries `exUnitPence` always.
- Gift cards: activation pinned 0/2000 by treatment; single-purpose redemption is a NEGATIVE
  2000bp line (`api.ts:997-1019`), multi-purpose redemption is a tender.
*Also in this WP (§3a rulings):* **receipt-print ordering is decided** — outbox commit FIRST, then
print from the committed payload (risk #2 resolved: a receipt can never exist for a sale that was
never queued; a print failure after commit is a reprint problem, not a money problem). The receipt
renders from the **portal template** (`GET /api/v1/stores/{id}/receipt-template`, cached with the
catalogue sync like the web till) instead of the hardcoded `PosPrinterManager` header — header/footer
lines and toggles obeyed, `** REFUND **` marked. And **refund-only baskets** are allowed: the T1.3
invariants are sign-agnostic (pinned by `SalesV2Tests`); mirror the web till's checkout rules — no
change on a refund, credit/gift-card tenders hidden as refund destinations.
*DoD:* soak — 1,000 sales offline, reconnect, all land exactly once in order with no server-side
`DeviceSeq` gaps; kill the app mid-drain, no loss or duplicates; one Failed sale does not halt
those behind it; 48h offline then reconnect drains clean; **a MAUI-originated sale moves stock**,
asserted explicitly, not just accepted; kill between commit and print → the sale is queued and
reprintable, never lost; a refund-only sale round-trips 201 and prints marked; a template change
reaches the next printed receipt after a sync. USER-VERIFY: paper output.

**WP4 — Enrolment, device identity, Settings.**
Replace the `DatabaseProvider.Cloud` throw with a real first-run flow: **Server URL + enrolment
code** (the web till is same-origin so it never needed an address; MAUI does — default
`https://plutus.huggett.dscloud.me` for the test env, the PlutusAppFactory client for automated
DoDs; persist in `Meta.serverUrl`). Then: enrolment code →
`POST /api/v1/tills/enrol {EnrolmentCode}` → `EnrolResult{DeviceId, ClientSecret, TillId, TenantId}`
(or **410 Gone** on a reused/expired/unknown code — surface it as a retryable message, not a crash)
→ store `DeviceId/TillId/TenantId` in `Meta`, and `ClientSecret` in platform `SecureStorage`,
**never** the SQLite file. Token client wraps `POST /api/v1/tokens/device`, refreshing at
`ExpiresInSeconds` minus a safety margin, with 401-retry-once around the pusher's calls.
Settings gains the server-backed half it has never had: device enrolment/identity with the
Active/PendingRemoval/Revoked lifecycle and manager-approved un-enrolment, server-validated till
renaming, a diagnostics panel (signed-in user, API reachability, business id), and sync-queue depth.
Keep the existing local preferences layer (printer config, checkout toggles) as-is.
**TWO TOKENS, not one — the rule every later WP leans on.** The device token (this WP) covers
`sales.ingest`-gated calls only: sale ingest, heartbeat, catalogue, store info, receipt template,
themes. Every `perm:*`-gated endpoint (tickets WP5b, stock/categories WP10, reports and sale
lookup WP11, customers WP12, gift cards WP13) resolves permissions **from RBAC by the token's
userId** — a device token can never pass it. So: **online operator login = local Pbkdf2 verify
AND a background `POST /api/Auth/Login`** minting that operator's server token (kept for the
session, re-minted on 401 per the 12h cache, runbook pitfall #10). **Offline login = local verify
only**, and every `perm:*`-gated screen shows its needs-connection state until a server token
exists. Hide the WP5b ticket/notification UI behind the same rule.
*DoD:* fresh install enrols and survives restart without re-prompting; app killed mid-refresh still
has a valid token next launch; `ClientSecret` never appears in the `.db3` file (grep-verified);
server-side revocation parks the pusher with a clear "device revoked" state while sales keep
committing locally; **per §9.3/§9.4, enrolment refuses to proceed while an un-archived legacy
database file exists** — the refusal message names the archive step.

**WP5 — Heartbeat + catalogue sync.**
`POST /api/v1/heartbeat` every 60s with `{deviceId, appVersion, outboxDepth, oldestUnsyncedAge,
deviceClock}`; server derives ONLINE (<2min) / STALE (2–5) / OFFLINE (>5). Store lastSeen in a fast
store, **not** per-heartbeat MySQL writes. The response carries pull signals: `catalogueVersion`
newer than `Meta` triggers a sync, `syncNow` kicks the pusher, `lock` locks the UI to a "contact
your administrator" screen with local sales data untouched. Heartbeat failures are **silent** —
they must never disturb selling.
Catalogue sync is cursor-based: `GET /api/v1/catalogue/changes?since={version}` upserting
`CatalogueItems`/`PriceSchedule`, with effective-dated prices compared at lookup time so a
scheduled price change activates offline at the right moment. Runs on app start, on heartbeat
signal, and on a 15-minute timer — **never during an open basket**.
**Backend specifics (nothing exists yet — build exactly this, don't invent):** the cursor is a
single bumped `BIGINT` CatalogueVersion row, incremented by item/price/category writes; the
changes feed returns rows with `UpdatedAt > cursor` **including binned/deleted items marked
`removed: true`** — without tombstones an offline till keeps selling a binned item. `syncNow` and
`lock` are nullable columns on `Device`, set from a small additive endpoint surfaced on the
portal's Locations screen. The "fast store" for lastSeen is an in-process
`ConcurrentDictionary` in DBService (single pm2 instance — no Redis exists in this stack and none
is being added).
`426 Upgrade Required` handling is **client-only for now** — test with a stubbed handler; the
server-side min-version gate is deliberately deferred with risk #6. Banner on 426; selling
continues, sync parks.
*DoD:* status transitions verified at the 2/5-minute boundaries with a fake clock; MySQL takes no
per-heartbeat writes; a price scheduled for 02:00 activates at 02:00 with the till offline; a 20k-item
full resync completes in <60s; a sale mid-sync sees a consistent snapshot — the price read at
basket-add is what's charged and what's in the payload.

**WP5b — Platform-citizenship screens (§3a rulings).** Three small consumers on the same 60-second
cadence, copied from the web till's shapes: **announcements** (`GET /api/v1/announcements/active`,
Maintenance/Incident banner only — any authenticated token), **pick-from-floor notifications**
(`GET /api/v1/notifications?unackedOnly=true` banner + acknowledge), and **support tickets**
(raise/read/reply via `/api/v1/support/tickets`, gated on `support.tickets` — which every built-in
role holds, because a lone cashier with a dead till must be able to shout for help).
⚠ Tickets and pick-note acks need the **operator server token** from WP4's two-token rule — a
device token cannot pass `perm:*` gates; hide these UIs until one exists.
⚠ **Sanctioned backend fix:** the two pick-note endpoints (`WebstoresController`) are gated
`"perm:sales.ingest"` — a scope-policy *name* used as a permission *code*, which no RBAC role
holds, so the gate fails for every caller. This is a pre-existing bug (the web till's polls fail
silently through their `.catch`). Change both to `[Authorize(Policy = PlutusPolicies.SalesIngest)]`
and re-verify the web till's pick-notes actually appear.
*DoD:* a seeded Incident announcement shows within a cycle and Info does not; an unacked pick-note
persists across restart until acknowledged; a ticket raised on the till appears in the portal
inbox and the reply comes back.

---

## 6. Work packages — feature parity

Ordered cheapest-first, because each builds confidence and reuses the last one's plumbing.

**WP6 — Store Information → read-only.** The two apps are *inverted*: the web till deliberately made
this read-only (WP6.1 — the portal is the single source of truth), while MAUI's
`StoreInformationViewModel` is a fully local admin editor with address-editing already stubbed out.
Strip every local-write command (`StoreNameChangeCommand`, `StoreLogoChangeCommand`,
`StoreContactNumberChangeCommand`, `VatINChangeCommand`, `StoreDefaultBagChangeCommand`) and
replace with one load from `GET /api/v1/stores/{id}/info` (policy `sales.ingest`, works with the
device token), rendering the per-day `openingHoursJson` as a read-only weekly table. **This is a
deletion, not a build** — among the smallest items here.
*DoD:* every field including opening hours renders from a live call with no local DB read or write;
zero remaining references to `StoreModel`-mutating commands in that file; network loss shows a
clear "unavailable" state rather than stale local data.

**WP7 — Theming.** AppClient's `App.xaml` registers only a value converter — **no theme resources at
all**. Port ClientUI's `Resources/Styles/Colors.xaml` verbatim (`Primary #272643`, `Secondary
#ffffff`, `Tertiary #e3f6f5`, `Quaternary #bae8e8`, `Quinary #2c698d`, `Error #FF9494`, the
Cyan/Blue accent scales and matching `*Brush` keys) under the **same `x:Key` names** so bindings
resolve unchanged, author a `Styles.xaml`, and merge both into `App.xaml`. Theme the Syncfusion
suite (pinned at `34.1.32`) via `SyncfusionThemeResourceDictionary`, remapping its palette slots to
these brushes. Native OS chrome will never pixel-match a browser; content and branding will.
*DoD:* no hard-coded hex left in XAML; an `SfListView` visibly reflects `Primary`/`Quinary`.

**WP8 — Operator RBAC + offline login.** Add the missing `GET /api/v1/tills/{id}/operators`
(additive, under `PlutusPolicies.PortalTillsEnrol`) returning each operator's user id and effective
permission set for that till's scope chain, from `EffectivePermissionsService`. MAUI caches it per
device. Login verifies **locally** via `Plutus.SharedKernel.Pbkdf2` — kept byte-identical to the
server's hashing so offline verification matches. `IPermissionGate.Can(operator, "pos.refund",
amountPence)` handles plain permissions, `pos.*.max:{pence}` ceilings and time windows against the
local clock; every gated action embeds `{operatorId, permission}` in the pushed payload for audit.
Supervisor override = a second operator authenticating for one action.
*Also in this WP (§3a ruling):* the **Users screen**, replacing the "not available in this version
yet" stopgap — the web till's smaller surface only: employee list/create + set password (legacy
`/api/Employee` + `/api/Auth/SetPassword`). Roles and effective permissions stay portal-side.
*DoD:* union-merged grants correct for a multi-level (company+store+till) fixture; matrix test —
cashier sells but cannot refund, supervisor refund ≤ ceiling passes and > ceiling demands override,
a Saturday-only operator is rejected on Sunday (fake clock) — **all offline**; audit fields present
in the pushed payload; an employee created on the till can sign in on the web till and vice versa.

**WP9 — Cash.** MAUI has nothing but `POSCashDrawer.cs`, a solenoid driver. Build `CashPage`/
`CashViewModel` with four actions — Open float, Paid in/out (reason mandatory), X snapshot, Z close
— each posting `POST /api/v1/cash-events` (`CashEventRequest`: `EventId` UUIDv7, `DeviceId`, `Type`
∈ `OpenFloat|PaidIn|PaidOut|XSnapshot|ZClose`, `BusinessDay`, `OccurredAtUtc`, `AmountPence`,
`CountedPence` for X/Z, `Reason`, `OperatorUserId`). Expected/counted/variance are computed
server-side and rendered from the response. Guard a second Z per business day client-side; the
server 409s anyway.
*DoD:* a second Z is blocked before the call fires, and the replayed case returns 409; blank-reason
paid-in/out rejected client-side; variance shown equals `CountedPence − ExpectedPence`.

**WP10 — Inventory + stock ledger.** The gap is structural, not cosmetic: MAUI writes a flat
`Item.Stock.Quantity`, the backend runs a movement ledger. Replace `CreateUpdateStock()` in
`ViewModels/MainTill/Inventory/Items/AddEditViewModel.cs` with `POST /api/v1/stock/movements`
(`{stockLocationId?, storeId?, itemIdOne, type: Receipt|Adjustment|WriteOff, qty, reason}` —
Adjustment/WriteOff need a non-empty reason, WriteOff needs a **negative** qty; the server 400s
otherwise). `ViewAllViewModel`'s quantity column becomes `GET /api/v1/stock/levels`. Wire category
CRUD to `/api/v1/categories`, surfacing the 409 *"{n} item(s) are still in this category"* as a
blocking reassign-first flow — MAUI has no reassign UI today. Mirror the VAT-band guard
(`|Price − ExPrice×Rate| > 2p` → 400) client-side before submit, and surface the server's exact
message when the client misses a case.
*DoD:* creating an item with an opening quantity produces exactly **one Receipt movement**, never a
bare stock row; adjusting without a reason is rejected before the request is sent; two devices
adjusting concurrently both land as separate movements and levels reflect the **sum**, never
last-write-wins; deleting a populated category blocks and offers reassign.

**WP11 — Reporting + cross-till refunds.** MAUI's three Statistics viewmodels query local SQLite directly — zero HTTP.
Even a pixel-perfect copy of the web till's screens would show **one till's data** if built that
way, so this is a rewrite, not a feature add. Point Summary at
`GET /api/v1/reports/summary-rich?from&to`, the bucket chart at `/reports/summary`, the VAT table at
`/reports/vat`; replace `StockOuttakeViewModel`'s local join with `/reports/items-sold`; add two
screens MAUI has never had (`/reports/category-sales`, `/reports/best-sellers`). Sale lookup goes
through `GET /api/v1/sales` drilling into `GET /api/v1/sales/{saleId}` — the **only** path to
another till's sale detail. Local SQLite is no longer read for any report.
*Also in this WP (risk #3, now scheduled):* the **return/refund flow switches to the same server
lookup**. `TillViewModel.ExecuteReturn` today validates against a locally-stored prior sale only —
once sales sync centrally, a customer returning an item bought on another till (or on this till
before a reinstall) has nothing local to find. Resolve the sale via `GET /api/v1/sales/{saleId}`,
enforce refund-remaining against the server record, and keep the local path only as the offline
fallback for sales still in this till's rolling window. This is a money path: it lands with
Reporting because it reuses the identical lookup, but its DoD is a hard gate.
*DoD:* two tills each push one sale; either till's Summary for that business day shows the
**combined** figures, not just its own; from till A, drilling into a `saleId` rung up on till B
renders identical lines/tenders/adjustments as seen from B; **a refund on till A against a sale
made on till B validates, caps at the refundable remainder, and posts**; offline, a sale inside
the rolling window still refunds and one outside it is refused with a clear "needs connection"
message — never a silent acceptance.

**WP12 — Loyalty.** Confirmed **zero** in both MAUI projects. No backend work needed — pure
consumption. Customer search/attach on the sale screen (`GET /api/v1/customers?search=`, then a
live `GET /api/v1/customers/{id}` for balance and membership), a create/edit dialog gated on
`perm:CustomersManage`, a store-credit tender mirroring the web till's synthetic `CREDIT_PAYID`,
and a management list off `GET /api/v1/loyalty`.
The hard part is **offline design**, and the rule is strict: a bounded local `LoyaltyCache`
(CustomerId, Name, Tier, AutoDiscountRate, CreditBalancePenceAsOf, RefreshedAtUtc) serves offline
name/tier/discount lookup and a discount hint **only** — it is *never* an input to redemption maths.
The credit tender needs all three of: customer attached, live balance > 0 fetched **this session**,
device online. No live fetch, no tender — exactly as the web till behaves.
*DoD:* airplane mode — cached name/tier/discount still shows, credit tender is **absent**;
reconnect → tender reappears with the live balance; two devices racing to redeem the last credit →
exactly one succeeds, the other gets `InsufficientCreditException`, confirming the append-only
ledger (D15) prevents double-spend.

**WP13 — Gift cards (§3a ruling).** Sell and redeem at the till, mirroring the web till
(`till/basket.ts` + `CheckoutDialog.tsx` are the reference): sell = a `GIFT-CARD` catalogue line
carrying the code, redeem = a tender, both **online-only** like store credit. Management (minting,
voiding, balance moves) stays portal-side — that needs `giftcards.manage`; selling only needs
`pos.sell`. ⚠ **The VAT-treatment gate is the sharp edge**: `GiftCardSettings`' absence 409s
generate/activate/redeem per tenant. The till must catch that 409 and say *"Gift cards aren't set
up for this company yet — an owner decides their VAT treatment in the portal first"*, never a raw
error. Under multi-purpose (Kapow's declared treatment) an activation posts **zero VAT** — the
provisioned `GIFT-CARD` item handles this; do not invent VAT lines.
⚠ **The treatment forks REDEMPTION mechanics too** (corrected 2026-08-08 — "redeem = a tender"
above is the multi-purpose shape only): under **single-purpose**, redemption is a **negative
standard-rated `GIFT-CARD` line** (`api.ts:997-1019`), because the card's VAT was declared when it
was sold and a plain tender would declare it twice. Mirror the webtill's shape per treatment.
*Known hazard, both tills, out of this WP's scope:* that redemption line hardcodes `/1.2` — a
standard-rate change breaks it on the webtill exactly as it would here. Noted for whenever WP2b's
bands gain their first real rate change; gift-card lines are exempt from ingest validation, so it
mis-states embedded VAT rather than quarantining.
*DoD:* sell → activate → redeem round-trips against a local backend with `GiftCardSettings` set —
**under both treatments, asserting the single-purpose negative-line shape**; the same flow on a
tenant WITHOUT settings surfaces the friendly 409 message at the first step; redemption offline is
hidden, like store credit; a redeemed card's remaining balance matches the portal's view of the
same card.

---

## 7. Risks this plan does not yet solve

Flagged now rather than discovered mid-build. Several need a decision before the WP that hits them.

1. ~~Existing local till data at enrolment~~ — **RESOLVED by binding default §9.3**: archive,
   never merge, never delete. The residual risk is an operator skipping the archive step; the WP4
   first-run flow refuses to enrol while an un-archived legacy file exists.
2. ~~Receipt-print vs outbox-commit ordering~~ — **RESOLVED, designed into WP3**: commit first,
   print from the committed payload. A print failure after commit is a reprint problem, not a
   money problem.
3. ~~Cross-till refund lookup~~ — **RESOLVED, scheduled**: now an explicit, hard-gated part of
   WP11 (server lookup for the money path, local fallback only within the rolling window).
4. **Stolen hardware.** Local-only operator login (WP8) means a stolen till carries cached
   credentials with no server-side kill switch, giving an attacker offline access up to that till's
   refund ceiling.
5. **GDPR, compounding #4.** WP12's `LoyaltyCache` holds customer PII. `RetentionSweeper` +
   `DeletionSchedule` handle erasure centrally — nothing propagates that to purge a till-side
   cache. A deletion request could be honoured centrally while a copy persists on till hardware.
6. **Fleet updates are hand-waved.** `426 Upgrade Required` is named, but not what the till does on
   receipt, nor how a binary update physically reaches hardware across many shop networks.
7. **Mixed versions in the field** during rollout — some tills outbox-wired, some not, both hitting
   the same backend.
8. **Card terminals are greenfield.** Grepped for Stripe/SumUp/Worldpay/Adyen/Zettle across the
   backend and both MAUI apps: **zero terminal-integration hits**. This is not "blocked on a
   decision" — there is no terminal code to build on. One nuance: the *gateway-awareness* layer
   already exists (WP17.2 config + the web till's `GET /api/v1/payments/gateway/active` display at
   checkout), so MAUI should copy that display cheaply; only the actual terminal drive is
   greenfield. Affects both tills; independent of this plan.

---

## 8. Testing

- ⚠ The "existing Appium suite (PR #8)" named by the superseded sync plan is **not in this
  branch** (verified 2026-08-07 — `Plutus.Frontend.AppClient.Tests` is a small xunit/Moq unit
  project). Keep THAT project green as the regression gate; UI regression is USER-VERIFY until an
  in-branch UI suite exists. Do not go looking for the Appium suite.
- Add an offline-mid-checkout fixture (mock connectivity gate): the sale still completes and lands
  in the outbox. Plus an online-transition test: queued sales drain correctly.
- Unit-test the client outbox retry contract against `OutboxDrainer`'s semantics: stay Pending,
  5s→5min backoff, retry forever on network failure.
- An integration test hitting real `/api/v1/tills/enrol` + `/api/v1/sales` against a disposable
  test tenant.
- Explicitly assert store-credit **stays disabled offline**. A regression there is silent
  data-integrity damage, not a UX gap.

---

## 9. Decisions — BINDING DEFAULTS for an autonomous build

An agent building from this document follows these **without asking**. Matt can veto any of them
before (or after — most are cheap to change) the relevant WP starts. This is the same contract
`further-enhancements-plan.md` used, and it held.

| # | Default (binding) | Used by |
|---|---|---|
| 1 | **AppClient is the go-forward app.** Harvest from ClientUI exactly two things — `Colors.xaml` (WP7) and the repository *interface shape* (WP1), never its implementation — then remove ClientUI from `Plutus.slnx` in WP7. Do **not** delete its directory; it stays as harvest source and history, marked retired. | Everything |
| 2 | **Offline credentials = synced password hashes verified locally** (`OfflineMode` §4.3 Option B) via `Plutus.SharedKernel.Pbkdf2`, byte-identical to the server. Not a new invention — the legacy Kapow DB carried `HashedPassword`+`Salt`; this restores the original design. No local-PIN interim step. | WP8 |
| 3 | **Existing local till data at enrolment: archive, never merge, never delete.** First-run takes a timestamped copy of the legacy SQLite file into a designated upload folder (the NatApp-Translation-Agent's input), then builds the v2 store from the server catalogue. Parked baskets import best-effort (WP2); local sales history lives only in the archive — history queries go to the server (WP11). Enrolment **refuses to proceed** until the archive step has succeeded. | WP2, WP4 |
| 4 | **Migrate first, enrol second.** A till enrols only after its store's legacy data has run through the translation agent — enforced technically by WP2's cutover check: spot-checked barcode→item IDs must equal the central migration's IDs, and the tool **stops** on mismatch (§10). | WP2, WP4 |
| 5 | **Legacy `TillController` in `Plutus.DBService`: deprecate, don't delete.** Mark `[Obsolete]` + doc-comment pointing at `Plutus.Tenancy`'s `TillsController`, note it in HANDOVER. Removal is a separate cleanup once MAUI is live — deleting mid-retrofit risks the NatApp still trading in the shop. | WP4 |
| 6 | **Card capture stays out of scope** (⏸ both tills, pending a provider decision — risk #8). MAUI copies the web till's gateway-*awareness* display only. | — |
| 7 | **§3a rulings stand**: everything found by the parity audit is IN, homed per §3a/§4 (gift cards = WP13, platform citizenship = WP5b, receipt template + refund baskets = WP3, users = WP8, cross-till refunds = WP11, Bin/untracked/add-unknown = WP10, theming = WP7). | §4 order |

---

## 10. Relationship to `NatApp-Translation-Agent-Plan-2026-08-05.md`

That document moves **legacy shop data** (the old NatApp SQLite backup) into the new backend. This
one makes **a till talk to** that backend. They are independent and can run in parallel, meeting at
exactly one point:

> **The item-ID remap must be deterministic** (or exported and imported), so that item IDs on a
> cutover till equal the item IDs the central migration produced. If it isn't, a till's local
> catalogue and the server's disagree about what a barcode means — and every sale that till pushes
> attributes stock and revenue to the wrong item.

That constraint appears as a hard stop in WP2's DoD. Nothing else couples the two plans.
