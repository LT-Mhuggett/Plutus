# Handover — next session

> ⚠ **This document is ONE DAY LONG, on purpose.** Matt, 2026-08-17: *"I only want the handover to be
> for the following day."*
>
> Everything durable now lives in **[`Build/till-design.md`](Build/till-design.md) Part E** (Matt's
> rulings, the binding defaults, the pitfalls, the VAT settlement) and
> **[`Build/MAUI_finaltest.md`](Build/MAUI_finaltest.md)** (what is still unproven). The MAUI retrofit
> finished and was **archived 2026-08-23**. The day-by-day narrative back
> to 2026-07-28 is kept verbatim in
> [`archive/handover-history-to-2026-08-17.md`](Build/archive/handover-history-to-2026-08-17.md).
> **Do not grow this file back into a history.** Rewrite it; the commits are the record.

**Written:** 2026-08-21 · **.NET suites — all four run, all green:** unit **1561** · integration **177**
· MAUI **640** (+3 skipped, was 621) · architecture **31**. AppClient Release builds **0 errors**.

> ## ✅ THE GATES RAN — ON THE MAC, AT DEPLOY TIME, AND THREE OF MY OWN TESTS WENT RED
>
> **web till: `tsc` 0 · `eslint` 0 errors · vitest 381/381 across 26 files** (including today's three
> new suites). **portal: `tsc` 0.** The warning below stood all day and is now discharged.
>
> ⚠⚠ **AND THE RED WAS THE HONEST ANSWER.** `receiptToDocument` calls `api.businessName()`, which read
> `localStorage` **unguarded** — while `getReceiptTemplateCached` two lines away has always been
> wrapped. So formatting a receipt throws outside a browser, `printOnReceiptPrinter`'s catch turned
> that into a silent *"nothing printed"*, and three tests were exercising the failure path while
> asserting the happy one. **Green would have been the wrong answer.** `businessName()` is now guarded
> — reading `localStorage` can THROW, not merely return null, and on the receipt path that is a
> receipt that quietly refuses to print with the money already taken.
>
> ⚠ **The lesson for the day's earlier work:** every "NOT TYPECHECKED" flag was right to be there. The
> first thing the gates did was find a real fault.

> ~~⚠⚠ **THE WEB TILL'S AND THE PORTAL'S GATES DID NOT RUN, AND COULD NOT.**~~ *(now discharged — above)* There is **no node on this
> Windows box** — not on `PATH`, not in `Program Files`, nowhere. So today's TypeScript (`cardPayment.ts`,
> `connectionCheck.ts`, their two test files, `LoginPage.tsx`, `CheckoutDialog.tsx`, `index.css`, and 12
> portal files) has been **read carefully and typechecked by nobody**. `tsc --noEmit`, vitest and eslint
> must run **on the Mac** before any of it is built. Stated plainly because "it looked right" is not a gate.

> ⚠⚠ **AND `dotnet` ON THE PATH IS THE x86 ONE WITH NO SDK — pitfall 19.** `dotnet build` prints *"No .NET
> SDKs were found"* **and the tool wrapper still reports exit code 0**, so a build that never ran reads as
> a build that passed. Use `"/c/Program Files/dotnet/dotnet.exe"` and **read the output**, never the exit
> code.

> ⚠ `dotnet build` of the whole `Plutus.slnx` reports **6 errors that are the BOX, not the code** — no
> Android SDK and no .NET Framework 4.7.2 targeting pack. Pre-existing. Build the projects you need.

---

## ⏰ START HERE

