# MAUI — the final test

> **Matt, 2026-08-23:** *"If MAUI-Retrofit.md is finished, please strip out all of the tests into a
> MAUI_finaltest.md doc and archive MAUI-retrofit.md."*
>
> This is that document. **`MAUI-retrofit.md` is archived** — the build it tracked is done.

## ⚠⚠ Where this actually stands, and what "done" means

**The build is finished. Nothing here is a code estimate.** Re-derived from
[`till-design.md`](till-design.md) with `awk` rather than counted by hand:

| A0, MAUI column | ✅ | 🟡 | ⬜ | ➖ | 🟠 |
|---|---:|---:|---:|---:|---:|
| 2026-08-23 | **44** | **41** | **0** | 3 | 2 |

The only two ⬜ left anywhere in Part B are not MAUI build work: **Store Information's local
editor** is a deliberate ⬜ because cutover step 20 *deleted* it (that row is inverted — a removal,
not a build), and **remote lock of a lost till** is ⬜ on the **web till too** and moved out to
platform WP-SL.

⚠⚠ **SO THE ENTIRE REMAINING RISK IS 41 🟡 ROWS: built, tested, and never once exercised by a
person.** A 🟡 is not a smaller ⬜ — it is an unknown. Only somebody at a screen turns one into a ✅.

## ⚠ Which document to open

| You want to… | Open |
|---|---|
| **Run the tests, step by step** | [`Test Maui.md`](Test%20Maui.md) — 5,960 lines, §A–§G, the executable script. **That is where the steps live.** |
| Know what still needs proving, and why it matters | **This document** |
| Check a capability or a money rule | [`till-design.md`](till-design.md) — A0, Part B, and C1/C2 |
| Read the retrofit's history | [`archive/MAUI-retrofit.md`](archive/MAUI-retrofit.md) |

⚠ **This page is deliberately NOT a second copy of `Test Maui.md`.** Two step-by-step scripts that
drift is the failure this project has had before, in five documents that became one. This page says
*what is unproven and why*; that page says *what to click*.

## ⚠⚠ The case for actually doing the hand-run

Not a formality. **Every hand-run of this retrofit has found faults no automated test here could
see**, and the record is in this document:

| Hand-run | Found | Of which invisible to automation |
|---|---|---|
| 2026-08-11 | **14** | **6** |
| 2026-08-13 | 5 | — (all five fixed the same day) |
| 2026-08-18 parity review | **10** | the two 🟠 below started here |

⚠ The 2026-08-18 review is the sharpest illustration: it found things the **capability register
cannot express**. A row that asks *"can the till show its store details"* is answered by data
arriving — not by anybody being able to read it. Every field label was light grey on near-white, so
the information was all present and none of it legible, and Part B said ✅ throughout.

## ⚠ The two 🟠 — known, and not closed by any test passing

These are real gaps that a ✅/✅ parity row cannot express. They come from §5c below.

| What | State |
|---|---|
| **Park a basket and recall it** | Saving works. ⚠ **The RETRIEVE is a toolbar item nobody finds** — §5c item 1. Parity now includes *look and feel* (Matt, 2026-08-19), so a capability an operator cannot locate is not at parity. |
| **Take its colours from the portal** | MAUI fetches and applies them, but **only ONE screen paints with them** — §5c item 10. |

---

## ⚠ Why the section numbers below are not sequential

They are the numbers these sections had in `MAUI-retrofit.md`, kept **deliberately**.
`till-design.md` and the commit history cite them by number — *"§5c item 10"*, *"§0.3b"*,
*"see §8"* — and renumbering would silently break every one of those references while looking
tidier. A gap in the numbering costs a moment's confusion; a reference that resolves to the wrong
thing costs a session.

---

## 8. USER-VERIFY — what only a person can close

Everything marked VERIFY in a step body is headless and gates the step. Everything here needs
hardware or a human eye: it **blocks sign-off, not the next step**. ⚠ **Never claim a USER-VERIFY
item is done.**

**Standing, per step:**

- [x] **Step 1: one real till launch on EF 9** — Matt ran 1.14.0–1.16.0 on Windows, 2026-08-10
- [ ] Step 4: real enrolment round-trip (fresh code from the portal)
- [ ] Step 14: paper receipt renders correctly
- [ ] Step 22: theme applied from the portal + a byte-identical receipt light/dark
- [ ] Step 23: ⚠ **the drawer physically kicks.** `POSCashDrawer.InitPOSObject` fell out of its own
      success path into `throw NotClaimable` (the `return` was missing; its sibling `POSPrinter` has
      it), so a working drawer reported "in use by another process" on every cash sale
- [ ] Step 23: the **"Silence" button actually silences** the drawer warning — it set a preference
      nothing read, so the modal returned on every cash sale for ever
- [ ] Step 25: scanner round-trip including the unknown-barcode add flow
- [ ] Any web-till TS edits: `npm run typecheck` **on the Mac** (5 pre-existing edits queued)

**Open on 1.48.0** — fixed in code, unproven on hardware. Full script: [`Test Maui.md`](Test%20Maui.md).

| Where | What to check |
|---|---|
| Inventory | Click into the search bar — it crashed reliably on 1.41.0 (**A**) |
| Inventory | **Edit an item.** The form must arrive with Name/Brand/Cost/Price **already filled in** — that was the whole of **K** |
| Till | Ring a sale AFTER a Z close — both the till and the platform must refuse it (**B**) |
| Till | Refund a CARD sale: cash must not be offered (**G**). Then re-add an item you have just sold (**C**) |
| Till | Overpay by card, underpay in cash — both must say what is wrong, not "something went wrong" (**D/E**) |
| Cash | Open a float and **stay on the tab**: "(waiting to send)" must clear on its own within a minute (**J**) |
| Cash | Close the day with the WRONG money — open £150, pay out £20, count £110. Within a minute the Z line must turn red: **£20.00 SHORT — Plutus expected £130.00** (**I**) |
| Reporting | Ring a sale and **stay on the tab** — today's takings must move; it used to freeze at sign-in (**N**) |
| Tabs | Till · Cash · Inventory Management · Reporting · Store Information · Settings · Plutus (**M**) |
| Offline | ⚠ **The safety case:** pull the network cable while signed in and **nothing should happen.** If the till signs you out when the line drops, "couldn't ask the server" is being read as "you are disabled" — which would sign a whole shop out mid-sale on every broadband blip. Pinned by a test; this is what would catch it on hardware |
| Cash, offline | Take a paid-out with the line down, reconnect, watch "(waiting to send)" clear |
| Printing | ⚠ **"No agent found" is a valid outcome, not a failure** — if no Plutus Till Agent is installed, printing degrades to the old behaviour by design |

⚠ **Press Plutus → "Re-download the whole catalogue" first.** The backend sends brand, description
and cost, but an existing till only receives new *fields* for items that change after it — that
button forces the backfill, and is why deploy order stopped mattering.


## 17. Testing

- Keep `Plutus.Frontend.AppClient.Tests` green as the regression gate. **UI regression is USER-VERIFY
  until an in-branch UI suite exists.**
- ⚠ `AppClient.Tests` has `InternalsVisibleTo` from the app, so **viewmodel and service logic is
  headlessly testable** — only XAML, printer, drawer and scanner genuinely need hardware. The old
  note that "WP6–13 need a device" was substantially false.
- An offline-mid-checkout fixture (mock connectivity gate): the sale still completes and lands in the
  outbox. Plus an online-transition test: queued sales drain correctly.
- Unit-test the client outbox retry contract against `OutboxDrainer`'s semantics: stay Pending,
  5s→5min backoff, retry for ever on network failure.
- An integration test hitting real `/api/v1/tills/enrol` + `/api/v1/sales` against a disposable test
  tenant.
