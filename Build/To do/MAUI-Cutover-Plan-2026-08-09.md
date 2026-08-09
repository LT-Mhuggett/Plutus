# MAUI cutover + parity — the execution plan (WP6–13, no questions)

**Created 2026-08-09; rewritten the same day as the SELF-SUFFICIENT execution document** for an
autonomous (Sonnet-grade) session. The goal, in Matt's words: **bring the MAUI till up to the same
functionality as the web till and portal.** One platform, three surfaces, equal capability.

**Authority order on any conflict:** `Build/plutus-platform-architecture.md` →
`Build/till-design.md` → **this document** → `MAUI-Retrofit-Plan-2026-08-07.md` (kept for WP prose,
DoDs and §9 defaults 1–9; **its §4 build order is superseded by this plan for WP6–13**).

**Why this document exists:** "finish WP6–13" is not a screen-porting job. The survey behind this
plan (2026-08-09, verified against the tree, not the plan prose) found that **the MAUI till cannot
post a sale to Plutus at all** — its checkout writes a legacy EF object graph and calls
`db.Save()`; it never builds an `IngestSaleRequest`, never touches the outbox, never calls
`/api/v1/sales`. Every screen port is cosmetic until Steps 9–14 land. Scale: **~55–75 working
days**. Steps 1–14 deliver the most working till soonest.

---

## A. Execution protocol (extends retrofit plan §0 — read that first, it all applies)

1. **One STEP per working session**, in this plan's order. Announce the step, build it, run its
   VERIFY, paste the test output, commit, tick the step's box in §C **and** update the retrofit
   §3b board and `till-design.md` Part B rows **in the same commit** (CLAUDE.md reflex).
2. **Required reading before any code, in order:** `Build/repo-runbook.md` (pitfalls §"Codebase
   pitfalls" have each cost a session) → `Build/till-design.md` **Part C2** → this document's §G
   pitfalls → the step body.
3. **NEVER deploy, NEVER push.** Backend changes are verified through `PlutusAppFactory`
   integration tests and a locally-run `Plutus.DBService`. Matt deploys via the runbook when he
   chooses. ETRIE is untouchable. Never run `ops/keycloak/run-keycloak.sh`.
4. **Verification split.** Everything marked VERIFY is headless and gates the step. Everything
   marked **USER-VERIFY** (real enrolment round-trip, paper receipts, drawer, scanner, visual
   theming) goes onto the §H checklist for Matt and does NOT block the next step — but the step is
   not *signed off* until Matt ticks it. Never claim a USER-VERIFY item is done.
5. **Money rules are mutation-checked.** Any step touching VAT, prices, refunds, tenders or
   totals: after its tests pass, deliberately break the rule, watch a named test fail, restore.
   Say so in the commit message. A test never seen to fail is not evidence.
6. **Line numbers in step bodies are anchors as of 2026-08-09, not gospel.** Re-locate by symbol
   name if the file has moved on. If a step's premise turns out false (the code already does it /
   no longer exists), say so in the commit and adapt — do not force the step.
7. **Versions:** bump the changed component's `versions/*.txt` in the same commit —
   `till-maui` for app changes, `platform` for `src/Plutus.*` library changes, `backend` for
   server changes. `MAJOR.FEATURE.FIX`.
8. **Surfaces in scope for the autonomous session:** MAUI app, `src/*` libraries, backend, tests.
   **Web-till TypeScript is edit-only-when-a-C2-rule-requires-dual-landing**, and every TS edit is
   flagged ⚠ NOT TYPECHECKED (no Node on this box) and added to §H. WP15/WP17 web-till work is
   OUT of scope here — it needs the Mac.
9. **Stop-and-report conditions** (the only reasons to halt): an architecture-doc conflict; a
   package not on the retrofit §0 allow-list seems required; a schema migration on the LIVE MySQL
   database seems required that this plan does not name; anything that would touch ETRIE or
   deploy. Everything else has an answer in this document — **§B is the answers.**

---

## B. Binding defaults — the decisions, already made (Matt can veto any before its step starts)

Continues the retrofit plan's §9 numbering (1–9 live there).