**Deployed: backend 1.20.0 · portal 1.16.0 · web till 1.29.0 · platform 1.49.0 · MAUI artefact
1.110.0** at `D:\tmp\plutus-till-1.110.0\` (the only build on the box).

**✅ DEPLOYED 2026-08-21 (afternoon): web till 1.31.0 · portal 1.17.0** — built on the Mac, all gates
run, verified live on all three axes. Rollbacks `/srv/apps/PLUTUS/web/current.pre-1.31.0` and
`/srv/apps/PLUTUS/portal/current.pre-1.17.0`.

**Still BUILT NOWHERE: MAUI 1.111.0** — builds only on request (Matt, 2026-08-16). §G69's MAUI half
needs it; **§G70, §G71 and §G72 are runnable now.**

⚠ **Backend NOT redeployed and did not need to be** — zero changes under `src/` since 1.20.0, checked
with `git diff` rather than assumed. Probed anyway: swagger **200**, and `POST /api/v1/tokens/device`
with a junk id → **401 "Device not enrolled or revoked."**, the axis that proves the DB path.

> ⚠⚠ **THAT SECOND LINE IS THE MOST IMPORTANT THING ON THIS PAGE.** Everything done on 2026-08-21 is in
> the tree and in **no artefact anybody can run**. Testing §G69–§G71 against what is on the box today
> means testing the old code and reporting it broken — which **already happened once**, on 2026-08-20,
> when Matt reported the basket buttons for the second time and the fix was sitting in an undeployed
> bundle. **A fix that is built and not shipped is indistinguishable from a fix that was never made.**

### What was done, and what a person still has to do

| What | Hand-run | State |
|---|---|---|
| 🔴→✅ **The back-out crash audit — CLOSED.** It was never 17 sites | *(no hand-run — code fix)* | ✅ in the tree |
| ✅ **WP14 — the checkout says which card machine to use**, both tills | **§G69** | ⚠ MAUI needs a **1.111.0 build**; web needs a **1.30.0 build** |
| ✅ **WP16a — the web till's login screen says whether Plutus is reachable** | **§G70** | ⚠ needs a **web till 1.30.0 build + deploy** |
| ✅ **All 21 portal dialogs now have a ✕** (12 files) | **§G71** | ⚠ needs a **portal 1.17.0 build + deploy** |
| ⏸ **Everything from 2026-08-20** — discounts, multi-barcode, barcode editing, Syncfusion out | **§G68 → §G65 → §G64 → §G62 → §G66 → §G67** | ✅ live / built, **still never run by a person** |

⚠ **§G65b is still the configuration step nothing works without.** All six discount rules have
`AutoApply = 0` and no day mask, so the feature is applying nothing *correctly*. Portal → Prices →
Discounts → **Warhammer Wednesday Discount** (id 5, already exists, already 15%, already targets a
category) → Edit → tick *Apply it automatically*, tick *Wed*, Save.

---

## ⚠⚠ THE FINDING OF THE DAY: TWO OF THE FIVE "DO FIRST" ITEMS WERE NOT REAL

The morning started by verifying the do-first list against the code instead of trusting it. **Item 1
was already closed. Item 4 was about the wrong till.** Both had been contradicted somewhere else in the
same documents for days.

### 1 — *"17 input-alert sites crash the till on back-out"* — ✅ closed, and it was never 17

**§0.3b's own 2026-08-19 re-audit already said so** — *"17 was wrong; it is five, of which one is
reachable"* — and nobody carried that up to §0.3's table, §7's first row, or `till-design.md` D4's
honesty section. **All three still said 17, and it sat at the top of the do-first list two days running.**

Re-enumerated by grep, not by the list: **23 `LaunchInputAlertAsync` call sites, and every one an
operator can open returns on `Count == 0` or a checked `TryGetValue`** — including all five the D4 note
names by hand (refund, gift-card sale, cash, the supervisor prompt, checkout).

**The real residue, closed the same morning:**

- **Five dead `answers is null` checks** — `SupervisorPrompt`, `LoginViewModel` ×2, `SettingsViewModel`
  ×2. The helper ends `await popUp.PageClosedTask ?? new Dictionary<…>()`, so **null never arrives**;
  those five only appeared to work because a whitespace validator ran after them. Now `Count == 0`.
- ⚠ **`ViewAllViewModel.ExecuteUpdateItemStock`** — `data.Any(d => IsNullOrEmpty(d.Value))` over an
  **empty** dictionary is `false`, so the cancel path fell through to `int.Parse(null)` **after
  `db.Add(stock)` had already written a row**. Its twin in `AddEditViewModel` was fixed on 2026-08-18
  and this one was not. Unreachable today, one condition, and its twin already carried it.
- ⬜ **Left alone deliberately: 4 unguarded sites in `CopperTransferPlatform`, all unreachable** —
  `ICopperTransfer` is registered in `MauiProgram` and nothing resolves it. Same ruling as `SliderAlert`:
  **delete the tool or wire it up**, don't restructure four legacy object initialisers for a path no
  operator can open.

**D4's rule 4 is now honoured on every reachable dialog. The dialog contract is 5/5.**

### 4 — *"WP16 — 0 references on `LoginView`/`LoginViewModel`"* — wrong, and about the wrong till

⚠⚠ **The grep was for `ConnectivityProbe`, and MAUI never names it.** It reaches the probe through
`Services.Connectivity.TillConnectionCheck`. MAUI's login screen has had the badge all along — four
bound properties, `RefreshConnectionAsync`, tap-to-refresh and the clock-skew sentence,
[`LoginView.xaml` 72–97](Plutus/Frontend/Plutus.Frontend.AppClient/Views/LoginView.xaml#L72-L97). 16b is
done too: `OperatorLogin` calls `OfflineCredentials.Assess`.

⚠ **A grep for a shared type is not a check for a capability when a wrapper sits between them.**

**The real gap was the other way round: `LoginPage.tsx` was 111 lines** — email, password, error, button
— **with no indicator at all**, so a shop whose backend was down met a failed sign-in indistinguishable
from a wrong password. Under the 2026-08-19 look-and-feel ruling that is a parity gap, and it is now
closed: `connectionCheck.ts`, the C2 twin of the probe, same four states, same four sentences, same dot,
same tap-to-refresh, `verifyIdentity: false` on both tills. 13 vitest cases mirroring
`ConnectivityProbeTests.cs` — **unrun, see the node warning above.**

---

## The four things that were actually built

### WP14 — the checkout says which card machine to use (MAUI + the web till's pin)

The one do-first row that was accurately ⬜: **0 references** to `PaymentGateway` in any MAUI view or
viewmodel, against a shared half with 15 tests.

⚠⚠ **The case that earns it is the middle one.** A tenant who has chosen a real provider with **no
wired integration** is still on the manual flow — and *every* provider on this platform reports
`Integrated: false` today, so that is not the rare case, it is the only one a real shop meets. A cashier
reading only *"Card via Worldpay"* stands waiting for a terminal prompt that is never coming.

- ⚠ **No extra round trip.** `GatewaySurcharge` already fetched the whole `ActiveGatewayDto` at
  checkout-open and kept **two of its five fields**. Renamed **`GatewaySettings`** — a name that
  described what it *kept* rather than what it *asked for* is how the other three stayed invisible.
- ⚠ **The cache holds the server's ANSWER, not the decision.** `Provider`/`Label`/`Integrated` go into
  Meta and `PaymentGateway.Resolve` runs on the way out, every time. Storing the resolved display would
  freeze one day's reading of the rule into every till's database. An upgrading till has none of the
  three keys → reads as `Provider = null` → **standalone**, the flow that works when nothing else does.
- ⚠ **`FormattedString` derives from `Element` and throws a `COMException` outside a UI host**, so the
  first cut of the sentence composer was untestable — on the one screen whose entire family of defects
  shipped for exactly that reason. It now returns plain `HintSpan` records and the view makes spans.
  7 tests.
- ⚠⚠ **And the WEB till's half was ✅ and had drifted anyway.** Its three cases were an inline ternary,
  `gateway.provider === "standalone"`, which differed from .NET in **three** ways nobody had noticed:
  case- and whitespace-sensitive (so `"Standalone"` rendered *"💳 Card via Standalone (integration
  pending…)"*), an empty provider rendered *"💳 Card via "* with nothing after it, and a blank label did
  the same instead of falling back to the provider key. Now `till/cardPayment.ts`, with vectors mirroring
  `PaymentGatewayTests.cs`. **New C2 row.** `PaymentGateway.cs`'s own header had described this drift
  risk in prose for weeks.

### The 21 portal dialogs

Mechanical, and it went wrong twice in ways worth keeping:

- ⚠⚠ **The list of 13 was 12.** `ReportPublicationSection` has **no dialog at all** — it is an inline
  page section that borrows the `.dialog-actions` button row. **D4's own check command grepped
  `className="dialog`, which prefix-matches `dialog-actions`**, so the contract's checker put a file with
  nothing to fix onto the to-do list. D4's pattern is now `className="dialog("| )`.
