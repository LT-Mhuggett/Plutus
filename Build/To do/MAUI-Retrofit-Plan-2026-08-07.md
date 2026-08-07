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

> **Confirm or veto this before WP1 starts.** Every work package below assumes it.

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

Six items are **not costed in any work package below**. Decide in or out before starting:

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

## 4. Build order

Each work package has a Definition of Done. Do them in order; **WP1 and WP2 gate everything else.**

| WP | Title | Why here |
|---|---|---|
| 1 | Shared contracts + client core | Nothing can call the API until the DTOs exist |
| 2 | Local store v2 + money/ID sweep | The wire format demands integer pence and UUIDs |
| 2b | VAT effective-dating (backend) | Compliance; independent, can run in parallel |
| 3 | Outbox + sale ingest | The core of the whole retrofit |
| 4 | Enrolment, device identity, Settings | Everything after this needs a device token |
| 5 | Heartbeat + catalogue sync | Fleet citizenship |
| 6 | Store Information (read-only) | Smallest lift; proves the "portal is the truth" pattern |
| 7 | Theming | Cheap, visible, unblocks nothing — do it when you want a win |
| 8 | Operator RBAC + offline login | Needs the new endpoint; replaces local-only auth |
| 9 | Cash | Self-contained, well-specified contract |
| 10 | Inventory + stock ledger | Rework of screens that already exist |
| 11 | Reporting | Rewrite local queries → backend calls |
| 12 | Loyalty | Highest effort, hardest offline design; last so it reuses everything |

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
Meta            (Key TEXT PK, Value TEXT)              -- deviceId, tenantId, tillId, storeId,
                                                       -- deviceSeq, catalogueVersion, schemaVersion
CatalogueItems  (Id BLOB PK, Name TEXT, Kind INTEGER, PricePence INTEGER,
                 VatRateBp INTEGER, CategoryId BLOB, BandData TEXT NULL, UpdatedAtUtc TEXT)
Barcodes        (Code TEXT PK, ItemId BLOB)
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
v2 store, and seeds `CatalogueItems`/`Barcodes` from it via `Plutus.Migration.Kapow`. ⚠ The ID
remap **must** be deterministic, or exported and imported, so till item IDs equal central item IDs.
Confirm that against the backend implementation and **stop if it isn't** — this is the seam where
`NatApp-Translation-Agent-Plan` meets this plan.

*DoD:* no `decimal`/`double` money property survives in the MAUI assemblies (same regex rule the
backend arch test uses); a basket property test holds (total == Σ lines − discounts, change ==
tendered − total, all pence); cutover against a copy of the real Kapow file reproduces the
catalogue count and 10 spot-checked barcodes resolve to the **same UUIDs the central migration
produced**; park → kill → restore works and the serialised form contains no `$type`.