- **Explicitly assert store-credit stays disabled offline.** A regression there is silent
  data-integrity damage, not a UX gap.
- ⚠ **CI ran none of the modern suites** until 2026-08-09 — Unit, Architecture and Integration (709
  tests, every VAT and till-client rule) were in no pipeline, and the push trigger listed only
  `master` while `Matt's-Horror` is the active branch. Both fixed. **Check what CI actually runs
  before trusting a green tick.**


---

# The record — what hand-runs and audits have already found

> ⚠ Kept because it is the evidence for the section above, not for nostalgia. Each of these was
> a person at a screen finding what the suites could not.

## 1b. Raised by the 2026-08-13 hand-run — ✅ ALL FIVE DONE the same day

| # | What | Where it landed |
|---|---|---|
| **Z1** | A visible door onto a refund | ✅ **Till 1.52.0.** A **"↩ Return an item"** button beside the scan box. ⚠ **It is not the web till's flow and the difference is recorded rather than papered over:** the web till starts from the SALE (a dialog finds it, then adds the line), MAUI starts from the BASKET (scan the item, then mark the line as going back). So the button drives the flow MAUI has, and when nothing is selected it says what to do instead of opening a dialog that cannot work yet. **Step 26's lookup is where the two shapes converge** |
| **Z2** | Opening hours, missing from MAUI entirely | ✅ **Till 1.52.0.** A read-only weekly table on Store Information, parsed exactly as the web till parses it (day key → spans, a missing day meaning **Closed**, not "unknown"). ⚠⚠ **WP6's DoD required this and step 20 was ticked without it** — a ⬜ wearing a ✅, which is the third this week. ⚠ Unparseable JSON reads "not set" rather than throwing: a store screen must never be the thing that takes a till down. ⚠ **If the web till also shows nothing, the hours are simply unset** — Portal → Locations & Tills → edit the store |
| **Z3** | Today's takings: say when it was read | ✅ **Till 1.52.0.** `· as at 16:32` on the line. It always refreshed (on appearing **and** on the 60s tick — verified in the code, not taken from a comment); what it could not do was let anyone tell a figure read five seconds ago from one read at sign-in. **On the number a manager counts a drawer against, that is not decoration** |
| **Z4** | Portal: amber the out-of-balance drawer tile | ✅ **Portal 1.8.0, DEPLOYED.** ⚠ Amber, not red: a drawer being out is a thing to look into, not a failure, and spending red here devalues it where errors live. Inline style rather than a new class, so there is no `stat-alert` for a second and third tile to dilute |
| **Z5** | Portal: stock adjustments its own tab | ✅ **Portal 1.8.0, DEPLOYED.** Inventory → **Items · Stock ledger · Stock adjustments · Categories · Bin**. ⚠ The report already existed at the bottom of the ledger page and Matt went looking for it and did not find it. It renders in **both** places: the reason for putting it on the ledger (somebody thinking about stock is already there) did not stop being true when it gained a front door. Deep-link `focus=adjustments` too |


## 23. The 2026-08-11 hand-run — all fourteen findings closed

Matt ran a shop day on till 1.41.0 and reported **A–N**. Every one is answered, and six more raised
that evening (**O–T**). Full detail with causes: [`handrun-2026-08-11.md`](archive/handrun-2026-08-11.md).

| | What was wrong |
|---|---|
| **A** | The till **crashed** when you clicked into the Inventory search box — focus alone rebuilt a grouped collection, and the search path had no exception guard |
| **B** | ⚠⚠ **A sale could be taken after the day was Z-closed, and the platform ACCEPTED it** — 201, onto a day already counted and banked. The rule existed only on the cash path. Now gated on the till **and** the server (quarantine, not reject — the money is real) |
| **C** | An item just SOLD could not be re-added — a dangling basket selection incremented a detached ghost |
| **D/E** | *"Something went wrong"* on an over-payment — it was a **crash**, not wording. Refusals now say what is wrong |
| **F** | Split payments — ⚠ **they already worked on both tills**; nothing needed writing. The web till gained its first-ever tests (19) while checking |
| **G** | Refunds now offer **only the tender the sale was paid with** |
| **H** | Stock adjustments report — the ledger always had it; there was no way to READ it |
| **I** | A counted drawer now says **SHORT/OVER**; the platform always knew and never told anyone. ⚠ The Z now waits for its own day's sales, or it reports a shortage equal to everything not yet sent |
| **J/N** | Screens did not redraw — the Cash tab showed "(waiting to send)" against money already banked, and **today's takings were read once, at sign-in** |
| **K** | ⚠ Edit item — **three attempts, and my first two diagnoses were both wrong.** Not a clipped form; the container (`InputAlert`, label+Entry only) was the constraint. Editable fields arrived as grey **placeholders** while the read-only rows were the only ones showing values, so the form looked like a tax viewer — and changing only the price submitted an empty name and was dropped in silence. Now one page |
| **L** | There is **no delete-item** and never was — item delete is structurally impossible (`ItemController` is CRU, not CRUD). Every "Delete…" label now says CATEGORY |
| **M** | Tabs renamed to match the web till |
| **O–T** | Reopen a Z close · a closed till refuses at the door · "Locations & Tills" · the `0.0.0` version bug · "Last online" was the enrolment date |

⚠ **The `0.0.0` bug was a BUILD-MACHINE fault, and the lesson generalises.** Both `vite.config.ts`
files read the version from `../../../versions/<app>.txt`, which resolves only inside a checkout — and
the Mac builds from **flat copies** with no `versions/` above them, so the read threw and the `catch`
returned `0.0.0`. **Every web-till and portal build ever made on the Mac shipped mislabelled**, while
MAUI was right all along because it builds inside the repo. ⚠ **Verify the number reached the bundle,
not that the build passed.**


## 5c. ⚠⚠ THE 2026-08-18 PARITY REVIEW — ten findings from a person at a screen

> Matt ran the MAUI till against the web till and reported nine things in one message, then asked about colour customisation — item 10. **This section
> is the work programme.** Sizes are honest; the order is mine and argued.
>
> ⚠⚠ **READ THIS FIRST: the register said most of this was done.** Reports, Loyalty, Settings and
> checkout were all **🟡 — "built, tested where a machine can reach, never seen by a human."** A human
> has now seen them, and 🟡 turned out to mean *"the data arrives and the screen is wrong."* That is
> the register working exactly as designed, and it is the strongest evidence yet for the hand-run:
> **every one of these was invisible to 1,348 unit tests and 598 MAUI tests.**

### What was done immediately (2026-08-18)