- ⚠ **The same prefix match, in the script written to do the insertion, put a ✕ above every button bar in
  the portal** on its first run. Caught by reading the diff. **A check that over-reports is not the safe
  direction — it teaches you to ignore its output.**
- Each ✕ is wired to **that dialog's own overlay close action**, read off the file rather than assumed —
  so the six that refuse to close while busy still refuse (`disabled={busy}`), and the four that close
  local state do exactly that.

**D4's check now returns empty on all three surfaces.**

## ⚠⚠ AND THEN STEP 11b — where the fourth stale claim was, and the real fault behind it

Matt asked to continue, so the top of §7 got the same treatment as the do-first list: **verified
against the code before starting.** Step 11b said 4 days. Its own section contradicted itself — the
body records the pence reshape as landing on 2026-08-16 and the to-do list eleven lines below still
asks for it — and §7's headline was worse: *"`ExecuteCheckoutTransaction` is a ~200-line `async void`
and the last money-adjacent cluster in this app with no test coverage at all."*

**It is 243 lines of which roughly 45 execute**; the rest is commentary. Its money was already covered
three ways over — `TenderSettlement` (mutation-checked, C2-twinned), `CheckoutHelper.Settle` (11
tests), `CheckoutCommit` (36). **What genuinely had nothing was the orchestration**, and that is what
this closed.

