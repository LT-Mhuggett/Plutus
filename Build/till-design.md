# Till design — the single source of truth for every till build

**Every till Plutus ships is designed here.** What the surfaces are, what each can do, and where
every shared rule lives. If a question is about a till, the answer is in this document or it belongs
in this document.

> ### The binding rule
>
> **Any work on any till reads this document first and updates it in the same commit.**
>
> A capability isn't done until its row exists in Part B, with every other till marked ✅ or a
> deliberate ⬜. A *rule* isn't done until Part C says where it lives — and, if it exists in more
> than one place, what stops the copies drifting.
>
> This is what keeps till versions in sync. Not memory, not review, not good intentions.

**Legend:** ✅ built · 🟡 partial · ⬜ absent · ➖ not applicable · ⏸ blocked externally.
Last verified against code **2026-08-08**. Retrofit WPs refer to
[`To do/MAUI-Retrofit-Plan-2026-08-07.md`](To%20do/MAUI-Retrofit-Plan-2026-08-07.md).

*Consolidated 2026-08-08 from `till-parity.md` (features) and `till-anatomy.md` (build + rules) at
Matt's instruction — the two questions were always one question, and splitting them meant a till
could look complete in one document while being wrong in the other.*

---

## Why this document exists

Two failures, a week apart, and neither was carelessness.

**The web till gained nine features in eight days** at the end of July. The MAUI parity analysis
written on 2026-08-01 was already missing several of them by 2026-08-07 — because there was nowhere
for a gap to show up. Prose written once decays; a table every feature has to touch does not.
*That's Part B.*

**The VAT arithmetic existed only in the web till's TypeScript**, described in the retrofit plan as
"the reference implementation" — meaning every other till was expected to re-derive it by reading
someone else's language and getting it right. There were already three partial copies, including a
hardcoded UK VAT band list inside a client library. A feature register could never have shown this:
it read ✅ / ✅, because both tills *do* have VAT. Two tills that disagree by a penny on the same
basket disagree on **every VAT return, forever**, and nothing flags it. *That's Part C.*

---

# Part A — What the tills are

## A1. The surfaces

Everything that can produce a sale, and what it's made of.

