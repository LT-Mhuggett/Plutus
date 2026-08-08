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
2. [`Build/till-design.md`](../till-design.md) — **the single source of truth for every till build**,
   and the register this plan exists to close. Its rule binds you: **a capability isn't done until
   its row is updated in the same commit**, and a *rule* isn't done until C1 says where it lives.
   ⚠ Read **C2, the drift register**, before writing anything that computes money on a client.
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

## 2a. VAT — the standing rules, and where they must come from

> **Added 2026-08-08 at Matt's instruction**, after checking HMRC guidance and the live Kapow data.
> **The governing principle: ALL VAT GUIDANCE COMES FROM THE PORTAL, DOWN TO THE TILLS.** A till —
> web or MAUI — never decides a VAT rule. It receives bands, applies them, and reports what it
> charged. Same shape as receipt templates (WP3) and themes (WP7).
>
> ⚠ **This is not yet true of the platform.** There is no portal VAT surface at all: `fetchTaxes`
> is READ-ONLY in both frontends, and the `Taxes` table is seeded legacy data with no effective
> dates and no editor. WP2b added `VatRatePoints` with no UI either. Closing that is **WP2c** below,
> and it should land before MAUI consumes any of it — otherwise the retrofit hard-codes a second
> copy of guidance that has no owner.

### The law (HMRC, checked 2026-08-08)

| Class | Rate | Taxable supply? | Input tax recoverable? |
|---|---|---|---|
| Standard | 20% | yes | yes |
| Reduced | 5% | yes | yes |
| **Zero-rated** | 0% | **yes** | **yes** |
| **Exempt** | none | **no** | **NO** |
| Outside scope | none | no | n/a |

**Zero-rated and exempt are not the same thing**, even though both charge the customer nothing.
Zero-rated is a taxable supply at 0% with full input-tax recovery; exempt is not a taxable supply
and *blocks* recovery of input tax attributable to it (partial exemption). They are different boxes
in the accounting and different money.

**Rounding.** HMRC's rounding-*down* concession is **explicitly not appropriate for retailers**
(VATREC12020). Permitted for retail: round up and down to the nearest 1p, or a published/bespoke
retail scheme. Plutus prices **VAT-inclusive** (both item editors do `exPrice = price / rate`, then
round to the penny), so the tax-inclusive price is the one the customer sees and the net is derived.

**Tax point.** VAT is accounted at the rate in force when the tax point occurs (Notice 700). For
retail that is the time of supply — the sale itself — which is why WP2b judges a line against
`OccurredAtUtc` and not against "now". That part is right.

### ✅ FIXED 2026-08-08 — the law decides, so these were corrected rather than asked about

Matt's instruction: *"I have not made rules. Whatever the UK government VAT rules are need to be
[followed]. Please look to fix what is broken."* All four defects below are now fixed and live.

| Was broken | Fixed |
|---|---|
| **VAT return summed per-line VAT.** HMRC Notice 727 §3.4.1 requires output tax = VAT fraction × takings at each rate. Summing thousands of penny-rounded lines understates it. | `/api/v1/reports/vat` now applies the VAT fraction to takings, and reports `vatChargedPence` + `roundingDifferencePence` alongside for reconciliation. **On live Kapow data the return was £10.77 light.** |
| **Takings were bucketed by DERIVED rate.** A till computes each line's rate from its price pair, so one 20% band arrived as 1993–2004bp — Kapow's return was split across **six** standard-rate buckets, which is not "the total value of sales at each rate". | Takings group by **band**; derived rates snap to the published band within 25bp. |
| **Off-band takings would have been folded into a real band.** Kapow has a genuine 2500bp line. | Reported as `unclassified` with its own bucket — never merged, never given an invented rate. |
| **Comics were classified Exempt.** HMRC Notice 701/10 zero-rates books, comics, magazines. Exempt **blocks input-tax recovery**; zero-rated does not — the wrong way round, and the expensive way. | `VatClass` added to the band model (Zero ≠ Exempt at the same 0%). Kapow's 14,740-item band reclassified **zero-rated** and relabelled "Zero rated (books)". **No money moved** — both are 0% output tax; only the recovery position changes, in Kapow's favour. |

**Also seeded:** Kapow's three bands into the portal-owned `VatRatePoints` with law-correct classes,
effective-from epoch. That arms WP2b's stale-band check — verified live that an ordinary
£14.99/£12.49 line (declaring 2002bp) still ingests **201**.

✅ **ANSWERED by Matt, 2026-08-08: Kapow sells NOTHING exempt.** Every item is standard-rated
(5,603) or zero-rated (14,740); the reduced band exists but is unused. Consequences, so this is
never re-litigated:
- **Partial exemption does not apply to Kapow.** All supplies are taxable, so input tax is
  recoverable in full — which is exactly why the old "Exempt" label was the expensive mistake.
- `VatClass.Exempt` stays in the model (other tenants may need it; it is a real UK class) but must
  **never** be assigned to a Kapow band. Anything that reintroduces it is a bug.
- The zero-vs-exempt reporting split, which would have needed line-level band identity, is
  **not required for Kapow**. It stays a WP2c capability rather than urgent work.

✅ **ANSWERED by Matt, 2026-08-08: correct the past returns.** WP2c built the restatement rather
than an estimate — `GET /api/v1/reports/vat-corrections` re-runs both methods over the same rollups,
per VAT period, and reports Box 1 as filed, Box 1 restated, and the net error. Portal → VAT →
**Corrections**.
- **Nothing is repaired, because nothing was broken in the data.** The sales records were always
  right; only the arithmetic on top of them was. So the corrected figures come from re-running the
  same rollups, not from rewriting history.
- **Periods follow the business's HMRC stagger group**, not calendar quarters — pinned by
  `VatCorrectionTests`. Attributing a correction to the wrong return is the easy way to get this
  wrong, and it looks fine on screen.
- **The route is worked out, not asserted:** net error vs the Notice 700/45 threshold (greater of
  £10,000 and 1% of Box 6, capped at £50,000). At £10.77 this is an adjustment on the next return
  (add to Box 1), not a VAT652.
- ⚠ **Plutus does the arithmetic half of the test only, and says so on the screen.** Whether the
  original error was *careless* — which forces a VAT652 however small it is — is Matt's and his
  accountant's judgement, and the software must never appear to have made it. It also does not file
  anything.

### The original findings, for the record

**1. The third band is named "Exempt", but comics and books are ZERO-RATED.**
`Taxes` holds `20%` (1.2), `5%` (1.05), `Exempt` (1.0). HMRC Notice 701/10 zero-rates books,
comics, magazines and newspapers. If Kapow's comic sales are being classified as *exempt* rather
than *zero-rated*, that is the wrong class in the more expensive direction: exempt supplies restrict
input-tax recovery on stock and overheads, zero-rated do not. Most likely this is a **misnomer in a
2019 seed row** rather than how the returns are actually filed — but it needs confirming, and the
band should be renamed to match whatever is true.

