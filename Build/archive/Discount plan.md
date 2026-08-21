# Discount plan — scheduled discounts and till-applied discounts, across all tills

**Product:** Discounts without a loyalty card — rule-driven (scheduled) and operator-applied (manual)
**Author:** Matt Huggett (Leading Talent) with Claude
**Date:** 20 August 2026
**Status:** ✅ **DELIVERED — built AND deployed 2026-08-20.**

> ## 📦 ARCHIVED 2026-08-20 — built, deployed, and live. Only the hand-run is left, and it lives elsewhere
>
> **DP1–DP5 shipped and are LIVE**: backend **1.18.0** → portal **1.14.0** → web till **1.27.0** →
> MAUI till **1.108.0**, all deployed and verified on the day. (Later work has moved those numbers on
> — the live set is in [`MAUI-retrofit.md` §0.1](../To%20do/MAUI-retrofit.md), which is the only place
> that tracks deploy state. Treat the versions in the body of this plan as *"the build that carried
> it"*, not as current.)
>
> ⚠⚠ **THE ONE THING A READER MUST KNOW: A RULE DOES NOTHING UNTIL SOMEBODY TICKS "APPLY IT
> AUTOMATICALLY".** All six pre-existing discount rules had `AutoApply = 0` and no day mask, so on the
> day this deployed the feature was working perfectly and applying nothing. Matt looked at a real
> basket and asked *"What about discounts? I still cannot see that?"* — and he was right that nothing
> was showing. That is configuration, not a defect, and it is the first thing to check before
> reporting this as broken.
>
> **What remains: only the hand-run** — **§G64 and §G65** of
> [`Build/Test Maui.md`](../Test%20Maui.md) (**§G65b first** — it is the tick-the-box step above).
> Nothing in this document is outstanding work.
>
> **Where the durable content went**, so nothing here needs to be read to work on discounts:
>
> | What | Now lives in |
> |---|---|
> | When a scheduled rule is LIVE, and what it lands on | `SharedKernel/ScheduledDiscounts.cs` + its TS twin — registered in [`till-design.md`](../till-design.md) **C1** and **C2** |
> | Which automatic discount WINS (larger, tie → member) | `SharedKernel/AutoDiscounts.cs` + its TS twin — **C2**, pinned both sides |
> | The capability rows for every till | [`till-design.md`](../till-design.md) **A0** and **Part B** |
> | Deploy state and rulings | [`MAUI-retrofit.md`](../To%20do/MAUI-retrofit.md) **§0** |
>
> ⚠ **Read the four "what the build changed" notes below before trusting §3.** Two of them are
> corrections to this plan's own text — §3.2's window boundary was wrong, and §3.1's `decimal Amount`
> was a money-unit ambiguity the architecture guard refused. **The code is right and this document was
> wrong**, which is exactly why a plan's own body is the least reliable thing in it.

> ## ✅ WHAT WAS BUILT, AND THE FOUR THINGS THE BUILD CHANGED ABOUT THIS PLAN
>
> The §4 decisions were taken as ratified (Matt: *"You should be able to implement this with no
> questions"*), so **D1 auto-apply · D2 larger-wins · D3 auto reason, no authoriser, no ceiling ·
> D4 webstore ➖ · D5 line-add instant · D6 portal-only · D7 mix-and-match out · D8 typed £/% on both
> tills** are all as recommended. What the build discovered:
>
> **1. ⚠⚠ The plan's §3.2 was WRONG about the window boundary, and the code was right.** It said the
> permission twin was *"inclusive-start/exclusive-end"*. `PermissionGrant.IsActiveAt` refuses `t <
> start` and `t > end` — **inclusive at BOTH ends**, so a 09:00–17:00 rule is live at 17:00:00. The
> rule mirrors the code, not this document, and both suites pin it.
>
> **2. ⚠⚠ One `decimal Amount` meaning POUNDS or a FRACTION was refused by the architecture guard,
> and the guard was right.** `No_module_declares_decimal_or_double_money_members` went red. The wire
> and the shared rule now carry **`PercentFraction`** (a ratio) and **`FixedAmountPence`** (integer
> pence) as separate fields; the legacy decimal-pounds column is collapsed once, server-side. This
> removed the most dangerous ambiguity in the original design — §3.1's own text needed a ⚠⚠ warning
> that `Amount` was pounds, which is the smell that says split the field.
>
> **3. ⚠⚠ MAUI could not have targeted a category at all.** Its basket holds a legacy `ItemModel`
> whose `CatId` is an **int** into the NatApp `Categories` table — empty and permanently so on a
> portal till. A category rule would have matched nothing on that till while working on the web till,
> silently. `BasketItem.CategoryId` now carries the v2 Guid, set at both add doors. §2.4's ⚠ ("check
> at build that the category id is actually on the object the basket holds") is what caught it.
>
> **4. §3.8's flagged worry resolved the other way: MAUI was ALREADY safe.** It populates
> `LineMeta.discounts[]` for **nothing at all**, so its typed discounts have always travelled as
> `DiscountPence` + authority. The web till needed the fix instead — its filter tightened from
> `!== 0` to **`> 0`**, so "only a positive id is a real catalogue discount" now covers both sentinels
> and any future third. The asymmetry (MAUI projects no catalogue discount into
> `Transaction_Discount`) is recorded in C2 rather than "fixed" — it is legacy-bridge reporting only.
>
> ### What was deleted, not left beside the new thing
>
> **`MemberDiscountBasket` is gone**, superseded by `AutoDiscountBasket`: one automatic discount
> became a SET, because two rules on two categories with a tier out-bidding one of them is two
> alterations. All 16 of its vectors are ported. Same for the web till's `applyMemberDiscount` /
> `clearMemberDiscount` actions — one `autoDiscounts` action recomputes from scratch, so "member
> detached", "rule expired" and "a bigger rule arrived" take one code path and there is no clear-down
> to forget.
>
> ### Verified — every suite, and both halves of every twin
>
> unit **1526** · MAUI **625** · architecture **26** · web till **335 vitest** (⚠ **run on the build
> Mac**, not merely written) · `tsc --noEmit` clean and `eslint` 0 errors on both frontends · both
> frontends **vite build** clean · backend builds. **Six mutants killed**: empty-target→everything,
> day-mask ignored, tie flipped, and the three the migration review caught below.
>
> ⚠⚠ **THE MIGRATION NEEDED A HAND EDIT, AND READING IT IS WHY.** EF generated
> `Active` with `defaultValue: false`. A C# property initialiser (`= true`) is applied by the CLR to
> new objects — it is **not** a schema default and does not touch existing rows — so every discount a
> shop already had would have arrived at the new feed switched OFF. Corrected to `defaultValue: true`,
> which also gives the column a DB default so an INSERT through the legacy `/api/Discount` CRUD still
> produces a live rule. This is runbook pitfall *"OPEN THE GENERATED MIGRATION AND READ ITS `Up()`"*
> paying for itself.
>
> ### ⚠ Two consequences recorded rather than fixed
>
> **1. An auto-apply rule ALSO appears in the manual picker.** Both tills fetch that list from the
> legacy `/api/Discount/Index`, which returns every discount and filters on neither `AutoApply` nor
> `Active`. So an operator can pick "Wednesday Warhammer" by hand on a Thursday, and a *paused* rule is
> still pickable. **Left deliberately**: applying it by hand is a decision, and it still demands a
> reason and still passes the operator's ceiling — the same guards as any catalogue discount. Filtering
> it would mean changing the legacy endpoint both tills depend on, for a case that is not wrong, only
> untidy. ⚠ If it turns out to confuse operators, the fix is to filter in the two dialogs, not on the
> server.
>
> **2. MAUI records no catalogue discount in `LineMeta.discounts[]` — for ANY discount, and always
> has.** It populates that array for nothing at all, so a catalogue discount taken on MAUI does not
> project into legacy `Transaction_Discount` while the same discount on the web till does. Found while
> checking §3.8 and recorded in C2. It affects legacy-bridge reporting only — the money, the reason and
> the authority all travel correctly — so it is a discrepancy to know about rather than a defect to
> rush.
>
> ### ⬜ What is NOT done
>
> **DP6 — the hand-run.** `Test Maui.md` **§G64** (both tills) and **§G65** (the portal screen) are
> written and have never been run. Every register row this work touched is **🟡, not ✅**, and only a
> person at a screen moves them. ⚠ **No MAUI artefact was built** — standing instruction: only when
> Matt asks. ⚠ **Nothing is deployed.** The backend carries a MIGRATION, so runbook § "Before any
> migration deploy" applies: dump first, and check the dump is not 20 bytes.

> ## The ask — Matt, 2026-08-20, verbatim
>
> *"Without using a loyalty card, I need to apply a discount to a users basket, specific item or
> items in a category. E.g. I have Wednesday Warhammer discount that should flag items in the
> warhammer catergory on a Wednesday as 'Should have 10%'. But I also need to be able to discount
> specific items in the till and or all at once. E.g. I clkick a discount button on the till, the
> till asks me to select items, with an option to select all."*
>
> Two features, and they are different sizes:
>
> 1. **Scheduled discounts** — a named rule ("Wednesday Warhammer"), set centrally, that lands 10%
>    on every Warhammer-category line rung up on a Wednesday, with no card and no operator action.
>    **This is the new build** — though the *schema* for most of it already exists, dormant (§2.2),
>    and every money rule it needs is already shared (§2.1).
> 2. **Manual discount with Select all** — the discount button asks which lines, with an option to
>    take everything. **Both tills already have the select-which-lines dialog** (§2.3); what is
>    missing is the *Select all* control, and look-and-feel parity between the two dialogs.
>
> **Authority order on a conflict:** `plutus-platform-architecture.md` → `till-design.md` → this
> document. Registers (Part A0/B/C) are updated **in the build commits**, not by this plan — §6
> lists exactly which rows. `till-design.md` was read for this plan on 2026-08-20 and is current.

---

## Contents

- §1 Overview and scope
- §2 What exists today — verified against the code 2026-08-20
- §3 The design
- §4 Decisions for Matt — recommended defaults, none ratified
- §5 The work packages — DP1…DP6
- §6 Registers this work must leave true
- §7 Size, honestly
- §8 What is deliberately NOT in this plan

---

## 1. Overview and scope

**In scope:** a portal-managed discount catalogue with targeting (everything / a category / named
items) and a schedule (days of week, time window, date range); automatic application at every till;
the manual select-items flow with Select all, identical on both tills.

**Out of scope (§8 says why):** loyalty/gems (its own programme), mix-and-match / BOGOF /
multi-buy, coupon codes, Woo online promotions, price *changes* (that is `PriceResolution` and the
effective-dated price lists — a scheduled discount keeps the shelf price and shows the money off).

⚠ **Parity is binding here, not aspirational** (CLAUDE.md / till-design Part B): a capability is
not done until its row is filled for every till, and Matt's 2026-08-19 ruling widens that to look
and feel — *"if a user swaps between the two, it doesnt matter and they would understand how to use
it."* The two discount dialogs are today functionally close and visually unrelated; DP1 closes that.

---

## 2. What exists today — verified against the code 2026-08-20

### 2.1 The money rules — all shared, none change

**This plan adds NO new discount arithmetic.** Every figure is computed by rules that already live
in `Plutus.SharedKernel` and are already pinned:

| Rule | Where | What it fixes |
|---|---|---|
| Pence off a line | `LineDiscounts.Percentage` (`LineDiscounts.cs:44`) / `FixedPerUnit` (`:25`) | Whole-line rounding AwayFromZero, capped at line value, **throws** above 1.0 (the legacy ×10 bug), returns take no discount |
| Net/VAT split of a discounted line | `VatLineMath.ForLine` (`VatLineMath.cs:75`) | Discount scaled into ex by the ex/inc ratio; VAT = gross − ex, never rate arithmetic |
| One discount across several lines | `DiscountApportionment.Across` (`DiscountApportionment.cs:33`) | Proportional, largest-remainder, ties to the earlier line |
| Bigger than the basket | `BasketDiscounts.Authorise` (`BasketDiscounts.cs:90`) | Inclusive boundary — 100% allowed |
| Typed percent → fraction | `PercentDiscountInput.FractionFromTyped` (`PercentDiscountInput.cs:38`) | `10` means 10%, both cultures, trailing `%` accepted |
| What a discount must record | `DiscountAudit` (`DiscountAudit.cs`) — incl. **`Automatic(reason, amountPence, requestedByUserId)`** at `:171`, built for exactly the auto-applied case | Mandatory normalised reason; self-approval refused |
| Authority → wire | `Client.Core/DiscountAuthorityWire.cs` → `LineMeta.discountAuthority[]` | ⚠ never onto `discounts[]` — the legacy-bridge FK tripwire |
| Ceiling + step-up | `pos.discount` `MaxPence` — MAUI `TillGate` (`TillViewModel.cs:2007`, `:2189`), web `permissions.ts` + `DiscountDialog.tsx:51-88` | Union-merge, fails closed, self-approval refused |
| Eligibility exclusions (the member precedent) | `MemberDiscount.LineIsEligible` (`MemberDiscount.cs:63`) + the tills' 4th exclusion (card surcharge: `basket.ts:90`, `MemberDiscountBasket.cs:76`) | Not a return, **not already discounted (no stacking)**, not a gift card, not the surcharge |

⚠ The C2 twins this touches: manual line discount (`:592` — half-pinned), apportionment (`:593` —
MAUI-only today; *"when the web till grows a basket-level discount this becomes a twin"* — **this
plan is that moment** if Select-all lands as one figure over many lines), reason normalisation
(`:597` — the exemplar, pinned both sides), authority wire (`:598`).

### 2.2 ⚠ The dormant catalogue-discount schema — most of the entity already exists

The legacy NatApp schema, live in the active DB and **already fetched by both tills**:

- **`Discount`** (`Plutus\Commons\Plutus.Entities\Models\Discount.cs`) — `Name`, `AllApplicable`
  (`:21`), `CanUseWithOtherDiscounts` (`:24`), **`AutoApply` (`:27`)**, `Type` (`:30` — 0 fixed /
  1 percent), `Amount` (`:33` — ⚠ a FRACTION, 0.1 = 10%), `UsesPerTransaction` (`:35`),
  `RequiredNumOfItems` (`:37`), `BusinessId` (`:46`). Real **int** ids.
- **`Discount_Category`** → table `DiscountCats` — a discount ↔ category join **with a
  `StartDateTime`/`EndDateTime` window**. **Nothing anywhere evaluates it.**
- **`Discount_Item`** → table `DiscountItems` — same shape per item (`ItemIdOne`/`ItemIdTwo`).
  **Nothing evaluates it either.**
- **`Transaction_Discount`** — what a sale's discounts project into for legacy reporting, PK
  `(TransactionId, DiscountId, SaleId)`, written by `LegacySaleBridgeConsumer.cs:195` from
  `LineMeta.discounts[]`. ⚠ **Keyed on a REAL `DiscountId`** — this is why the member discount
  (sentinel id 0) is deliberately kept OFF `discounts[]`, pinned by
  `An_authority_does_not_add_a_catalogue_discount_the_legacy_bridge_would_FK_fail_on`.
- Fetch paths that already work: web `api.ts:333` `fetchDiscounts()` → `GET /api/Discount/Index`,
  cached offline; MAUI `TillViewModel.cs:1945` `db.Get<DiscountModel>()`, with the empty-list
  sentence at `:1953-1960`.

⚠ **Only `AllApplicable` is consumed anywhere at a till** (`DiscountDialog.tsx:108`). `AutoApply`,
the joins, the windows, `UsesPerTransaction`, `RequiredNumOfItems`, `CanUseWithOtherDiscounts` are
stored and enforced by nothing. **"Wednesday Warhammer" is, almost column for column, what this
schema was built to say** — what it cannot say is *day of week* or *time of day*, and nothing reads
it. §3.1 extends it rather than building a rival.

⚠ **No screen anywhere creates a `Discount` today, as far as this survey found** — the only write
path is the legacy `/api/Discount` CRUD in `Plutus.DBService`. Whatever rows the tills list came
from NatApp's day or the migration. The portal Discounts screen (DP3) is therefore not a
nice-to-have; it is the only way a shop will ever get a rule in. *(To verify at build: whether the
19_08 full-replace carried `Discounts` rows across.)*

### 2.3 The manual flows both tills already have

- **Web till** — `till\DiscountDialog.tsx`: pick a discount from the catalogue list (`:111`
  renders `${Math.round(d.amount*100)}% off`), *"Tick the lines it applies to"* checkbox list
  (`:139-156` — ⚠ **no Select all**), mandatory reason (`:161-173`), W-P3 ceiling + supervisor
  step-up (`:178-215`). Uses `DialogX` per the D4 contract. ⚠ **No typed/ad-hoc amount** — the
  catalogue list is the only source of a figure.
- **MAUI till** — the Alterations flow: `InputMultiSelectAlert` (a multi-select `CollectionView`,
  `InputMultiSelectAlert.cs:29`) launched at `TillViewModel.cs:2049` (⚠ **no Select all**, and
  `SelectionList` is `static` at `:41` — an acknowledged latent bug if two are ever open), a
  Cash/Percent typed box through the shared `PercentDiscountInput` (`:2030-2078`), the gate at
  `:2007`/`:2189`, then `CheckoutCommit.ApplyAlterations` apportions via
  `DiscountApportionment.Across` (`CheckoutCommit.cs:137`) with the authority shares from the SAME
  call (`:140-156`).
- Both record the reason and authority per binding default 22(c); both hold the operator to the
  `pos.discount` ceiling with step-up.

⚠ So the manual half of the ask is **two small gaps, not a feature**: Select all on both, and the
two dialogs converging to one look (2026-08-19 ruling). Plus one real asymmetry to settle:
**MAUI can type an ad-hoc figure and the web till cannot** — decision D8.

### 2.4 Categories at the till — the targeting data is already there

- **One category per item, required** — `Item.CatId` (`Item.cs:69`).
- The id already reaches every till: `CatalogueItemDto.CategoryId` (`SyncContracts.cs:90`) →
  `LocalItem.CategoryId` (`LocalSchema.cs:60`) on MAUI; `Item.catId` (`api.ts:141`) on the web
  till. Names via `GET /api/Category/Index` (web `api.ts:1122`; .NET
  `PlutusApiClient.GetCategoriesAsync:467`).
- ⚠ MAUI's `BasketItem` rides the legacy `ItemModel` — check at build that the category id is
  actually on the object the basket holds, not only in the local store.

So "items in the warhammer category" is a `CategoryId` comparison a till can make **offline**, at
the moment a line is added. No server round-trip, no new sync data beyond the rules themselves.

### 2.5 The scheduling shape the codebase already trusts

`PermissionGrant` (`Permissions.cs:289`) is the house pattern for "is this live right now":
`DaysOfWeekMask` (**bit 0 = Sunday**, null = any day), `WindowStartLocal`/`WindowEndLocal` (LOCAL
wall clock), `ValidFromUtc`/`ValidToUtc` (UTC instants), `IsActiveAt(nowLocal)` at `:309`, and the
midnight-wrap window (22:00–02:00) **deliberately unsupported** rather than half-supported
(`:305-307`). Two more rules it comes with, both load-bearing here:

- **Windows travel RAW to a till, never pre-evaluated** (C1: the Saturday-only-grant lesson) — the
  till judges them against its own clock at the moment of the action, so a Wednesday rule works on
  a till that has been offline since Monday.
- The server delegates to the same shared implementation, so the portal preview and the till answer
  cannot drift.

**The scheduled-discount rule copies this shape exactly.** Not a new calendar, not a cron table.

### 2.6 What does NOT exist

No promotion/offer/coupon/markdown entity; no rule evaluator; no day-of-week column anywhere in the
discount schema; no Select all; no portal discounts screen; no v1 discounts endpoint (the tills
fetch through legacy `/api/Discount`); no TS twin for any of this (nothing exists to twin). The
Loyalty design (`Loyalty Update across all tills.md`) is explicitly not built and this plan does
not depend on any of it.

---

## 3. The design

### 3.1 One catalogue, extended — not a second discount entity

**Extend `Discount` and consume the joins that already exist.** The alternative — a fresh
platform-native `DiscountRule` — was considered and rejected:

- The existing entity has **real int ids the legacy bridge can project** into
  `Transaction_Discount`. A new entity would need either a bridge change or the member-discount
  exclusion dance for every rule discount ever taken.
- Both tills already fetch, cache and render this catalogue. The manual flow keeps working
  mid-migration.
- `Discount_Category` / `Discount_Item` already say "this discount targets that category/item",
  with date windows. Building a rival join is drift by construction.

**New columns on `Discounts`** (nullable, so every existing row keeps meaning what it meant):
`DaysOfWeekMask byte?`, `WindowStartLocal`/`WindowEndLocal TimeOnly?`, `ValidFromUtc`/
`ValidToUtc DateTime?`, plus `Active bool` if the base doesn't already carry one. `AutoApply`
(`Discount.cs:27`) stops being dead: **`AutoApply = true` is what makes a rule self-apply at the
till; `false` keeps it a manual-list-only entry.** A "Wednesday Warhammer" row is then:
`Type=1, Amount=0.10, AutoApply=true, DaysOfWeekMask=Wednesday, one Discount_Category row → Warhammer`.

⚠ The rule-evaluation logic goes to **`SharedKernel/ScheduledDiscounts.cs`** (DP2) — per D3 in
till-design: *"put the logic where every till can reach it"*. The backend home for the v1 endpoint
is **`Plutus.Catalogue`**, which already owns categories and pricing (`PricesController.cs` lives
there); no new module.

### 3.2 The schedule — `PermissionGrant`'s shape, travelling raw

`ScheduledDiscounts.IsLiveAt(rule, nowLocal)` mirrors `PermissionGrant.IsActiveAt` semantics
verbatim: day mask bit 0 = Sunday; window LOCAL and **inclusive-start/exclusive-end**, exactly as
the permission twin decided; validity UTC; **midnight-wrap unsupported on purpose, mirrored** — a
22:00–02:00 rule matches nothing on every surface until somebody fixes it everywhere at once.
Null day mask = every day; no window = all day; no validity = until deactivated.

### 3.3 Targeting — everything, a category, or named items

Precedence per rule, most specific set wins nothing — the sets are a UNION of eligible lines:
`AllApplicable` ⇒ every eligible line; else lines whose `CategoryId` is in the rule's
`Discount_Category` rows **or** whose `ItemIdOne` is in its `Discount_Item` rows. Eligibility
exclusions are the member discount's four, unchanged: not a return, not already discounted, not a
gift card, not the card surcharge.

⚠ The per-join `StartDateTime`/`EndDateTime` columns stay **unread** — the schedule lives on the
`Discount` row, one place, because two date windows that can disagree is the drift C2 exists to
prevent. Recorded here so nobody "completes" the join columns later.

### 3.4 Reaching the tills — portal decides, till obeys

New contract, same pattern as VAT bands / carrier bags: **`GET /api/v1/discounts/rules`** (any
authenticated caller — a device token works) returns the tenant's full active rule set with
targeting ids and the raw schedule. Cached by `Client.Core` on the 60 s cadence (MAUI) and beside
`fetchDiscounts()`'s existing offline cache (web). Portal writes via `POST`/`PUT` on a portal
permission (`portal.prices.manage` or its own — decide at build with the RBAC catalogue open).

**Fail direction: last-known-good.** A rule with a `ValidToUtc` self-expires offline (the till
evaluates the raw window at the sale's instant — same trust model as the future-dated VAT change
applying on a till that never reconnects). A deleted never-ending rule keeps applying until the
till next syncs — that is the price of offline, it fails in the customer's favour, and it is the
same exposure the catalogue price cache already accepts.

### 3.5 At the counter — "Wednesday Warhammer"

On line-add (and on rule-cache refresh against lines already in the basket — decision D5 bounds
this), the till asks the shared rule: *is any auto-apply rule live now, and does it land on this
line?* If yes:

- The line takes a normal **line discount** — `discountId` = the rule's real id, `type`/`amount`
  from the row, **reason auto-filled with the rule's name** via `DiscountAudit.Automatic`
  (`:171`), no authoriser (nobody stepped up), **not counted against the operator's
  `pos.discount` ceiling** (the shop set it, not the operator).
- The line shows a visible badge — the rule's name and the money off, the same way the member
  discount shows today (`TillPage.tsx:605-609`), and the receipt prints the existing per-line
  discount sub-line (`ReceiptDocument.cs:151-153` / `receiptDoc.ts:90-92`) with a shared
  `ScheduledDiscounts.Label` fixing the wording on both tills, exactly as `MemberDiscount.Label`
  does.
- The operator can clear it per line (the web till's existing per-line clear), because the person
  at the counter is always allowed to charge full price. Clearing needs no reason — there is no
  money off left to explain.

⚠ Matt's word was *"flag … as 'Should have 10%'"* — decision **D1** asks whether that means
auto-apply (recommended) or show-and-confirm. Everything above except the automatic write is the
same either way.

### 3.6 One auto discount per line — member tier vs scheduled rule

Both tills are structurally one-discount-per-line (web: the single `discount?` slot on
`BasketLine`; MAUI: `EligibleLines` excludes lines already in another alteration). A Gold member
buying Warhammer on a Wednesday must get ONE answer, the same on both tills. **Recommended rule
(D2): the line takes the LARGER of the two** — deterministic, customer-best, and cheap to compute
since both are known at line-add. This means the member-discount reactive rebuild
(`applyMemberDiscount` / `MemberDiscountBasket`) grows into a single **auto-discount resolver**
per till that considers both sources — one engine, not two racing ones, which is also what stops
re-entrancy pain on MAUI (`_refreshingMemberDiscount` already exists for one source).

Manual always beats automatic: an operator-applied discount occupies the slot and the resolver
skips the line (that is today's `hasDiscount` exclusion, unchanged).

### 3.7 The manual flow — select items, with Select all

DP1. Both dialogs gain a **Select all / none** control above the line list; selecting all is
exactly the whole-basket discount, so the money path is unchanged — the web till per-line as
today, MAUI through the same `DiscountApportionment` call it already makes. The two dialogs
converge to one layout (title, list with Select all, amount presentation, reason, refusal wording)
per the 2026-08-19 look-and-feel ruling. Ceiling, step-up, mandatory reason: untouched.

### 3.8 Audit, ceiling, and the wire — what changes and what must not

- **Nothing new reaches the sale schema.** `IngestLine.DiscountPence` + `LineMeta.discounts[]` +
  `discountAuthority[]` already carry everything. **No server ingest change, no migration on the
  sales side.**
- A scheduled discount **rides `discounts[]`** with its real catalogue id — unlike the member
  discount it projects cleanly into `Transaction_Discount`, which is precisely why §3.1 keeps the
  real entity.
- An ad-hoc typed discount (D8) has **no catalogue row and must NOT ride `discounts[]`** with an
  invented id — the FK tripwire test exists because that projection fails. It travels as
  `DiscountPence` + authority only, like the member discount. ⚠ Verify at build what MAUI's
  Alterations flow sends today for a typed figure — if it already writes a synthetic id into
  `discounts[]`, that is a live bug this plan flushes out.

### 3.9 The legacy bridge

`LegacySaleBridgeConsumer.cs:195` keeps working unmodified for scheduled and catalogue-manual
discounts (real ids). Member and ad-hoc discounts stay off `discounts[]` as today. No change —
stated so nobody "tidies" it.

### 3.10 Receipts

Already done: per-line discount name + money-off sub-line on both tills (§2.1 table). The only new
thing is the label rule (shared, `ScheduledDiscounts.Label`) so both tills print the same words.

### 3.11 The webstore channel

The Woo connector ingests whatever discount Woo itself applied (back-computed at
`WooOrderMapper.cs:93-112`) and reads no coupon lines. **Recommended (D4): scheduled discounts are
➖ for the webstore** — Woo owns online promotions, and a shop wanting Wednesday Warhammer online
sets it up in Woo, whose figures the connector already ingests faithfully. Recorded as a ➖ with
this reason in Part B, and re-read if the webstore ever stops being Woo-fronted (the D3 rule that
➖ goes stale).

---

## 4. Decisions for Matt — recommended defaults, none ratified

| # | Question | Recommendation |
|---|---|---|
| **D1** | *"Flag as 'Should have 10%'"* — auto-apply, or show-and-confirm? | **Auto-apply with a visible badge and one-tap removal.** A prompt per eligible line is unworkable on a 20-line basket, and the member discount already trained operators on auto-apply-with-badge. The alternative (badge saying "Wed Warhammer −10% available", one tap applies to all flagged) is a smaller build of the same parts if preferred |
| **D2** | A member's tier discount AND a live rule hit the same line — which applies? | **The larger of the two, never both.** One discount per line is structural on both tills; "largest wins" is deterministic and customer-best. Alternatives: rule always wins, or member always wins — both leave a customer worse off in one direction for no stated reason |
| **D3** | Does an automatic discount need a reason and authority? | **Reason = the rule's name, auto-filled (`DiscountAudit.Automatic`); no authoriser; not counted against the operator's ceiling.** The shop authorised it in the portal; "who approved this?" is answered by the rule's existence |
| **D4** | Does Wednesday Warhammer apply on the webstore? | **➖ — Woo owns online promotions** (§3.11). Confirm this is shop-floor-only intent |
| **D5** | The clock crosses the rule boundary mid-basket (23:59 Wed → 00:01 Thu), or a Wednesday basket is parked and retrieved Thursday | **A line keeps what the screen showed when it was added** — the screen must never silently re-price under the operator's hands (the D5 live-data lesson). A retrieved parked basket keeps its discounts as parked. The one instant that matters is line-add |
| **D6** | Where are rules managed? | **Portal only; tills read.** Same argument as roles: a till is the wrong place to widen authority from. The tills' manual list stays read-only over the same catalogue |
| **D7** | The other dormant columns — `UsesPerTransaction`, `RequiredNumOfItems`, `CanUseWithOtherDiscounts` | **Leave unenforced, out of scope.** The first two are mix-and-match (a different feature, §8); the third is superseded by one-discount-per-line and must not be half-implemented beside it |
| **D8** | The web till cannot type an ad-hoc figure; MAUI can. Converge which way? | **Both tills offer the catalogue list AND a typed £/% (through shared `PercentDiscountInput`, gated by the existing ceiling).** An owner who hasn't set up a rule still needs to knock something off a dented box. ⚠ Carries the §3.8 wire constraint |

---

## 5. The work packages — DP1…DP6

> ✅ **DP1–DP5 ARE DONE (2026-08-20). DP6 — the hand-run — is not.** The briefs below are kept as
> written, unedited, because they say *why* each slice was shaped that way and the status box at the top
> of this document says what actually happened. ⚠ Two things in them were falsified by the build and are
> corrected there, not here: the window boundary (inclusive at **both** ends) and the single `Amount`
> field (split into a fraction and integer pence).

> ⚠⚠ **Ground rules for every slice** (the W-P0 idiom): read `till-design.md` Part C2 before
> touching anything that computes money on a client; every dialog change honours the D4 dialog
> contract (✕ via `DialogX`/`DialogHeader`, Escape cancels, caller survives backed-out); every new
> TS/.NET twin is pinned **on both sides over the same vectors, with vectors that discriminate in
> each host language** (the surcharge lesson — a copied vector is not a copied guarantee); registers
> update in the same commit as the code; MAUI artefacts are built **only when Matt asks**.

### DP1 — Select all, and one dialog on both tills · **~1d** · independent, do first

**Build:** Select all/none above the web checklist (`DiscountDialog.tsx:139-156`) and in MAUI's
`InputMultiSelectAlert` (fix the `static SelectionList` at `:41` while in there). Converge the two
dialogs to one layout per the 2026-08-19 ruling. If D8 is ratified: the typed £/% entry lands on
the web till here, through `PercentDiscountInput`'s TS twin (new C2 row).
**DoD:** an operator on either till taps Discount → ticks lines or taps Select all → mandatory
reason → ceiling bites over the limit with step-up → the receipt shows the money off per line. An
operator who swaps tills mid-shift sees the same dialog.
**Registers:** A0 Selling row *"Take a discount off chosen lines, or every line, in one go"*;
Part B B1 Discounts row Notes; C2 row if D8 adds the percent-input twin.
**Version:** both till version files.

### DP2 — the rule, in SharedKernel first · **~1–1½d**

**Build:** `SharedKernel/ScheduledDiscounts.cs` — `IsLiveAt(rule, nowLocal)` (§3.2, mirroring
`PermissionGrant.IsActiveAt` semantics), `LandsOn(...)` (§3.3 targeting + the four exclusions),
`BestAutoDiscount(...)` (D2's largest-wins), `Label(name, type, amount)`. Wire DTO in
`Plutus.Contracts.Client`. TS twin `till/scheduledDiscounts.ts`. Tests both sides over the same
vectors, mutation-checked (drop the day-mask check; swap inclusive/exclusive window ends; let a
return through; stack on a discounted line).
**DoD:** the .NET and TS suites answer identically for: Wednesday 09:00 (applies), Wednesday 23:59
(applies), Thursday 00:00 (does not), a Warhammer item vs not, a return, an already-discounted
line, a gift card, the surcharge, a member-vs-rule tie each way.
**Registers:** C1 rows (§6); C2 twin row, pinned both sides from day one.

### DP3 — backend: schema, v1 endpoint, portal screen · **~2–3d**

**Build:** migration adding the §3.1 columns to `Discounts` (nullable — see `repo-runbook.md`
before running anything); `GET /api/v1/discounts/rules` in `Plutus.Catalogue` (device-token
readable) returning rules + targeting ids + raw schedule; portal write endpoints on a portal
permission; portal **Discounts** screen — list (per `table-standard.md`), create/edit: name, £/%,
amount, auto-apply, targeting (all / categories / items), schedule (days, window, dates), active.
Writer STRICT in the opening-hours sense: refuse to save what a till would have to guess at.
**DoD:** Matt creates "Wednesday Warhammer, 10%, Warhammer category, Wednesdays" in the portal;
`GET /api/v1/discounts/rules` returns it with the raw schedule; the legacy `/api/Discount/Index`
list the tills fetch today is unbroken.
**Registers:** the C1 *Portal decides, till obeys* table gains the discounts contract row.
**Version:** backend + portal.

### DP4 — web till auto-apply · **~1–1½d**

**Build:** rule cache beside `fetchDiscounts()`'s offline cache, refreshed on the existing poll;
the member-discount reactive apply (`TillPage.tsx:129-134` + `basket.ts:204-223`) grows into the
one auto-discount resolver (§3.6); badge + per-line clear as today; boundary per D5 (line-add
instant, never re-priced).
**DoD:** ring a Warhammer item on a Wednesday → the line shows "Wednesday Warhammer −10%" and the
total moves; same item Thursday → nothing; attach a Gold member on Wednesday → each line carries
the larger discount, never both; clear it on one line → full price, other lines untouched; pull
the network first → identical behaviour from cache.
**Registers:** A0 + Part B rows (🟡 until a person runs it); C2 statuses.
**Version:** web till.

### DP5 — MAUI auto-apply · **~1½–2d**

**Build:** rule cache in `Client.Core` on the 60 s cadence; `MemberDiscountBasket` generalised to
the same one-resolver shape (it already owns the alteration composition, the sentinel/real-id
distinction, and the re-entrancy guard — extend, don't duplicate); confirm `CategoryId` is on the
basket's item object (§2.4 ⚠); badge on the line; receipt via the alteration name as today.
⚠ **Build the artefact only when Matt asks** (standing instruction).
**DoD:** the DP4 script, verbatim, on the MAUI till — same wording, same figures, same receipt.
**Registers:** Part B MAUI column; A0.
**Version:** MAUI till.

### DP6 — hand-run + registers close · **~½d**

**Build:** new `Test Maui.md` sections (both tills): the DP4/DP5 script plus the manual Select-all
flow and the member-vs-rule collision. Flip 🟡→✅ only from a person at a screen — every hand-run
so far has found faults the tests could not.
**DoD:** the sections exist, are in the RUN ORDER table, and name the build they were written for.

---

## 6. Registers this work must leave true

Written here so the build commits copy rather than compose:

- **A0 · Selling** — new rows: *"Take a discount off chosen lines, or every line, in one go"*;
  *"Apply a scheduled discount (day/time rule) automatically"*. Both start ⬜/⬜.
- **Part B · B1** — new row **Scheduled discounts** (Web | MAUI | `GET /api/v1/discounts/rules` |
  Notes incl. the webstore ➖ per D4 and its reason); the existing **Discounts** row gains the
  Select-all / one-dialog note and, if D8 lands, the ad-hoc-entry note with the §3.8 wire
  constraint spelled out.
- **C1 · Money and VAT** — new rows: *"Whether a scheduled discount is live, and which lines it
  lands on"* → `SharedKernel/ScheduledDiscounts.cs`, TS twin deliberate → C2; *"Which auto
  discount a line takes when two apply"* (D2) → same home. The *Portal decides, till obeys* table
  gains the discounts-rules contract.
- **C2** — new row `ScheduledDiscounts` ↔ `till/scheduledDiscounts.ts`, pinned both sides from
  day one (the reason-normalisation standard, not the usual half-pinned start); update the
  apportionment row (`:593`) if the web till grows a basket-level path; add the
  `PercentDiscountInput` twin row if D8 lands.
- **D4/D5 contracts** — the converged dialog keeps its ✕ on both tills; any portal/till screen
  listing rules follows the live-data contract.

---

## 7. Size, honestly

| Package | Size |
|---|---|
| DP1 Select all + one dialog | ~1d |
| DP2 shared rule + twins | ~1–1½d |
| DP3 backend + portal | ~2–3d |
| DP4 web till | ~1–1½d |
| DP5 MAUI till | ~1½–2d |
| DP6 hand-run + close | ~½d |
| **Total** | **~7½–9½d** |

DP1 is independent and gives Matt the Select-all ask this week. DP2→DP3→DP4/DP5 is the dependency
chain; DP4 and DP5 can interleave. Estimates assume the decisions in §4 are settled first —
especially D1, D2 and D8, which shape DP2's API.

## 8. What is deliberately NOT in this plan

- **Mix-and-match / BOGOF / multi-buy** ("3 for 2", "cheapest free") — a different engine
  (quantity thresholds across lines, allocation to bundles). `UsesPerTransaction` and
  `RequiredNumOfItems` hint the legacy schema wanted it; it stays a future document, and nothing
  in this plan's shape blocks it.
- **Coupon / voucher codes** — bearer-instrument territory (see the gift-card code rules); a
  different risk profile.
- **Woo online promotions** — Woo's own machinery, already ingested faithfully (§3.11).
- **Loyalty gems/credits** — its own ~45–55d programme (`Loyalty Update across all tills.md`);
  the only touchpoint is D2, decided here.
- **Scheduled price CHANGES** — already exist (`PriceResolution`, effective-dated points). A shop
  that wants the shelf price itself to drop on Wednesdays has that today; this plan is for money
  visibly taken off at the till.