| Surface | Technology | Runs on | Local state | Reaches the server via |
|---|---|---|---|---|
| **Web till** — `Plutus.Frontend.WebApp` | React 19 + TypeScript, Vite | Any browser | IndexedDB (outbox + catalogue cache) | `POST /api/v1/sales`, plus some legacy `/api/*` |
| **MAUI till** — `Plutus.Frontend.AppClient` | .NET MAUI (Sean's rework, NatApp lineage) | `net10.0-windows`, `-android`, `-ios` | SQLite — legacy schema today, local store v2 at cutover | Not yet — WP5+ wires it through `Plutus.Client.Core` |
| **Webstore connector** | Backend module `Plutus.Webstore` | Server | — | Its sink builds an `IngestSaleRequest` and calls `SalesIngestService` **directly** |
| **Hardware agent** — `tools/Plutus.TillAgent` | WinForms tray app + Kestrel on `127.0.0.1:9123` | `net10.0-windows` | Token in local config | Not a sales path — it prints and kicks the drawer for the *browser* till |
| **Portal** — `Plutus.Frontend.Portal` | React 19 + TypeScript | Any browser | — | The **source of truth**, not a till: it publishes what tills obey |
| ~~`Plutus.Frontend.ClientUI`~~ | MAUI | — | — | **Abandoned port, being retired.** Kept only to harvest its colour palette. Do not build on it. |

⚠ **The webstore is a sales channel, and it is easy to forget.** It doesn't look like a till, but it
writes sale lines, so every rule in Part C applies to it. It was the channel found sending no VAT
band at all.

## A2. The shared spine

The libraries any .NET till uses. All are plain `net10.0` — no MAUI, no UI, no OS-specific target.

| Project | What it holds | Rule |
|---|---|---|
| `src/Plutus.SharedKernel` | **The rules.** Money, VAT arithmetic, ids, permissions, band resolution. | No project references at all. |
| `src/Plutus.Contracts.Client` | The wire contract (DTOs + the `LineMeta` envelope). | **No references, no packages** — it ships onto tills. |
| `src/Plutus.Client.Core` | Outbox engine, pusher, API client, device-token provider. | May reference only SharedKernel + Contracts.Client. |
| `src/Plutus.Client.Storage` | Local store v2 (SQLite) + cutover. The only place that knows SQLite. | As above. |

**Three architecture tests hold this, so it can't rot by accident:**

| Test | What it refuses |
|---|---|
| `Till_client_libraries_stay_free_of_MAUI_and_backend_modules` | A UI/MAUI package, or a reference to a backend module, in a till library. |
| `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own` | An OS-specific target framework in a till library, or a literal VAT rate in one. |
| `No_module_references_another_module` | Backend modules reaching into each other. |

---

# Part B — What each till can do

**The rule:** when you add a capability to any till, add its row here in the same commit. If another
till isn't getting it, say so in Notes and why. A deliberate ⬜ is a decision; a missing row is a
surprise six months later.

## B1. Selling (the Till screen)

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Scan / search / manual add | ✅ | ✅ | `GET /api/Item` | |
| Basket qty, price adjust, reorder | ✅ | ✅ | — | |
| Discounts | ✅ | ✅ | — | |
| Returns / refund lines | ✅ | 🟡 | — | MAUI validates against a **local** prior sale only — breaks once sales sync centrally. Retrofit risk #3. |
| **Refund-only baskets** (negative sale) | ✅ | ⬜ | `POST /api/v1/sales` | Shipped 2026-08-07. Invariants are sign-agnostic, so it's client work only. |
| Park / retrieve basket | ✅ | ✅ | — | MAUI must reserialise as contract JSON, no `$type` — WP2. |
| Split payment / change | ✅ | ✅ | — | |
| **Gift cards — sell & redeem** | ✅ | ⬜ | `/api/v1/giftcards/*` | **Whole feature missing.** Zero references in MAUI. ⚠ Gated on the per-tenant VAT-treatment decision — `GiftCardSettings`' absence **409s** generate/activate/redeem, so a MAUI till that doesn't understand this gets errors it can't explain. Not in any WP yet. |
| Customer attach at sale + auto-discount | ✅ | ⬜ | `/api/v1/customers` | WP12 |
| Member-number scan-to-attach | ✅ | ⬜ | `/api/v1/customers` | FE2 `NNNNNNC` barcode. WP12 |
| Store credit as tender | ✅ | ⬜ | `.../credit/redeem` | Online-only by design. WP12 |
| **Add unknown scan as a new item** | ✅ | ⬜ | — | Shipped 2026-08-07. Small; fold into WP10. |
| Effective pricing at basket-add | ✅ | ⬜ | `GET /api/v1/prices/effective` | Was till-retrofit row 1. MAUI still reads the legacy `Item.price`. WP5 (offline cache) + WP10. |
| Card capture events | ⏸ | ⏸ | `POST /api/v1/payments/events` | Blocked on a provider for **both**. Terminal integration is greenfield — but note the till already *displays* the configured gateway (next row). |
| Payment-gateway awareness at checkout | ✅ | ⬜ | `GET /api/v1/payments/gateway/active` | WP17.2 — checkout shows the standalone hint or the selected provider with "integration pending" (`CheckoutDialog.tsx:226-233`). |

## B2. Receipts & hardware

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Silent receipt print | ✅ | ✅ | — | Different transports, same outcome: web goes through `Plutus.TillAgent`, MAUI drives OPOS directly. MAUI does **not** need the agent. |
| Cash drawer kick | ✅ | ✅ | — | As above. |
| **Portal-controlled receipt template** | ✅ | ⬜ | `GET /api/v1/stores/{id}/receipt-template` | Shipped 2026-08-06. MAUI's layout is **hardcoded C#** (`PosPrinterManager.cs:138-167`) — it *does* print the store's name/logo/address/VAT from the locally-synced StoreModel, but it ignores the portal template entirely (header/footer lines, toggles), contradicting the "all receipts controlled from the portal" requirement. Not in any WP yet. |
| Reprint from a past sale | ✅ | 🟡 | `GET /api/v1/sales/{saleId}` | MAUI reprints locally only; cross-till needs WP11. |
| Barcode on receipt (sale id) | ✅ | ✅ | — | |

## B3. Tabs the MAUI app has no equivalent for

| Area | Web | MAUI | Retrofit |
|---|:--:|:--:|---|
| **Cash** — float, paid in/out, X, Z | ✅ | ⬜ | WP9. MAUI has only `POSCashDrawer.cs`, a solenoid driver. |
| **Loyalty** — members, tiers, credit | ✅ | ⬜ | WP12. Zero references in MAUI. |
| **Users** — employee list/create + set password (legacy `/api/Employee`, `/api/Auth/SetPassword`) | ✅ | ⬜ | **Not in any WP.** MAUI's add-user command is a stopgap dialog reading *"not available in this version yet"*. Roles and effective-permissions management are **portal-side**, not till capabilities — the MAUI parity target is this smaller surface. Overlaps WP8 (RBAC sync) but is a distinct screen. |

## B4. Tabs both have, in different shapes

| Area | Web | MAUI | Retrofit |
|---|:--:|:--:|---|
| Inventory CRUD | ✅ | ✅ | — |
| Stock as a movement ledger | ✅ | ⬜ | WP10. MAUI writes a flat quantity column; concurrent edits are last-write-wins. |
| VAT-band consistency guard | ✅ | ⬜ | WP10 |
| **Portal-published VAT bands** (`GET /api/v1/vat/bands`) | ✅ | ⬜ | **WP2c shipped 2026-08-08 (backend + web till).** The web till caches the bands and their whole effective-dated timeline at boot + every 60 s, and its last hard-coded VAT rate (the single-purpose gift-card `/1.2`) is gone. MAUI must consume the same contract before it prices anything — `PlutusApiClient.GetVatBandsAsync` + `VatBandsResult.RateBpAt` already exist for it. ⚠ **Caching only "today's rate" is a bug**: the timeline is what lets an offline till apply a future-dated change on the day. |
| **VAT band on the sale line** (`LineMeta.vatBand`) | ✅ | 🟡 | **WP2c-exempt, 2026-08-08.** Zero-rated and exempt both declare `vatRateBp = 0`; the band is the only thing separating them, and it decides whether input tax is recoverable (Notice 706). **MAUI SHOULD send it** — resolve from the item's legacy `TaxId` against each band's published `legacyTaxIds`; `LineMeta.VatBand` exists in `Plutus.Contracts.Client`. **Leave it null rather than guessing** when the portal hasn't mapped the tax row. 🟡 not ⬜ because **the server backfills any line that arrives without one** (`VatBandStamp`, applied in `SalesIngestService` — the single choke point every channel passes through), so MAUI is correct-by-default and only needs to send it for cases the catalogue can't know. A single-purpose gift-card activation/redemption is `"standard"` by the voucher treatment, not its catalogue row — that IS such a case. |
| Category create / rename / reassign / delete | ✅ | 🟡 | WP10. MAUI creates locally; no reassign UI, so the server's 409 has nowhere to land. |
| The Bin (soft delete) + untracked stock | ✅ | ⬜ | FE5 concepts MAUI has no model for. Fold into WP10. |
| Reporting — cross-till, server-aggregated | ✅ | ⬜ | WP11. MAUI queries its **own** SQLite, so it can only ever show one till. |
| Store Information | read-only ✅ | local editor ⬜ | WP6 — the two are *inverted*; this is a deletion, not a build. |
| Settings — local prefs | ✅ | ✅ | — |
| Settings — device enrolment & identity | ✅ | ⬜ | WP4. The largest missing sub-area; no local equivalent to extend. |
| First-time startup / setup | ➖ | ✅ | MAUI-only, and correct — a browser has no first run. |

## B5. Platform citizenship

Everything here exists in the web till and is **absent from MAUI**. None of it was in the original
parity analysis.

| Capability | Web | MAUI | Backend | Notes |
|---|:--:|:--:|---|---|
| Offline trading + outbox | ✅ | 🟡 | `POST /api/v1/sales` | MAUI is offline-*only* — it has local persistence but nothing to sync to. WP3. |
| Device enrolment | ✅ | ⬜ | `/api/v1/tills/enrol` | WP4 |
| Un-enrol request + manager approval | ✅ | ⬜ | `/api/v1/tills/unenrol-request` | WP4 |
| Heartbeat / fleet status | ✅ | ⬜ | *general till heartbeat missing — build it (WP5)* | The web till's presence comes via device enrolment + the agent-status route below; a proper `POST /api/v1/heartbeat` for any till is still to build. |
| Hardware-agent telemetry (agent version/health → Locations) | ✅ | ➖ | `POST /api/v1/tills/agent-status` (`TillsController.cs:283`) | FE3.0. ➖ for MAUI: it drives OPOS directly and has no agent to report on. |
| **Pick-from-floor notifications** (web sale sold shop-floor stock) | ✅ | ⬜ | `GET /api/v1/notifications?unackedOnly=true` + `POST /api/v1/notifications/{id}/ack` | Phase 6. Banner + acknowledge on the till (`App.tsx:264-272`), polled on the 60s cadence. Not in any WP. |
| **Announcements banner** (maintenance/incident) | ✅ | ⬜ | `GET /api/v1/announcements/active` | Not in any WP. Cheap — one poll, one banner. |
| **Help / support tickets** | ✅ | ⬜ | `/api/v1/support/tickets` | Not in any WP. Closes the `support-heavy` churn signal. |
| App-update prompt | ✅ | ⬜ | — | Web polls a build stamp; MAUI needs the `426 Upgrade Required` path in WP5. |
| **Portal-controlled theming** (colour schemes pushed to stores / tills / groups) | ✅ | ⬜ | `GET /api/v1/themes/effective` + `/api/v1/themes` CRUD | Shipped 2026-08-07. Built-in light/dark + custom schemes, assigned per tenant/store/group/till from Locations. MAUI: consume the same effective endpoint and map the eight slots onto XAML resources — folded into retrofit WP7. |
| Operator RBAC + offline login | ✅ | 🟡 | *`tills/{id}/operators` missing* | MAUI logs in locally with no server-derived permissions. WP8. |

---

# Part C — Where the rules live

Part B answers *can this till do X*. Part C answers **would every till get X right** — which is a
different question, and the one that produces silent money bugs.

## C1. The rules register

For each cross-cutting rule: where the one implementation lives, who else implements it and why,
and what stops them drifting.

### Money and VAT

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| **Money is integer pence** | Everywhere; `SharedKernel/Money.cs` | — | `No_module_declares_decimal_or_double_money_members` |
| **A line's VAT figures** (rate from the price pair, VAT = gross − ex, discount scaled by ex/inc, returns negated with the discount dropped) | `SharedKernel/VatLineMath.cs` | Web till `api.ts` checkout — **deliberate**, it's TypeScript | `VatLineMathTests` (.NET side only — see C2) |
| **Which VAT rate applies** | The portal. `GET /api/v1/vat/bands` publishes the whole effective-dated timeline | None — **no till may hold a rate** | `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own` |
| **Which VAT band a line was sold under** | `LineMeta.vatBand` on the wire; backfilled server-side by `Plutus.Entities/VatBandStamp.cs` when a channel sends none | Client may state it (and then wins) | `VatBandsE2eTests` |
| **Legacy tax row → band** | `SharedKernel` `VatBandResolution` + the portal's `VatBandTaxMap` | — | `VatExemptBandTests` |
| **Band snap tolerance** (25bp) | `VatAccounting.BandSnapToleranceBp` — one constant | — | `VatExemptBandTests` |
| **Output tax on a return** (VAT fraction × takings) | `SharedKernel/VatAccounting.cs` — **server only**, a till never computes a return | — | `VatAccountingTests` |
| **Was the rate legal at the time of sale** | `SharedKernel/VatRates.cs` `VatRateHistory.Assess` — server-side at ingest | — | `VatRateChangeE2eTests` |
| **Which HMRC rules the platform applies** | `SharedKernel/VatGuidance.cs` — served to the portal's VAT → Rules tab | — | Rendered from code, so it cannot drift from behaviour |

### Identity and sale shape

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| **Item id from a barcode** | `SharedKernel/DeterministicGuid.cs` `ForItem(businessId, itemIdOne)` | Web till `pipeline.ts` `itemGuid()` — **deliberate** | Frozen golden vector in `LegacySaleBridgeTests` — see C2 |
| ⚠ **The barcode is the real invariant**, `ItemId` rides along | `SaleLine.ItemIdOne`, carried in `LineMeta.itemIdOne` | — | Retrofit plan §10 |
| **Entity ids are UUIDv7** | `SharedKernel/Uuid7.cs` | Web till `pipeline.ts` `uuidv7()` — **deliberate** | `No_module_mints_entity_ids_with_Guid_NewGuid` (.NET side) |
| **The four sale invariants** | `SaleV2.Validate()` — enforced at ingest, so a client cannot diverge undetected | — | `SalesV2Tests` |
| **Ingest status policy** — 201 recorded · 200 duplicate · 202 quarantined (never retry) · 400 skip | `Plutus.Client.Core` `OutboxPusher` | Web till `pipeline.ts` | `TillOutboxSoakE2eTests`, `ClientCoreE2eTests` |

### Portal decides, till obeys

Each follows the same shape: the portal owns it, the till caches it on the sync cadence and renders
it. **A till that computes one of these locally is a bug.**

| Rule | Contract | Notes |
|---|---|---|
| Receipt layout | `GET /api/v1/stores/{id}/receipt-template` | The house exemplar for this pattern. ⚠ Receipts are deliberately immune to theming. |
| Colour scheme | `GET /api/v1/themes/effective` | Resolves till > group > store > tenant > default server-side. |
| VAT bands | `GET /api/v1/vat/bands` | See above. |
| Store details | `GET /api/v1/stores/{id}/info` | ⚠ Carries the **legacy `businessId`**, which is not the tenant id. |
| Prices | `GET /api/v1/prices/effective` | Web till only so far. |
| Permissions | Token carries the user's full effective set; `perm:*` resolves from RBAC by userId | ⚠ `"perm:x"` and `PlutusPolicies.X` are different namespaces — a typo between them fails closed and silently. |
| Gift-card VAT treatment | `GiftCardSettings` — single- vs multi-purpose | Locks at the first card sale. Absence 409s. |

## C2. The drift register — where the same rule exists twice

**This is the section that decays invisibly, and the one to read before writing anything that
computes money on a client.**

The web till is TypeScript and everything else is .NET, so some rules genuinely exist twice. That is
a deliberate cost. What matters is being honest about which twins are tested and which are trusted.

| Twin | Status | The honest position |
|---|---|---|
| **VAT line arithmetic** — `VatLineMath` ↔ `api.ts` | 🟡 **half-pinned** | `VatLineMathTests` fixes the .NET side to the numbers the web till produces, including the JS-vs-.NET midpoint-rounding trap. Nothing executes the **TypeScript** against those numbers, so a change to `api.ts` would not fail a test. |
| **Item id derivation** — `DeterministicGuid.ForItem` ↔ `pipeline.ts itemGuid` | 🟡 **frozen vector** | `LegacySaleBridgeTests` asserts a hardcoded GUID the TS produced in a 2026-07-24 smoke test. It will catch .NET drift. It will **not** catch TS drift. Getting this wrong corrupts item ids silently — stock still moves, because lines key on the barcode. |
| **Basket totals / ex-VAT apportionment** — `VatLineMath` ↔ `till/basket.ts basketTotals` | ⚠ **third copy, unpinned** | `basketTotals` re-implements the same discount apportionment for the on-screen total. It must agree with the checkout payload or the screen and the receipt disagree. Nothing enforces it. |
| **Item search matching** | ⚠ **unshared, and results differ** | The web till's word-matching (`batman one` → *Batman Year One*) and `"quoted"` exact-phrase are a **client-side device pref**; the server default is whole-phrase. A MAUI till searching a cached catalogue offline returns *different results for the same query*. WP1 puts a shared matcher in `Plutus.Client.Core` — this is why that matters. |
| **Money representation** | ⚠ **divergent today** | Web is integer pence throughout; MAUI is `decimal` across 11 model files. WP2. |
| **UUIDv7** — `Uuid7` ↔ `pipeline.ts uuidv7` | ➖ **low stakes** | Only has to be a valid, time-sortable v7. Divergence costs ordering, not money. |
| **Business day** — the wire value ↔ `pipeline.ts businessDay` | ➖ | A till's day is deliberately wall-clock, not UTC. The server takes what it is given. |

### ⚠ The root cause, stated plainly

**The web till has no test suite.** `package.json` has `dev`, `build`, `preview` and `typecheck` —
no test runner, no test files. So every "pinning" test above holds only the .NET half of its twin.
TypeScript changes are held by `tsc --noEmit` (types) and by review (behaviour).

### What the server catches regardless — and what it does not

The real safety net is that ingest refuses malformed sales, so a divergent client is usually caught
at the door rather than in a return three months later.

**`SaleV2.Validate()` rejects a sale unless:**
1. it has at least one line;
2. every line's `LineGrossPence == unit × qty − discount`;
3. `GrossPence == Σ line gross`;
4. `VatPence == Σ line VAT`;
5. net tender (`Σ amount − Σ change`) `== GrossPence`.

**`VatRateHistory.Assess` additionally** judges each line's *price pair* against the bands in force
at the moment of sale, and quarantines a till trading on a superseded rate.

⚠ **The gap those leave.** Validation checks that the header agrees with the lines — it does **not**
check that a line's `vatAmountPence` is the *right* figure for its price pair. A till that computed
VAT by rate arithmetic instead of `gross − ex` would be internally consistent, pass all five
invariants, pass the band check, and be wrong by a penny on about a third of standard-rated lines.
**That is the precise shape of the bug shared code prevents and validation does not.**

---

# Part D — Working with this

## D1. Not yet in any work package

Found by audit; the retrofit plan predates them. Decide whether each is in or out:

1. **Gift cards** (B1) — a whole feature, and the VAT-treatment gate makes it more than a screen.
2. **Portal-controlled receipt templates** (B2) — contradicts a stated requirement today.
3. **Users / employee management** (B3) — a whole screen.
4. **Announcements** and **Help/support tickets** (B5) — both small, both platform-citizenship.
5. **Refund-only baskets** and **add-unknown-item** (B1) — both small, both shipped 2026-08-07.
6. **The Bin + untracked stock** (B4) — inventory concepts with no MAUI model.
7. **Pick-from-floor notifications** (B5) — small; poll + banner + acknowledge.
8. **Theming** (B5) — consume `GET /api/v1/themes/effective`; already named in WP7, needs its scope
   extended from "port the palette" to "apply the pushed theme".
9. **A test runner for the web till**, and pinning `basketTotals` (C2) — the two open items from the
   drift register. Both are Matt's call.

## D2. Adding a till

1. Reference `Plutus.Contracts.Client` + `Plutus.Client.Core` (+ `.Storage` if it trades offline).
   Do not re-implement the wire contract, the outbox, or the VAT arithmetic.
2. Read the portal contracts in C1 and cache them on the sync cadence. Hold no rates, no receipt
   layout, no colours of your own.
3. Build sale lines with `VatLineMath`. If you find yourself writing `gross × bp / (10000 + bp)`,
   stop — that disagrees with the receipt the customer is holding.
4. Populate `LineMeta.itemIdOne` on every line. `StockProjectionConsumer` **silently skips** lines
   without it: accepted ≠ stock moved.
5. Add its column to Part B and its rows here.

## D3. Adding a feature, or a rule

- **A feature:** add the row in Part B in the same commit. Not afterwards. A deliberate ⬜ is fine;
  an empty row is not — say why in Notes.
- **A rule:** put it in `Plutus.SharedKernel` and add a row to C1. If it must exist twice, add a row
  to C2 saying what pins the copies — or, honestly, that nothing does.

## D4. Keeping this true

- **Verify against code, not memory.** Every ⬜ in Part B was checked by grepping both codebases;
  that is the standard, and it took under an hour.
- **C2 is the section that rots dangerously.** Parts A, B and C1 go stale *visibly* — a project
  appears, a contract moves, someone notices. C2 goes stale *invisibly*: a twin quietly added, or a
  pinning test quietly deleted, looks exactly like a document that is still true. So: **when you
  make a rule exist in two places, or stop one existing in two places, say so in C2 in the same
  commit.**
- **The API is the mechanical backstop.** Endpoint usage can be diffed without judgement:

  ```bash
  # every endpoint the web till calls — MUST exclude src/api/types.gen.ts: it is a GENERATED
  # OpenAPI type file whose path string-literals are not calls (including it injects ~30
  # endpoints the till never touches: purchase-orders, tenants, suppliers, roles, periods…)
  grep -rhoE '"/api/[^"`]*|`/api/[^`"$]*' Plutus/Frontend/Plutus.Frontend.WebApp/src \
    --exclude=types.gen.ts | sed 's/^[`"]//; s/[?&].*//' | sort -u
  ```

  Run the equivalent over the MAUI client once WP1 lands and diff the two lists. An endpoint one
  till calls and the other doesn't is either a missing row in Part B or a deliberate ⬜ — never a
  surprise. Two caveats: about a third of the web till's calls are still **legacy `/api/*`
  controllers** (Item, Employee, SavedTransaction, Auth/Login…), so a diff against a
  `/api/v1`-only MAUI client must not read those as parity gaps; and the diff won't catch
  behavioural drift (C2), which is why C2 exists as prose.
