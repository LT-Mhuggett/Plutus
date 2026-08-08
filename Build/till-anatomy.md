# Till anatomy — what each till is made of, and where every shared rule lives

Companion to [`till-parity.md`](till-parity.md). Two different questions:

| Ask | Document |
|---|---|
| *Can this till do X?* | [`till-parity.md`](till-parity.md) — the feature register |
| *How is this till built, and where does the rule for X actually live?* | **this document** |

Last verified against code **2026-08-08**.

---

## Why this document exists

On 2026-08-08 the VAT work turned up something a feature register cannot show. The rule for
"how a basket line becomes the VAT figures on the wire" existed **only in the web till's
TypeScript**, and the retrofit plan described that file as *"the reference implementation"* — meaning
every other till was expected to re-derive it by reading someone else's language and getting it
right. There were already three partial copies in the repo, including a hardcoded UK VAT band list
inside a client library.

Two tills that disagree by a penny on the same basket disagree on **every VAT return, forever**, and
nothing would flag it. `till-parity.md` would have shown ✅ / ✅ throughout: both tills *have* VAT.

So this document asks a different question of every cross-cutting rule: **how many implementations
are there, and what stops them drifting?** A rule with one implementation is safe. A rule with two
and a test that pins them is acceptable. A rule with two and no test is a latent penny bug.

---

## 1. The surfaces

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
writes sale lines, so every rule in §3 applies to it. It was the channel that turned out to be
sending no VAT band at all.

---

## 2. The shared spine

The libraries any .NET till uses. All four are plain `net10.0` — no MAUI, no UI, no OS-specific
target — so they are a build target on any platform rather than a port.

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

## 3. The rules register

For each cross-cutting rule: where the one implementation lives, who else implements it and why,
and **what stops them drifting**.

### Money and VAT

| Rule | The implementation | Second implementation | Pinned by |
|---|---|---|---|
| **Money is integer pence** | Everywhere; `SharedKernel/Money.cs` | — | `No_module_declares_decimal_or_double_money_members` |
| **A line's VAT figures** (rate from the price pair, VAT = gross − ex, discount scaled by ex/inc, returns negated with the discount dropped) | `SharedKernel/VatLineMath.cs` | Web till `api.ts` checkout — **deliberate**, it's TypeScript | `VatLineMathTests` (.NET side only — see §4) |
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
| **Item id from a barcode** | `SharedKernel/DeterministicGuid.cs` `ForItem(businessId, itemIdOne)` | Web till `pipeline.ts` `itemGuid()` — **deliberate** | Frozen golden vector in `LegacySaleBridgeTests` — see §4 |
| ⚠ **The barcode is the real invariant**, `ItemId` rides along | `SaleLine.ItemIdOne`, carried in `LineMeta.itemIdOne` | — | Retrofit plan §10 |
| **Entity ids are UUIDv7** | `SharedKernel/Uuid7.cs` | Web till `pipeline.ts` `uuidv7()` — **deliberate** | `No_module_mints_entity_ids_with_Guid_NewGuid` (.NET side) |
| **The four sale invariants** | `SaleV2.Validate()` — enforced at ingest, so a client cannot diverge undetected | — | `SalesV2Tests` |
| **Ingest status policy** — 201 recorded · 200 duplicate · 202 quarantined (never retry) · 400 skip | `Plutus.Client.Core` `OutboxPusher` | Web till `pipeline.ts` | `TillOutboxSoakE2eTests`, `ClientCoreE2eTests` |

### Portal decides, till obeys

Each of these follows the same shape: the portal owns it, the till caches it on the sync cadence and
renders it. **A till that computes one of these locally is a bug.**

| Rule | Contract | Notes |
|---|---|---|
| Receipt layout | `GET /api/v1/stores/{id}/receipt-template` | The house exemplar for this pattern. ⚠ Receipts are deliberately immune to theming. |
| Colour scheme | `GET /api/v1/themes/effective` | Resolves till > group > store > tenant > default server-side. |
| VAT bands | `GET /api/v1/vat/bands` | See above. |
| Store details | `GET /api/v1/stores/{id}/info` | ⚠ Carries the **legacy `businessId`**, which is not the tenant id. |
| Prices | `GET /api/v1/prices/effective` | Web till only so far. |
| Permissions | Token carries the user's full effective set; `perm:*` resolves from RBAC by userId | ⚠ `"perm:x"` and `PlutusPolicies.X` are different namespaces — a typo between them fails closed and silently. |
| Gift-card VAT treatment | `GiftCardSettings` — single- vs multi-purpose | Locks at the first card sale. Absence 409s. |

---

## 4. The same rule written twice, on purpose — and what actually holds it

The web till is TypeScript and everything else is .NET, so a handful of rules genuinely exist twice.
That is a deliberate cost, not an accident. What matters is being honest about which twins are
tested and which are trusted.

| Twin | Status | The honest position |
|---|---|---|
| **VAT line arithmetic** — `VatLineMath` ↔ `api.ts` | 🟡 **half-pinned** | `VatLineMathTests` fixes the .NET side to the numbers the web till produces, including the JS-vs-.NET midpoint-rounding trap. Nothing executes the **TypeScript** against those numbers, so a change to `api.ts` would not fail a test. |
| **Item id derivation** — `DeterministicGuid.ForItem` ↔ `pipeline.ts itemGuid` | 🟡 **frozen vector** | `LegacySaleBridgeTests` asserts a hardcoded GUID the TS produced in a 2026-07-24 smoke test. It will catch .NET drift. It will **not** catch TS drift. Getting this wrong corrupts item ids silently — stock still moves, because lines key on the barcode. |
| **Basket totals / ex-VAT apportionment** — `VatLineMath` ↔ `till/basket.ts basketTotals` | ⚠ **third copy, unpinned** | `basketTotals` re-implements the same discount apportionment for the on-screen total. It must agree with the checkout payload or the screen and the receipt disagree. Nothing enforces it. |
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
**That is the precise shape of the bug a shared implementation prevents and validation does not.**

---

## 5. Adding a till, or adding a rule

**A new till (any platform):**
1. Reference `Plutus.Contracts.Client` + `Plutus.Client.Core` (+ `.Storage` if it trades offline).
   Do not re-implement the wire contract, the outbox, or the VAT arithmetic.
2. Read the portal contracts in §3 and cache them on the sync cadence. Hold no rates, no receipt
   layout, no colours of your own.
3. Build the sale lines with `VatLineMath`. If you find yourself writing `gross × bp / (10000 + bp)`,
   stop — that disagrees with the receipt the customer is holding.
4. Populate `LineMeta.itemIdOne` on every line. `StockProjectionConsumer` **silently skips** lines
   without it: accepted ≠ stock moved.
5. Add its column to [`till-parity.md`](till-parity.md) and its row here.

**A new cross-cutting rule:** put it in `Plutus.SharedKernel`, and add a row to §3. If you must write
it twice, add a row to §4 saying what pins the copies — or, honestly, that nothing does.

---

## Keeping this honest

`till-parity.md`'s rule is *a feature isn't done until its row is updated in the same commit*. This
document's rule is narrower and easier to keep:

> **When you make a rule exist in two places, or stop one existing in two places, say so in §4 in the
> same commit.**

That is the only section that decays dangerously. §1–§3 go stale visibly (a project appears, a
contract moves) and someone notices. §4 goes stale invisibly: a twin quietly added, or a pinning test
quietly deleted, looks exactly like a document that is still true.
