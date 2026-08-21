# Handover — next session

> ⚠ **This document is ONE DAY LONG, on purpose.** Matt, 2026-08-17: *"I only want the handover to be
> for the following day."*
>
> Everything durable — deploy state, every ruling, open items, the plan, the estimates — lives in
> **[`Build/To do/MAUI-retrofit.md`](Build/To%20do/MAUI-retrofit.md) §0**. The day-by-day narrative back
> to 2026-07-28 is kept verbatim in
> [`archive/handover-history-to-2026-08-17.md`](Build/archive/handover-history-to-2026-08-17.md).
> **Do not grow this file back into a history.** Rewrite it; the commits are the record.

**Written:** 2026-08-20 · **Suites — all five run, all green:** unit **1561** · integration **177** ·
MAUI **621** (+3 skipped) · architecture **31** · web till **351** (vitest, on the build Mac). Both
frontends: `tsc --noEmit` clean, web-till `eslint` **0 errors**, `vite build` clean. Backend publishes
for `osx-arm64`.

> ⚠ `dotnet build` of the whole `Plutus.slnx` reports **6 errors that are the BOX, not the code** — no
> Android SDK and no .NET Framework 4.7.2 targeting pack, hitting `CoppperToCSV` (×2), `I18N_L10N`,
> `ClientUI`, `CustomViews` and `AppClient`'s android target. Pre-existing. Build the projects you need.
>
> ⚠ **The portal has no eslint config and no vitest** — so "portal lint clean" is not a thing anybody can
> claim. Its only automated gate is `tsc --noEmit`.

> ⚠ **Run all four .NET suites plus the web till's.** The architecture suite is the one that goes red
> unnoticed — it caught a real design fault today (see below), and it is not in anybody's muscle memory.

> ⚠⚠ **AND `dotnet` ON THE PATH IS THE x86 ONE WITH NO SDK — pitfall 19, and it cost a step today in
> the way that pitfall predicts.** `dotnet build` printed *"No .NET SDKs were found"* **and the tool
> wrapper still reported exit code 0**, so a build that never ran read as a build that passed. Use
> `"/c/Program Files/dotnet/dotnet.exe"` and **read the output**, never just the exit code.

---

## ⏰ START HERE — everything below is LIVE and verified end-to-end. Nothing is waiting on a deploy.