**2. Zero-rated and exempt are INDISTINGUISHABLE once a sale is recorded.** Both arrive as
`VatRateBp = 0` on `SaleLine`, so no report can separate them. 76,810 of Kapow's lines are 0bp.
If any genuinely exempt supply is ever sold alongside zero-rated stock, the partial-exemption
figure cannot be derived from Plutus data at all. Fixing this means carrying the **band identity**
(not just its rate) onto the line — a schema change affecting both tills and the rollups.

**3. VAT rounding differs from the VAT-fraction method on ~⅓ of standard-rated lines.**
Measured on live data: of **6,152** standard-rated lines, **2,006 differ by exactly 1p**, and the
difference is **systematically one-directional — £20.06 less VAT declared** than the VAT-fraction
method (`gross × 1/6`) would give.
The cause: the till derives VAT as `lineGross − lineEx` where `lineEx` is the *penny-rounded* net,
rather than applying the VAT fraction to the gross. Both appear on HMRC's list of acceptable retail
methods, so this is likely defensible — but it is a real, quantified, one-way divergence, it is the
same on both tills, and **it is an accountant's call, not an engineering one.** Whatever is decided
must change *both* tills together, or they will disagree penny-for-penny on the same basket.

### What this fixes in the plan

- **WP2b** (shipped, correct as far as it goes) validates the *price pair* against effective-dated
  bands. Its band vocabulary must gain **`exempt` as distinct from `zero`**, even though both are
  0bp, so the two can never be silently merged.
- **WP2c** (new, below) builds the portal surface that owns all of this.
- **WP3** must send the band's *identity* alongside the derived rate once WP2c exists, so finding 2
  becomes fixable rather than permanently lost.
- MAUI must **never** compute a VAT rule locally. It caches what the portal published and applies it.

## 3a. Scope found after this plan's sources were written

The parity analysis inherited from `MAUI-Backend-Sync` (2026-08-01) covered six areas — Cash,
Inventory, Reporting, Loyalty, Store Information, Settings — plus theming. An audit on 2026-08-07
against the *current* web till found it had missed the **Till screen itself** and everything
platform-level. Full register: **[`Build/till-design.md`](../till-design.md)**.

**Rulings (binding, veto-able): everything below is IN**, homed as follows — gift cards become
**WP13**; announcements + support tickets + pick-notes become **WP5b**; the receipt template folds
into **WP3**; the users screen folds into **WP8**; refund-only baskets into **WP3**; add-unknown-item,
the Bin and untracked stock into **WP10**; theming into **WP7**; cross-till refunds into **WP11**.