| # | Finding | Done |
|---|---|---|
| 1 | **Cannot retrieve a saved basket** | ✅ **DONE 2026-08-18 (till 1.81.0).** Now **Save Transaction** and **Retrieve** side by side, Retrieve disabled via `HasStoredTransactions`. The toolbar item is gone. ⚠ And it was hiding a CRASH: the old path called `StoredTransactions.First()` on an empty collection — `InvalidOperationException` out of an `async void`, i.e. a dead till. Guarded in the method as well as by the disabled button, because a disabled button is a UI state and only one of the two is a guarantee. ⚠ `RetrieveTransaction` had no resx key, and `.Translate()` on a non-key **throws in DEBUG** (runbook pitfall 18) — added, or the till screen would not have opened |
| 6a | ⚠⚠ **Add member / Set tier answered 403 for EVERY operator** | ✅ **DONE 2026-08-18 (till 1.81.0).** Found while starting item 6: both used `TillPlacement.TryCreateApiAsync()` — the **DEVICE** client — while `POST`/`PUT /api/v1/customers` are `perm:`-gated and RBAC resolves by the token's `NameIdentifier`, which on a device token is the device id. **Adding a member has never worked on this till.** Same fault as the Reports tab (1.75.0), in a second place; both now use `PlutusApi.GetOperatorAsync()`. ⚠ This is very likely what Matt experienced as *"the save fails but DOESN'T tell you"* — the failure was real, and its message depended on the server populating a problem string |
| 6b | ⚠⚠ **MAUI had NO WAY TO ADD A MEMBER AT ALL** — for a few hours, and I caused it | ✅ **DONE 2026-08-18 (till 1.82.0).** Item 4 took Add member / Set tier off the till screen, correctly. But the **Loyalty tab — the one screen with a member list — had neither**, so between those two commits the till could not add a member anywhere. ⚠ Its own class header had asserted that assigning a tier *"is done on the Till tab against an attached member"*, which my removal falsified in the same commit that removed it; **the comment was the only thing that would have caught this, and it was wrong by then.** Both actions are now on the Loyalty tab beside Search — where the web till puts them — **moved, not copied**: the originals are deleted from `TillViewModel` (255 lines), because two divergent add-member paths is exactly the drift C2 exists to prevent. ⚠ Set tier asks *"whose tier?"* from the rows on screen (labelled name **·** membership number, since two J Smiths are ordinary and putting the wrong one on Gold is money) — an action sheet rather than a row tap, because `TillTable` has no row-tap hook and it is shared with Cash, Statistics and Inventory. ⚠ Mandatory marked (`Name *`), inactive tiers not offered, empty tier list **names the portal**, and the operator's client throughout (6a) |
| 4 | **"Search" and "Add member" on the till screen** — *"Neither the webtill or original NatApp has this here."* | ✅ **REMOVED.** ⚠ I argued once that they belonged and was **wrong on the facts**: the web till's till screen has no customer control at all — a member is attached by **scanning their card** (`MEMBER_CARD` in its scan handler), and MAUI has the same path (`MemberNumbers.LooksLikeMemberScan`). Nothing was lost. The attached-customer row (who is on this sale + Remove) stays, because that is about the basket, not about managing the scheme |

### The programme