### ⚠⚠ The real fault: the till derived its own basket total FOUR times, in decimal pounds

`SaleIncTax`, `SaleExTax`, and `sale.Total`/`TotalExTax` at two sites — while
`CheckoutCommit.BasketMoneyPence` is the figure the commit reconciles the payload against. **One of
the four ended `Pence.FromDecimal(sale.Total)`** — rounding the *sum* rather than the *lines*, which
`BasketMoneyPence`'s own header forbids in as many words — and it fed **the number on the checkout
screen the operator tenders against**.

⚠ **Latent, not live, and worth stating precisely:** they agreed to the penny because `Price` is an
exact projection of `PricePence`. **Nothing was holding that invariant.** The first basket record
priced from anywhere else would have produced a till whose screen and payload differ by a penny —
visible to a shop only as the commit refusing a sale with a message about a discount that isn't
attached to anything.

- ✅ **One derivation**: `BasketMoneyPence` + a new `BasketMoneyExPence`, in pence, projected to pounds
  for the two labels. ⚠ They stay `decimal` — `TillView.xaml` binds both `StringFormat='{0:C2}'`, and
  a `long` behind that renders £3.30 as **£330.00**, silently. That trap is what the whole reshape
  exists to avoid, so it was not "fixed" by switching types.
- ✅ **`refundOnly` lifted to `CheckoutCommit.IsRefundOnly`, predicate UNCHANGED.** It decides whether
  a customer can be paid back in cash. Tightening it during an extraction would be a silent money-path
  change behind a refactor — so the empty-basket case still answers `true`, and there is a test
  *named* `An_empty_basket_answers_true_because_that_is_what_shipped`.
- ✅ `SaleLinesGrossPence` stopped round-tripping through `Pence.FromDecimal(b.Price)` to reach a
  number already sitting in `b.PricePence`.
- ✅ Six `is BasketReturnItem` type-tests in `PosPrinterManager` collapsed onto `IsReturn`. ⚠ Checked
  for reachability **first** — it is a live fallback on the print path, not dead code like
  `CopperTransferPlatform`.

✅ **`BasketMoneyTests` — 12 cases, mutation-checked three ways**: dropping the return negation (3
red), `BasketMoneyExPence` reading the inc-VAT price (2 red), dropping `IsRefundOnly`'s negation (5
red).

⚠⚠ **And the mutant it CANNOT kill is written into the test's own header.** Sum-then-round stays green
while `Price` remains an exact projection — which is exactly why the fault was dormant. **A test file
that overstates what it pins is worse than one that admits the gap.**

---

---

## ⚠⚠ Two tooling traps that cost time today — worth a runbook line