| # | Decision | Why | Used by |
|---|---|---|---|
| **10** | **When in doubt, MATCH THE WEB TILL.** `Plutus.Frontend.WebApp/src/api.ts` + `till/*.tsx` are the reference for behaviour, wording, payload shape and arithmetic. If this plan and the web till disagree, the web till wins and the discrepancy is noted in the commit. | Parity IS the requirement; inventing a better answer on one till is how C2 rows are born. | every step |
| **11** | **Online operator auth = `POST /api/Auth/Login`, exactly like the web till.** No new token endpoint. MAUI calls it when online, holds the token in memory for the session, and its `pos.sell` scope satisfies `SalesIngest`; `perm:*` routes resolve from RBAC by the token's userId (runbook pitfall 5). Offline sign-in stays the roster. **Bundle the fixes:** `/api/Auth/Login` must check `Employee.Active` (verified missing); `POST /api/v1/tills/unenrol-request` gets a device-token policy; `GET /api/v1/sales` and `GET /api/v1/cash-events` get `pos.*` alternatives alongside their portal permissions. | The web till already trades on this token — evidence it works. A parallel v2 endpoint would be a second door to maintain. | Step 19, then 23–27 |
| **12** | ✅ **CONFIRMED — Matt, 2026-08-09: "You should not be able to refund MORE than the price paid for it."** That invariant is enforced at BOTH gates. **At the till** (step 16): `RefundRules.Authorise` caps at the remainder and refuses past it — ⚠ **no override, supervisor included, may exceed the remainder**; ceilings authorise *up to* what is owed, never beyond it. **At ingest** (step 17): `SalesIngestService` re-runs the same `Authorise` against the origin sale's recorded refunds and **quarantines (202)** anything claiming more — never recorded as a clean sale, never in a report or a VAT figure until a human reviews it. Quarantine rather than 400 because the money (if any) already left a drawer on a till that broke the rule: a 400 makes the evidence vanish into the till's Failed queue, quarantine preserves it where the portal can see it. | The cap was client-only on both tills; a modified till could over-refund silently. Two gates running ONE shared rule survive a compromised client. | Steps 16, 17 |
| **13** | ✅ **CONFIRMED — Matt, 2026-08-09 ("Do I need more?" — no).** **Tenders = the fixed `TenderType` set (Cash, Card, Online, Credit, GiftCard), moved to SharedKernel.** Five covers everything the platform takes today: the web till exposes exactly these (`api.ts tenderTypeFor`), Online is how webstore orders ingest, Credit is store credit, GiftCard is WP13's redemption. No payment-method roster endpoint; the legacy `/api/PaymentMethod/Index` is not consumed. ⚠ If a sixth ever arrives (PayPal, cheque…), it is a cheap ADDITIVE change — one enum member, one till button, one C2 pin for the TypeScript copy — not a redesign; the wire is a byte with room. | A roster endpoint for five constants is machinery without a requirement. | Steps 8, 9 |
| **14** | **Discounts = manual line discount first** (positive inc-VAT `DiscountPence`, gated `pos.discount` with the operator's ceiling), scaled by `VatLineMath.ForLine`. Catalogue/scheduled discounts wait until a server endpoint exists — there is none, and the legacy `/api/Discount/Index` is not the answer. | Matches what the wire supports today (`discounts: [{id, rate}]`); anything more invents server behaviour. | Steps 9, 12 |
| **15** | **Parked baskets are local-only**, in the v2 `SavedBasket` table, serialised as **contract JSON via `PlutusApiClient.Json` — no Newtonsoft `$type`**. No server sync (the web till parks locally too). | `$type` coupling already broke discounted parked baskets once (`LocalSchema.cs` records it). | Step 18 |
| **16** | **First sign-in of any account on a device must be ONLINE** *(Matt, 2026-08-09)*. That first online login (default 11's `/api/Auth/Login`) mints a **device-local PBKDF2 verifier** (fresh salt) stored in the v2 store; offline sign-in verifies against the local verifier. The roster keeps shipping hashes until both tills run verifiers, then the server stops shipping them (flagged, separate change, C2 row required). Cold start — a till that has never been online — cannot sign anyone in, deliberately: it has no catalogue or prices either. Horizons unchanged (retrofit §9.8). | Password hashes stop travelling to every counter; offline access is only ever granted to someone already verified on that machine. | Step 28 |
| **17** | **Reporting series with no server answer are DROPPED, not locally recomputed**: the pro-rated "per-tender ex-VAT" series and the per-day-by-tender breakdown go, gross-per-tender stays; calendar bounds come from the queried range. A dropped series is noted in Part B. `summary-rich` is in **POUNDS**, everything else in **PENCE** — encode it in the contract type names. | Local re-derivation is C2 drift by construction — the exact thing WP11 exists to remove. | Step 26 |
| **18** | **Additive feed fields are allowed**: `CatalogueItemDto` gains `Brand`, `Description`, `CostPence?`, `StockQty?`, `Barcodes[]` (server populates from legacy tables; nullable, so old servers stay compatible), and `ApplyCatalogueAsync` writes barcode aliases + the price timeline into `Barcodes`/`PriceSchedule` (⚠ VERIFY FIRST — the survey says nothing populates either; if it already does, skip). The MAUI store-info screen **drops the logo** (no contract field; the portal owns branding). | WP10's item list cannot render from fields that never arrive; multi-barcode items silently do not scan today. | Steps 10, 25, 20 |

---

## C. Status board — tick here, same commit as the work

```
Phase 0  FOUNDATION            [x]1 EF9  [x]2 reference  [x]3 TillStoreAccess  [x]4 EnrolmentFlow ✅ COMPLETE
Phase 1  LINE PRIMITIVES       [x]5 price pair  [x]6 TaxId+StockUntracked  [x]7 VAT band store  [x]8 tender values→SharedKernel ✅ PHASE COMPLETE
Phase 2  MONEY PATH            [x]9 basket+assembler ✅  [ ]10 v2 lookup  [ ]11 CommitSaleAsync  [ ]12 permission gates
                               [~]13 sync services (CatalogueSyncService ✅; OutboxPushService + 60s scheduler ⬜)
                               [ ]14 receipt re-signature
Phase 3  RETURNS/PARK/REPRINT  [ ]15 sale read path  [ ]16 RefundRules wiring  [ ]17 server refund cap  [ ]18 parked baskets
Phase 4  OPERATOR AUTH         [ ]19 /api/Auth/Login wiring + the four backend fixes
Phase 5  SCREENS               [ ]20 WP6  [ ]21 first-run deletions  [ ]22 WP7  [ ]23 WP9  [ ]24 WP8 users
                               [ ]25 WP10  [ ]26 WP11  [ ]27 WP12+WP13
Phase 6  HARDENING             [ ]28 online-first login (default 16)
```

Steps 1–3 landed in `ea9787f` (2026-08-09) — EF unified on 9.0.18, `Plutus.Client.Storage`
referenced, `TillStoreAccess` + `CatalogueSyncService` built, sign-in kicks a background catalogue
sync, the Plutus tab's catalogue button performs the real sync. `LegacyDatabaseUnderEf9Tests`
pins the legacy layer under EF 9.

**→ Milestone worth shipping: end of Step 14.** The till sells into Plutus with correct VAT, a
durable outbox and a receipt. Everything after is breadth.

---

## D. The steps

Line refs are 2026-08-09 anchors — re-locate by symbol if drifted (§A.6).

### Phase 0 — Foundation

**Step 1 — EF Core unified on 9.0.18** ✅ `ea9787f`. The wall: legacy migrations were *compiled*
against EF 3.1 and died under 9 with `MissingMethodException: MigrationBuilder.CreateIndex(...)`;
fixed by retargeting `Plutus/Data/Database` to `net10.0`. ⚠ Residual risk: the Till/Inventory
eager-`Include` chains (`SearchId(...).Include(DisItems).ThenInclude(Discount)...`) are untested
under EF 9 — **extend `LegacyDatabaseUnderEf9Tests` with one test per Include chain before Step 10
retires them**. USER-VERIFY: one real till launch.

**Step 2 — `Plutus.Client.Storage` referenced** ✅ `ea9787f`.

**Step 3 — `TillStoreAccess`** ✅ `ea9787f`. Single owner, one semaphore, `TryUseAsync` never
throws. VERIFY (still owed): `AppClient.Tests/Storage/TillStoreAccessTests.cs` — concurrent calls
serialise; a poisoned path returns default.

**Step 4 — Route enrolment through `EnrolmentFlow`** ✅ **DONE 2026-08-09.** Enrolment now goes
through the flow (Meta gets TillId/StoreId/BusinessId/TenantId/ServerUrl); `TillPlacement` is the
single resolver — Meta first, then the legacy Preferences value, then the device-status endpoint,
back-filling Meta each time — which **replaced three separately-written copies** of that recovery
block in sign-in, roster sync and store lookup. Placement refreshes on every start in the
background, so tills enrolled before this self-heal. ⚠ The archive gate is deliberately passed
`null` until step 21 exists to archive; enabling it first would refuse enrolment with no way
through, on every till that has ever opened its legacy file — which is all of them, because the
`Database` constructor creates one on first touch. **Step 21 must switch it on.** Verified:
`Placement_refresh_is_idempotent_and_never_blanks_what_it_already_knew` (mutation-checked — blanking
before refresh fails it), Integration 125 · Unit 640 · Arch 13 · AppClient 309.
⚠ Still owed from Step 3: `TillStoreAccessTests`.

*Original body, kept for the reasoning:* ⚠ SILENT BLOCKER. Nothing writes
`MetaKeys.StoreId/BusinessId/TillId/ServerUrl` — `ConnectionViewModel.EnrolAsync` (≈`:287`) calls
`api.EnrolAsync` directly. Switch it to `src/Plutus.Client.Storage/EnrolmentFlow.cs` —
`EnrolAsync` then `RefreshPlacementAsync` (learns StoreId from `/tills/{id}/name`, BusinessId from
`/stores/{id}/info`). Also call `RefreshPlacementAsync` on app start for already-enrolled devices
(Matt's till enrolled before this existed — it must self-heal, the same pattern as the till-id
recovery in `88ded02`). ⚠ `BusinessId` ≠ TenantId — `DeterministicGuid.ForItem` seeds from it;
wrong value = every item id diverges from the web till's, silently.
VERIFY: after enrolment all four Meta keys non-null; `RefreshPlacementAsync` idempotent; an
already-enrolled device back-fills on start. USER-VERIFY: real enrolment round-trip.

### Phase 1 — Line primitives ✅ **COMPLETE 2026-08-09**

All four landed together as the plan intended. Two things worth carrying forward:

⚠ **Step 8 changed shape, and the reason matters.** Declaring `TenderType`/`SaleChannel` enums in
SharedKernel was tried and **reverted**: those names already exist in `Plutus.Entities.Models`, and
**69 backend files import both namespaces**, so every use became `CS0104: ambiguous reference`.
Renaming the backend's copy instead would change the CLR type of mapped EF properties and move the
model snapshot — a `PendingModelChangesWarning` against a live database, which is the failure that
took the test backend down earlier the same day. `IngestTender.TenderType` is a **byte** on the
wire, so the values were all a client ever needed: SharedKernel got `Tenders`/`SaleChannels`/
`Adjustments` **constants** plus `Tenders.FromMethodName`, and `TenderTypeParityTests` pins them to
the backend enum AND to the web till's `tenderTypeFor` (read out of `api.ts`).

⚠ **The declared VAT rate wobbles further at low prices than the docs say.** `VatLineMath` quotes
1993–2004bp for a 20% line; that is the range for £10–£20. A £5.00 item resolves to **1990bp** and a
penny item lands further out still, because the rounding error is a fixed half-penny against a
smaller base. Correct per C1 rule 2 (the rate comes FROM the pair) — but **anything that ever
range-checks a declared rate must scale with the line, not use a flat window.**

*Original body:*

**Step 5 — `TillStore.EffectivePricePairAsync` (inc AND ex).** `EffectivePricePenceAsync` returns
inc only; `VatLineMath.ForLine` needs the pair, and C1 rule 2 **forbids deriving ex from a rate**.
The ex derivation exists as private `ExFromInc` (≈`TillStore.cs:175`) — surface it.
VERIFY: a scheduled reprice returns the correct pair either side of its instant; the pair
round-trips `VatLineMath.RateBpFromPair` to the item's band.

**Step 6 — Expose `TaxId` + `StockUntracked` on `CatalogueItem`.** `TaxId` is buried in
`BandData` via private `LocalPricing` — `VatBandCache.BandKeyForTaxIdAsync` has nothing to be
given, so `LineMeta.VatBand` can never be set (zero-vs-exempt indistinguishable). And
`TillStore.Map` drops `CatalogueItemDto.StockUntracked` — WP10's untracked DoD has nothing to read.
VERIFY: a synced item exposes both; band key resolves for an unambiguous tax row and nulls for an
ambiguous one.

**Step 7 — `MetaVatBandStore : IVatBandStore`.** No production implementation exists anywhere
(the only one is a test fake); nothing constructs `VatBandCache`. Implement over
`TillStore.Get/SetMetaAsync`. ⚠ Cache the WHOLE effective-dated timeline, never today's rate —
the timeline is what lets an offline till apply a future-dated VAT change on the day.
VERIFY: a future-dated band point resolves old-rate-before / new-rate-after with the network down.

**Step 8 — `TenderType`/`SaleChannel` → SharedKernel.** They live in `Plutus.Entities/SalesV2.cs`
(a backend module client libraries may not reference); the only name→byte map is the web till's
`tenderTypeFor` (`api.ts:1014–1022`). Move to SharedKernel, make `Plutus.Entities` use it (or
pin equality in a test), add the C2 drift row for the TypeScript copy.
VERIFY: architecture tests green; a pinning test asserts each byte matches the web till's mapping.

### Phase 2 — THE MONEY PATH ⚠ highest risk; read till-design C2 first

**Step 9 — Basket + assembler** ✅ **DONE 2026-08-09.** `Client.Core/Basket.cs`: `BasketLine` (long pence, `IsReturn` a flag not a subclass), `SaleAssembler.Assemble` and `.Total` — ONE calculation, so the screen total and the payload cannot disagree. `SharedKernel/LineDiscounts` holds the discount rule. ⚠ **The MAUI-side reshape of `Models/BasketItem.cs` and the nine `is BasketReturnItem` type-tests deliberately did NOT land here** — reshaping them without repointing `TillViewModel` would not compile, so they move with steps 10–11. Verified by 19 unit tests AND `SaleAssemblerE2eTests`, which posts an assembled mixed-rate basket to the REAL `/api/v1/sales` and asserts **201 Recorded, not 202 quarantined**. Mutation-checked: rate-arithmetic VAT and a removed IdOne guard each fail a named test.

*Original body:* ⚠ the biggest single missing piece.
Build in `src/Plutus.Client.Core/Basket.cs` (headlessly testable, reusable by any future till).
- Reshape `Models/BasketItem.cs`: drop `Database.Models.ItemModel`; hold `ItemId (Guid)`, `IdOne`,
  `Name`, `UnitIncPence`/`UnitExPence` (**long**), `VatBandKey`, `OverriddenFromPence`,
  `bool IsReturn` + `ReturnRef` (collapse `BasketReturnItem` — `VatLineMath.ForLine(isReturn:true)`
  handles every sign and refuses a negative quantity precisely because it would double-negate).
  Money becomes `long` pence throughout (`IBasketRecord`, `TillViewModel`).
- Per line: `VatLineMath.ForLine` → `IngestLine`. **Sale totals are Σ of line figures, never
  recomputed** — the server's reconcile invariants reject a sale that disagrees with its lines.
- ⚠ `LineMeta.ItemIdOne` is load-bearing and fails SILENTLY: `StockProjectionConsumer` skips lines
  without it — stock quietly stops moving while every sale reports success. **Assert it in the
  assembler.** The payload envelope (band identity, ex price, return origin) rides in the line's
  meta exactly as `api.ts:958–1019` builds it — that block is the reference implementation.
- ⚠ Fix the percent-discount bug while here: `TillViewModel` (≈`:657,:672`) computes
  `item.Price * Decimal.Parse(amount)` — entering `10` for 10% charges **ten times** the price.
  Discounts become default 14's `DiscountPence` through `ForLine`.
VERIFY: mixed-rate basket Σ==totals to the penny; a discounted line matches the web till's
arithmetic on the same input (fixture from `api.ts` numbers); return-line signs; a line missing
`ItemIdOne` throws. **Mutation-check the lot (§A.5).**

**Step 10 — Item lookup → v2 store.** `TillViewModel.FindItem` (≈`:1172`): `db.SearchId(needle)` →
`TillStoreAccess.UseAsync(s => s.FindByBarcodeAsync/SearchAsync)`. Fixes a live defect: `SearchId`
ignores tombstones, so **a binned item is still sellable today**. Price from Step 5's pair.
⚠ Default 18 VERIFY-FIRST applies: check whether `ApplyCatalogueAsync` populates `Barcodes` and
`PriceSchedule`; wire whichever is dead.
VERIFY: binned item does not resolve; scheduled reprice applies at its instant; alias scan resolves.

**Step 11 — Checkout → `CommitSaleAsync`; delete the stock decrement.** `FinaliseTransation`
(≈`:1046–1159`): both legacy writes → one `CommitSaleAsync(request)` (sale + outbox row are ONE row
in ONE transaction; DeviceSeq allocated inside). **Delete the per-line stock decrement**
(≈`:1055–1061`, one `Save()` per line, no transaction) — v2 has no local stock; the server
attributes movement from `LineMeta.itemIdOne`. ⚠ Commit BEFORE printing.
VERIFY: after commit, outbox depth 1, DeviceSeq unique and monotonic; crash-between-commit-and-print
leaves the sale recorded.

**Step 12 — Permission gates → `SignedInOperator.Can(...)`.** Replace `IsAuthorised("Till", …)`
string gates with `PermissionCatalogue` codes + the operator's own ceilings; supervisor escalation →
`OperatorLogin.AuthoriseOverrideAsync` (refuses self-auth, applies the supervisor's ceiling, names
both people). ⚠ Fix: refund threshold (≈`:999`) sums `bRI.Price` **without quantity** — five £30
returns test as £30. ⚠ `ExecuteAdjustItem` has NO gate today → `PosPriceOverride`. ⚠ Do NOT port
`Authorisation.RequestAuthorisedUserInput` — it never assigns `authEmpId` and always returns
default. ⚠ A null `SignedInOperator` **blocks**, never silently allows.
VERIFY: ceiling boundaries; self-auth refused; null operator blocks. Mutation-check.

**Step 13 — `OutboxPushService` + the 60s cadence.** `CatalogueSyncService` ✅ exists; build
`OutboxPushService` over `Client.Core.OutboxPusher.DrainAsync` (referenced NOWHERE in the app
today) and one 60s scheduler driving heartbeat + outbox drain + catalogue sync + notices poll —
the same cadence as the web till (`App.tsx`). ⚠ A failing heartbeat is silent and never stops
selling. ⚠ Never poll `POST /api/v1/tokens/device` (rate-limited 5/min/IP) — status comes from
`GET /api/v1/tills/devices/{id}/status`. ⚠ Don't run a catalogue sync mid-basket — the basket
owner gates it.
VERIFY: 202→Quarantined never retried; 400→Failed skipped without blocking the queue; 401→re-mint
once; transport failure→Pending + drain stops; backoff 5s→5min cap.

**Step 14 — Receipt off legacy models.** `PosPrinterManager.SetUpSalePrint(SaleModel, …,
StoreModel)` → contract sale + line records + a store header cached from `StoreInfoResult` into
Meta (nothing caches it today). Rendering from the portal's `ReceiptTemplateJson` stays deferred
(no schema/parser exists in any client — that is WP3's business, not this step's).
VERIFY: existing `PosPrinterManagerTests` composition. USER-VERIFY: paper.

### Phase 3 — Returns, parking, reprint

**Step 15 — The sale READ path (three missing methods).** A committed sale cannot be read back at
all today (`GetPendingAsync` filters Pending) — blocking reprint, offline refunds and X/Z at once.
Build: `TillStore.FindLocalSaleAsync(saleId)` + payload→`IngestSaleRequest` deserialiser;
`TillStore.AlreadyRefundedPenceAsync(originSaleId)` — ⚠ needs a real **indexed column** on
`LocalSale` (origin id is buried in JSON, unqueryable; add it at commit time);
`PlutusApiClient.GetSaleAsync(id)` → `GET /api/v1/sales/{saleId}` (exists; policy already includes
`pos.refund`; needs Step 19's operator token).
VERIFY: commit → find → deserialise round-trips; refunded-so-far sums across two part-refunds.

**Step 16 — Wire `RefundRules` into `ExecuteReturn`.** Delete the legacy query block
(≈`:486–547`); `ClassifyLocal` (14-day window) → `Authorise`. Server record preferred when online
(only it knows other tills' refunds); local only inside the window. ⚠ Surface `WasCapped` — the
doc comment is explicit that silently refunding less starts disputes. Keep `NeedsConnection` and
`UnknownSale` as different messages. ⚠ Fix two live defects: `trans.First()` throws when the item
is not on the sale; non-short-circuit `&` across two `TryGetValue`s.
VERIFY: cap/window/unknown matrix, capped-and-said-so. Mutation-check.

**Step 17 — Server-side refund cap (default 12 — ⚠ confirm with Matt first).**
`SalesIngestService` + `RefundRules.Authorise` against the origin sale's recorded refunds;
over-refund → quarantine 202 with reason.
VERIFY: integration — hand-crafted over-refund posts 202 not 201; legitimate part-refunds post 201.

**Step 18 — Parked baskets (default 15).** `SavedBasket` table exists, mapped, and `TillStore`
never touches it. Add `SaveBasketAsync/ListBasketsAsync/DeleteBasketAsync`; replace the Newtonsoft
`TypeNameHandling.Auto` park/recall (≈`:687–819`) with contract JSON; load async (the ctor
currently blocks the UI thread on a DB read).
VERIFY: park → kill → restore, including a discounted and a return line.

### Phase 4 — Operator auth

**Step 19 — `/api/Auth/Login` wiring + four backend fixes (default 11).** MAUI online sign-in
calls the same endpoint the web till uses; token held in memory; offline stays the roster. Backend
fixes bundled: `Employee.Active` checked at login (verified missing — deactivation is currently
theatre); `unenrol-request` reachable by the device token its own doc-comment names as the caller;
`GET /api/v1/sales` + `GET /api/v1/cash-events` gain `pos.*` alternatives.
VERIFY: integration — device token 403s a `perm:*` route; operator token passes; deactivated
employee refused online AND their next roster sync drops them.

### Phase 5 — Screens, cheapest-first (retrofit plan WP bodies hold the full DoDs — read them)

**Step 20 — WP6 Store Information (~1 day, a deletion).** Strip the five local-write commands +
`StoreModel`; one `GetStoreInfoAsync` + an `OpeningHoursJson` weekly table; cache last-good; show
"unavailable", never stale. **Drop the logo** (default 18 — no contract field).

**Step 21 — Delete the obsolete first-run screens (~2 days).** `SetupViewModel`,
`TransferThirdPartyViewModel` (self-labelled LEGACY; the latter throws from `async void`), their
views and `<Compile Update>` items. ⚠ **Delete `LoginViewModel.EnsureStoreAsync`** — it was a
2026-08-09 hotfix that writes API data into the legacy `Stores` table, which is exactly the bridge
default 9 forbids; Step 14's Meta-cached store header replaces it. ⚠ Keep `RecoveryViewModel` —
reframed as the cutover on-ramp feeding `Cutover.ArchiveLegacyDatabase`. ⚠ Delete
`SettingsViewModel.ExecuteDeleteDb` outright (destroys the translation agent's only input, no
undo); `ExecuteBackupDb` becomes "Archive legacy database" stamping `MetaKeys.LegacyArchivedAtUtc`.
⚠ Fix: `AppViewModel.EmployeeId` = `Employees.Last().Id` throws for every portal-roster operator —
route through `SignedInOperator`.

**Step 22 — WP7 theming (~3–4 days).** 7a: port ClientUI's `Colors.xaml` verbatim (same `x:Key`
names) + `Styles.xaml` + Syncfusion theme mapping. 7b: `GetEffectiveThemeAsync` on start + Step 13
cadence, cached (an `IVatBandStore`-shaped Meta store) so a scheme survives an offline restart;
map `baseMode` → `UserAppTheme`, `colorsJson`'s seven tokens → the 7a keys. ⚠ Receipts ignore the
theme entirely (C1). Removes ClientUI from `Plutus.slnx` (keep the directory). USER-VERIFY: visual
+ byte-identical receipt under light/dark.

**Step 23 — WP9 cash (~4–5 days).** `CashEventRequest` DTO + client method (absent from
contracts); `CashPage`/`CashViewModel`: Open float · Paid in/out (reason mandatory) · X · Z.
`POST /api/v1/cash-events` is `SalesIngest` → **device token suffices; ship write-first**.
⚠ Expected/counted/variance are SERVER-computed — render from the response. Guard the second Z
client-side; the server 409s anyway. History list needs Step 19.

**Step 24 — WP8 Users screen + roster store (~3 days).** Employee list/create + set-password via
legacy `/api/Employee` + `/api/Auth/SetPassword` (no client methods exist — build them; roles stay
portal-side). Move the roster from `FileOperatorStore` (JSON file) onto
`TillDbContext.Operators` — declared, mapped, used by nothing; its stated blocker (EF 3.1) died in
Step 1. Two roster stores is drift by construction.

**Step 25 — WP10 inventory + stock ledger (~8–10 days) ⚠ largest screen gap.** Feed + schema
first (default 18: Brand/Desc/Cost/StockQty/Barcodes) — the list cannot render from fields that
never arrive. `CreateUpdateStock()` → `POST /api/v1/stock/movements`; quantity column →
`GET /api/v1/stock/levels`; category CRUD → `/api/v1/categories` with the 409 reassign-first flow.
**Delete `ViewAllViewModel.FilterItems`** — `TillStore.SearchAsync` owns the rule; build
`TillStore.ListAsync(skip, take)` for the browse case. Mirror the band guard
(`|Price − ExPrice×Rate| > 2p`) client-side. ⚠ Needs a `pos.*` inventory permission code — add
`pos.stock.adjust` to `PermissionCatalogue` + the built-in roles (Owner/Company Admin/Store
Manager), and a migration-free RBAC re-seed note for Matt. ⚠ Fix two latent crashes: the
`GroupDescriptor` ctor-order dereference, and `KeySelector` on an empty name. Full DoD in the
retrofit WP10 body (add-unknown-scan, Bin, untracked).

**Step 26 — WP11 reporting + cross-till lookup (~8–10 days) — a rewrite.** Build
`ReportContracts.cs` against `ReportsController` cross-checked with `api.ts:502–563`; pounds/pence
trap and `vatPence` vs `vatChargedPence` encoded in type names (default 17). Point the three
Statistics viewmodels at `/reports/summary-rich`, `/reports/summary`, `/reports/vat`,
`/reports/items-sold`, + the two new screens (`category-sales`, `best-sellers`). Local SQLite is
no longer read for any report. Surface the 5000-line/200-row server caps as a visible marker.
⚠ Move viewmodel construction out of XAML (`SalesReportsView.xaml:16`) so they can be DI'd and
tested. Cross-till refund wiring completes here (Steps 15–16 + operator token). Full DoD in the
retrofit WP11 body.

**Step 27 — WP12 loyalty then WP13 gift cards (~12–15 days).** ⚠ Extract `MemberNumbers` from
`src/Plutus.Customers` to SharedKernel FIRST (backend module; MAUI may not reference it; the
Crockford check character is a rule). `LoyaltyCache` table into `LocalSchema` (hint only, never
redemption input; credit tender = attached + live-balance-this-session + online — exactly the web
till). Member-card scan: leading `C` + valid check digit routes to customer attach, bad check says
so. WP13: redemption forks on VAT treatment (multi-purpose = tender + ZERO-VAT activation on the
provisioned `GIFT-CARD` item; single-purpose = negative standard-rated line); ⚠ replace the
hardcoded `/1.2` with the Step 7 band cache — **on the web till too** (C2, flag NOT TYPECHECKED);
`GiftCardSettings` absent → 409 → render the friendly message. Full DoDs in the retrofit bodies.

### Phase 6 — Hardening

**Step 28 — Online-first login (default 16).** Local verifier minted at first online login;
offline verifies against it; roster hash-shipping retired behind a flag once both tills carry
verifiers (web-till half → Mac session, C2 row now). VERIFY: first-ever login offline is refused
with "connect once" wording; after one online login the same account signs in offline; a leaver
deactivated in the portal is refused online immediately and offline at the horizon.

---

## E. What must be BUILT before it can be used (consolidated)

**Client libs:** price pair · TaxId/StockUntracked exposure · `MetaVatBandStore` ·
SharedKernel `TenderType`/`SaleChannel` · `Basket` + assembler · `SavedBasket` CRUD ·
`FindLocalSaleAsync` + deserialiser · `AlreadyRefundedPenceAsync` (indexed column) ·
`GetSaleAsync` · `ListAsync(skip,take)` · theme cache · `ReportContracts.cs` · DTOs/methods for
cash, stock, categories, customers, loyalty, gift cards, `/api/Employee`, `/api/Auth/SetPassword`.

**MAUI services:** `OutboxPushService` · the 60s scheduler · `MetaVatBandStore`. (`TillStoreAccess`
and `CatalogueSyncService` ✅ exist.)

**Backend:** `Employee.Active` at login · un-enrol device policy · pos-gated sales list ·
pos-gated cash read · server refund cap (default 12) · additive feed fields (default 18) ·
`pos.stock.adjust` permission code.

## F. Riskiest steps, ranked

1. **Step 9** — every penny of every sale and every VAT return flows through the assembler; a
   wrong rounding mode diverges from the web till forever; a missing `ItemIdOne` stops stock
   silently. 2. **Step 17** — money, server-side, default 12 needs Matt's confirmation.
3. **Step 27** — the gift-card VAT fork mis-states silently if wrong. 4. **Step 23** — a double Z
   or wrong variance is a same-day cash dispute. 5. **Step 12** — a null operator falling open
   removes every ceiling. 6. **Step 21** — `ExecuteDeleteDb` destroys unarchived history.

## G. Pitfalls learned 2026-08-09 (each cost real time; all still live)

- **Green build ≠ working EF.** The EF-9 break compiled clean and died at runtime. Same lesson as
  the deploy: verify columns/behaviour, not history tables or build output.
- **`Microsoft.Data.Sqlite` POOLS connections from v6**: disposing a context does not release the
  file. `SqliteConnection.ClearAllPools()` before any copy/move/delete of a database file — the
  cutover archive is the case that matters.
- **EF 9 does not value-generate string keys** the way 3.1 apparently did (`ValueGeneratedOnAdd`
  on string `Id`): always set legacy `Id`s explicitly (`Guid.NewGuid().ToString()`).
- **EF 3.1-era `DbSet` + .NET 10 = ambiguous `Where`** (`IAsyncEnumerable` vs `IQueryable`):
  disambiguate with `.AsQueryable()`.
- **Never mutate `HttpClient.BaseAddress`** — one client per address via `PlutusHttp.TryFor`.
- **`ShellContent` does not inherit Title/Icon from its page** — copy them (`AppShell.Tab(...)`).
- **A viewmodel constructor that can throw takes down whatever constructs it** — `AppShell` builds
  every tab eagerly; null-guard reads, never dereference app state in a ctor.
- **Debug now validates XAML (XamlC validate-only)** — a bad property fails the BUILD, by design.
- **The device is what "enrolled" means; the till id is a separate, recoverable fact**
  (`GET devices/{id}/status` returns it). Never tell an enrolled till it isn't paired.
- **Tested components are not a working feature until something calls them** — WP5's entire sync
  spine sat unreferenced while the till showed no stock. Every step's VERIFY must include the
  *call site*, not just the library.

## H. USER-VERIFY checklist (accumulate; Matt ticks)

- [ ] Step 1: one real till launch on EF 9 before anyone trades on it
- [ ] Step 4: real enrolment round-trip (fresh code from the portal)
- [ ] Step 14: paper receipt renders correctly
- [ ] Step 22: theme applied from portal + byte-identical receipt light/dark
- [ ] Step 23: drawer kicks on cash events
- [ ] Step 25: scanner round-trip incl. unknown-barcode add flow
- [ ] Any web-till TS edits: `npm run typecheck` on the Mac (5 pre-existing edits already queued)