**Deployed state: backend 1.20.0 · portal 1.16.0 · web till 1.29.0 ·
platform 1.49.0 · MAUI artefact 1.110.0 built at `D:\tmp\plutus-till-1.110.0\`** (the only build on
the box — earlier ones deleted, so the folder listing is the truth).

| What | Hand-run | State |
|---|---|---|
| ⚠⚠ **SYNCFUSION IS OUT OF THE MAUI TILL** — with `DocumentFormat.OpenXml`, both legacy report screens and the stale licence key. **264 MB → 169 MB (−36%)** | **§G68** ⚠ the most important one on the page | ✅ built 1.110.0, **not yet run by a person** |
| **Editing an item's barcodes, and its change history** (portal + web till) | **§G67** | ✅ deployed, **endpoints exercised live** |
| **Multi-barcode** — one item scans under several codes | **§G66** (do §G66a first) | ✅ deployed + built |
| **Scheduled discounts — "Wednesday Warhammer"** + Select all + typed £/% on the web till | **§G65 → §G64** (§G65b first) | ✅ deployed |
| **A long basket no longer hides the Checkout buttons** (both tills) | **§G62** | ✅ deployed + built |
| **The portal's Company page collapses** like Locations & Tills | **§G63** | ✅ deployed |
| **Wording + ordering**: *Apply Discounts* replaces *Alter Transaction* on both tills, MAUI's two dialogs retitled *Apply discount*, **Discount Settings** at the top of Prices, and the web till's discount tick-list follows the till's own line order | **§G64h2** | ✅ deployed |

### 🆕 AND MULTI-BARCODE IS BUILT AND ✅ DEPLOYED (2026-08-20, later)

**One item can now scan under several barcodes.** [`Multi-barcode plan.md`](Build/archive/Multi-barcode%20plan.md)
was written to be executed by Sonnet with no questions, then executed here: backend **1.19.0**
(⚠ carried the `AddItemBarcodes` migration) · portal **1.15.0** · web till **1.28.0** — **all three
DEPLOYED and verified** — plus MAUI **1.109.0** built, and platform **1.49.0**. Hand-run **§G66**
(eight sections, never run).

✅ **The migration was verified as a TABLE, not a history row**: `ItemBarcodes` exists with its six
columns and `IX_ItemBarcodes_TenantId_Code` is UNIQUE. **20,474 items before and after** — additive,
as designed. Pre-deploy dump verified at 76.5 MB / 103 tables and confirmed to contain no such table,
so that before/after means something. Rollbacks: `~/PLUTUS/backend.pre-1.19.0`,
`/srv/apps/PLUTUS/web/current.pre-1.28.0`, `/srv/apps/PLUTUS/portal/current.pre-1.15.0`.

⚠⚠ **`Item.IdOne` IS STILL THE IDENTITY** — it seeds the deterministic item GUID (frozen golden
vector, TS twin), it is half the composite PK with five FK families on it, and it is on every
historical sale line. Barcodes are ADDITIVE rows that resolve to an item; nothing was re-keyed. The
alias string never travels past resolution, because the two faults a leak causes are both silent: a
**phantom `StockLevel`** from the stock projection, and a **null VAT band** from the band stamp.
**§G66e is the money check for exactly that.**

⚠ This **reverses the 2026-08-09 ruling** ("multi-barcode not needed") recorded in `TillStore` and
`LocalSchema`. Their own removal note named the prerequisites — *"a server entity, a feed field and a
portal UI first"* — so both comments were rewritten to record the reversal rather than left asserting
the opposite of the code.

⚠ **Two things found on the way, both worth knowing:** the web till's `findItemById` returned null the
instant the server 404'd **without consulting its offline cache**, so an alias scan on a warm cache
would have minted a DUPLICATE item — now fixed. And `CatalogueSkuResolver` hashed the SKU string it
was handed rather than the item's own code: behaviour-neutral today, wrong the day anyone makes it
alias-aware. Fixed while free.

⚠ **MAUI does one full catalogue re-sync on its first 1.109.0 launch**, by design — local schema v7
clears the catalogue cursor, because adding a wire field changes no item's `ModifiedAt` and an
existing till would otherwise only ever see barcodes for items somebody edited afterwards.

### 🆕 LATER STILL — EDITING BARCODES, AND AN ITEM'S CHANGE HISTORY (backend 1.20.0 · portal 1.16.0 · web till 1.29.0)

Matt, on the deployed 1.19.0: *"When I am trying to edit an item in the portal or on the webtill, I
cannot edit or add a new barcode?"* — correct, and the reason is worth recording: **the endpoints were
there and neither surface had a control.** 1.19.0 shipped the whole multi-barcode spine and no way for
a person to use it. Nine asks were actioned; the item editor on both surfaces now has an
additional-barcode list and a change history.

**What it does now.** A locked list of the item's extra codes (`🔒 Edit` to unlock a row), a collapsed
*Add another barcode* box, live checks as you type, and a collapsed *Change history* at the bottom.

- ⚠⚠ **CORRECTION IS ONE `PUT`, NOT DELETE-THEN-ADD** (`PUT api/v1/items/{id}/barcodes/{code}`, new in
  1.20.0). Two calls can fail between them and leave the item with **neither** code — and for a barcode
  that means an item that silently stops scanning.
- ⚠⚠ **EVERY ROW IS LOCKED**, because Matt asked for it and the reason is real: a barcode is the string
  a scanner matches on, so a stray keystroke in an always-live box is an item that stops scanning with
  nothing to say so.
- ⚠ **The live check is a courtesy, not the gate.** Uniqueness is answered with **no round trip** (the
  barcodes endpoint hands over the whole tenant's codes); whitespace **warns and still saves** (the
  server trims) while a clash **blocks**. The reserved shapes — membership cards, gift cards, bag ids,
  the platform ids — stay **only** in `SharedKernel.ItemBarcodeRules` and arrive as a sentence shown
  verbatim. A copy of an identity rule in a client is exactly the C2 fault.
- ⚠ **History is built on the existing `AuditLog`**, not a new table, and the pre-logging era shows as
  **synthetic bookends** derived from `Item.CreatedAt`/`ModifiedAt` that say so in the row. An audit
  trail that is silently incomplete is worse than one that admits a gap.
- ⚠ Gated `portal.reports.view`, **not** `portal.stock.adjust`: naming who changed a price is a
  supervisory record, not a stock task, and different people hold the two.
- ⚠ **MAUI is a deliberate ⬜, on an existing WP.** It has **no item editor at all** —
  `ExecuteOpenAddItem` is unreachable dead code and the screen offers only "View all items", because a
  till-created item reaches no report, no other till and no VAT return. Item writes are the portal's;
  inventory parity is **WP10** / `MAUI-retrofit.md` §10 (L2).

✅ **EXERCISED LIVE, not just deployed** — against the real backend with a minted operator token:
add → **201** · rename via the new PUT → **204** and the list confirms · a membership-card shape →
**400** *"That is the shape of a membership card…"* · an interior space → **400** · a code another item
owns → **409** naming that item · a leading/trailing space → **trimmed and accepted** · history →
**3 rows** including the honest *"Created before change logging began"* bookend · and **the load-bearing
one**: scanning the alias answered the **CANONICAL** `idOne`, byte-identical to scanning the item's own
barcode. All test aliases removed afterwards; `GET /api/v1/items/barcodes` is back to `[]`.

### 🆕 LAST THING — SYNCFUSION IS OUT (till 1.110.0). 264 MB → 169 MB

Matt: *"if the packaging of it removes all you see, what about removing syncfusion now? Worth it?"*
Yes — **for the licence hazard, not the megabytes.** No key was ever coming, keys are version-specific,
and an unlicensed control does not fail a build: it puts a **modal with no way back** in front of a
shop-floor screen. That trap is now structurally impossible rather than dormant, and the stale key is
deleted from `App.xaml.cs`.

⚠⚠ **IT WAS UNBLOCKED BECAUSE L4's CONDITION HAD QUIETLY BEEN MET — the ruling was not overridden.**
Matt's 2026-08-17 wording was conditional: *"Do not drop anything. **I have a more recent DB to
import** and will need to translate where required and **retain all legacy sales**."* The 19_08 import
ran on 2026-08-20, and `salesv2` now holds **21,914 sales back to 2019-01-23** — so the two screens
stopped being *"the only reader of a migrated till's pre-cutover file"*, which was the whole reason to
keep them. They read only the local pre-Plutus file and showed **zero** for everything sold since
cutover. ⚠ Both were already unreachable: their commands existed but **nothing bound them.**

| | 1.109.0 | 1.110.0 |
|---|---:|---:|
| Publish size | 264 MB | **169 MB** (−95 MB, −36%) |
| Root files | 299 | **268** |
| Subfolders | 121 | **88** |

⚠⚠ **AND A DOCUMENT LIED, WHICH IS THE FINDING WORTH KEEPING.** `Shrink MAUI Build.md` §3 said
`DocumentFormat.OpenXml` was *"15 MB the till ships and never calls… verified by grep"* and that the fix
was *"delete line 102, one line, no screen changes."* **The grep result was false** —
`Helpers/FileIO/ExcelHandling.cs` was built on `SpreadsheetDocument`, so that one line would not have
compiled. `SalesReportsViewModel` had **two** independent Excel export paths, and the till carried 69 MB
for one hidden screen. **"Verified by grep" is a claim, and it was very nearly acted on.** §3 is now
struck through with the correction.

⚠ **The order was load-bearing and was followed**: screens → `ExcelHandling.cs` → packages → licence
registration. Deleting the screens first is what discharged the trial-dialog hazard.

✅ Release build 0 errors · MAUI suite **621** (was 625 — the 4 `ExcelHandlingTests` went with the class)
· **0** Syncfusion and **0** OpenXml DLLs in the artefact · version stamped, no stale string · **launched
and ran 25 s** with no licence or XAML failure.

⚠⚠ **NONE OF THAT IS A HAND-RUN.** XAML and resource failures surface **on navigation**, and this repo
has no automated coverage of any MAUI screen. **§G68 is the test that matters** — especially §G68c, the
four controls swapped off Syncfusion back in 1.33.0.

⚠ **On the folder Matt asked about**: the 33 satellite language folders are gone
(`SatelliteResourceLanguages=en`). The **86 native `.mui` folders could not be moved into a `languages/`
folder** — they appear nowhere in `deps.json` and the Win32 loader probes `<the DLL's own
directory>\<culture>\`, so moving them would fail *silently* on non-English Windows. And **the flat
268-file root is an unsigned-MSIX workaround**: signing the package is the actual fix, ≈half a day, and
it makes every other tidy-up moot. `Shrink MAUI Build.md` §6.

### ⚠⚠ THE REAL FIND OF THE DAY: THE PORTAL HAD NO ✕ ON ANY DIALOG, AND D4 COULD NOT SEE IT

Not asked for, not suspected, and **found by accident** — by a twin-file guard added for the barcode
work. `Ask.tsx` exists twice (the portal and the web till are separate npm apps that cannot share a
package) and the two copies are meant to be byte-identical. They were not: the till's imported
`DialogX` and the portal's did not.

**So every confirm / choose / prompt dialog in the portal was missing the ✕ that `till-design.md` D4
makes mandatory — and had been since D4 was written.** The portal did not even *have* a `DialogX.tsx`,
or the `.dialog-x` style. Matt's instruction on 2026-08-18 was *"add x's to all relevant boxes … so
that it is not missed in future"*; it was missed, in fifteen files.

⚠⚠ **The cause is the shape of the contract, not the CSS.** D4's implementation table listed **two**
surfaces — MAUI and the web till — so it read as complete while a third surface nobody had added to it
sat outside. **A contract that enumerates its own surfaces silently excludes the ones nobody added.**

**Closed:** the shared `Ask.tsx` host (one file, so it covers **every** confirm/choose/prompt in the
portal) and the item editor. **Still open: the 13 other portal files that build their own dialog** —
listed in D4's honesty section. Each closes by Cancel and by clicking the backdrop, so this is an
intuitiveness gap rather than a trap — **but that is exactly what the two tills' gap was.**

✅ **And it is now mechanically pinned.** `FrontendTwinTests` (architecture suite) compares the bytes of
all five twins — `DataTable.tsx`, `Ask.tsx`, `DialogX.tsx`, `Barcode39.tsx`, `barcodeProblem.ts` —
with line endings normalised, and **a missing twin fails too**, because "the portal never had a copy"
is precisely the fault it found. ⚠ **Verified by breaking a twin on purpose and watching it go red**
(1 of 5), then restoring it. ⚠ It lives in .NET rather than vitest because the web till has no
`@types/node`, and the architecture suite already reads source off disk.

⚠ **The lesson for C2 generally:** four of those five twins had nothing but a *"keep these in sync"*
comment for months, and a comment has never stopped anybody. **Where a twin can be compared
mechanically, compare it mechanically.**

### 🎯 TOMORROW — Matt is finishing the MAUI retrofit. Here is what is actually left

**≈10–15 working days, re-costed 2026-08-20 against the code.** ⚠ The document said **≈35–40** until
that afternoon: §0 was recounted on 2026-08-19 (8 ⬜ → 3, steps 26 and 27 both landed) and **§6/§7 were
left saying "15 MAUI ⬜ rows" and "35–40 days"**, so the deeper half of the document was the pessimistic
one. Matt read it and asked *"I thought we had finished it."* Both sections are now corrected and say to
re-derive from `till-design.md` rather than maintain a count by hand.

**Counted from the register: A0 MAUI ✅44 🟡36 ⬜4 · Part B MAUI ✅60 🟡19 ⬜4.** All four A0 ⬜ rows are
**one cluster — the item editor**; one Part B ⬜ (*remote lock*) is ⬜ on the **web till too**. **One row
separates MAUI from the web till on anything a shop does today.**

| Do first | Work | Est. | Why it is top |
|---|---|---|---|
| ~~1~~ ✅ | **~~🔴 §0.3b — 17 input-alert sites that crash the till on back-out~~ — CLOSED 2026-08-21** | ~~1–2d~~ **0** | ⚠⚠ **STALE, AND IT WAS #1 TWO DAYS RUNNING.** §0.3b's own 2026-08-19 re-audit already said 17 was wrong; the correction was never carried up here or to §7. Re-grepped 2026-08-21: **23 call sites, every reachable one guarded.** Residue closed the same morning — 5 dead `is null` checks + `ViewAllViewModel`'s `Any(…)`-over-empty fall-through. Build 0 errors, MAUI **621** |
| 2 | ⚠ **Step 11b — reshape the basket** | 4d | `ExecuteCheckoutTransaction` is still ~200 lines of `async void` and **the last money-adjacent code in this app with no test coverage at all**. Also unblocks L6 |
| 3 | ⬜ **WP14 — payment gateway on the checkout XAML** | 1–2d | Shared half done (13 tests, mutation-checked). **0 references** in any MAUI view or viewmodel |
| ~~4~~ 🔄 | **~~⬜ WP16 — connectivity states on the login screen~~ — MAUI HAS IT. The gap is the WEB TILL's login screen** | ≈½d | ⚠⚠ **"0 references" was a bad grep** — MAUI reaches the probe via `TillConnectionCheck`, not `ConnectivityProbe`. `LoginViewModel` has all four properties + `RefreshConnectionAsync`; `LoginView.xaml` binds them at lines 72–97. 16b is done too (`OperatorLogin` → `OfflineCredentials.Assess`). **`LoginPage.tsx` is 111 lines with no indicator at all** — that is the real ⬜, and under the look-and-feel ruling it is a parity gap |
| 5 | ⬜ **Step 28** online-first login · 🟡 **Step 24** roster move | 3–4d | Hardening + ~1d tail |
| — | ⬜ **WP10 — an item editor on the till at all** | ? | ⚠ **Matt's decision, not a gap to close blind.** All four A0 ⬜s are this, and MAUI has no editor *deliberately*: a till-created item reaches no report, no other till and no VAT return. C1 says *"Portal decides, till obeys"* — **decide whether it is ever wanted before costing it** |
| — | ⏸ **L1–L10 legacy removal** | — | Matt actions last. **L4 closed 2026-08-20** with the Syncfusion removal |

⚠⚠ **AND THE BIGGEST ITEM IS NOT ON THAT LIST: 36 A0 ROWS SIT AT 🟡** — built, tested where testable,
**never once exercised by a person**. No amount of building reduces it. The first hand-run of this
retrofit found **fourteen faults, six invisible to every automated test here.** A 🟡 is not a smaller ⬜;
it is an unknown.

⚠ **Two housekeeping notes for tomorrow.** **(a) Nothing is committed** — the whole of 2026-08-20 is in
the working tree, including two `git mv`-style archive moves that show as an unstaged delete plus an
untracked add (`Shrink MAUI Build.md`, `Migrate back end to Linux.md` → `Build/archive/`). **(b)
`Migrate back end to Linux.md` is now in `archive/` but is UNBUILT** (~1–2d, nothing started) — a plan
in `archive/` reads as delivered, so if that move was not deliberate it belongs back in `To do/`.

### ⏭ AND THE HAND-RUN, WHICH NOTHING ELSE SUBSTITUTES FOR: §G68 → §G65 → §G64 → §G62 → §G66 → §G67

Everything is live. **Nothing has been run by a person.**

⚠⚠ **AND THERE IS ONE CONFIGURATION STEP BEFORE ANY DISCOUNT WILL EVER SHOW.** All six existing rules
have **`AutoApply = 0` and no day mask** — so the feature is working correctly and applying nothing,
because nothing has been *asked* to apply itself. **§G65b is that step**: open
**Portal → Prices → Discounts → Warhammer Wednesday Discount → Edit**, tick *Apply it automatically*,
tick *Wed*, Save. Until then a till charges the shelf price, which is exactly what it did yesterday.

⚠ This is worth saying plainly because it is the obvious thing to misread as "the feature is broken":
Matt looked at a 13-line basket on the deployed till and asked *"What about discounts? I still cannot
see that?"* — and he was right that nothing was showing, for this reason and not a defect.

### ⚠⚠ THE LESSON FROM TODAY, AND IT IS NOT ABOUT CSS

Matt reported the till buttons walking off the bottom **for the second time**: *"I thought I had asked
for this already."* He had. The fix was real, committed to the working tree, and **sitting in an
undeployed bundle** while he tested **1.26.0** — his own screenshot's footer said so.

**A fix that is built and not shipped is indistinguishable from a fix that was never made**, and the
person testing cannot tell the difference. When reporting work as done, say which version it is IN and
whether that version is LIVE. The deploy table above exists for that reason.

⚠ **And a second, real fault was found only because he pushed back.** `.basket-grid` carried
`min-height: 14rem` — a 224px floor a flex item cannot shrink below — so on a SHORT window the basket
refused to give up height and pushed the buttons off *anyway*. `min-height` also beats `height`, so
`.shell`'s `min-height: 100vh` would have defeated the viewport lock on any device with a retracting URL
bar. The first fix was correct and incomplete; a maximised 1080p window would never have shown it.

✅ **So the web-till half is now MEASURED, not asserted** — `tools/layout-check/` drives the real
stylesheet in headless Chromium at six viewport sizes, and **the harness was proved by watching it fail
5 of 6 against the 1.26.0 CSS** (buttons at y=1911–2001 in a 1080-high viewport). ⚠ Note that 8 rows
PASSES on the broken CSS, which is why the screenshot looked nearly right and why a casual check would
have stopped there. ⚠ It does **not** cover MAUI — WinUI layout is measurable by nothing in this repo,
so **§G62a stays the only check for the MAUI half**.

### ✅ What the backend deploy proved

Verified on all three axes: swagger **200** · `POST /api/v1/tokens/device` with a junk id →
**401 "Device not enrolled or revoked."** (the axis that proves the DB path; a 500 would mean schema and
model disagree) · `GET /api/v1/discounts/rules` → **401, not 404**, so the new controller is routed.
Migration `20260820092535_AddDiscountSchedule` applied, all six columns present.

⚠ **Rollback:** `~/PLUTUS/backend.pre-1.18.0`. **Pre-deploy dump:**
`~/PLUTUS/backups/nightly/plutus-20260820.sql.gz` — taken before the swap and verified properly
(**75.5 MB uncompressed, 103 `CREATE TABLE`s**), not just "the log said ok".

⚠ The log carries `Duplicate entry … for key 'salesv2.PRIMARY'` and a `Failed to determine the https
port` warning. **Neither is this deploy**: the first is `WebstoreReconciler` re-seeing a Woo order it
already has and reporting `dup=1` correctly in its own summary line; the second is pre-existing behind
Caddy.

### ✅✅ AND THE DATABASE ANSWERED §G65a BY ITSELF — plus a find worth reading

**All 6 pre-existing discounts came out `Active = 1`.** That is the check the hand-edited migration
existed for (EF wanted to default the column to *false*, which would have switched every one of them
off), and it passed.

⚠⚠ **"Warhammer Wednesday Discount" ALREADY EXISTS — id 5, live, at 15%, and it already targets a
category.** It has simply never been able to say *when*, and nothing has ever applied it by itself.
Matt's rule is **one edit away**: tick *Apply it automatically* and tick *Wed*. Four of the six rows
already carry a category join. **§G65b is rewritten around editing row 5 rather than creating a
duplicate**, and it lists all six rows so you can see what the shop actually has. ⚠ Note **15%**, not
the 10% in the original example — the row is the shop's, so 15% is the truth unless Matt says otherwise.

### ⚠ One thing about the artefacts you cannot trust

Both were built from the **working tree with the discount work uncommitted**, so their version suffix
names commit `9741816a`, which does **not** contain it. The code inside is correct and was verified
**string by string in both binaries** (see *Verified in-binary* in `Test Maui.md`), including the
negative check that the deleted `MemberDiscountBasket` really is gone. ⚠⚠ **Committing would make the
release record honest** — `git log versions/till-maui.txt` is supposed to read as that till's history —
and it has not been done, because nobody asked for a commit.

---

## What the discount work actually is

Matt, 2026-08-20: *"Without using a loyalty card, I need to apply a discount to a users basket,
specific item or items in a category… E.g. I have Wednesday Warhammer discount… But I also need to be
able to discount specific items in the till and or all at once… with an option to select all."*

The plan, its eight decisions and the full record of what the build changed about it:
**[`Build/archive/Discount plan.md`](Build/archive/Discount%20plan.md)** (archived — delivered). In one
paragraph: the portal
now owns a discount **catalogue with a schedule** (day mask, local time window, UTC date range) that
every till caches and **evaluates against its own clock**, so a Wednesday rule works on a till that has
been offline since Monday. A rule targets everything, categories, or named barcodes. One discount per
line — where a rule and a member's tier both apply the line takes **the larger, never both** — and an
operator's own discount always wins over both.

⚠ It **extends the existing `Discounts` table** rather than adding a rival entity, because that row has
a real id the legacy bridge projects into `Transaction_Discount`, both tills already cache that
catalogue, and the category/item join tables already existed and were read by nothing.

### ⚠⚠ Four things worth knowing, because each was nearly a silent fault

1. **The architecture suite refused the design, and it was right.** One `decimal Amount` meaning
   POUNDS for a fixed discount and a FRACTION for a percentage tripped
   `No_module_declares_decimal_or_double_money_members`. The wire now carries `PercentFraction` and
   `FixedAmountPence` as separate fields. The smell was already visible in my own comment warning that
   the field was pounds — if a field needs a ⚠⚠ to say what unit it is in, split it.
2. **MAUI could not have targeted a category at all.** Its basket carries a legacy `ItemModel` whose
   `CatId` is an **int** into the NatApp `Categories` table — empty and permanently so on a portal
   till. A category rule would have worked on the web till and matched nothing on MAUI, silently. That
   is the Gold-member money difference in a new costume. `BasketItem.CategoryId` now carries the v2
   Guid. ⚠ **§G64g is written on the assumption this is the thing most likely to be broken.**
3. **The migration needed a hand edit.** EF generated `Active` with `defaultValue: false`; a C#
   property initialiser is not a schema default and does not touch existing rows, so every discount a
   shop already had would have arrived switched OFF. Runbook pitfall *"OPEN THE GENERATED MIGRATION AND
   READ ITS `Up()`"* paying for itself.
4. **Two faults live in the basket, not in the rule**, and no unit test of the rule can see either:
   the resolver eating its own output (the discount vanishes on the next scan) and a cleared discount
   coming straight back (full price unreachable). Both are pinned now — the web till's **real reducer**
   is exported and under test — and **§G64b and §G64c** are the checks that prove it in a real basket.

### What was deleted rather than left beside the new thing

`MemberDiscountBasket` (MAUI) and the web till's `applyMemberDiscount`/`clearMemberDiscount` actions.
One automatic discount became a set, so both were superseded by one resolver; all 16 of the MAUI
class's vectors are ported into `AutoDiscountBasketTests`. Two divergent paths for one job is the drift
C2 exists to prevent.

### Registers left true

Part A0 (three new Selling rows) · Part B B1 (scheduled discounts, ad-hoc typed discount, and the
basket-scroll note) · C1 (three money rules + the new *portal decides, till obeys* contract) · C2
(three new twins, **both sides pinned and both sides RUN**) · MAUI-retrofit §5c items 11 and 12.

⚠⚠ **And one correction worth more than the new rows: C2's "root cause" paragraph claimed the web till
has NO TEST SUITE.** It has 21 files and 335 cases. Several rows on that table still say "the
TypeScript half is unexecuted by anything" and were already out of date — a register that describes its
own tooling as absent invites the next author not to look. The honest limitation is that there is **no
CI**: the suite runs when somebody runs it, on the Mac.

---

## What a person needs to decide

| | |
|---|---|
| ⚠⚠ **Deploy the discount work?** | backend 1.18.0 (**migration**), portal 1.14.0, web 1.27.0. My recommendation: **dump, deploy backend + portal, run §G65, then deploy the web till and run §G64.** The rules feed is inert until a rule exists, so the blast radius before §G65b is nil |
| **Deploy §G62/§G63?** (web 1.26.1, portal 1.13.1) | Small and independent. They can ride along with the above |
| **Build the MAUI artefact?** | 1.107.1 and 1.108.0 are both committed and unbuilt. §G62a and §G64g need it |
| **`origin` history surgery** | `origin` cannot be pushed — a 151 MB blob in old history that `upstream` already has. Needs an LFS migration or an orphan branch. `upstream` is the off-machine copy meanwhile |
| **Whether W5's mechanics come next** | Its policy half is built (`AgentUpdatePrompt`); the packaging, exe swap and `ExpectedAgentVersion` are not |

⚠ **Still true from before, and still the highest-value thing anybody can do:** every capability row on
both tills is ✅ or 🟡, and **only a person at a screen turns a 🟡 into a ✅.** The run order is at the
top of [`Build/Test Maui.md`](Build/Test%20Maui.md).

---

## The standing documents

| | For |
|---|---|
| ⚠⚠ [`Build/To do/MAUI-retrofit.md`](Build/To%20do/MAUI-retrofit.md) | **THE plan.** §0 is the live state and every ruling; §3 the open steps; §5c the parity findings; §10 what comes out afterwards |
| 📦 [`Build/archive/Discount plan.md`](Build/archive/Discount%20plan.md) | **Archived 2026-08-20 — delivered and live.** The discount design, its eight decisions, and what the build changed about it. ⚠ Only the hand-run remains (§G64/§G65) |
| 📦 [`Build/archive/Multi-barcode plan.md`](Build/archive/Multi-barcode%20plan.md) | **Archived 2026-08-20 — delivered and live**, in two halves: the spine (1.19.0) then the UI nobody had (1.20.0). ⚠ Its banner records why that split is the lesson. Only §G66/§G67 remain |
| [`Build/Test Maui.md`](Build/Test%20Maui.md) | The hand-run script — run order at the top |
| [`Build/till-design.md`](Build/till-design.md) | **A0** parity at a glance · **B** the capability register · **C** the money rules and what stops them drifting. Read before writing anything that computes money on a client |
| [`Build/repo-runbook.md`](Build/repo-runbook.md) | Build, test, migrate, deploy — and the pitfalls that have each cost a session |
| [`Build/plutus-platform-architecture.md`](Build/plutus-platform-architecture.md) | **Wins any design conflict** with a plan |
| [`Build/index.md`](Build/index.md) | What every other document is for, and which are archived |