1. **`perl -0pi -e` DOUBLE-ENCODES UTF-8 in this repo.** Without an encoding layer, perl reads the file's
   UTF-8 bytes as Latin-1 characters; the moment the replacement contains one wide character (⚠, —), it
   re-encodes the *whole* string and every existing ⚠ becomes `â\x9a\xa0`. It printed a *"Wide character
   in print"* warning and corrupted a file that then had to be reverted. **Pass the replacement as raw
   bytes** — via `$ENV{}` or `\x{e2}\x{9a}\x{a0}` escapes — and **read the diff**, every time.
2. ⚠⚠ **A `perl -0pi` one-liner that reassigns `@ARGV` mid-stream TRUNCATED `CheckoutDialog.tsx` TO ZERO
   BYTES.** Recovered from the commit made an hour earlier. **This is the argument for committing before
   a batch of scripted edits, not after.**

---

## What a person needs to decide

| | |
|---|---|
| ⚠⚠ **Run the TS gates on the Mac** | `tsc --noEmit` + vitest + eslint for the web till, `tsc --noEmit` for the portal. **Nothing from today's TypeScript has been executed.** This is the gate, not a formality |
| **Build + deploy portal 1.17.0 and web till 1.30.0?** | Small and independent of each other. §G70 and §G71 cannot be run until they are |
| **Build MAUI 1.111.0?** | Only when you ask — Matt, 2026-08-16. §G69's MAUI half needs it |
| **~~Step 11b (4d)~~ → ≈1–2d, and it is the SEAM that is left** | ⚠⚠ **"243 lines of `async void` with no coverage at all" was the stalest claim on the page** — ~45 of those lines execute, and the money was covered three ways over. ✅ The **orchestration** was closed 2026-08-21 (see below). ⬜ What remains is the wiring between the dialogs and the commit — the seam finding U broke while `TenderLoop`'s 19 tests stayed green. **Only §G58's hand-run reaches it** |
| **WP10 — an item editor on the till at all** | ⚠ Matt's call, and arguably not a gap: all four A0 ⬜s are this, and MAUI has no editor *deliberately* — a till-created item reaches no report, no other till and no VAT return |
| **`origin` history surgery** | Still unpushable — a 151 MB blob in old history. `upstream` is the off-machine copy |

⚠ **And still the highest-value thing anybody can do:** **36 A0 rows sit at 🟡** — built, tested where
testable, **never once exercised by a person**. Only a person at a screen turns a 🟡 into a ✅, and the
first hand-run of this retrofit found **fourteen faults, six invisible to every automated test here.**
A 🟡 is not a smaller ⬜; it is an unknown.

---

## ⚠⚠ The lesson, and it is now three days running

Of the five items on §7's do-first list, **two were stale and one named the wrong till** — and every one
of them had already been contradicted somewhere else in the same documents. §6 says this table *"should
be re-derived from `till-design.md`, never maintained by hand"*, and it was maintained by hand for three
days after saying so.

**Verify a row against the CODE before scheduling a day of work against it.** The check costs minutes;
twice today it deleted the item.

⚠ The estimate is now **≈7–9 days**, down from 10–15 — but the number is the least of it. What is left
is **one real build** (step 11b, 4d), **one hardening pass** (step 28, 2–3d) and **a ~1d tail**
(step 24).

---

## The standing documents

| | For |
|---|---|
| [`Build/archive/MAUI-retrofit.md`](Build/archive/MAUI-retrofit.md) | ✅ **ARCHIVED 2026-08-23 – history only.** Live successors: `till-design.md` Part E and `MAUI_finaltest.md`. Was: §0 the live state and every ruling · §3 the open steps · §5c the parity findings · §7 what is left · §10 what comes out afterwards |
| [`Build/Test Maui.md`](Build/Test%20Maui.md) | The hand-run script — **run order at the top**. §G69–§G71 are new and need builds first |
| [`Build/till-design.md`](Build/till-design.md) | **A0** parity at a glance · **B** the capability register · **C** the money rules and what stops them drifting · **D4** the dialog contract, now 5/5 |
| [`Build/repo-runbook.md`](Build/repo-runbook.md) | Build, test, migrate, deploy — and the pitfalls that have each cost a session |
| [`Build/plutus-platform-architecture.md`](Build/plutus-platform-architecture.md) | **Wins any design conflict** with a plan |
| [`Build/index.md`](Build/index.md) | What every other document is for, and which are archived |