| # | Finding | What it needs | Size |
|---|---|---|---|
| 2 | **Checkout must match the web till** | ✅ **DONE 2026-08-19 (till 1.104.0).** The sequential prompt chain is gone: every method is on ONE screen with a live Paid / Remaining / Change, "rest" on each row, the ceiling stated per row, named refusals, and the gift card behind a *"🎁 Pay with a gift card"* button **below** the tenders (Matt: *"There is no point showing it all, unless you ahve a card"*). ⚠⚠ **THE ARITHMETIC WAS NOT ALREADY SHARED — this row said it was, and it was wrong.** `TenderLoop` ↔ `tendering.ts` are twins for the SEQUENTIAL question; the one-screen functions (`assess`, `restFor`, `refusalReason`, `apportionChange`, `parseAmounts`) existed **only in TypeScript**. So the first half of this slice was building `Client.Core.TenderSettlement`, the .NET twin, against the web till's own 40 vectors — **36 tests, three mutants killed**, including the JS-vs-.NET midpoint trap that would have put a penny of change in the wrong place. ⚠⚠ **IT KILLS THE DOUBLE-TAKE BY CONSTRUCTION**: the loop asked for a method and an amount over and over, so a capped tender could be picked twice and take its cap each time (the money defect of 2026-08-19, fixed there with an accumulating guard). One box per method means there is no second pass to take — the shape of the bug is gone, not defended against. ⚠ **The card fee now comes OFF again** when the card row is cleared; the loop could only ever add it, so picking card and changing your mind left the fee on the basket. ⚠ The screen returns the same `TenderOutcome` the loop did, so the sale model, drawer, receipt and commit are untouched — replacing how money is ASKED FOR should not reach the code that records it. ⚠ 🟡 until **§G58**. | **done** |
| 2b | ⚠⚠ **Store credit must come from a KNOWN customer, or be gated with a recorded reason** | ✅ **RULING GIVEN AND MOSTLY ALREADY TRUE — 2026-08-18 (backend 1.17.4 + portal 1.10.0).** Matt: *"Store credit needs to be for a KNOWN customer. Adding credit needs to have a reason and be viewable in the customers history."* ⚠ **So there is NO anonymous-credit path to build** — the plan assumed a supervisor-authorised exception and the answer is that it is simply refused. Credit is a liability the shop owes a **named** person; a bearer instrument is what a **gift card** is (WP13, already built). ✅ **SPENDING credit was already correct on MAUI** and needed nothing: the tender is only offered when `CreditAvailablePence > 0`, which requires an attached customer, and `TryRedeemStoreCreditAsync` refuses defence-in-depth if the customer is detached mid-checkout — *"committing a sale carrying credit nobody owns would be money from nowhere"*. The ruling confirms that design rather than changing it. ⚠⚠ **WHAT WAS ACTUALLY WRONG WAS THE REASON, AND IT WAS WRONG IN TWO PLACES.** `POST /customers/{id}/credit/issue` did `body.Reason?.Trim() ?? "grant"` and the portal sent `issueReason \|\| "goodwill grant"` — so credit could be granted with **no reason anybody typed**, and the history then showed a plausible-looking word that means nothing. ⚠ **That is worse than a blank, because it READS as an audit trail** — the same class of fault as a substituted figure. ✅ The endpoint now **refuses** a missing, empty or whitespace reason (all three reached the old default), and the portal marks the field, gates the button and no longer substitutes. ✅ **The history was already there** — `GET /customers/{id}/credit` returns the entries with their reasons and the portal's *Credit history* table already shows a Reason column, so *"viewable in the customers history"* was met; the round-trip is now **asserted** rather than assumed. ⚠ **Known-customer is structural on issue**: the route carries the id and an unknown one is a 404, which the test pins too. ⚠ 1 new integration test, **watched going red** (a reasonless grant returned `OK`). ⚠ It also exposed a suite fragility worth knowing: WP13.5 throttles **50 rps per tenant** and every test shares Kapow, so adding requests anywhere can rate-limit a **different** test — a sibling failed reporting `TooManyRequests` while asserting `BadRequest`. Both now use the retrying `Send` helper that two other test classes already carry. ⚠ **Not built, and not asked for: an add-credit path on the till.** Credit is granted in the portal; if a till ever needs one it inherits the same mandatory reason from the endpoint | **done** |
| 3 | **Click the price in the row to adjust it** | ✅ **DONE 2026-08-18 (till 1.86.0).** The **price cell is now the way in** — tapping it opens the adjust box, which is what the web till does (its price cell is a `linklike` button). ⚠ Before this, **Adjust existed only on the right-click / long-press context menu** with nothing on screen to say so: the identical discoverability fault already reported about editing an item (*"I didn't know how to open it"*, 2026-08-10). ⚠ An adjusted line is now **marked with a `*`**, as the web till marks it — money-visibility, not decoration, because a £5 item retyped to 50p otherwise looks exactly like an item that costs 50p (finding W's lesson on a different control). ⚠⚠ **NOT a literal in-cell text box, and that is a decision worth challenging.** A `Button` or an `Entry` in the cell would match the web till's *implementation*, but either needs its colours resolved from the theme, and **a price that renders invisible on the money screen is the fault that cost build 1.74.0** — so the existing `Label` and its binding are untouched and the gesture sits on a wrapper. If the tap turns out not to fire inside a `ViewCell` on WinUI, the price still renders exactly as before and the context menu still works: **feature-absent beats price-invisible.** ⚠ Unverifiable by machine — **§G42a** is the only check. Parity here is in FUNCTIONALITY (Matt, 2026-08-17), and the function asked for was *"click the price"* | **done**, well inside 1–1½ d |
| 3b | ⚠⚠ **A MONEY BUG FOUND BESIDE IT: a second scan joined a hand-adjusted line AT THE ADJUSTED PRICE** | ✅ **FIXED 2026-08-18 (till 1.86.0).** Ring a £5 item, adjust it to 50p, **leave the line selected**, scan the same item again — and the second unit joined that line at **50p**. The shop sold it for a tenth of its price, silently, with the receipt as the only evidence. ⚠ **The fault was TWO PATHS, not a missing check.** MAUI's search path compared the price pair (and so behaved correctly by arithmetic accident); its *selected-line fast path* checked only the item id and "not a return". The web till has never done this — `basket.ts` excludes `!l.adjusted`. ⚠ Found by **reading the web till's reducer instead of ours** while doing item 3, which is the lesson: the recorded rule is *when Matt says a screen does not match, check the other screen before defending ours*, and it pays on logic as well as layout. ✅ The rule now lives in `SharedKernel.BasketMerge` (**C1**), both MAUI call sites ask it, and its TypeScript twin is pinned by a **shared ten-case vector table** (**C2**) — `BasketMergeTests` and `till/basketMerge.test.ts`, same figures, **and both sides were RUN** (10 green in C#, 10 green in vitest on the build Mac; 224 green there overall). ⚠ Two mutants watched dying: removing the `adjusted` guard, and dropping the ex-half of the price comparison. ⚠⚠ **The `adjusted` flag is tested BEFORE the price pair** — adjusting a line to exactly its catalogue figure would otherwise pass the arithmetic and re-merge, which is the accident the old code relied on. ⚠ Honest weakness recorded in C2: the TS side is a **copy under test**, because the web till's predicate lives inside a `find(…)` inside `addLine` and is not exported. **Exporting it is the real fix and is not done.** ⚠ **§G42d** is the hand-check, and it is written as a money check | **found and fixed** |
| 5 | **Reports do not match the web till** | ✅ **ALL THREE GAPS CLOSED 2026-08-18 (till 1.85.0 + backend 1.17.3).** ⚠ First, the gap was smaller than this row said and in a different place. The web till has **eight** subtabs (Summary, Custom, VAT, Items sold, Category sales, Best sellers, Stock, Negative stock); MAUI had **five**. ⚠ *"Custom"* is **not** a missing report — it is the web till's separate custom-range takings tab, and MAUI's From/To pickers already apply a range to **every** report, which subsumes it. So the real gaps were **Stock**, **Negative stock** and **drill-down**. ✅ **Stock and Negative stock are in** — and the framework did what it promised: `ReportCatalogue` took **one entry each**, no screen, no viewmodel, no XAML (Matt, 2026-08-16: *"Can I check that you are writing the framework that new reports can just be dropped into MAUI?"* — this is the first time that claim has been tested by a new report, and it held). ⚠⚠ **AND THE GATE WAS WRONG AGAIN.** `GET /api/v1/stock/levels` was `portal.reports.view` only, which no Supervisor or Cashier holds — **the fourth time this same defect has been fixed**, and the third fix is recorded in a comment on `POST /api/v1/stock/levels/bulk` **six lines above the endpoint it missed**. Every fix so far landed on the endpoint that happened to be in use rather than on the rule; the two URLs are now in `TillHardeningE2eTests`, added **first** and watched returning 403. ⚠ Also honest: **no date range applies to stock** (it is a fact about *now*, a sum of the movement ledger) and the report **says so on screen**, because the From/To pickers sit above it. ⚠ 5 new tests, and the one that matters was **mutation-checked** — dropping `filter=negative` makes Negative stock list the whole shop, which under that heading reads as *"everything is below zero"*; the mutant was watched dying. ✅ **AND DRILL-DOWN IS IN — the one that turns a report into an answer.** A new **Sales** report lists every sale in the range across **every till** (looking for a sale you did not ring up is the usual reason for looking), and tapping a row opens it: lines with their VAT and their discounts, how it was paid **including change given**, what has already been refunded or voided **and why**, and the totals. ⚠ A refund is itself a sale with a **negative** gross and is shown as one. ⚠ **Read-only** — refunding stays on the till screen where the ceiling and the cross-till cap live; a second door to the money with none of that behind it is not a convenience. ⚠ `TillTable` gained an **opt-in** row tap (default null), so the four tables that predate this cannot change behaviour, and only rows carrying a `DrillSaleId` do anything — a tap on a VAT bucket is inert **and silent**. ⚠⚠ Found on the way: `GetAsync<T>` in the shared client **never caught `JsonException`**, hidden because every report until now deserialised an OBJECT and `{}` is valid for one; the first caller to want a JSON **array** threw, and the test written for exactly that (`No_report_throws_when_the_server_answers_with_nonsense`) caught it. ⚠ Fixing it too widely then broke `A_dead_network_is_an_outcome_not_an_exception` — a dead line must carry its REASON — so the catch was narrowed to deserialisation alone. **Two tests I wrote caught two of my own mistakes in one slice**  | **done**, inside the 2–3 d estimate |
| 5b | ⚠ **"I thought we had built this where reports approved in the portal are pushed to each till version?"** | ⚠⚠ **ANSWERED 2026-08-18, AND IT IS A NEW WORK PACKAGE — NOT A §5c SLICE.** Matt: *"Portal shows which reports a till can show. Separate permissions need to be created for viewing them."* **Both halves, and they are independent.** ⚠ **(a) THE PORTAL CURATES THE SET.** A till shows what the portal has published to it, so `ReportCatalogue` stops being the answer and becomes **a superset the portal chooses from** — on every till. Needs: storage for a published set (per tenant, overridable per till), an endpoint the till reads on the cadence, a portal screen, and both clients consuming it instead of rendering their whole catalogue. ⚠ **(b) EACH REPORT GETS ITS OWN PERMISSION.** Today all eight share `portal.reports.view` / `pos.reports.view`, which is exactly why widening that gate for a Supervisor widened it for **every** report at once — and why the same gate defect has now been fixed **four** times (`/api/v1/sales`, `/reports/summary`, the five report endpoints, `/stock/levels`). Needs new `PermissionCatalogue` entries, `RbacSeeder` defaults per built-in role, and the endpoints re-gated one by one. ⚠⚠ **A QUESTION THE RULING SETTLES WITHOUT BEING ASKED**: a till *published* a report its operator may not *read* must show **nothing**, not a refusal — **the publish decides the menu, the permission decides the door.** A greyed-out row would leak what other roles can see. ⚠ **Nothing is built yet, and deliberately so**: half a permissions model is worse than none, because the gaps look like grants. ⚠ Sizing now that it is understood: **(b) ≈ 1–1½ d** (mechanical, and it makes the four-times-fixed gate defect structurally impossible), **(a) ≈ 2–3 d** across backend, portal and two tills. **Do (b) first** — it is smaller, it is the security half, and (a) without it just hides reports rather than protecting them. ⚠⚠ **(b) IS DONE — 2026-08-19.** Seven new codes in `PermissionCatalogue`, the shared rule in `SharedKernel/ReportPermissions` with its C2 twin `reportPermissions.ts` (31 vectors both sides), both tills filtering their own menus (till 1.95.0 → 1.96.0, web 1.19.0), and **the nine endpoints re-gated** so the door agrees with the list — backend 1.17.9. ⚠ The re-gate was done from a full endpoint enumeration rather than from memory, which mattered: `sales/{saleId}` carried a third code (`pos.refund`, for a supervisor doing a return) and a mechanical sweep skipped it silently. ⚠ Two apparent asymmetries in the enumeration were VERIFIED rather than "fixed" — `items-sold.csv` and `export.csv` are portal-only and correct, because **no till calls any `.csv` report endpoint**. ⚠ Three endpoints keep the master key and no specific code on purpose (`vat-integrity`, `stock/levels/bulk`, `cash-events`): `ReportPermissions` names no report key for them and inventing one is how the list and the door come apart again. ⚠⚠ **The four-times-fixed gate defect is now structurally impossible**: `ReportGateTests` (architecture suite, 3 tests, mutation-checked both directions) fails if a report is gated without its code OR if a code opens no endpoint. ⚠⚠ **(a) IS DONE TOO — 2026-08-19, and 5b IS CLOSED.** `SharedKernel/ReportCatalogue` is the superset (8 entries, key + label + blurb) and owns the one question that mattered: **what does silence mean.** A `ReportPublication` row is `(TenantId, TillId?) → KeysJson` with the more specific winning; `ReportPublicationController` serves the catalogue, a till-readable `published`, and a `portal.company.manage` GET/PUT/DELETE that is audited; both tills filter on **published AND readable**; the portal screen is **Locations  **(a) IS NOW DONE TOO — 2026-08-19.** | **(b) ✅ DONE 2026-08-19 · (a) ≈ 2–3 d, not started** | tills → Reports on the tills**. Backend 1.17.10, portal 1.11.0, till 1.98.0, web 1.20.0. ⚠⚠ **NO STORED ROW MEANS EVERY REPORT** — a tenant who has never opened the screen sees yesterday's menu, and defaulting to "nothing published" would have emptied the Reports tab in every shop on deploy. An **empty** list is a different state (deliberately publishing none), which is why never-chosen is `null` and not `[]`. ⚠ Three fallbacks and the last is "everything": server → cached last-known-good → full catalogue, because a till that loses its Reports tab to a network blink is worse than one showing a report an owner hid. ⚠ Rebuilt on APPEARING, not only in the constructor (pitfall 17), and only when it changed — re-filling the picker resets the selection. ⚠ Both clients move the operator off a tab that has just been withdrawn. Pinned by `ReportCatalogueTests` (11) + `publishedReports.test.ts` (12) on the same questions, with a C2 row for the three implementations. **Hand-run §G54, and §G54a is the one that must pass first.** | **✅ 5b CLOSED 2026-08-19 — both halves** |
| 6 | **Loyalty on MAUI is unusable** | ✅ **DONE 2026-08-18 (till 1.89.0) — and it is now the WEB TILL'S screen.** Matt: *"Ensure the webtill and maui are inline."* So the web till's `LoyaltyPage.tsx` was read first and MAUI matched to it, rather than the other way round. ⚠ **Columns went from three to the web till's six, in its order**: Customer (with the **email under the name**, exactly as it renders it), Member no., Tier, **Discount**, **Renews**, Credit. The two that were missing are not decoration — *"why didn't they get their 10%?"* cannot be answered from a screen showing neither the rate nor the renewal. ⚠ Empty cells are **"—"**, the web till's `<span className="muted">—</span>`: a blank reads as a screen that failed to load. ✅ **EDIT EXISTS AT LAST** — the web till has had a per-row Edit since it was written and MAUI had none. ⚠⚠ **It was absent ON PURPOSE and the ruling removed the reason**: `PlutusApiClient`'s own header said it *"deliberately exposes no customer edit"* because an email change was thought to redirect an account. Matt, 2026-08-18: *"A customer needs to have a unique ID, because people can change emails over time. Audit please."* Identity is `Customer.Id`; nothing resolves a customer by email; the server records `before`/`after`. ⚠ **A row TAP, not a button column** — the web till has room for a per-row action and a till screen does not; hand-run 1 proved a gesture fires inside a `ViewCell`. Silent for an operator without `customers.manage`, because the web till simply does not render the button for them. ⚠ `prefillWithPlaceholder: true` puts the current values in as **real editable text** — without it they are grey hints, `InputResults` reads `entry.Text`, and changing only the phone would submit an empty name. That is **finding K** (*"I can ONLY change the tax"*), and this is the first edit form written since it was fixed. ⚠ **The tier stays a separate action**, unlike the web till's combined dialog: `InputAlert` has no picker, and the web till's own comment explains why create-then-tier must remain two separately-gated calls. Same capability, one more tap — **parity in FUNCTIONALITY** (Matt, 2026-08-17). ⚠ `UpdateCustomerAsync` added to the shared client, which never had one | **done** |
| 7 | ⚠ **Nothing updates unless you navigate away and back** | ✅ **DONE 2026-08-18 (till 1.83.0).** ⚠⚠ **This was the THIRD report of one fault, and the first two fixes are why it came back.** 2026-08-11: *"The open float was 'Waiting' and never updated. I navigated away and back onto the cash tab and it had updated."* Then finding N: today's takings read at sign-in, a day of trading never moving them. Then this. Each fix was correct **and local**, so the next screen inherited nothing — and pitfall 17 already *said* "when you find one stale screen, go and look for its siblings straight away", which nobody did. **A documented pattern was the thing that failed**, so it is now a shared class: `Services/Sync/LiveScreen.cs`, two lines in a page constructor, no `OnAppearing` override and nothing to remember to unsubscribe. ⚠ **The screen that could not update AT ALL was Store Information** — loaded in its constructor, no refresh of any kind, so a portal correction could not reach the till until somebody signed out and back in. That is almost certainly the concrete case behind the report, and it is the same screen whose opening hours were argued about the day before: **a till that cannot be shown a corrected value is indistinguishable from a portal that never saved it.** ⚠ Cash and Statistics were **rewritten onto the shared class though they already worked** — so that the rule has no exceptions, which is the only thing that stops the fourth report; §G39b is the regression check on those two. ⚠⚠ **A judgement call worth challenging: the long tables deliberately do NOT tick** (`onCadence: false` — Items, Loyalty, Reports). Rebuilding a 500-row `ObservableCollection` every 60 seconds sends a `CollectionView` back to the top under the operator's hands: **a worse fault than the staleness, and a self-inflicted one.** They still reload on arrival and on an explicit Search. ⚠⚠ **NONE of this is machine-testable** — a MAUI `Page` cannot be *constructed* in the test project (`BindableObject` needs a live WinUI3 dispatcher; it is why 3 tests are skipped), so `LiveScreen` has no unit test and cannot have one. **§G39 is the only check that exists**, and it is written knowing that |
| 8 | **"Choose bag item" in Store Information** | ✅ **DONE 2026-08-18 (till 1.94.0).** It was **misfiled, not mysterious** — the web till keeps the same setting in **Settings** (`prefs.ts bagBarcode`, Settings → Till), and it belongs there: which carrier bag *this machine* sells is a per-DEVICE preference, while Store Information is read-only and about the SHOP. Moved to **Settings → Till** as *"Quick-sell bag item"*, still gated `pos.settings.manage`, still validated against the **v2 catalogue** (the same list a scan resolves against — validating against a different one is how a setting is accepted here and fails at the counter). ⚠ The original command is **deleted** from `StoreOptionsViewModel`, not left behind: two copies of one rule is the drift C2 exists to prevent. ⚠ It also gained the finding-K fix on the way — the current barcode now prefills as **real editable text** rather than a grey hint. ⚠⚠ **AND SUPERSEDED THE NEXT DAY (2026-08-19), which does not make it wrong.** The setting was misfiled *and* the setting itself was: Matt asked for carrier bags to be created in the **portal** and pushed to every till, so "Quick-sell bag item" is gone from MAUI entirely along with the web till's `prefs.bagBarcode` twin. Consolidating both tills' bag setting into one place each is what made deleting it from both a small change rather than a hunt | **done, then superseded** |
| 9 | **Settings must match the web till** | ✅ **DONE 2026-08-18 (till 1.94.0).** Matt: *"most of the MAUI Plutus tab would move into settings"*. ⚠ **The section NAMES are now the web till's** — `SettingsPage.tsx` has Till, Checkout, Printer, Hardware, Database, Till device, Environment; MAUI had Printer, Checkout and Help, so the two screens shared almost no vocabulary. **Till** comes first, as it does there. ✅ **THE "PLUTUS" TAB IS GONE**, and the web till has no such tab either — its equivalent is the **Till device** section. ⚠⚠ **The SCREEN is not deleted, only un-tabbed**: `OpenTillDeviceCommand` pushes the same `ConnectionView` modally, so the enrolment flow and the five diagnostics survive intact. Flattening them into a button list would have lost the thing that makes them useful — a failure pointing at ONE layer rather than at "the network". ⚠ **The old argument for the tab did not survive contact**: it said this is *"the screen someone opens when the till is NOT working, and it has to be findable"* — but the tab bar is only built AFTER sign-in, so a till too broken to sign in never showed it. Enrolment before sign-in has its own route. ⚠ **A live fault fixed on the way**: the section headings were hard-coded `Colors.LightGray` — the same near-invisible grey that made Store Information unreadable in 1.74.0. They take `ThemeInkMuted` now, so muted is a role rather than a colour somebody typed. ⚠ **What is NOT done**: MAUI's Settings is still a **button list**, while the web till's sections hold inline controls (toggles, live values, a receipt preview). The vocabulary matches; the interaction does not. That is a further ~1 d and it is honest to say so rather than tick the row clean ⚠⚠ **AND THE INLINE-CONTROLS HALF IS NOW DONE TOO — 2026-08-19 (till 1.99.0), so item 9 is CLOSED.** The two checkout options are **switches** with the sentence explaining each, built inside their own section by the same loop; "Quick-sell bag item" and "Receipt printer" carry a muted **live value** under the button that changes them, worded as the consequence rather than the field state ("No printer chosen — receipts print as PDF"). ⚠⚠ **AND IT FOUND AN UNGATED SETTING**: both checkout options were plain `DisplayAlert` yes/no prompts with **no permission check at all**, so any cashier could turn the receipt prompt off or tell the till it had no cash drawer — while the web till has always disabled them without `pos.settings.manage`. The switches are now **disabled, not refused**, with the reason under them: a control that cannot move tells a cashier where they stand before they touch it, where a dialog that says no afterwards teaches them the screen is unpredictable. ⚠ The three gated BUTTONS still refuse on press, because a button gives nothing away by being pressable. ⚠ The two superseded commands, their fields and their `Execute…` methods are **deleted, not left with a note** — an unreferenced `Command` on a viewmodel is indistinguishable from one whose binding has a typo, which is how `StoreOptionsViewModel` kept `AddEmployeeCommmand` looking live for months. **Hand-run §G55; §G55b is the behaviour change to check.** | **✅ CLOSED 2026-08-19 — names in 1.94.0, interaction in 1.99.0** |
| 10 | ⚠⚠ **Colour customisation reaches the WEB till and barely touches MAUI** | ✅ **DONE 2026-08-18 (till 1.91.0).** ⚠⚠ **I read the LIVE theme before writing anything, and it explained the symptom exactly**: `Kapow Test` is `baseMode: "dark"` with `{"accent":"#337061","line":"#2c3a4d"}`, assigned at **Scope 0 (tenant-wide)** — so it always did reach MAUI, and "assigned to the wrong till" was never the answer. ✅ **First**, `Styles.xaml` (1.88.0) gave the app implicit styles for `ContentPage`, `Label`, `Entry`, `Editor` and `Button`, so a scheme reaches every screen rather than the two files that read a slot before. ⚠⚠ **Then two real gaps that only a live theme reveals.** ① **THE STOCK PALETTE HAD NO DARK HALF** — `Colors.xaml` defines one set of seven values and they are the light ones. A scheme asking for **dark** while setting only an accent therefore got `UserAppTheme = Dark` **and `ThemeSurface = #ffffff`**: white pages, dark chrome, a scheme that looks like it did nothing. ⚠ A partial scheme is the NORMAL case — the portal lets you set one slot. `Theming` now carries a dark stock table and falls back to it when the base mode is dark; the scheme always wins, ink and surface flip together, and the **accent keeps its hue in both modes** because it is the brand. ⚠ `Unspecified` keeps the light stock: the device's mode is not knowable there, and guessing would invent a decision nobody made. ② **NOTHING READ `ThemeLine`** — half of what this shop actually chose was landing in a dictionary no control looked at. **A slot the portal offers and no till renders is a setting that lies to whoever sets it.** `TillTable` now draws its heading rule from it, which also makes headings read as headings rather than as a first row. ⚠ Guarded by `XamlResourceTests`: a referenced-but-undefined resource, or a theme slot reached by `StaticResource` (frozen at parse time), fails the build. ⚠ **What is deliberately NOT themed: dialogs' own hard-coded white is gone, but receipts remain immune** (till-design C1) — printing from a dark scheme once put near-white ink on paper | **done** |
| 12 | ⚠⚠ **SCHEDULED DISCOUNTS — "Wednesday Warhammer", and Select all** *(new work, 2026-08-20)* | ✅ **BUILT 2026-08-20 (till 1.108.0 + web 1.27.0 + portal 1.14.0 + backend 1.18.0).** The plan is [`Discount plan.md`](archive/Discount%20plan.md); this row is the MAUI half of it. ⚠⚠ **THE FINDING THAT JUSTIFIES THE ROW: a category-targeted rule would have matched NOTHING on this till, silently.** MAUI's basket carries a legacy `ItemModel` whose `CatId` is an **int** into the NatApp `Categories` table — empty and permanently so on a portal till — while the v2 catalogue's category is a Guid. So the rule would have been "working" on both tills and answering "no category" for every item in the shop on one of them. That is the exact shape of the Gold-member money difference (step 27), and it was found by asking where the id actually comes from rather than assuming the model had room for it. `BasketItem.CategoryId` now carries it, set at **both** add doors from `ItemLookup`. ⚠⚠ **`MemberDiscountBasket` IS DELETED, NOT LEFT BESIDE THE NEW ONE** — `AutoDiscountBasket` supersedes it (one automatic discount became a SET: two rules on two categories, with the member's tier out-bidding one and not the other, is two alterations). Two divergent paths for one job is the drift C2 exists to prevent, and every vector from its 16 tests is ported into `AutoDiscountBasketTests` (22) because each one records a trap. ⚠ **The rebuild identifies its own work by a FLAG, not by discount id**: a rule's id is a real catalogue id an operator can pick by hand off the Alterations list, so matching on id would overwrite the operator's own choice. ⚠ Clearing an automatic discount **waives it for that line** — detected as a removal seen while the rebuild flag is DOWN, since ours all happen inside it. Without it the promise "you can always charge full price" lasts one scan. ⚠ 🟡 until a person runs **§G64**. | **done** |
| 11 | ⚠⚠ **A long basket pushed the Checkout buttons off BOTH tills** *(new finding, 2026-08-20)* | ✅ **DONE 2026-08-20 (till 1.107.1 + web 1.26.1).** Matt: *"if you add many items, the buttons 'Checkout etc' go off the bottom of the screen. The buttons always need to stay and the items need to become 'Scrollable'."* ⚠ **One fault, two mechanisms.** MAUI: the selling screen was a vertical `StackLayout`, which measures children UNBOUNDED — the basket ListView grew with its content and carried the totals + button grid below the fold. Now a `Grid RowDefinitions="Auto,Auto,*,Auto"` in `TillView.xaml`: the list sits in the star row, the ONLY thing on the screen allowed to give up height, so it scrolls inside itself and the buttons cannot move. Web till: `.shell` and `body` are `min-height: 100vh` — nothing bounded the document, so `.basket-grid`'s own `flex:1 + overflow-y:auto` (the intended design, already in the CSS) never engaged and the page just grew. Now `.shell.till-locked` (`height: 100dvh`) applied **on the Till tab only** — reports and inventory are meant to scroll as a page and keep doing so — with deliberate graceful degradation: no `overflow:hidden`, so a window too short for even the fixed parts falls back to body scroll rather than clipping Checkout out of reach. ⚠⚠ **Neither fix is machine-verifiable** (WinUI layout; browser flex) — suites after the change: MAUI **616 green**, architecture incl. `XamlResourceTests` green, and neither could have caught the fault either. **§G62 is the check**, written for both tills, and §G62b pins the Till-tab-only scope | **done, ~½d** |

### ⚠ The order I would work in, and why

✅ **Items 1, 4, 6a, 6b and 7 are done (2026-08-18)** — the first three lines below, plus item 4. What
is left starts at line 4.

1. ✅ **6's silent save** (½ d) — it is a data-integrity bug wearing a UX coat: the operator thinks a member exists. **Done**, and it turned out to be worse than reported: the save was 403ing for *every* operator (6a), and then there was nowhere left to save from (6b).
2. ✅ **1, Save/Retrieve buttons** (½ d) — cheapest real win, and it un-hides a feature that already works. **Done**, and it was hiding a crash on an empty list.
3. ✅ **7, refresh on appear** (1–1½ d) — it makes every other screen trustworthy, including ones already "done". **Done**, and the screen that could not refresh at all was Store Information — the one whose data Matt had just queried.
4. **2b, credit gating** (1–2 d) — money, and the rule is missing rather than misplaced. ⚠ **BLOCKED on a decision**: is store credit for an unknown customer permitted at all, or only with a supervisor and a recorded reason?
5. **5 + 5b decision, then 3, 9 + 10 together, 6-rest, 2** — largest last, and 2 is step 11b regardless. ⚠ 5b is also **BLOCKED on a decision**: does the portal control *which* reports a till shows, or only who may see them?

⚠ **10 goes WITH 9, not on its own.** Both are "make MAUI's screens look like the web till's", and both
edit the same XAML: converting a screen's colours to `DynamicResource` while you are already moving its
panels is most of one job, whereas doing them separately means touching every view twice.

**Total ≈ 15–19 days**, of which about three are money or data integrity and the rest is screens.
**≈ 2½ days of that are now spent** (items 1, 4, 6a, 6b, 7), leaving **≈ 12–16**, two of which cannot
start until somebody answers the questions in 4 and 5 above.

⚠ **Nothing here changes a Part B row to ✅.** Several rows go the other way — see the register.
⚠⚠ **And nothing in items 1, 6 or 7 could be unit-tested** — the suite went 598 → 598 across all of
it. Every one of those fixes is verified only by `Test Maui.md` **§G38** and **§G39**, both written for
a person and neither yet run.


## 0.3b ⚠⚠ The input-alert back-out audit (2026-08-18)

> ⚠⚠ **THE PREMISE OF THIS WHOLE SECTION WAS WRONG, CORRECTED 2026-08-19.** It says the helper returns
> **null** on back-out. It does not: `InputAlertHelper.ShowAsync` ends
> `return await popUp.PageClosedTask ?? new Dictionary<uint, string>();`, and because `await` binds
> tighter than `??` the back-out yields an **EMPTY DICTIONARY**. The method's own header comment says
> so. `InputAlert.CancelBut_Clicked` also calls `InputResults.Clear()`, so ✕ and Cancel agree with it.
>
> **What that changes:**
> - Guards must test **`Count == 0`** or a failed `TryGetValue` — **never `== null`**. The surviving
>   `== null` tests (`SupervisorPrompt.cs:43`, `LoginViewModel.cs:227`/`:269`) are **dead checks** that
>   only appear to work because a whitespace validator runs after them. `SupervisorPrompt` is safe
>   because it happens to check both.
> - ⚠ The warning below — *"do NOT fix this by making the helper return an empty dictionary"* — is
>   **inverted**. It already returns empty; changing it to null now would break the sites since written
>   against empty. **Leave the helper alone**, for the opposite of the stated reason.
> - ⚠ **`null` IS still the back-out signal for `DisplayActionSheet`**, which is a different MAUI API.
>   That contract is real; do not conflate the two.
> - ⚠ The line numbers in the table below are **stale**, and several ⚠ rows have since been fixed.
>   Re-locate by method name before trusting any row.
>
> ⚠⚠ **And the crash is not the worst outcome.** An absent key throws `KeyNotFoundException` — still
> fatal inside `async void` — but a caller that reads the empty dictionary via `FirstOrDefault()` gets a
> **zeroed struct and carries on**. On a cash movement or a price adjust that is a silent wrong number,
> which no crash log will ever show you.

`LaunchInputAlertAsync` returns **null** when the operator leaves a dialog without confirming —
tapping outside, Escape, or the Cancel button. Every ⚠ row below dereferences that null, most inside
an **`async void`**, so the exception reaches the dispatcher unhandled and **the till dies**.

> ⚠⚠ **RE-AUDITED AGAINST THE TREE 2026-08-19, AND "17 SITES" WAS WRONG — IT IS FIVE, OF WHICH ONE IS
> REACHABLE.** Twelve of the rows below had already been fixed since this table was written and nobody
> came back to strike them through. **Every call site was re-enumerated by grep, not by this list**, and
> each was read for a guard within 18 lines of the call — the earlier narrow-window sweep produced three
> false alarms of its own (`TillViewModel` return, `ViewAllViewModel:1349`, the checkout amount prompt
> all guard correctly, just further down than a 7-line window sees).
>
> **The verified state, 2026-08-19:**
>
> | Site | State |
> |---|---|
> | 20 of 21 input-alert call sites | ✅ **guarded** — `Count == 0`, a checked `TryGetValue`, or an explicit back-out branch |
> | `Helpers/Security/Authorisation.cs` (`RequestAuthorisedUserInput`) | ✅ **RETIRED 2026-08-19** — see below. It was the one *reachable* unguarded site |
> | `CopperTransferPlatform.cs` ×4 (192, 219, 292, 299) | ⬜ **unguarded and UNREACHABLE** — `ICopperTransfer` is registered in `MauiProgram` and **nothing resolves it**: no injection, no `GetService`. Left alone deliberately, exactly as `SliderAlert` was (till-design D4) — restructuring four legacy object-initialiser expressions for a path no operator can open is how dead code starts looking maintained. **Delete the tool or wire it up.** |
>
> ⚠⚠ **`RequestAuthorisedUserInput` COULD NEVER HAVE SUCCEEDED, and the back-out crash was the least of
> it.** Traced line by line: `authEmpId` was declared `default` and **never assigned**, under
> `while (string.IsNullOrEmpty(authEmpId))` — so correct credentials re-prompted **for ever**, and the
> only exit was cancelling the first box. The password prompt was passed `idElements` (`passElements` was
> built the line above and discarded), so it asked for an Employee ID while saying "password". The answer
> was read as `.First().ToString()` on a `Dictionary<uint, string>`, which yields the KeyValuePair's text
> — `"[1, secret]"` — so `Password.Verify` compared the wrong string and could not match. `.First()` on
> the empty back-out dictionary threw. And it verified against a local `EmployeeModel` row a
> portal-provisioned till does not have.
>
> ⚠ **Three comments in this codebase already said so** (`SupervisorPrompt`, `TillViewModel`'s override
> method, `StoreOptionsViewModel`) and it stayed wired to five live call sites regardless, reading like a
> working supervisor gate. **It now refuses immediately, with a sentence an operator can act on, and
> logs.** Every caller already treats `default` as "abandon the action", so refusing is the shape they
> were written for — and a readable refusal beats a spinner nobody can escape.
>
> ⚠⚠ **~~WP-A1 — MIGRATE THE FIVE CALLERS (~½ d)~~ — VOID, 2026-08-19. IT DUPLICATED §10 L3, AND EVERY
> ONE OF THOSE CALLERS IS UNREACHABLE.** I wrote WP-A1 the same day without checking the legacy-removal
> register, which already tracked exactly this — the failure mode CLAUDE.md warns about, one register
> short of a sixth document.
>
> **What L3 already said, verbatim:** *"the last callers (`AddEditViewModel` ×3, `ViewAllViewModel` ×1,
> **all now unreachable from the UI but still compiled**)"*, and *"`RequestAuthorisedUserInput` never
> assigns the id it returns, so its `do/while` re-prompts for ever and only Cancel escapes — supervisor
> override on this till has never once succeeded."* Both of my "findings" were on the page already.
>
> **Verified against the tree, 2026-08-19** — and L3's count is the right one, mine was wrong:
>
> | Call site | Reachable? |
> |---|---|
> | `AddEditViewModel.ExecuteCreate` / `ExecuteUpdate` / `ExecuteCreateCategory` | ⬜ **No** — `AddEditView` was hidden 2026-08-10 and `ExecuteOpenAddItem` carries *"⚠ Unreachable — 'Add item' has no button"* |
> | `ViewAllViewModel.ExecuteUpdateItemStock` | ⬜ **No** — `UpdateItemStockCommandArg` is declared and **bound to nothing**: no XAML, no other reference |
> | `SettingsViewModel.ExecuteChangeBarcodeType` | ➖ **Not a caller at all** — it sits inside a block comment (`/*` 899 → `*/` 964), which is why L3 counts four and I counted five |
>
> **So there is nothing to migrate.** Rewiring a supervisor-override flow through code no operator can
> open would be half a day spent making dead code more correct — and it would put a fifth caller on
> `SupervisorPrompt` that no test could ever reach.
>
> ⚠ **What the retirement DID buy** (2026-08-19, still worth having): the method can no longer hang the
> till or throw out of an `async void` if any of that code is ever reawakened, and it logs when it is
> reached. Its "Not supported on this till" alert is itself unreachable today — deliberately, as a
> backstop rather than a feature.
>
> ⚠ **The real work is L3, unchanged and still ordered after L2 and L4**: delete `Authorisation.cs`'s
> `IsAuthorised`, `LegacyCheck` and `RequestAuthorisedUserInput` **with their callers**, not before. That
> is Matt's call under Part 1's legacy-removal register, not a slice to be taken unilaterally.


⚠⚠ **THE TABLE BELOW IS THE ORIGINAL 2026-08-18 AUDIT AND IS SUPERSEDED BY THE BLOCK ABOVE.** Its line numbers are stale and twelve of its ⚠ rows are fixed. It is kept for the per-flow reasoning ("what does cancelling MEAN here"), not as a to-do list — **re-locate by method name and re-check before trusting any row**.

| File (method) | Line | Back-out handled? |
|---|---|---|
| `Till/TillViewModel` (`ExecuteAdjustItem`) | 771 | ✅ **fixed 2026-08-18** — `if (data == null) return;` |
| `Till/TillViewModel` (`ExecuteRemoveAll`) | 644 | ⚠ no |
| `Till/TillViewModel` (`ExecuteReturn`) | 957 | ⚠ no — **hands money back** |
| `Till/TillViewModel` (`ExecuteAttachCustomer`) | 1215 | ⚠ no |
| `Till/TillViewModel` (`ExecuteAddMember`) | 1381 | ⚠ no |
| `Till/TillViewModel` (`OfferToSellGiftCardAsync`) | 1764 | ⚠ no — **sells a gift card** |
| `Till/TillViewModel` (`ExecuteStoreTransaction`) | 2318 | ⚠ no |
| `Till/TillViewModel` (`ExecuteCheckoutTransaction`) | 2714 | ⚠ no — ⚠⚠ **the CHECKOUT path** |
| `Helpers/Security/Authorisation.cs` | 93, 125 | ⚠ no — **the security prompt** |
| `Cash/CashViewModel.cs` | 225 | ⚠ no — **drawer money** |
| `Inventory/Items/AddEditViewModel.cs` | 269 | ⚠ no |
| `Inventory/Items/ViewAllViewModel.cs` | 1106, 1349 | ⚠ no |
| `CopperTransferPlatform.cs` | 192, 219, 292, 299 | ⚠ no — legacy tool, least reachable |
| `SupervisorPrompt` · `LoginViewModel` ×2 · `SettingsViewModel` ×2 · `StoreOptionsViewModel` | — | ✅ already null-checked |

⚠⚠ **THE TRAP TO AVOID.** The tempting one-line fix — have the helper return an **empty dictionary**
instead of null — is wrong and dangerous: the six ✅ rows detect cancellation *by testing for null*.
`SupervisorPrompt` is one of them, so an empty dictionary would carry it straight past the check and
into reading an override password nobody typed. **The contract stays "null means backed out."**

⚠ **Each ⚠ row needs its own answer to "what does cancelling MEAN here"** — abort the sale, keep the
old value, leave the basket untouched — which is why they were not swept with a blanket `return` on
the night they were found. **~½ day**, including reading each flow.

⚠ Cheapest partial mitigation if the hand-run starts hitting these: add
`catch (Exception ex) { CrashLog.Write(…); }` to the `async void` methods, so a back-out **logs
instead of killing the app**, then fix the meanings properly afterwards.

⚠ **Why it was found at all:** giving the adjust dialog the Cancel button Matt asked for would have
taken a crash reachable only by clicking outside and made it **the obvious button to press**. The fix
surfaced its own hazard, which is the argument for auditing the call sites of anything you make more
discoverable.

---


## 26. Lessons this retrofit keeps re-learning

Written here because each one cost real time more than once.

1. **A step's VERIFY passing means its *logic* is right. It says nothing about whether a person can
   use the screen.** Steps 1–21 all verified green; the first hand-run found fourteen faults, six
   invisible to every automated test. Budget a hand-run into every step.
2. **Tested components are not a working feature until something calls them.** Seven found so far.
   Grep for callers before debugging the component.
3. **A stale ⬜ is as wrong as a stale ✅**, and it fails in the direction nobody watches: it makes the
   work look bigger, so it gets re-planned and possibly rebuilt.
4. **Check what already exists before estimating.** Cash looked like five days and the server was
   finished.
5. **A comment is not a pin.** `vatBandForTaxId`'s doc comment described the strict rule the code did
   not implement.
6. **A fix that changes where a value comes from can break a consumer that never changed.** The
   "Invalid Date" rendering was never touched; it simply began receiving a differently-shaped string.
7. **Ask the running server, and grep for the route string.** Both would have prevented a duplicate
   endpoint reaching production.
8. **Verify the artefact, not the process.** A bundle labelled `0.0.0` builds and serves perfectly; a
   20-byte backup logs `ok`; a migration named for three columns can contain only an index.
9. **A ruling in a table is not a specification.** Only the bodies are, because they are the only half
   anyone builds from.
10. ⚠⚠ **A fix in the wrong place can be worse than the bug it fixes.** Finding U: a crash at the
    payment prompt was correctly diagnosed and then guarded **twice**, a day apart, in two different
    files — and the second guard turned a crash into a **silent permanent hang** that also killed the
    scan box and every dialog in the app. **Before adding a guard, check whether the thing you are
    guarding already guards itself.**
11. **Green tests can coexist with a dead app.** `TenderLoop` had 19 passing tests while the till
    could not take a penny, because the fault was in the wiring, not the logic. **Coverage of a
    component says nothing about the seam that calls it** — the same lesson as the seven
    built-and-uncalled components, from the other direction.

---