**WP2b — VAT-rate-change ingest compliance (backend).**
A till offline across a government VAT-rate change will push sales computed at a stale cached rate.
First **verify** whether `Plutus.Entities`' tax model already stores rates with an effective-from
date; add the history table only if it doesn't. Then extend the existing VAT-integrity guardrail so
`SalesIngestService` validates against the rate **in effect at each sale's `OccurredAtUtc`** — not
merely "is this a currently-valid rate". On mismatch, route into the existing
quarantine/reconciliation path (mirroring the Kapow migration's `VatReconstructed=1` flag). Never
silently accept, and never silently rewrite a customer-facing total.
*DoD:* a sale timestamped after a seeded rate-change boundary carrying the pre-change rate is
quarantined; one carrying the post-change rate ingests normally; one timestamped *before* the
boundary carrying the pre-change rate also ingests normally — no false positives.

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
*DoD:* soak — 1,000 sales offline, reconnect, all land exactly once in order with no server-side
`DeviceSeq` gaps; kill the app mid-drain, no loss or duplicates; one Failed sale does not halt
those behind it; 48h offline then reconnect drains clean; **a MAUI-originated sale moves stock**,
asserted explicitly, not just accepted.

**WP4 — Enrolment, device identity, Settings.**
Replace the `DatabaseProvider.Cloud` throw with a real first-run flow: enrolment code →
`POST /api/v1/tills/enrol {EnrolmentCode}` → `EnrolResult{DeviceId, ClientSecret, TillId, TenantId}`
(or **410 Gone** on a reused/expired/unknown code — surface it as a retryable message, not a crash)
→ store `DeviceId/TillId/TenantId` in `Meta`, and `ClientSecret` in platform `SecureStorage`,
**never** the SQLite file. Token client wraps `POST /api/v1/tokens/device`, refreshing at
`ExpiresInSeconds` minus a safety margin, with 401-retry-once around the pusher's calls.
Settings gains the server-backed half it has never had: device enrolment/identity with the
Active/PendingRemoval/Revoked lifecycle and manager-approved un-enrolment, server-validated till
renaming, a diagnostics panel (signed-in user, API reachability, business id), and sync-queue depth.
Keep the existing local preferences layer (printer config, checkout toggles) as-is.
*DoD:* fresh install enrols and survives restart without re-prompting; app killed mid-refresh still
has a valid token next launch; `ClientSecret` never appears in the `.db3` file (grep-verified);
server-side revocation parks the pusher with a clear "device revoked" state while sales keep
committing locally.

**WP5 — Heartbeat + catalogue sync.**
`POST /api/v1/heartbeat` every 60s with `{deviceId, appVersion, outboxDepth, oldestUnsyncedAge,
deviceClock}`; server derives ONLINE (<2min) / STALE (2–5) / OFFLINE (>5). Store lastSeen in a fast
store, **not** per-heartbeat MySQL writes. The response carries pull signals: `catalogueVersion`
newer than `Meta` triggers a sync, `syncNow` kicks the pusher, `lock` locks the UI to a "contact
your administrator" screen with local sales data untouched. Heartbeat failures are **silent** —
they must never disturb selling.
Catalogue sync is cursor-based: `GET /api/v1/catalogue/changes?since={version}` upserting
`CatalogueItems`/`Barcodes`, with effective-dated prices compared at lookup time so a scheduled
price change activates offline at the right moment. Runs on app start, on heartbeat signal, and on
a 15-minute timer — **never during an open basket**. Handle `426 Upgrade Required` with a banner;
selling continues, sync parks.
*DoD:* status transitions verified at the 2/5-minute boundaries with a fake clock; MySQL takes no
per-heartbeat writes; a price scheduled for 02:00 activates at 02:00 with the till offline; a 20k-item
full resync completes in <60s; a sale mid-sync sees a consistent snapshot — the price read at
basket-add is what's charged and what's in the payload.

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
*DoD:* union-merged grants correct for a multi-level (company+store+till) fixture; matrix test —
cashier sells but cannot refund, supervisor refund ≤ ceiling passes and > ceiling demands override,
a Saturday-only operator is rejected on Sunday (fake clock) — **all offline**; audit fields present
in the pushed payload.

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

**WP11 — Reporting.** MAUI's three Statistics viewmodels query local SQLite directly — zero HTTP.
Even a pixel-perfect copy of the web till's screens would show **one till's data** if built that
way, so this is a rewrite, not a feature add. Point Summary at
`GET /api/v1/reports/summary-rich?from&to`, the bucket chart at `/reports/summary`, the VAT table at
`/reports/vat`; replace `StockOuttakeViewModel`'s local join with `/reports/items-sold`; add two
screens MAUI has never had (`/reports/category-sales`, `/reports/best-sellers`). Sale lookup goes
through `GET /api/v1/sales` drilling into `GET /api/v1/sales/{saleId}` — the **only** path to
another till's sale detail. Local SQLite is no longer read for any report.
*DoD:* two tills each push one sale; either till's Summary for that business day shows the
**combined** figures, not just its own; from till A, drilling into a `saleId` rung up on till B
renders identical lines/tenders/adjustments as seen from B.

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

---

## 7. Risks this plan does not yet solve

Flagged now rather than discovered mid-build. Several need a decision before the WP that hits them.

1. **Existing local till data is never migrated.** Each live till has real sales and held baskets in
   its own SQLite file. Nothing says what happens to them at enrolment — orphaned, imported once,
   or lost. **Blocks WP4.** Overlaps `NatApp-Translation-Agent-Plan`; settle it there.
2. **Receipt-print vs outbox-commit ordering is undesigned.** If printing fires before the outbox
   commit and one fails, a customer holds a receipt for a sale that was never queued. Printing is
   physical and irreversible — this needs an explicit ordering decision in **WP3**, not silence.
3. **Cross-till refund lookup.** `TillViewModel`'s return flow validates against a **locally
   stored** prior sale. Once sales sync centrally, a customer returning an item bought on another
   till (or the same till after a reinstall) has nothing to look up. Needs the WP11 cross-till path
   applied to the **money-handling** refund route — materially higher risk than a report view, and
   in no work package above. **Schedule it explicitly.**
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

- Run the existing Appium suite **unchanged** through WP1–WP4 as a regression gate — enrolment
  should not alter basket or checkout flows.
- Add an offline-mid-checkout fixture (mock connectivity gate): the sale still completes and lands
  in the outbox. Plus an online-transition test: queued sales drain correctly.
- Unit-test the client outbox retry contract against `OutboxDrainer`'s semantics: stay Pending,
  5s→5min backoff, retry forever on network failure.
- An integration test hitting real `/api/v1/tills/enrol` + `/api/v1/sales` against a disposable
  test tenant.
- Explicitly assert store-credit **stays disabled offline**. A regression there is silent
  data-integrity damage, not a UX gap.

---

## 9. Decisions needed from Matt

| # | Question | Blocks |
|---|---|---|
| 1 | **AppClient as the go-forward app, harvest-then-retire ClientUI** (§1) — confirm or veto | Everything |
| 2 | Operator credentials offline: local PIN now, or synced password hashes verified locally (`OfflineMode` §4.3, A vs B)? Evidence favours B — the legacy Kapow DB carries `HashedPassword`+`Salt`, so it's a restoration, not an invention | WP8 |
| 3 | What happens to a till's existing local sales data at enrolment (risk #1)? | WP4 |
| 4 | Does a till enrol before or after its store's legacy data is migrated? | WP2, and the seam with NatApp-Translation-Agent |
| 5 | Legacy `TillController` in `Plutus.DBService` — deprecate, delete, or keep read-only? It bypasses tenant scoping, and two till-shaped endpoints once MAUI is live is a foot-gun | WP4 |
| 6 | Card-capture provider (risk #8) — affects both tills, independent of this plan | WP-none |

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