> ### ⚠ Re-audited 2026-08-08 — a ruling in a table is not a specification
>
> Matt made parity binding (*"The tills need to be in parity… when adding new functionality, it
> needs to be added to all tills going forward"*), so every Part B row was cross-checked against the
> actual **WP bodies** above rather than against this ruling table. **Seven items had been ruled IN
> here and never specified anywhere** — no body, no DoD, nothing anyone could build from:
>
> | Item | Was | Now |
> |---|---|---|
> | MAUI consuming `GET /api/v1/vat/bands` | ⚠ **no home at all** — WP2c asserted "both tills cache it", only the web till does | **WP5** |
> | Portal-**pushed** theming (`/themes/effective`) | WP7 said "port the palette" and would have closed | **WP7b** |
> | Add unknown scan as a new item | ruled into WP10, absent from its body | **WP10** |
> | The Bin + untracked stock | ruled into WP10, absent from its body | **WP10** |
> | Member-number scan-to-attach | ruled into WP12, absent from its body | **WP12** |
> | Payment-gateway awareness | cited "WP17.2" — not a work package in this plan | **WP14** |
> | Web-till test runner + `basketTotals` pin | never ruled either way | **WP15** |
>
> **Four more were specified in a body but gated by no DoD** — the VAT-band consistency guard
> (WP10), `426` handling (WP5), `LineMeta.vatBand` (WP3) and the un-enrol request/approval round
> trip (WP4). All four now have DoD lines. A shared item-search matcher was attributed to WP1, which
> **shipped without it**; it moves to WP5.
>
> The lesson is worth keeping: **§3a is a decision log, §5/§6 are the specification.** When they
> disagree, the bodies win, because they are the only half anyone builds from.

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
| **2b** VAT effective-dating | ✅ **corrected + ARMED** | `3ec4eff` shipped exact-bp validation — wrong (Matt's catch). Corrected `88e5c26`: pair-based `Assess`, three verdicts, only StaleBand blocks. **Kapow's bands are now seeded** (`cb9dc05`) with law-correct classes, so the check is live. Verified: an ordinary £14.99/£12.49 line declaring 2002bp ingests 201. |
| **VAT law fixes** | ✅ | `cb9dc05`. Four defects fixed against HMRC guidance: return now uses the **VAT fraction on takings** (Notice 727 §3.4.1 — Kapow's was £10.77 light); takings group by **band** not derived rate (was fragmented across 6 buckets); off-band takings report **unclassified**; comics reclassified **zero-rated** not exempt (Notice 701/10 — exempt was blocking input-tax recovery). |
| **2c** Portal VAT surface | ✅ | 2026-08-08. **The portal is now the source of VAT truth.** `VatBandsController`: `GET /api/v1/vat/bands` (sales.ingest — ships the whole effective-dated timeline, future points included, so an offline till applies a rate change on the day) + the editor endpoints (`portal.company.manage`, every write audited). A rate change **adds a dated point and is refused in the past**; a *scheduled* one can be cancelled, an *in-force* one cannot. Zero ≠ Exempt survives the round trip. Portal gained a **top-level VAT tab** — Return · Bands · Corrections · **Rules** (every rule the code applies, with its HMRC citation, served from `VatGuidance` so text and behaviour ship together). The web till's last hard-coded rate (the gift-card `/1.2`) is gone. **Past returns restated** — see the row below. |
| **VAT past-return correction** | ✅ | 2026-08-08, Matt's instruction ("correct past return"). `GET /api/v1/reports/vat-corrections` re-runs both methods over the same rollups per VAT period (quarterly with a real HMRC **stagger group**, or monthly), giving Box 1 as-filed vs restated, the net error, and which HMRC Notice 700/45 route the arithmetic points at. ⚠ **It does not file anything** — and whether the original error was *careless* (which forces a VAT652 however small) is explicitly left to Matt and his accountant. |
| **2** Local store v2 + cutover + money | ✅ (one VAT note) | 2026-08-07 · `6e46734`. New `Plutus.Client.Storage` (schema v2, SQLite). Cutover archives-never-merges and implements the §10 STOP. ⚠ The snapped catalogue band is a **label only** — at sale time lines derive `vatRateBp` from the price pair like the webtill (2026-08-08 correction; see the WP2 note + WP3). Money property test over 2,000 randomised baskets — its VAT arithmetic gets corrected with WP2b. |
| **3** Sale commit path + outbox | ✅ | 2026-08-07 · `6e46734`. `CommitSaleAsync` (one transaction, sequence allocated, **commit before print** — risk #2 decided in code). `TillStore` implements `IOutboxStore`, so the WP1 pusher drove it unchanged. Soak: 120 offline sales drain exactly once in order, no gaps; crash mid-drain records 20 of 20. |
| **4** Enrolment + device identity | ✅ | 2026-08-07 · `f90dac5`. Server URL + code; **archive gate refuses enrolment** while a legacy DB is un-archived; placement (storeId + legacy businessId) refreshed each start; secret asserted absent from the DB file. |
| **5** Heartbeat + catalogue sync · **5b** | 🔨 | **The backend gap is CLOSED (2026-08-08).** Built + tested headlessly: `POST /api/v1/heartbeat` (in-process `TillPresence`, 2/5-min boundaries), `GET /api/v1/catalogue/changes` (keyset cursor, tombstones), `SyncNow`/`Locked`/`LockReason` on `Device` (migration `AddDeviceSyncSignals`, **carries an index** — see below), and the client half in `Client.Core/SyncClient.cs` + `TillStore : ISyncStore`. 22 new tests. **Remaining:** the PRICE-SCHEDULE half of the feed (see the ⚠ below), MAUI's VAT-bands consumption, the shared search matcher, and a timer in MAUI to call any of it. |
| **8** Operator RBAC + offline login | ✅ | 2026-08-08. `GET /api/v1/tills/{id}/operators` (device-token gated), the roster cached on the till, offline sign-in verified with `SharedKernel.Pbkdf2`, ceilings + time windows + the `OfflineCredentials` staleness tier at the gate. **An enrolled till can sign someone in.** 21 new tests. ⚠ Two pieces of WP8 remain: the **Users screen** and **supervisor override**. ⚠ The roster is cached as a JSON file, not in `Plutus.Client.Storage` — see the note in WP8's body. |
| 6–7, 9–13 | ⬜ | The remaining parity WPs — MAUI **UI** work (XAML + viewmodels), so they need a device to verify. WP7, WP10 and WP12 each gained real scope on 2026-08-08 (pushed theming · add-unknown + Bin/untracked · member-number scan). |
| **14** Payment-gateway awareness | ⬜ | Added 2026-08-08. Was cited in Part B as "WP17.2", which is not a work package in this plan. |
| **15** Web-till test runner + C2 pins | ⬜ | Added 2026-08-08. **Web till, not MAUI** — parity runs both ways. Timing is Matt's call. |
| **16** Connectivity + offline credentials | 🔨 | Added 2026-08-08 on Matt's instruction. **Shared half DONE:** `/api/v1/ping`, `ConnectivityProbe`, `OfflineCredentials` horizons + 42 tests. **Remaining:** MAUI login-screen UI, the web till's move off `navigator.onLine`, and 16b's enforcement (pairs with WP8). |

**Two notes for whoever picks this up:**
1. **The transport spine is done, and so is the VAT surface.** WP1–WP4 mean a till can enrol,
   commit sales offline, and drain them exactly once — all provable headlessly. WP2c means no till
   holds a VAT rule of its own. **WP5 is the last backend gap**, then the remaining WPs are screen
   work against endpoints that already exist.
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
| 2c | **Portal VAT surface** | Makes the portal the source of VAT truth — §2a. Gates any MAUI VAT work |
| 3 | Outbox + sale ingest (+ portal receipt template, refund baskets) | The core of the whole retrofit |
| 4 | Enrolment, device identity, Settings | Everything after this needs a device token |
| 5 | Heartbeat + catalogue sync · **5b** announcements, tickets, pick-notes | Fleet citizenship |
| 6 | Store Information (read-only) | Smallest lift; proves the "portal is the truth" pattern |
| 7 | Theming (portal-pushed) | Cheap, visible, unblocks nothing — do it when you want a win |
| 8 | Operator RBAC + offline login + the Users screen | Needs the new endpoint; replaces local-only auth |
| 9 | Cash | Self-contained, well-specified contract |
| 10 | Inventory + stock ledger (+ Bin, untracked, add-unknown) | Rework of screens that already exist |
| 11 | Reporting + **cross-till refund lookup** | Rewrite local queries → backend calls; the refund path is money-handling, not a report |
| 12 | Loyalty (+ member-number scan-to-attach) | Highest effort, hardest offline design; reuses everything before it |
| 13 | Gift cards | Sell + redeem at the till; last — reuses WP12's customer plumbing |
| 14 | Payment-gateway awareness at checkout | Tiny, self-contained display; do it any time after WP5 |
| 15 | **Web till** test runner + pinning the C2 twins | Not MAUI work at all — parity runs both ways. Matt's call on timing |
| 16 | Connectivity indicator + offline-credential horizons | 16a stands alone; **16b pairs with WP8**, which is what puts credentials on the till |

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
catalogue count and 10 spot-checked barcodes derive **the same GUIDs the WEB TILL derives** (see
the corrected seam in §10); park → kill → restore works and the serialised form contains no `$type`.

⚠ **The earlier DoD wording — "the same UUIDs the central migration produced" — was wrong and has
been corrected** (2026-08-08, after reading `NatApp-Translation-Agent-Plan`). It is unsatisfiable:
the server's catalogue has **no item UUIDs at all** (`Items` is still barcode-PK'd, F4 unfixed —
translation-agent plan §1.3/§3.3), and the only central UUIDs that exist are the **random** ones
`Migration.Kapow`'s `IdRemap` minted for historic sale lines. Comparing against those would fail a
perfectly good cutover. See §10.

**WP2b — VAT-rate-change ingest compliance (backend).**

> ✅ **CORRECTED AND SHIPPED 2026-08-08 (Matt's catch).** The first implementation (`3ec4eff`)
> validated each line's `VatRateBp` by **exact membership** of the in-force set — which contradicts
> how the platform declares VAT and would have quarantined **ordinary webtill sales** the moment
> any tenant's bands were seeded. Replaced by the pair-based rule below (`VatRateHistory.Assess`,
> shipped in `88e5c26`). **Kapow's bands were seeded 2026-08-08 (`cb9dc05`) with law-correct
> classes, so this check is now ARMED** — verified live that an ordinary £14.99/£12.49 line
> declaring 2002bp still ingests 201.

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

**WP2c — Portal VAT surface (backend + portal). ✅ SHIPPED 2026-08-08 — spec kept for the record.**
*Delivered:* `src/Plutus.Tenancy/Controllers/VatBandsController.cs`, `src/Plutus.SharedKernel/VatGuidance.cs`,
the portal's VAT tab (`VatPage` → `VatReturn` · `VatBands` · `VatCorrections` · `VatRules`), the
web till's band cache, and `ReportsController.VatCorrections`. Pinned by `VatBandsE2eTests` (11),
`VatCorrectionsE2eTests` (5), `VatCorrectionTests` (10) and `VatExemptBandTests` (11).

**§2a finding 2 is CLOSED — Matt's instruction, 2026-08-08: "I do need to include the option for
exempt… other stores might. The option NEEDS to be there."** The first pass shipped Exempt as a
*class* (in the dropdown, in the contract, applied at 0% by the till) but the **report still could
not separate it from zero-rated**, because a recorded sale carried only the rate — so for a shop that
genuinely sells exempt supplies the option wasn't really there. Now:
- **`SaleLine.VatBand` + `VatRollup.VatBand`** — the band travels on the line and is part of the
  rollup grain. ⚠ Keyed on the rate alone, a zero-rated row and an exempt row for the same store and
  day **collide**, and merging them loses the partial-exemption figure irrecoverably.
- **`VatBandTaxMap`** — items are priced against legacy `Taxes` rows carrying a name and a
  multiplier, so the band could only ever be *inferred from the rate*, which cannot tell two 0% bands
  apart. This makes the mapping explicit and portal-owned. It stays dormant until a tenant actually
  has two bands at one rate (`VatBandResolution.NeedsExplicitMapping`), so Kapow is never nagged.
- **`VatAccounting.BandFor` now returns null on a TIE** instead of "nearest, first wins" — that
  silent arbitrary pick would have attributed 0% takings to whichever band happened to sort first,
  corrupting the exact number this work exists to produce.
- **`GET /api/v1/reports/vat` gained a `partialExemption` block** (Notice 706): taxable vs exempt
  turnover, the standard turnover-based recoverable percentage, and — for a shop like Kapow — an
  explicit *"partial exemption does not apply, input tax is recoverable in full"*.
- **Consistency is now STRUCTURAL, not per-client** (Matt asked "are these bands consistent across
  the tills now? Also future tills based on Mac and Linux"). Two gaps were real and are closed:
  - **`VatBandStamp` backfills the band server-side** for any line that arrives without one, in
    `SalesIngestService` — the single choke point every channel passes through, *including the
    webstore connector*, whose Woo mapper sent no band at all. A till on any platform is therefore
    correct by default before it implements band awareness. ⚠ A band the client STATED is never
    overwritten (the voucher treatment overrides the catalogue for gift cards).
  - **`VatLineMath` in `Plutus.SharedKernel`** is now the one implementation of the line arithmetic.
    `Cutover.cs` had a hardcoded UK band list and its own snap tolerance — a till holding VAT
    knowledge, the very thing this WP removes; it is now an explicitly-documented cutover fallback
    using the platform-wide tolerance. New architecture test
    `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own` fails on an
    OS-specific TFM in a till library or a literal VAT rate in one.
  - **A macOS/Linux till is a build target, not a port**: the three client libraries are plain
    `net10.0`, MAUI-free and package-free, and now carry the VAT rules. Only UI and hardware are
    platform-specific.

The principle is *all VAT guidance comes from the portal down to the tills*, and today no portal
VAT surface exists at all — bands are seeded legacy rows, read-only in both frontends, with no
effective dates and no owner. This WP makes the portal the source of truth:
- **Band identity, not just a rate.** A band is `{key, displayName, class, rateBp, effectiveFrom}`
  where `class` ∈ `standard | reduced | zero | exempt | outside-scope`. ⚠ `zero` and `exempt` are
  BOTH 0bp and must stay distinguishable — that is finding 2 in §2a, and merging them loses the
  partial-exemption figure permanently.
- **Effective dating on the band, not a parallel table.** Seed `VatRatePoints` from `Taxes` (one
  row per band, effective-from epoch), then the portal edits the history: adding a rate change is
  adding a row with a future `effectiveFrom`, never mutating the current one.
- **Portal editor** on the Company tab, gated `perm:portal.company.manage` and audited, with a
  plain-English warning that changing a band affects every item priced against it, and that a
  future-dated change will apply itself on the day — including on tills that are offline.
- **One published contract for tills**: `GET /api/v1/vat/bands` (sales.ingest, so device OR
  operator token), returning the bands with their effective dates. Both tills cache it on the
  catalogue-sync cadence and apply it; **neither till may hold a hard-coded rate.**
  ⚠ That includes the webtill's current `/1.2` in the single-purpose gift-card redemption line —
  it must read the standard band from this contract instead (see WP13's hazard note).
*DoD:* seeding from `Taxes` reproduces Kapow's three bands with no change in behaviour; a
future-dated standard-rate change is invisible before its date and live from it, on a till that
never reconnects in between; `zero` and `exempt` survive a full round trip (portal → contract →
till → sale → report) as **distinct** bands; the editor refuses a rate change dated in the past
(that would retrospectively invalidate recorded sales); every write is audited.
*USER-VERIFY / NOT FOR AN AUTONOMOUS SESSION:* the three §2a findings are **Matt's and his
accountant's decisions** — whether Kapow's "Exempt" band is really zero-rated, whether the
partial-exemption split matters for them, and which rounding method is correct. Do not pick one.

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
Matt's catch — parity §6 in practice). Mirror it exactly, never "improve" it. Once **WP2c** lands,
also send the band's **identity** alongside the derived rate (in the `LineMeta` envelope), so
zero-rated and exempt stop being indistinguishable at 0bp — §2a finding 2. Until then, mirror:
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
reaches the next printed receipt after a sync; **a line resolved from a mapped legacy tax row
carries `LineMeta.vatBand`, and a line whose tax row the portal has NOT mapped carries `null` —
never a guess** (added 2026-08-08; the body has required this since WP2c and nothing gated it).
USER-VERIFY: paper output.

⚠ **How the band is resolved, since the plan never said.** Take the item's legacy `TaxId` and match
it against each published band's `legacyTaxIds` from `GET /api/v1/vat/bands` (WP5). **Leave it null
rather than guessing**: the server's `VatBandStamp` backfills any line that arrives without a band,
from the same catalogue data, at the single choke point every channel passes through — so null is
*correct by default*, and a wrong guess is the one thing that can't be undone (a stated band is
never overwritten). State it only where the till knows something the catalogue doesn't — a
single-purpose gift-card line is `"standard"` by the voucher treatment, not by its catalogue row.

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
database file exists** — the refusal message names the archive step; **the un-enrol REQUEST →
manager approval → Revoked round trip is exercised end to end, and a till in `PendingRemoval` keeps
trading throughout** (added 2026-08-08 — the body has specified the lifecycle since the first draft
and the DoD only ever tested revocation).

⚠ **`PendingRemoval` is not a stop signal, and that is deliberate.** Halting a till the moment
someone requests it back would make un-enrolment a way to take a shop's till down. Only `Revoked`
stops. The till learns which it is from `GET /api/v1/tills/devices/{deviceId}/status` — and it must
poll it, because device tokens are bearer tokens with **no server-side denylist**, so a revoked
till otherwise keeps working until its 12h token expires.

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

**⚠ Also in this WP: the VAT BANDS contract (added 2026-08-08 — it had no WP home at all).**
WP2c shipped `GET /api/v1/vat/bands` and asserted "both tills cache it on the catalogue-sync
cadence". Only the **web till** does. `PlutusApiClient.GetVatBandsAsync` and
`VatBandsResult.RateBpAt` already exist, unused by anything — the plumbing was built and no work
package ever said to wire it up. This is the sync WP, so the bands sync here:
- Fetch on app start and on the same cadence as the catalogue; persist so it survives restart
  offline. **A till with no cached bands has nothing to apply.**
- ⚠ **Cache the WHOLE effective-dated TIMELINE, not today's rate.** The contract ships future
  points on purpose. A till that stores only the rate in force at fetch time is a till that goes
  offline before a rate change and keeps charging the old rate — and WP2b then quarantines every
  sale in its backlog on reconnect. `RateBpAt(key, atUtc)` is the accessor that makes this correct;
  use it at sale time, never a stored scalar.
- **No till may hold a hard-coded VAT rate** — pinned platform-wide by
  `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own`. The cutover's
  `FallbackBandsBp` is the *only* sanctioned exception, and only until the first sync.

> ### ⚠ Built 2026-08-08, and one half deliberately NOT built — read before claiming WP5 done
>
> **Done and tested headlessly:** both endpoints, the `Device` signal columns, and the client loop
> (`SyncClient` + `TillStore : ISyncStore`).
>
> **Two design decisions worth knowing:**
> 1. **The cursor is `(ModifiedAt, IdOne)`, not the bumped `BIGINT` this plan originally specified.**
>    A counter needs every catalogue write path to remember to increment it — the audit found
>    **nine** such paths for Items alone, plus categories, plus a raw-SQL purge — and the tenth,
>    added next year, would leave every till silently stale. `ModifiedAt` is stamped for every
>    `IAuditable` in `RepositoryContext.SaveMethods()`: one choke point every write already passes.
>    Same reasoning as `VatBandStamp`. The barcode breaks timestamp ties, which a bulk edit produces
>    by the hundred.
> 2. **The migration carries an index** (`IX_Items_Tenant_Modified_IdOne`). Without it the feed is a
>    full scan of `Items` per till per sync — ~20k rows for Kapow today, and it would degrade
>    quietly as the catalogue grows rather than failing anywhere visible.
>
> ⚠ **NOT built: the price-schedule half of the feed, so this WP's "price scheduled for 02:00"
> DoD is NOT yet met.** Effective prices live in their own effective-dated tables
> (`PriceListEntries`, `PriceOverrides` — `PricesController`), and writing one does **not** touch
> `Item.ModifiedAt`. So a central price change or store override does not currently reach a till
> through this feed; only the baseline `Item.Price` does. The feed needs a second stream with its
> own cursor in the same envelope. **Stated rather than glossed, because a half-built sync that
> looks complete is how a till ends up confidently charging last month's price.**

**And the shared item-search matcher (till-design C2, attributed to WP1 — which closed without
it).** The web till's word-matching (`batman one` → *Batman Year One*) and `"quoted"` exact-phrase
are a **client-side device preference**; the server default is whole-phrase. A MAUI till searching
its cached catalogue offline therefore returns *different results for the same query* than the web
till returns for the same shop. Put one matcher in `Plutus.Client.Core` and have MAUI use it.

*DoD:* status transitions verified at the 2/5-minute boundaries with a fake clock; MySQL takes no
per-heartbeat writes; a price scheduled for 02:00 activates at 02:00 with the till offline; a 20k-item
full resync completes in <60s; a sale mid-sync sees a consistent snapshot — the price read at
basket-add is what's charged and what's in the payload; **a 426 response raises the banner, parks
sync and leaves selling working** (specified since the first draft, never gated); **a standard-rate
change dated next Tuesday is cached today, is NOT applied on Monday, and IS applied on Tuesday by a
till that has been offline throughout** — the same DoD WP2c set for the web till, which is the
point: two tills, one contract, one behaviour; **`batman one` returns *Batman Year One* on a MAUI
till searching its offline cache, matching the web till exactly.**

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

**WP7 — Theming: the palette AND the pushed theme.**

*7a — the palette.* AppClient's `App.xaml` registers only a value converter — **no theme resources at
all**. Port ClientUI's `Resources/Styles/Colors.xaml` verbatim (`Primary #272643`, `Secondary
#ffffff`, `Tertiary #e3f6f5`, `Quaternary #bae8e8`, `Quinary #2c698d`, `Error #FF9494`, the
Cyan/Blue accent scales and matching `*Brush` keys) under the **same `x:Key` names** so bindings
resolve unchanged, author a `Styles.xaml`, and merge both into `App.xaml`. Theme the Syncfusion
suite (pinned at `34.1.32`) via `SyncfusionThemeResourceDictionary`, remapping its palette slots to
these brushes. Native OS chrome will never pixel-match a browser; content and branding will.

*7b — the PUSHED theme (added 2026-08-08; this WP previously stopped at 7a and would have closed
with a hardcoded palette).* FE10 shipped portal-controlled theming to the web till on 2026-08-07:
schemes are defined in the portal and assigned per tenant / store / till-group / till, and
`GET /api/v1/themes/effective` (`sales.ingest` — device **or** operator token) resolves
till > group > store > tenant > default **server-side** in `ThemeResolution`. A till does not
choose its colours; it is told them. So:
- Call `PlutusApiClient.GetEffectiveThemeAsync` on app start and on the WP5 sync cadence, caching
  `EffectiveThemeResult` so the assigned scheme survives a restart with the network down.
- Map the payload onto the 7a resource keys: `baseMode` ∈ `system|light|dark` drives
  `Application.UserAppTheme`, and `colorsJson` carries the web till's seven tokens — `--accent`,
  `--accent-ink`, `--surface`, `--surface-2`, `--ink`, `--ink-muted`, `--line`. **Same slots, same
  names, same resolution order as the web till**, or the two tills show different colours for one
  assignment and the portal's preview is a lie.
- Clearing an override must restore the stock palette exactly, which is what makes 7a the fallback
  rather than dead code.
- ⚠ **Receipts are deliberately immune to theming** (till-design C1). The web till pins `#111` on
  `#fff` in its print block because printing from a dark scheme once put near-white ink on paper.
  MAUI's `PosPrinterManager` must ignore the theme entirely.

*DoD:* no hard-coded hex left in XAML; an `SfListView` visibly reflects `Primary`/`Quinary`; **a
scheme assigned to this till in the portal is applied after one sync and survives a restart with
the network off; a scheme assigned at STORE level reaches a till with no till-level override, and a
till-level override beats it; clearing every override returns the stock palette; a printed receipt
is byte-identical under a light and a dark scheme.**

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
in the pushed payload; an employee created on the till can sign in on the web till and vice versa;
**the employee LIST renders from `/api/Employee` and a set-password on an EXISTING employee via
`/api/Auth/SetPassword` takes effect on the next sign-in** (added 2026-08-08 — the body named both
and the DoD only tested create).

> ### ✅ Built 2026-08-08 — and three decisions worth knowing
>
> 1. **Windows ship RAW.** `ResolveAsync` pre-evaluates `InWindow` and then discards the windows, so
>    building the payload with it would give a Saturday-only supervisor synced on a Wednesday **no
>    permissions at all** until the next sync — silently. The endpoint ships the raw dates/mask/window
>    and the till judges them against its own clock at the moment of the action. The rule itself moved
>    to `SharedKernel.PermissionGrant.IsActiveAt`; the server's `InWindow` now delegates to it, so
>    there is one implementation rather than a C2 drift row.
> 2. **The roster is narrowed to people who work here.** Company- and tenant-scope assignments sit on
>    *every* till's ancestor chain, so "everyone RBAC-reachable" would mirror the whole company roster
>    — and its password hashes — onto every counter. Narrowed to `Employee.StoreId == Till.StoreId`
>    **or** an assignment made at this till/store specifically. ⚠ That is a product decision as much
>    as a technical one, and the first thing to revisit if a manager covering another shop cannot
>    sign in.
> 3. ⚠ **The cache is a JSON file, not `Plutus.Client.Storage`.** Referencing that project from
>    AppClient fails `restore` outright (NU1605: EF Core 3.1.17 vs 9.0.18, verified with a probe
>    project), and the obvious fix — dropping AppClient's direct 3.1.17 pin — silently swaps the LIVE
>    till database onto EF 9 with a stranded EF 3.1 Proxies package: a runtime `TypeLoadException` in
>    the code a shop is trading on. That unwiring is **WP2's cutover**, and it must also unwire
>    `AppClient.Tests`, which references `Database.csproj` too — the plan's "sole referencer" note is
>    stale. None of the RULES live in the file store, so moving it later changes where bytes sit and
>    nothing else.

⚠ **Shipping password hashes to till hardware is a real change of threat model, and WP8 is where it
happens.** The cached blob is the operator's *platform* password, verifiable offline at
PBKDF2-HMAC-SHA1 / 101,010 iterations — roughly 13× below current OWASP guidance for that PRF — and
it works on the web till too. Two consequences this WP owns:
1. **The cache must expire.** Nothing can be pushed to an offline till (risk #5), so expiry is the
   only mechanism that ever revokes a leaver on one. Use
   `Plutus.SharedKernel.OfflineCredentials` — the horizons are tiered, shared and tested, not
   invented here. See **WP16**.
2. ⚠ **`POST /api/Auth/Login` does not check `Employee.Active`** (verified 2026-08-08). Deactivating
   a user leaves their `WebCredentials` row intact, so they can still sign in and receive a full 12h
   token. Fix it server-side in this WP — an offline expiry policy is pointless while the *online*
   path lets a deactivated user straight back in.

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
*Also in this WP (§3a rulings — bodies added 2026-08-08; they were ruled IN but never specified, so
WP10 would have closed without them):*

- **Add an unknown scan as a new item.** Shipped to the web till 2026-08-07. A scan that matches
  nothing currently dead-ends; instead offer *"Add {barcode} as a new item"*, gated on
  `portal.stock.adjust`. The dialog takes the minimum a sellable line needs — name, price, VAT
  band, category — and **the scanned code becomes `IdOne`**, never a generated id: `IdOne` *is* the
  barcode, it is what every v1 stock/price/sale-line call keys on, and it cannot be recovered from
  the item GUID (a one-way hash). The item id is `DeterministicGuid.ForItem(businessId, idOne)`
  like everywhere else. An opening quantity, if given, posts a **Receipt movement**, not a bare
  stock row. ⚠ Offline this must queue rather than block the sale — the customer is standing there.
- **The Bin (soft delete) and untracked stock.** FE5 concepts with no MAUI model at all today.
  *The Bin* is soft delete: binning an item hides it from sale and search but keeps its history, and
  the Bin view + restore are gated on `inventory.bulk` (deliberately separate from
  `portal.stock.adjust` — one mistake here moves thousands of items). ⚠ A binned item must stop
  being sellable **on an offline till too**, which is precisely why WP5's changes feed carries
  tombstones (`removed: true`) rather than plain upserts — `CatalogueItem.Removed` already exists in
  the local schema and nothing reads it yet. *Untracked stock* is an item that sells without
  decrementing anything (services, carrier bags): the till must not show a stock level for it, must
  not warn about selling below zero, and must not post a movement.

*DoD:* creating an item with an opening quantity produces exactly **one Receipt movement**, never a
bare stock row; adjusting without a reason is rejected before the request is sent; two devices
adjusting concurrently both land as separate movements and levels reflect the **sum**, never
last-write-wins; deleting a populated category blocks and offers reassign; **a band-inconsistent
price pair (`|Price − ExPrice×Rate| > 2p`) is refused client-side before the request is sent, and
the server's exact message is shown when the client misses a case** (the guard was specified above
but never gated — a guard nothing tests is a guard that regresses); **an unknown barcode scanned at
the till becomes a sellable item whose `IdOne` is the scanned code and whose GUID matches the web
till's derivation for the same barcode; a binned item disappears from sale and search on a till
that has been OFFLINE since it was binned; an untracked item sells with no movement written and no
stock warning.**

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
message — never a silent acceptance; **a sale rung up on till B REPRINTS from till A** (added
2026-08-08: the DoD asserted the sale *renders* and never that it *prints*, which is the half a
customer actually asks for at the counter).

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
*Also in this WP (added 2026-08-08 — a Part B row with no body):* **member-number scan-to-attach.**
FE2 gave every member a printable card carrying `NNNNNNC` — a 6-digit sequence plus a Crockford
check character — as a `C…` barcode. At the till, a scan beginning `C` with a valid check digit
routes to **customer attach**, not item lookup; a bad check digit says so rather than searching for
an item that will never exist. Reuse `Plutus.SharedKernel`'s `MemberNumbers` validator — the check
character is a rule, and re-deriving it in MAUI is exactly the drift Part C exists to stop.

*DoD:* airplane mode — cached name/tier/discount still shows, credit tender is **absent**;
reconnect → tender reappears with the live balance; two devices racing to redeem the last credit →
exactly one succeeds, the other gets `InsufficientCreditException`, confirming the append-only
ledger (D15) prevents double-spend; **scanning a member card attaches that customer and applies
their tier's auto-discount to the basket; a card with a corrupted check character is rejected as a
bad member number, never treated as a barcode.**

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
*Known hazard, both tills:* that redemption line hardcodes `/1.2` — a standard-rate change breaks
it on the webtill exactly as it would here, and gift-card lines are exempt from ingest validation
so it would **mis-state embedded VAT silently** rather than quarantining. **WP2c fixes this
properly**: read the standard band from `GET /api/v1/vat/bands` instead of hard-coding it. That is
the "no till holds a VAT rule" principle applied to the one place the webtill currently breaks it.
*DoD:* sell → activate → redeem round-trips against a local backend with `GiftCardSettings` set —
**under both treatments, asserting the single-purpose negative-line shape**; the same flow on a
tenant WITHOUT settings surfaces the friendly 409 message at the first step; redemption offline is
hidden, like store credit; a redeemed card's remaining balance matches the portal's view of the
same card.

---

## 6a. Work packages added 2026-08-08 — the parity sweep

Matt, 2026-08-08: *"The tills need to be in parity. This is the point of the MAUI retrofit. In
addition when adding new functionality, it needs to be added to all tills going forward."*

A cross-audit of [`till-design.md`](../till-design.md) Part B against every WP body above found
**seven capabilities that had been ruled IN by §3a but never specified anywhere** — a ruling in a
table is not a thing anyone can build. Four are now folded into the WPs that own them (theming →
WP7, add-unknown + Bin/untracked → WP10, VAT bands + search matcher → WP5, member scan → WP12).
The three below had no natural home and become work packages of their own.

**WP14 — Payment-gateway awareness at checkout.** The Part B row cited "WP17.2", which is not a
work package in this plan — it is a *shipped web-till feature*, referenced only inside risk #8.
Binding default 6 says MAUI copies the display; nothing said how. Read
`GET /api/v1/payments/gateway/active` and render the configured provider at checkout exactly as
`CheckoutDialog.tsx:226-233` does: **standalone** (the default — external chip & pin, cashier
confirms) shows the standalone hint; any other selection shows the provider name with *"integration
pending"* and **keeps the standalone confirm flow**. ⚠ Selling must never block on this: an
unreachable gateway endpoint falls back to the standalone hint. Actual terminal drive stays out of
scope for both tills (risk #8 — greenfield, no provider).
*DoD:* a tenant on `standalone` and a tenant on `stripe-terminal` each render the same text the web
till renders for that setting; with the endpoint failing, checkout still completes.

**WP15 — The web till's missing test suite, and the C2 twins.** ⚠ **This is WEB TILL work in the
MAUI retrofit plan, and that is deliberate** — parity runs in both directions, and the drift
register (till-design C2) is a MAUI risk precisely because nothing holds the TypeScript half.
`package.json` has `dev`, `build`, `preview`, `typecheck` — **no test runner and no test files** —
so every cross-language "pinning" test in C1 holds only its .NET side. Concretely: `VatLineMathTests`
fixes .NET to the numbers `api.ts` produces and nothing executes `api.ts`; `LegacySaleBridgeTests`
pins item-id derivation to a GUID the TypeScript produced in a 2026-07-24 smoke test and would not
notice the TypeScript changing; and `till/basket.ts basketTotals` is a **third, entirely unpinned**
copy of the discount apportionment that has to agree with the checkout payload or the screen and the
receipt disagree.
Add a runner (Vitest — same Vite toolchain, no new build concept) and port the .NET pinning vectors
across so both halves of each twin execute the same numbers. Then add a C2 row saying what now pins
them.
⚠ **Matt's call whether this lands before or after the MAUI UI WPs.** It is recorded here rather
than left in D1 as an unowned item, because a twin nobody tests is how two tills come to disagree by
a penny on the same basket — forever, on every VAT return, with nothing flagging it.
*DoD:* `npm test` runs in CI-able form; `VatLineMathTests`' vectors pass against `api.ts`;
`LegacySaleBridgeTests`' golden GUID is produced by `pipeline.ts itemGuid`; `basketTotals` and the
checkout payload are asserted equal across a randomised basket set including discounts and returns.

**WP16 — "Am I connected?", and how long a cached login lasts.** *(Matt, 2026-08-08: "when 1st
logging into MAUI, it needs to show if it's connected to the internet and can see the back end.
[Then] store local credentials if it loses internet access.")*

*16a — the connection indicator.* ⚠ **"Offline" is three different faults wearing one word**, and
the operator standing at the till is the person who has to act on the difference: no network (their
cable/wifi), no server (nothing they can do at the till), or **this till has been revoked** (a
manager's job, and no amount of rebooting the router fixes it). A single red badge sends shops to
reboot routers over a portal setting. So the till asks two questions in order and reports which one
failed:
1. `GET /api/v1/ping` — anonymous, **touches no database**, so it still answers during a MySQL blip
   and answers for a till that has not enrolled yet or has been revoked.
2. `GET /api/v1/tills/devices/{deviceId}/status` — the device's standing. ⚠ **Not** a token mint:
   `POST /api/v1/tokens/device` is rate-limited 5/min per IP, so probing it would make a healthy
   till report itself revoked, and in a shop where several tills share one public IP they would do
   it to each other. Device status is also the **only** revocation signal that reaches a till, since
   device tokens have no server-side denylist.
The states, the wording and the two-step order live in `Plutus.Client.Core.ConnectivityProbe` — one
implementation, so the web till and any future macOS/Linux till give the same answer.
⚠ **The web till is behind here, not ahead.** It uses `navigator.onLine` only (`App.tsx:60`), which
reports the network interface and never asks whether the server is there — so it shows "online" in a
shop whose broadband is down. Bringing it onto the same probe is part of this WP, per the parity
rule.

*16b — offline credentials.* Today MAUI's login reads `EmployeeModel` straight out of the legacy
SQLite file and verifies with `Helpers.Security.Password.Verify` — **entirely local, with no server
involvement and no expiry at all**, which is risk #4 in unbounded form. WP8 replaces the source
(server-synced `LocalOperator` rows); this WP bounds the trust. The horizons live in
`Plutus.SharedKernel.OfflineCredentials` and are **tiered by what the permission can do**, because
one number cannot satisfy all three constraints at once — see §9 default 8 for the numbers and the
reasoning.
*DoD:* the three connection states are distinguishable on the login screen against a real backend —
including **revoked reading as revoked, not as offline**; the probe never calls the token endpoint;
a hanging server is bounded by the timeout and never blocks sign-in; a till 8 days stale signs in
and sells but cannot refund; a till 31 days stale is refused with a message naming the fix; a
session ends at the business-day rollover; **the web till reports "server unreachable" when the
backend is down but the network is up.**

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
| 8 | **Offline credential horizons are TIERED, and a till never hard-locks out of selling.** Numbers and reasoning below; implemented in `Plutus.SharedKernel.OfflineCredentials`. | WP8, WP16 |

### 9.8 — How long a cached login lasts (the numbers, and why)

Matt asked for a recommendation. **The recommendation is to stop asking for one number**, because
three constraints pull in different directions and any single value loses two of them:

| Constraint | What it wants |
|---|---|
| **Keep selling** | A shop whose till refuses logins during an outage falls back to a cash tin and paper — which is a *worse* compliance event than a stale staff roster, because it produces no HMRC-attributable records at all. |
| **Shrink the theft** | A stolen till holds operators' **platform** passwords at PBKDF2-HMAC-SHA1 / 101,010 iterations — ~13× below current OWASP guidance for that PRF — and they work on the web till too. |
| **Reach the leaver** | Nothing can be *pushed* to an offline till (risk #5). Expiry is the **only** mechanism that ever revokes a dismissed employee on one, so this horizon *is* the erasure SLA you can put in a DPA. |

Tiering by what the permission can do satisfies all three. Ringing up sales is how a shop survives
an outage and is worth almost nothing to a thief — the money lands in the ledger. Refunds, cash-out
and price overrides are how a stolen till becomes cash, and are exactly what a shop can live
without for a few days.

| Lifetime | Value | Why that number |
|---|---|---|
| **Money-out permissions** (refund, void, discount, no-sale, price override, all admin) | **7 days** since the last operator sync | Covers the realistic UK worst case — a Friday-night line fault on an end-of-next-working-day care level, over a bank holiday, is ~5 days — and is short enough to state as an erasure SLA inside the UK GDPR Art 12(3) one-month window *even if the request lands on day one of an outage*. |
| **Selling** (`pos.sell`, `support.tickets`) | **30 days** | The alternative to a stale roster is a shop that cannot trade. It also covers the two cases that actually meet this boundary: the spare till from the cupboard, powered on the morning the main one dies, and a convention/pop-up till offline for a planned week. |
| **Warning** | from **3 days** | A warning that first appears an hour before the cliff is decoration. Its whole job is to get someone to plug the cable in while that is still enough. |
| **Idle lock** | **15 min** | PCI-DSS 8.2.8's figure, and right on its merits for an unattended shop-floor device. ⚠ It **locks, it does not log out** — the basket survives, unlock is one password entry. That is what makes it cost seconds rather than sales. |
| **Absolute session** | **min(12h, business-day rollover, Z-close)** | Matches the server's own token TTL. The rollover is the load-bearing half: a session spanning two days attributes the incoming shift's sales to the outgoing operator — silently, in exactly the records HMRC would ask about. |
| **Server operator token** | **keep 12h** | The TTL is not the problem; **irrevocability** is. See below. |

**At every boundary the till degrades, it never bricks.** Past 7 days: sells normally, refunds and
manager functions withheld, screen says so in shop English. Past 30 days: offline sign-in refused,
with a manager break-glass extension as the escape hatch. The floor set is an **allow-list**
(`OfflineCredentials.SellFloor`), so a permission added to the catalogue next year is withdrawn when
stale until someone deliberately says otherwise.

⚠ **Two server-side findings that make these numbers enforceable — neither is optional.**
1. **`POST /api/Auth/Login` does not check `Employee.Active`.** Deactivating a user leaves their
   `WebCredentials` row intact, so they can still sign in and get a full 12h token. An offline
   expiry policy is theatre while the online path lets a deactivated user straight back in. → WP8.
2. **There is no server-side session revocation for any principal.** Tokens are HMAC bearer tokens
   checked for signature and `exp` only — no denylist, no DB lookup. Revoking a device or resetting
   a password stops the *next* sign-in and does not eject a live session. The cheap fix is a
   per-user `TokenEpoch` integer emitted as a claim and compared on each request (one indexed,
   cacheable lookup), which turns 12 hours of irrevocability into seconds. **Recommended, not yet
   scheduled — Matt's call.**

---

## 10. Relationship to `NatApp-Translation-Agent-Plan-2026-08-05.md`

That document moves **legacy shop data** (the old NatApp SQLite backup) into the new backend — and
Matt confirmed 2026-08-08 that it is the mechanism for exactly that. This plan makes **a till talk
to** that backend. They run in parallel and meet at item identity.

> ### ⚠ CORRECTED 2026-08-08 — the original seam statement was wrong
>
> It said: *"item IDs on a cutover till must equal the item IDs the central migration produced."*
> **They cannot, and they must not be compared.** Verified against the code and the live database:
>
> 1. **The server's catalogue has no item UUIDs at all.** `Items` is still keyed
>    `(IdOne barcode, IdTwo tenant)` — gap-analysis finding F4, explicitly deferred as option (b)
>    in the translation-agent plan §3.3. There is nothing to compare a till's GUID against.
> 2. **The only central item UUIDs that exist are random.** `Migration.Kapow`'s
>    `IdRemap.GetOrMint` mints a fresh `Uuid7` per run, in memory, with no export
>    (`KapowSaleMapper.cs:102`) — and only for **historic sale lines**, never the catalogue.
> 3. **So two populations already coexist in live data, by design.** `DeterministicGuid`'s own doc
>    comment says so. Confirmed in production: barcode `761941391632` carries **two distinct
>    `ItemId` GUIDs across 161 sale lines** — random ones on migrated history, derived ones on
>    web-till sales. Several hundred barcodes are like this.
>
> **The real invariant is the BARCODE, not the GUID.** `StockProjectionConsumer` attributes stock
> by `itemIdOne`; `ItemId` rides along. So the correct requirement for a cutover till is:
>
> > **MAUI must derive `ItemId` exactly as the web till does** —
> > `DeterministicGuid.ForItem(businessId, itemIdOne)` on the **legacy Business id** — so the two
> > TILLS agree with each other. It must **not** be checked against migrated history.
>
> WP2's DoD is corrected accordingly. `Cutover.SeedCatalogueAsync`'s `centralIdLookup` hard stop is
> still the right shape — but its lookup must be fed the web till's derivation, which is what the
> existing test `Agrees_with_a_server_that_derives_ids_the_same_way` already asserts. **Pass it
> `null` against today's server**, because today's server has no catalogue UUIDs to ask about.

**Two consequences worth carrying into the translation-agent work:**
- If §3.3 option **(a)** is ever taken (real UUID PKs on `Items`), those UUIDs **must** be
  `DeterministicGuid.ForItem`, not freshly minted — otherwise the catalogue disagrees with both
  tills on day one.
- The two-population split is tracked debt, not damage: reports key on `ItemIdOne`. But **8,120 of
  82,965 sale lines carry no barcode at all** (~10%, mostly migrated history), and those can never
  be item-attributed by any report. Worth knowing before anyone trusts an all-time item ranking.
