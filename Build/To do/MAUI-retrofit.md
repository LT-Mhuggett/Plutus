# The MAUI retrofit — the one document

> ## This replaces five documents. There is no sixth place to look.
>
> **Matt, 2026-08-12:** *"I need these documents MAUI-Cutover-Plan, MAUI-Retrofit-Plan,
> legacy-removal, MAUI-parity, maui-whats-left and what is relevant in the handover, consolidated
> into one MAUI retrofit document. I do not know why its splintered into so many."*
>
> | Folded in | What it held that survives here |
> |---|---|
> | `MAUI-parity.md` (2026-08-12) | The counted status, and the what-remains-first shape Matt asked for the day before |
> | `maui-whats-left.md` (2026-08-10) | The order of work and the reasoning for it; the built-but-uncalled record |
> | `To do/MAUI-Cutover-Plan-2026-08-09.md` | The 28 dependency-ordered steps with bodies, VERIFYs and binding defaults 10–18 |
> | `To do/MAUI-Retrofit-Plan-2026-08-07.md` | The WP bodies and DoDs, binding defaults 1–9, the risk register, the item-identity seam |
> | `legacy-removal.md` (2026-08-10) | L1–L10 in dependency order — §10 below, unchanged |
>
> All five are in [`archive/`](../archive/) with a banner pointing here. **Nothing was summarised away:
> every step body, DoD, binding default, pitfall and L-row is below in full.**
>
> ⚠ **Why they splintered, so it does not happen again.** Each one was written to answer a question
> the previous one could not: the retrofit plan answered *how do we build it*, the cutover plan
> answered *in what order*, `maui-whats-left` answered *how much is left*, `MAUI-parity` answered
> *where are we*, and `legacy-removal` answered *what comes out*. Every split was defensible on the
> day and the result was **four documents carrying a status board, disagreeing with each other** —
> the cutover plan still showed steps 23 and 25 unticked after both had shipped. **One document, five
> questions, one status.**

---

## How to use this page

| Where | For |
|---|---|
| **This page** | Everything about the MAUI retrofit: what remains, how to build it, what is done |
| [`till-design.md`](../till-design.md) **Part B** | The row-by-row capability register — which capability, on which till. ⚠ **A capability is not done until its Part B row is updated in the same commit** |
| [`till-design.md`](../till-design.md) **Part C2** | The drift register — **read before writing anything that computes money on a client** |
| [`Test Maui.md`](../Test%20Maui.md) | The hand-test script to give a person |
| [`handrun-2026-08-11.md`](../archive/handrun-2026-08-11.md) | The 2026-08-11 hand-run findings A–T with causes |
| [`repo-runbook.md`](../repo-runbook.md) | Build, test, migrate, deploy — read before writing code |
| [`../HANDOVER.md`](../../HANDOVER.md) | The living state: what is deployed, rollback tags, today's resume point |
| [`plutus-platform-architecture.md`](../plutus-platform-architecture.md) | **Wins on any design conflict** with this document |

**Authority order on a conflict:** architecture doc → `till-design.md` → this document.

⚠ **It lives in `Build/To do/` because it has unbuilt work in it**, which is the folder rule — and
**when the last step closes it moves to [`archive/`](../archive/) with a banner.** It does not become a
permanent standard. Anything in it that outlives the retrofit (a convention, a rule, a pitfall) gets
lifted up to `Build/` level — into `till-design.md` or `repo-runbook.md` — **before** it is archived,
per [`index.md`](../index.md). ⚠ That lift matters: binding defaults 1–18, §15's pitfalls and §16's
item-identity seam all outlive the retrofit, and archiving them unlifted buries them.

## Where it stands

**Counted from Part B, not estimated — 2026-08-12: 75 capability rows.**
**40 ✅ both tills · 15 MAUI ⬜ · 7 MAUI 🟡 · 5 where MAUI is AHEAD of the web till.**

| | |
|---|---|
| **Till build to run** | **`D:\tmp\plutus-till-1.51.0\Plutus.Frontend.AppClient.exe`** — unpackaged, no signing, just run the .exe. ⚠ **Six builds on 2026-08-13, each fixing what the next test found:** 1.48.0 could not take a sale (U) · 1.49.0 crashed on a card overpay (V) · 1.49.1 said nothing during a split payment (W) · 1.49.2 let a closed day take items from the item list (X) · 1.49.3 and 1.50.0 let a split-paid refund go on one card (Y). ⚠ **§A and §B are run through** (C needs two people) — **open: [Y](#1-open-faults--before-any-new-work)'s web-till half, and Z1–Z5** |
| **Versions** | till-maui **1.52.0** · backend **1.16.0** (deployed) · platform **1.30.0** · portal **1.8.0** (deployed) · till-web **1.7.0** (deployed) · agent **1.3.3** |
| **Deploy state** | ⚠ **Nothing MAUI-side is blocked on a deploy.** Every backend endpoint the remaining steps need is live on the test environment |
| **Suite** | Unit **907** · Integration **169** · Architecture **15** · AppClient **425** (+3 skipped) · web till **19** — all green |

**≈35–40 working days remain.** Two thirds of it is two items: **step 27 (12–15d)** and
**step 26 (8–10d)**.

---

# Contents

**Part 1 — what remains:** [1 Open faults](#1-open-faults--before-any-new-work) ·
[2 Small closers](#2-small-and-each-closes-a-real-inconsistency) ·
[3 The open steps](#3-the-open-steps-in-order) ·
[4 Smaller rows](#4-smaller-rows-that-ride-along-and-which-step-carries-each) ·
[5 Where the WEB till is behind](#5-where-the-web-till-is-behind-parity-runs-both-ways) ·
[6 The 15 ⬜ rows](#6-the-15-maui--rows-grouped) ·
[7 How long, honestly](#7-how-long-honestly) ·
[8 USER-VERIFY](#8-user-verify--what-only-a-person-can-close) ·
[9 Risks](#9-risks-this-document-does-not-solve) ·
[10 Legacy removal](#10-what-comes-out-afterwards--the-legacy-removal-register) ·
[11 Not on the list](#11-what-is-not-on-this-list-deliberately)

**Part 2 — how to work:** [12 Execution protocol](#12-execution-protocol) ·
[13 Binding defaults 1–18](#13-binding-defaults--the-decisions-already-made) ·
[14 Offline horizons](#14-how-long-a-cached-login-lasts-the-numbers-and-why) ·
[15 Pitfalls](#15-pitfalls-that-have-each-cost-a-session) ·
[16 Item identity](#16-item-identity--the-seam-with-the-translation-agent) ·
[17 Testing](#17-testing) ·
[18 The two MAUI apps](#18-the-architecture-decisions-restated)

**Part 3 — what is done:** [19 The one-line answer](#19-the-one-line-answer) ·
[20 The cutover steps](#20-the-cutover-steps--the-record) ·
[21 The work packages](#21-the-work-packages--the-record) ·
[22 VAT](#22-vat--settled-and-not-to-be-re-derived) ·
[23 The hand-run](#23-the-2026-08-11-hand-run--all-fourteen-findings-closed) ·
[24 Platform work alongside](#24-platform-work-that-landed-alongside) ·
[25 Where MAUI is ahead](#25-where-maui-is-ahead-of-the-web-till) ·
[26 Lessons](#26-lessons-this-retrofit-keeps-re-learning)

---
---

# PART 1 · WHAT REMAINS

## 1. Open faults — before any new work

### U — the till could not take a sale on 1.48.0. ✅ **FIXED IN 1.49.0** · ⚠ needs a hand-run

**Matt, testing 1.48.0:** *"Something has gone wrong, I now don't appear to be able to make a sale. I
click on Checkout, click cash or card, the box disappears and it appears to get stuck. Its not
popping the 'Amounts' box? I think this is what stopped the search working."*

⚠⚠ **He is right that it is one fault, and it also answers finding Q** (below). **It is a deadlock,
and the cause is two fixes for the same problem landing a day apart.**

**The chain, verified against the tree:**

| | What happens |
|---|---|
| 1 | `ExecuteCheckoutTransaction` sets **`IsBusy = true`** (`TillViewModel.cs:1197`) |
| 2 | The tender picker runs through `Modal.ShowAsync` (`:1276`), the operator taps **Cash** or **Card**, and it **releases the gate normally** — ✔ *"the box disappears"* |
| 3 | The loop calls `askAmount`, which wraps the amount prompt in **`Modal.ShowAsync` (`:1347`)** → the gate is **taken** |
| 4 | That calls `InputAlertHelper.LaunchInputAlertAsync`, whose private `ShowAsync` **wraps its body in `Modal.ShowAsync` AGAIN** (`InputAlertHelper.cs:85`) → `Gate.WaitAsync()` on a **`SemaphoreSlim(1,1)` the same flow is already holding** |
| 5 | ⚠⚠ **Deadlock, permanently.** The popup is never pushed — ✔ *"its not popping the Amounts box"* |
| 6 | The `finally { IsBusy = false; }` at `:1560` **never runs, because the `try` body never completes.** `IsBusy` stays `true` for the rest of the session |
| 7 | `ExecuteItemAdd` — the **scan box / search** entry point — opens `if (IsBusy) return;` (`:315`). So typing and pressing enter **silently does nothing** — ✔ *"this is what stopped the search working"* |
| 8 | And `Modal.Gate` is never released either, so **every dialog routed through `Modal` anywhere in the app is dead for the rest of the session**: discounts (`:904`), the return sale picker (`:676`), the unknown-scan offer (`:2026`), category management, the cash reason prompt, receipt reprint, the Settings printer picker |

⚠ **Provenance — and this is the lesson.** `Modal` was created on 2026-08-10 (`a51c1cd`) to stop the
COMException that closed the till, and it gated **inside** `InputAlertHelper.ShowAsync`, so every
input alert was already serialised. On 2026-08-11 (`976094a`), while fixing finding **D/E**
(*"something went wrong taking payment"*), the amount prompt was wrapped **at the call site** as
well — the comment there still asserts the prompt "was not" going through `Modal`. **Two fixes for
one problem, on a non-reentrant semaphore. A crash became a silent permanent hang, which is worse.**

⚠ **Only ONE call site double-wraps.** Every other `LaunchInputAlertAsync` caller in the app calls it
bare and is correct — checked all fourteen. So the blast radius is the payment prompt only, and from
there the whole session.

✅ **FIXED — till 1.49.0, built to `D:\tmp\plutus-till-1.49.0`, artefact verified at `1.49.0`.**

1. **The outer wrap is gone** (`TillViewModel.cs`, the `askAmount` callback) and the comment that
   asserted the prompt "was not" gated went with it — a wrong comment on a money path is how the
   second guard got added in the first place.
2. ⚠ **`Modal` is now re-entrancy-safe**, so this class of mistake cannot hang the app again: an
   `AsyncLocal<bool>` marks the flow that holds the gate, and a nested call **passes through** while
   the outermost call keeps ownership of the gate and the settle. `AsyncLocal` and not a `static bool`
   deliberately — a static flag would let a modal raised from a *different* flow skip the gate
   entirely, reintroducing the COMException the gate exists to prevent.
3. **`ModalGateTests` — 7 tests**, and the gate still serialises two independent flows, which is the
   half a careless re-entrancy fix would have quietly deleted.

⚠⚠ **Mutation-checked, and the mutation taught us something.** With the guard removed, the test file
did not fail — **it HUNG, and took the whole run down for ten minutes**, because the gate is a private
static semaphore and one deadlocked test strands every test behind it. Every test in that file now
carries a bound: the same mutation now gives **6 red tests in 39 seconds**, the first reading
*"a nested dialog did not complete within 3s — the gate deadlocked."* **A regression that hangs CI is
a regression nobody diagnoses.**

⚠ **Do not "fix" a future recurrence by raising `SettleMs` or by putting a timeout on the gate.** The
gate was never slow; it was held by a caller waiting on itself. A timeout would have converted this
into an intermittent 30-second stall and hidden it for months.

**AppClient suite 425 → 432, all green.** ⚠ **`TenderLoop`'s 19 tests passed throughout, before and
after** — the loop was never wrong. That is the argument for step 11b, made by the code.

**Still needs a person:** ring up a cash sale, a card sale, a split across two tenders, and a refund;
Cancel at both prompts; then **type in the scan box afterwards** and confirm search still works —
that last one is what proves Q is closed rather than merely explained.

✅ **A1, A2 and A3 passed on 1.49.0** (Matt, 2026-08-13). **A4 then crashed the till — see V.**

### X — a closed day refused the scan box and accepted the item list. ✅ **FIXED IN 1.49.3**

**Matt, hand-test A8:** *"A8 works that if you try to add an item from the till it stops it, but you can
add an item from inventory, add to till. This needs to be stopped as well, either the same message or
the add to till button greyed out with 'Till closed' next to it."*

**Confirmed and fixed.** The day-closed check sat inside `ExecuteItemAdd` (the scan box). Inventory →
"Add to till" sends the `AddToBasket` message, which lands in **`ExecuteItemAddArg`** — a different
method, guarded only by `IsBusy`. So a Z-closed till refused a scan and cheerfully accepted the same
item from the item list.

⚠ **This is the same class as finding B, one level up, and worth saying out loud.** B was "the gate is
at COMMIT, which is right for the ledger and far too late for the operator". The answer was a gate at
the door — **but only at one of the doors.** A rule enforced per-entry-point is a rule with a hole in
it. It now lives in one `RefuseIfDayClosedAsync()` that both paths call, and the next path to be added
has one obvious thing to call.

⚠ **It fails OPEN on a lookup error**, deliberately: refusing to sell because a local read threw would
turn a bad day into a closed shop, and the commit gate is still behind it.

⚠ **Matt's alternative — greying the button out with "Till closed" — is the better UX and is NOT what
shipped.** The message is honest but it still lets an operator get as far as pressing the button. Doing
it properly means the item list knowing the day's state and re-reading it when the day is reopened;
that is a small piece of **step 25's** screen rather than a one-liner here. **Captured, ~½d.**

### Y — a split-paid sale could be refunded entirely to one tender. ✅ **CLOSED 2026-08-13 on every surface** — rule, ingest, MAUI, web till

**Matt, 2026-08-13, hand-test B1:** *"I do not believe either till is taking into account the split
payment return? I can return an item that was just cash, and it only gives me the cash option. But when
I try to return an item that was split, it wants to put the full amount to that card. It needs to be
aware of how the payments were split for it to work."*

**He is right, and nothing anywhere catches it.** Traced 2026-08-13:

| Layer | What it does about it |
|---|---|
| **MAUI** | `OriginTenderTypesAsync` collects the **SET** of tender types the origin used (`types.Add(tender.TenderType)`) and passes it to `TillTenders.OfferedForRefund`. For a cash+card sale that offers **both** — correctly — **and then caps neither.** The amount prompt pre-fills the whole outstanding refund, so putting all £4.40 on the card is the path of least resistance |
| **Web till** | ⚠ **No origin-tender restriction at all** — grep finds none. It offers every method for a refund, so it is *worse* than MAUI, not merely different |
| **The server** | ⚠⚠ **`RefundRules` has no notion of a tender.** It caps the refund against the origin sale's **gross** and its recorded refunds — nothing per method. So ingest accepts it, the rollups accept it, and no report flags it |

⚠⚠ **The consequence is real money, in the shop's direction of loss.** A customer pays £2.00 cash +
£2.40 card. The refund goes £4.40 to the card. **The card is credited £2.40 more than it ever took, and
the £2 stays in the drawer** — the till balances, the customer is £2 up, and the shop is £2 down with
nothing in any report to show it. Reverse the signs and it is a way to walk cash out of a shop.

**The rule that is missing** (and it belongs in `SharedKernel` beside `RefundRules`, not in a screen):
> **A refund to tender T is capped at what T actually took on the origin sale, less whatever has
> already been refunded to T.** Refunding MORE to a method than it took can never be right.

✅ **AND THERE IS NO CASH EXCEPTION — Matt, 2026-08-13: *"If the card machine is down, we cannot refund
cards."*** That is now **binding default 19**. It was worth asking, because it costs a customer a second
trip; the answer is the strict one, because refunding card takings out of the drawer is the oldest till
fraud there is and an *honest* one empties the drawer just as effectively. **Do not re-litigate it in
code.**

**Size: ~2–3 days**, four pieces, **one done**:
1. ✅ **DONE 2026-08-13 — `SharedKernel.RefundRules` now carries the rule.** `RefundCapacities` gives
   what each tender may still give back (what it took, less what has gone back to it); `AuthoriseSplit`
   refuses a proposed split that overpays any tender, names the offender and says what it *could* have
   had. ⚠ **The sale-level cap falls out for free**: if every tender is within what it took, the sum is
   within what the sale took. **18 tests, mutation-checked three ways** — removing the cap, giving an
   unused tender unlimited capacity, and summing-vs-last-wins each fail named tests. Unit 909 → **927**.
   ⚠ **The mutation check earned its place immediately:** the first draft asked "did we find a capacity
   for this tender?" via `FirstOrDefault` on a **struct**, whose `default` has `TenderType = 0` — the
   same byte as `Tenders.Cash`. The tests passed anyway, because a later `TookPence <= 0` check happened
   to catch it. **A guard that only works because another guard is behind it is not a guard**; it is a
   dictionary lookup now. ⚠ **And one mutation attempt lied to me**: `if (false)` produced
   unreachable-code build errors that my grep filter hid, so a broken experiment read as "safe". **A
   mutation that does not compile is not a mutation** — always watch for `error CS` in the output.
2. ✅ **DONE 2026-08-13 — ingest enforces it.** `SalesIngestService.ValidateRefundCapAsync` now runs a
   per-tender pass beside its sale-level and per-item ones, and **quarantines (202)** a refund that
   overpays a tender. **3 E2E tests**, and mutation-checked: skipping the gate fails exactly the two
   that expect a quarantine and leaves the legitimate split passing. Integration 169 → **172**.
   ⚠ **Refund-only requests only** — a mixed basket's tenders take money IN for the sold lines as well
   as paying it out, so they are not the same kind of number; the sale-level and per-item caps still
   cover those. ⚠ **Capacities are POOLED across the origins a basket returns against**, and prior
   refunds are attributed whole, which can refuse slightly early when one refund spanned two sales.
   **Failing closed on a money path is the right way round**, and the alternative is arithmetic nobody
   could check at a counter.

   ⚠⚠ **THE MONEY IS NOW PROTECTED, THE COUNTER EXPERIENCE IS NOT.** Until piece 3 lands, a MAUI
   operator can still put the whole refund on one card: the till accepts it, the money leaves the
   terminal, and the platform **quarantines** it. That is strictly better than the silent acceptance it
   replaced — the evidence is preserved where the portal can see it, which is default 12's whole
   reasoning — **but it is a refund that looks done at the till and is not on the books.** Piece 3 is
   what turns it into a refusal in front of the customer, and it should not wait.
3. ✅ **DONE 2026-08-13 — MAUI refuses it at the counter (till 1.50.0).** `TenderChoice` carries a
   `CapPence`, **the loop enforces it** (a caller that pre-fills sensibly and then accepts whatever comes
   back has no rule in it — an operator can always type over a default), `askAmount` is told the smaller
   of the balance and the cap so the box pre-fills a number that will be accepted, and a new
   `TenderRefusal.OverTenderCapacity` gets its own sentence: *"That is more than card took on this sale,
   so it cannot all go back that way. Refund what this method paid, then pick the other one for the
   rest."* ⚠ **The wording has to name where the rest goes**, or the operator's next move is to hunt for
   a different card instead of splitting the refund. **6 tests, mutation-checked twice.** Unit 927 →
   **933**. ⚠ The cap binds on a **sale** too, not just a refund — a gift card holding £5 cannot take £8
   of a basket — same code path, opposite sign.
4. ✅ **DONE 2026-08-13 — the web till too (till-web 1.7.0, DEPLOYED).** It had **no origin-tender
   restriction of any kind**, so it was the worst of the three. Now: `refundCapacities` /
   `refundSplitRefusal` / `capacityFor` in `till/tendering.ts`, a refund offers **only** the methods the
   original sale used (a method with nothing left is not rendered — an operator should never be given a
   button that can only refuse), "rest" fills that method's remainder, and **Complete is gated**, not
   merely pre-filled.
   ⚠⚠ **AND WP15's PREMISE WAS STALE — the web till HAS a test runner.** `package.json` has
   `"test": "vitest run"` and `till/tendering.test.ts`already mirrors `TenderLoopTests` (added 2026-08-11
   with finding F). So this landed **pinned on both sides**: **12 new tests using the same vectors as
   `RefundTenderSplitTests`** — £2.00 cash + £2.40 card, £4.40 refused on the card, cash refusing a
   card-only sale — **31 web-till tests total, mutation-checked twice** (not comparing against the cap
   fails 3; treating an unknown wire name as Card fails 2). ⚠ **Update the C2 register**: this twin is
   now executed on both sides, which is the first one that can say so.
   ⚠ `tenderTypeFromWireName` mirrors `Tenders.TryFromWireName` and is deliberately **not** `api.ts`'s
   lenient `tenderTypeFor` — same reasoning as the .NET side.
   ⚠ The origin's tenders ride in **page state, not basket state**: basket state is what a PARKED basket
   serialises, and widening that shape would be a migration on every parked basket in the field for
   something the checkout needs for ninety seconds.
   ⚠ `SaleDetail.payments` gained an exact `pence` field — it already carried the tender name, but only
   the amount in **pounds**, and converting that back for a cap is a rounding argument at a counter.
4b. ✅ **DONE 2026-08-13 — a cross-till refund is capped at the counter too (till 1.51.0).**
   ⚠⚠ **And the server had been sending what was needed all along.** `GET /api/v1/sales/{saleId}` has
   projected `tenders` since it was written; **`SaleDto` simply had no property for them**, so the till
   could only read the split for its own sales. **No server change was needed — the data was there and
   nobody had asked for it.** That is the eighth-and-a-bit instance of this repo's most persistent
   pattern, one layer out from the built-and-uncalled components.
   `SaleDto.Tenders` + `SaleDtoTenders.TenderPairs()` in `Client.Core` (⚠ **not on the DTO** —
   `Contracts.Client` references nothing at all by design; it is the wire shape and only that), and the
   till falls back to the platform when a sale is not local.
   ⚠ **`Tenders.TryFromWireName` is STRICT where `FromMethodName` is lenient**, and the difference is
   money: the lenient one falls back to **Card** so a cashier typing an odd method name can still sell,
   but applied to a wire value that hands the card a refundable capacity it never earned. An
   unrecognised name is **dropped**, so a refund to it is refused. **5 unit tests + 1 E2E.**
   ⚠ The E2E asserts against the **real payload**, because the names are the enum's (`"Cash"`,
   `"GiftCard"`) and a unit test on a hand-written DTO would have proved nothing about what the server
   sends. Integration 172 → **173**.

⚠ **Also not deducted at the till: what previous refunds already put back on each tender.** Nothing
local records which tender a past refund went to, so MAUI's caps are what each tender *took*. A second
visit could in principle overpay one across two refunds — which is exactly the case the pooled server
gate refuses. **The till's job is to make the right thing easy; the platform's is to make the wrong
thing impossible.**

⚠ **And the parity register was wrong to be comfortable here.** "Refunds go back the way they were
paid" was ✅ for MAUI on the strength of finding G — which restricted the *set* and never the *amounts*.
**A ✅ earned by a partial rule is how a money hole hides in a register.**

### W — a split payment looked like it had swallowed the money. ✅ **FIXED IN 1.49.2**

**Matt, 1.49.1:** *"When I try to do a split payment. e.g. an item is £4.40, I press cash, put in £2, it
takes me back to the 'Card or cash' screen but doesn't tell me anything has been paid or there is X to
pay. I assume its not actually working."*

✅ **It WAS working.** `A_split_payment_accumulates_to_exactly_the_total` has pinned that since the loop
was extracted: three tenders of £1.00/£1.00/£1.30 on a £3.30 basket all land, and the sale posts once.
The £2 was held and £2.40 was outstanding.

⚠⚠ **But "it works and looks broken" is not a smaller problem than "it is broken", it is a worse one.**
An operator who cannot tell a working split payment from a swallowed £2 will stop using split payments
— or take the money twice, in front of the customer. **The title said only "Payment Method", on the
second pass exactly as on the first**, and the balance appeared one screen LATER in the amount prompt,
which is no use to someone deciding whether the till just ate £2.

**Fixed:** the tender picker now says what has been taken and what is left —
`Paid £2.00 — £2.40 left to pay`, and `Refunded … — … left to refund` on a refund. The first ask names
the amount too: `Payment Method — £4.40 to pay`.

⚠ **The figure comes from `TenderLoop`, not from the basket, and that is the point.** The loop adds the
card surcharge to what is owed, so a caller deriving `paid = myTotal − outstanding` would be right
until a tenant switched a card fee on and then **wrong by the fee — on a screen telling an operator how
much money they are holding.** `chooseMethod` therefore takes `(outstanding, paidSoFar)`.
`What_has_been_paid_accounts_for_a_surcharge_the_loop_added` pins exactly that case: £4.40 basket + 50p
fee, £2.00 taken → the second ask must report **£2.90 left, £2.00 taken**, not the £2.40 a
basket-derived figure would have given.

⚠ **Shape, not wording, is the residual gap.** The web till shows a standing **Paid / Remaining** pair
on one form (`CheckoutDialog.tsx`); MAUI asks in sequence, so the same two numbers ride in the title.
Same money, same vocabulary, different shape — **they converge when the checkout screen is rebuilt
(step 11b)**, and until then this is a Part B 🟡 rather than a ✅.

**Unit 907 → 909 · AppClient 434.** Till **1.49.2**, platform **1.30.0** (a `src/` library changed, so
both bump — nearly missed).

### V — overpaying by card crashed the till on 1.49.0. ✅ **FIXED IN 1.49.1**

**Matt, hand-test A4 on 1.49.0:** *"card overpay crashes the till."*

⚠ **This was NOT a regression from U's fix — it was hiding BEHIND it.** On 1.48.0 the deadlock stopped
you at the first amount prompt, so the refusal path had not been reachable since 1.47.0. Fixing U
exposed the next layer.

**The crash log named it exactly:**

```
2026-08-13 16:16:53  [UnhandledException]  System.Runtime.InteropServices.COMException:
   at Microsoft.UI.Xaml.Controls.ContentDialog..ctor()
   at Microsoft.Maui.Controls.Platform.AlertManager.AlertRequestHelper.OnAlertRequested(…)
   at System.Threading.Tasks.Task.ThrowAsync(…)          ← rethrown on the POOL, unobservable
```

**The cause: the dialog was built on the wrong thread.** `DisplayAlert` and `DisplayActionSheet`
construct a WinUI `ContentDialog` **on the calling thread**, and `Client.Core.TenderLoop` awaits its
three callbacks with **`ConfigureAwait(false)`** — which is *correct* for a shared library with no UI
to return to. So every dialog from the second pass onwards was raised from a **thread-pool thread**,
and WinUI refuses to build XAML there.

⚠ **Why a normal sale did not crash, because that is the confusing part and it is the whole diagnosis:**
`MopupService` marshals internally, so the amount prompt hops onto the UI thread and stays there; and
the viewmodel awaits `TenderLoop.RunAsync` **without** `ConfigureAwait(false)`, so the happy path is
back on the UI thread before it shows anything else. **Only the refusal path raised a dialog while
still on the pool** — hence A2/A3 passing and A4 killing the app.

⚠⚠ **And no `catch` could ever have caught it.** The exception is delivered by `Task.ThrowAsync` on a
pool thread, outside the checkout's awaited path. **Getting the thread right is the only fix; a
try/catch is not an alternative** — which is also why 1.42.0's *"something went wrong taking payment"*
was the same fault wearing a friendlier face.

**Fixed at the choke point:** `Modal.ShowAsync` now marshals its dialog onto the UI thread
(`MainThread.InvokeOnMainThreadAsync`), so **every** dialog in the app is thread-safe by construction —
including the action sheet on the *next* pass, which would have crashed immediately after this one.
⚠ The dispatcher is probed via `Application.Current?.Dispatcher` rather than by attempting the marshal
and catching: a retry-on-failure fallback cannot tell whether the delegate already ran, and could show
the same dialog twice.

⚠ **A dormant twin was fixed with it.** `chooseMethod` does `Basket.Add(feeLine)` for the card
surcharge — a bound collection, mutated from a pool thread on any pass after a refusal. **Kapow's
surcharge rate is zero**, so `SurchargeItem` returns null and nothing is added; it would have surfaced
on the first tenant that charges a card fee, as a crash nobody could reproduce here. Now marshalled.

**Suite 432 → 434.** ⚠ **The two new tests pin the off-thread path, NOT the marshalling** — there is no
dispatcher in the test host, so the delegate runs inline there by design. **That the dialog lands on
the UI thread is USER-VERIFY (A4)**, and saying so is better than a test that pretends otherwise.

| # | What | State |
|---|---|---|
| **Q** | ⚠⚠ **"I could cancel the item, but then searching stopped working."** (Matt, 2026-08-11) | ✅ **EXPLAINED 2026-08-13 — same root cause as U above.** Any checkout that reaches the amount prompt wedges `IsBusy` on, and every command guarded by it — including the scan box — then does nothing silently. ⚠⚠ **The original rule-out was wrong, and worth remembering why:** *"`IsBusy` stuck — ruled out, every set has a `finally`"* checked that a `finally` **exists**, not that the body could ever **reach** it. **A `finally` does not run when the `try` deadlocks.** ⚠ Closes only when U is fixed and a hand-run confirms search works after a completed sale |
| — | **Hand-run** | 🔨 **IN PROGRESS on 1.49.1.** Started 2026-08-13 on 1.48.0 and stopped at the first sale (U); **A1–A3 then passed on 1.49.0 and A4 crashed it (V)**. ⚠ Everything downstream of taking money is still **untested on this build line**: refunds, the drawer, X/Z with sales in it, the Cash tab's "(waiting to send)", today's takings. **[`Test Maui.md`](../Test%20Maui.md) §B**, from the checkout. Nine till builds have now shipped since a person last completed a shop day |
| 🟠 | **`LoginViewModel.EnsureStoreAsync` throws on EVERY sign-in** — `InvalidOperationException: Unable to track an entity of type 'StoreModel' because its primary key property 'Id' is null` (`LoginViewModel.cs:389`) | Caught and harmless; the screen it fed is now read-only off `StoreInfoCache`. ⚠ It also **creates the legacy `Database.db` on every sign-in**, which is what made the enrolment gate a one-way door. **Step 21 deletes it** — scheduled, not forgotten (also register row [L7](#l7--loginviewmodelensurestoreasync)) |

## 1b. Raised by the 2026-08-13 hand-run — ✅ ALL FIVE DONE the same day

| # | What | Where it landed |
|---|---|---|
| **Z1** | A visible door onto a refund | ✅ **Till 1.52.0.** A **"↩ Return an item"** button beside the scan box. ⚠ **It is not the web till's flow and the difference is recorded rather than papered over:** the web till starts from the SALE (a dialog finds it, then adds the line), MAUI starts from the BASKET (scan the item, then mark the line as going back). So the button drives the flow MAUI has, and when nothing is selected it says what to do instead of opening a dialog that cannot work yet. **Step 26's lookup is where the two shapes converge** |
| **Z2** | Opening hours, missing from MAUI entirely | ✅ **Till 1.52.0.** A read-only weekly table on Store Information, parsed exactly as the web till parses it (day key → spans, a missing day meaning **Closed**, not "unknown"). ⚠⚠ **WP6's DoD required this and step 20 was ticked without it** — a ⬜ wearing a ✅, which is the third this week. ⚠ Unparseable JSON reads "not set" rather than throwing: a store screen must never be the thing that takes a till down. ⚠ **If the web till also shows nothing, the hours are simply unset** — Portal → Locations & Tills → edit the store |
| **Z3** | Today's takings: say when it was read | ✅ **Till 1.52.0.** `· as at 16:32` on the line. It always refreshed (on appearing **and** on the 60s tick — verified in the code, not taken from a comment); what it could not do was let anyone tell a figure read five seconds ago from one read at sign-in. **On the number a manager counts a drawer against, that is not decoration** |
| **Z4** | Portal: amber the out-of-balance drawer tile | ✅ **Portal 1.8.0, DEPLOYED.** ⚠ Amber, not red: a drawer being out is a thing to look into, not a failure, and spending red here devalues it where errors live. Inline style rather than a new class, so there is no `stat-alert` for a second and third tile to dilute |
| **Z5** | Portal: stock adjustments its own tab | ✅ **Portal 1.8.0, DEPLOYED.** Inventory → **Items · Stock ledger · Stock adjustments · Categories · Bin**. ⚠ The report already existed at the bottom of the ledger page and Matt went looking for it and did not find it. It renders in **both** places: the reason for putting it on the ledger (somebody thinking about stock is already there) did not stop being true when it gained a front door. Deep-link `focus=adjustments` too |

## 1c. Captured earlier, kept for the reasoning

Each is real and none is a blocker. **The two portal items need the Mac** (no Node on Windows), so they
ship with the next portal build.

| # | What | ~ | Detail |
|---|---|---|---|
| **Z1** | **A visible "look up a sale / return" button on the MAUI till** | **½d** (rides step 26) | Matt, B: *"The webtill allows you look up returns and sales via a button next to the barcode entry bar. Is this set to be replicated within MAUI?"* **Partly** — the lookup itself is step 26's cross-till screen, but the **entry point** was never a row. Today MAUI's only door is a **right-click on a basket line → Returns**, which nothing on screen advertises; the web till has a plain button beside the scan box (`TillPage.tsx:390` → `ReturnDialog`). ⚠ **A feature reachable only by right-click on a touch till is a feature that does not exist.** Part B row added |
| **Z2** | ⚠ **Opening hours are missing from MAUI entirely** | **½d** | Matt: *"Opening hours is not reflected on the webtill or Maui."* Traced: the **portal can set them** (`StoresPage.tsx` → `OpeningHoursEditor`), the **server stores and serves them** (`StoresController`, `StoreInfoResult.OpeningHoursJson`), and the **web till renders them** (`StoreInformationPage.tsx` → `OpeningHours`). **MAUI has ZERO references to `openingHours` anywhere.** ⚠⚠ **WP6's DoD explicitly required "the per-day `openingHoursJson` as a read-only weekly table" and step 20 is marked ✅** — so this is a ⬜ wearing a ✅, the third such this week. ⚠ **On the web till the likely answer is that nobody has filled them in** — check Portal → Stores → edit before treating that half as a bug |
| **Z3** | **Today's takings: say WHEN the figure was read** | **~2h** | Matt, B3: *"does this refresh automatically? It only refreshed when I moved between tabs."* ✅ **It does refresh automatically** — `StatisticsView.OnAppearing` loads and subscribes to `TillCadence.Ticked`, `OnTicked` reloads, and `LoadToday` marshals its own redraw (verified, not taken from the comment). **But the tick is 60 seconds**, so anything less than a minute of watching looks like "only on tab change". ⚠ **The real gap is that nothing on screen distinguishes a figure read 5 seconds ago from one read at sign-in** — which is exactly what made finding N worth fixing. Stamp it: *"as at 16:32"* |
| **Z4** | **Portal: make the out-of-balance drawer tile AMBER** | **~1h** ⚠ Mac | Matt, A7: *"would be good to have the button/info amber to highlight it."* The tile reads **⚠ DRAWERS OUT OF BALANCE (3) · £135.95 short** in the same grey as every other stat, so the one number that wants a manager's attention looks like the six that do not |
| **Z5** | **Portal: stock adjustments as its own tab** | **~2h** ⚠ Mac | Matt, B4: *"stock adjustments needs its own tab e.g. Items, Stock ledger, stock adjustments, Categories, Bin."* The data is live (`GET /api/v1/stock/adjustments`, backend 1.12.0) and currently has no home of its own |

⚠ **Z4 and Z5 are the portal, and there is no Node on the Windows box** — they are written but cannot be
built or verified here. Both land on the next Mac build, per the runbook's frontend deploy section.

## 2. Small, and each closes a real inconsistency

| # | What | ~ | Why it matters |
|---|---|---|---|
| **W1** | **Reopen a Z-closed day on the WEB till** | 1d | MAUI has it (till 1.48.0); the web till does not. Matt asked for *"Web and MAUI"*. The two tills currently disagree about whether a closed day can be recovered — a supervisor on the browser is stranded until midnight. **Server side is done and live** (backend 1.15.0: `ZReopen`, a compensating event, and `CashDay.IsClosed` where the latest Z-mark wins) |
| **W2** | **Add item as ONE screen** | ½d | The EDIT screen was rebuilt as a single page (finding K, three attempts). **Add** still opens three questions then a form — the same shape that was wrong for edit. `EditItemPage` is written; this is a create mode on it |
| **W3** | **Portal screen for the expected till version** | ½d | `GET`/`PUT /api/v1/platform/till-release` is live and works; nothing sets it from a UI, so the heartbeat's update check cannot be switched on without curl. ⚠ The table is empty, which is correct — the feature is inert until a platform admin PUTs a version |
| **W4** | **Web till: roster + permissions on a cadence** (WP17.4) | 1–2d | ⚠ A disabled operator is signed out of MAUI within 60s; the web till has **no proactive check at all** — it signs out only when a request happens to 401 (`api.ts:50`), and login tokens are cached 12h with their permission set. Same change also stops it discarding the whole heartbeat response (`Locked`, `SyncNow`, `CatalogueCursor` are dead there) |

⚠ **W1 and W4 are web-till TypeScript, so they need the Mac** — there is no Node on the Windows box.
Every TS edit made here is flagged **NOT TYPECHECKED** and goes onto §8.

## 3. The open steps, in order

Step numbers are the cutover plan's and do not renumber — they are cited in commits, code comments
and Part B Notes. Line references in bodies are **2026-08-09 anchors, not gospel**: re-locate by
symbol name if the file has moved on (§12.6).

### Step 11b — reshape the basket · **4d** · ⚠ DO THIS FIRST

`BasketItem` to long pence, `BasketReturnItem` collapsed to an `IsReturn` flag, the nine
`is BasketReturnItem` type-tests, both Mapster configs, `BasketDataTemplateSelector`, and every XAML
binding onto those members.

⚠ **Why it is promoted, and it is no longer a tidiness job.** `ExecuteCheckoutTransaction` is a
~200-line `async void` holding the tender loop, cancel handling, the surcharge line, the change
calculation and the commit — **none of it exercisable without a UI host**. It is **the only cluster of
money-adjacent logic left in this app with no test coverage at all**; every checkout defect so far was
found by hand and several are pinned by nothing. It also unblocks [L6](#l6--the-legacy-models):
`ItemModel` is load-bearing in the till screen purely because the basket binds to it.

✅ **The tender half is DONE (2026-08-10).** `src/Plutus.Client.Core/TenderLoop.cs` owns the tender
sequence; `ExecuteCheckoutTransaction` now only ASKS (the two dialogs) and maps the answer onto the
sale model. **19 tests, mutation-checked three ways** (accepting a zero/wrong-way tender, charging the
surcharge per tender, letting a card give change — each fails a named test and only that test). It
closed two defects beyond the three it was written for, both **non-terminating loops** the original
could not express: a `0` tender was accepted and re-prompted for ever, and `paid > total` cannot mean
"wrong way" for a refund, where both numbers are negative — so **over-refunding walked straight
through**. `MaxConsecutiveRefusals` is the backstop so a mis-wired prompt cannot spin the loop.
⚠ **New C2 twin** — the web till has its own tender logic in TypeScript; it gained its first 19 tests
on 2026-08-11 while checking the split-payment finding.

**⚠ STILL TO DO — and the trap is silent:**

- The `BasketItem` → **long pence** reshape and the nine `is BasketReturnItem` type-tests.
- **The XAML binding inventory is already done.** The Till view binds `Name`, `Price`, `Tax`,
  `Quantity`, `Basket`, `SelectedBasketRecord`, `SaleExTax`, `SaleIncTax` through `IBasketRecord`
  (`Quantity`, `Name`, `Price`, `PriceExTax`, `Tax`) plus `BasketDataTemplateSelector`.
- ⚠⚠ **`Price`/`PriceExTax` are `decimal` and the rows format them `StringFormat='{0:C}'` —
  switching them to `long` renders £3.30 as £330.00 and NOTHING FAILS.** The reshape needs a display
  member and a hand-run of every row type: item, return, note, alteration.
- ⚠ **MAUI bindings fail silently.** A binding to a property that no longer exists renders blank
  rather than crashing. **Enumerate them first; check each renders.**
- ✅ The card-surcharge question this step once waited on is resolved: the fee is a real line against
  the provisioned `CARD-SURCHARGE` item, priced by `SharedKernel.CardSurchargeVat`, configured per
  tenant on the gateway settings row, applied by `CheckoutCommit.SurchargeItem`. The reshape needs no
  fee field and `BasketNote` never carries money.
- **Refund-only baskets** settle here (Part B row): `refundOnly` is already computed and drives the
  prompt wording and surcharge suppression.

**VERIFY:** the tender-loop tests stay green; a hand-run of cash, card, a split across two tenders, a
refund, and Cancel at both prompts. **Mutation-check anything touching money** (§12.5).

⚠⚠ **Finding U is the argument for this step, made by the code itself.** On 2026-08-13 the till could
not take a sale at all because the two dialogs and the loop were wired together wrongly — and
**`TenderLoop`'s 19 tests all passed throughout**, because they drive fake callbacks. The loop is
covered; **the seam between the loop and the UI is not, and that seam is where every checkout defect
has now come from.** Whatever this step does, it must leave that seam testable.

### Step 22 — WP7 theming · **3–4d**

*7a — the palette.* AppClient's `App.xaml` registers only a value converter — **no theme resources at
all**. Port ClientUI's `Resources/Styles/Colors.xaml` **verbatim** under the same `x:Key` names
(`Primary #272643`, `Secondary #ffffff`, `Tertiary #e3f6f5`, `Quaternary #bae8e8`, `Quinary #2c698d`,
`Error #FF9494`, the Cyan/Blue accent scales and matching `*Brush` keys) so bindings resolve
unchanged, author a `Styles.xaml`, merge both into `App.xaml`. Native OS chrome will never
pixel-match a browser; content and branding will.

*7b — the PUSHED theme.* A till does not choose its colours; it is told them.
`GET /api/v1/themes/effective` (`sales.ingest` — device **or** operator token) resolves
till > group > store > tenant > default **server-side** in `ThemeResolution`. So: call
`GetEffectiveThemeAsync` on app start and on the 60s cadence, caching `EffectiveThemeResult` (an
`IVatBandStore`-shaped Meta store) so an assigned scheme survives an offline restart. Map `baseMode`
∈ `system|light|dark` onto `Application.UserAppTheme`, and `colorsJson`'s seven tokens — `--accent`,
`--accent-ink`, `--surface`, `--surface-2`, `--ink`, `--ink-muted`, `--line` — onto the 7a keys.
**Same slots, same names, same resolution order as the web till**, or the two tills show different
colours for one assignment and the portal's preview is a lie. Clearing an override must restore the
stock palette exactly — that is what makes 7a the fallback rather than dead code.

⚠ **Receipts are deliberately immune to theming** (till-design C1). The web till pins `#111` on
`#fff` in its print block because printing from a dark scheme once put near-white ink on paper.
`PosPrinterManager` must ignore the theme entirely.

⚠ **Removes ClientUI from `Plutus.slnx`** (keep the directory) — **the port must land BEFORE the
project is dropped** ([L10](#l10--plutusfrontendclientui)), or the till loses its colour
scheme.

*DoD:* no hard-coded hex left in XAML; a scheme assigned to this till in the portal is applied after
one sync and survives a restart with the network off; a scheme assigned at STORE level reaches a till
with no till-level override, and a till-level override beats it; clearing every override returns the
stock palette; **a printed receipt is byte-identical under a light and a dark scheme**.
**USER-VERIFY:** visual, plus the receipt under both schemes.

⚠ **Cheap to carry here:** the announcements banner and pick-from-floor notices (§4) are one cadence
step plus XAML each, and this step is already touching the shell.

### Step 24 — WP8 Users screen · **3d**

Employee list/create + set password via the legacy `/api/Employee` and `/api/Auth/SetPassword` —
both still carry the web till, and **no client methods exist, so build them**. ⚠ Roles and effective
permissions stay **portal-side**: the MAUI parity target is the web till's smaller surface, not the
portal's. MAUI's current add-user command is a stopgap dialog reading *"not available in this version
yet"*.

**Also in this step:** move the roster off `FileOperatorStore` (a JSON file) onto
`TillDbContext.Operators` — declared, mapped, used by nothing. Its stated blocker (EF 3.1 vs 9.0.18)
**died with step 1**, and two roster stores is drift by construction.

*DoD:* the employee LIST renders from `/api/Employee`; a **set-password on an EXISTING employee**
takes effect on the next sign-in; an employee created on the till can sign in on the web till and
vice versa.

⚠ **Help / support tickets ride here** (§4) — `/api/v1/support/tickets`, gated `support.tickets`,
which every built-in role holds because a lone cashier with a dead till must be able to shout for
help. Needs the operator token, which is wired.

### Step 26 — WP11 reporting + cross-till lookup · **8–10d** · ⚠ a rewrite, not a port

MAUI's three Statistics viewmodels query local SQLite directly — **zero HTTP**. Even a pixel-perfect
copy of the web till's screens would show **one till's data** if built that way.

⚠ **The Statistics tab currently reads ZERO** for everything sold since step 11, because both reports
read the legacy local database and sales no longer go there. It carries a red warning saying exactly
that, and it is **warned rather than hidden** only because a till migrated from NatApp still holds
real history in that file and this is the only way to see it. This step replaces it and removes
[L4](#l4--till-side-reporting).

**Build:**
- `ReportContracts.cs` against `ReportsController`, cross-checked with `api.ts:502–563`. ⚠ The
  pounds/pence trap and `vatPence` vs `vatChargedPence` go **in the type names** (default 17):
  `summary-rich` is in **POUNDS**, everything else in **PENCE**.
- Point Summary at `/reports/summary-rich?from&to`, the bucket chart at `/reports/summary`, the VAT
  table at `/reports/vat`, `StockOuttakeViewModel`'s local join at `/reports/items-sold`, plus two
  screens MAUI has never had: `/reports/category-sales`, `/reports/best-sellers`.
- **Local SQLite is no longer read for any report.**
- Surface the **5000-line / 200-row server caps** as a visible marker.
- ⚠ Move viewmodel construction out of XAML (`SalesReportsView.xaml:16`) so they can be DI'd and
  tested.
- **Cross-till sale lookup** completes here: `GET /api/v1/sales` drilling into
  `GET /api/v1/sales/{saleId}` — the **only** path to another till's sale detail.

⚠ **The refund picker and the reprint screen are the same screen — build them together.** The picker
in front of the Returns dialog shows **this till's last 20 sales only** (`ListRecentSalesAsync`), so
goods bought at another branch still need the sale id typed. Reprint-from-a-past-sale has the same
shape and the same 🟡 for the cross-till case.

⚠ **Report series with no server answer are DROPPED, never locally recomputed** (default 17): the
pro-rated per-tender ex-VAT series and the per-day-by-tender breakdown go, gross-per-tender stays,
calendar bounds come from the queried range. Local re-derivation is C2 drift by construction.

**Also here: the portal-controlled receipt template** (Part B ⬜) —
`GET /api/v1/stores/{id}/receipt-template`, cached with the catalogue sync like the web till, so
header/footer lines and toggles are obeyed instead of `PosPrinterManager`'s hardcoded layout.

*DoD:* two tills each push one sale and either till's Summary for that business day shows the
**combined** figures; from till A, drilling into a `saleId` rung up on till B renders identical
lines/tenders/adjustments; **a refund on till A against a sale made on till B validates, caps at the
refundable remainder, and posts**; offline, a sale inside the rolling window still refunds and one
outside it is refused with a clear needs-connection message — never a silent acceptance; **a sale
rung up on till B REPRINTS from till A** (the half a customer actually asks for at the counter).

#### ⚠ Splitting reports per store and per till — it WORKS, with one catch Matt has parked

Verified against live data 2026-08-10: three sales rang up on Matt's till landed as `SalesRollups`
**StoreId 4 · TillId 019fe244… · 3 txns · £97.94**. So *"does all of this information get sent?"* is
**yes** — nothing needs to change on the till.

- **Per till is solid.** `SalesV2.TillId` is on every sale and is **server-authoritative**, derived
  from the enrolled device at ingest rather than trusted from the payload — a till cannot claim to be
  another one. `DeviceId` is there too, so two devices on one till are separable.
- **Per store works today** through `SalesRollups.StoreId`.

⚠ **But the store is DERIVED, not recorded.** `SalesV2` has **no `StoreId` column**;
`RollupProjection.ResolveSpineAsync` resolves till → store → company when the rollup is written. Rows
already written keep the store they were written with — **but `RollupRebuilder.RebuildAsync`
re-derives from the till's CURRENT store**, and a rebuild is exactly what you run after a projection
bug or to fold in migrated rows. **So: move a till between stores, then rebuild, and every historical
sale it ever took moves with it.** Yesterday's takings change shop. Nothing errors and nothing flags
it. Cheap to close now (stamp `StoreId` at ingest; have the rebuild read the stamped value) and
expensive later. **Matt's call, taken: no changes for now** — one store is live so the exposure is
nil. Recorded so it is a decision rather than a year-end discovery.

### Step 27 — WP12 loyalty, then WP13 gift cards · **12–15d** · ⚠ the largest single block

**WP12 — loyalty.** Confirmed **zero** in both MAUI projects; no backend work needed, pure
consumption. Customer search/attach on the sale screen (`GET /api/v1/customers?search=`, then a live
`GET /api/v1/customers/{id}` for balance and membership), a create/edit dialog gated
`perm:CustomersManage`, a store-credit tender mirroring the web till's synthetic `CREDIT_PAYID`, and
a management list off `GET /api/v1/loyalty`.

⚠ **Extract `MemberNumbers` from `src/Plutus.Customers` to SharedKernel FIRST** — it is a backend
module MAUI may not reference, and the Crockford check character is a *rule*. Member-card scan: a
code beginning `C` with a valid check digit routes to **customer attach**, not item lookup; a bad
check digit says so rather than searching for an item that will never exist.

**The hard part is offline design, and the rule is strict.** A bounded local `LoyaltyCache`
(CustomerId, Name, Tier, AutoDiscountRate, CreditBalancePenceAsOf, RefreshedAtUtc) serves offline
name/tier/discount lookup and a discount **hint** only — it is **never** an input to redemption
maths. The credit tender needs all three of: customer attached, live balance > 0 fetched **this
session**, device online. No live fetch, no tender — exactly as the web till behaves.

**WP13 — gift cards.** Sell = a `GIFT-CARD` catalogue line carrying the code; redeem = a tender. Both
**online-only**, like store credit. Management (minting, voiding, balance moves) stays portal-side.
`till/basket.ts` + `CheckoutDialog.tsx` are the reference.

⚠ **The VAT treatment forks the mechanics, and getting it wrong mis-states VAT silently:**
- **Multi-purpose** (Kapow's declared treatment): activation posts **ZERO VAT** — it is a liability,
  not revenue — handled by the provisioned `GIFT-CARD` item. **Do not invent VAT lines.** Redemption
  is a **tender**.
- **Single-purpose:** redemption is a **negative standard-rated `GIFT-CARD` line**
  (`api.ts:997–1019`), because the card's VAT was declared when it was sold and a plain tender would
  declare it twice.
- ⚠ **Replace the hardcoded `/1.2`** with the step 7 band cache — **on the web till too** (C2, flag
  NOT TYPECHECKED). Gift-card lines are exempt from ingest validation, so a stale hardcoded rate
  mis-states embedded VAT **without quarantining anything**.
- ⚠ **`GiftCardSettings` absent → 409** on generate/activate/redeem. Catch it and say *"Gift cards
  aren't set up for this company yet — an owner decides their VAT treatment in the portal first"*,
  never a raw error.

*DoD:* airplane mode — cached name/tier/discount still shows and the credit tender is **absent**;
reconnect → tender reappears with the live balance; two devices racing to redeem the last credit →
exactly one succeeds, the other gets `InsufficientCreditException`; scanning a member card attaches
that customer and applies their tier's auto-discount; a corrupted check character is rejected as a
bad member number, never treated as a barcode; sell → activate → redeem round-trips **under both
treatments**, asserting the single-purpose negative-line shape; the same flow on a tenant without
settings surfaces the friendly 409 at the first step; a redeemed card's remaining balance matches the
portal's view.

**Also here:** the **refund-only basket** Part B row's server half and the customer-attach-at-sale
row. Six of the fifteen ⬜ rows close with this step.

### Step 28 — online-first login · **2–3d** · hardening

Default 16: the **first sign-in of any account on a device must be ONLINE**. That first online login
mints a **device-local PBKDF2 verifier** (fresh salt) in the v2 store; offline sign-in verifies
against the local verifier. The roster keeps shipping hashes until both tills run verifiers, then the
server stops shipping them (flagged, separate change, **C2 row required**). A cold-start till — one
that has never been online — cannot sign anyone in, deliberately: it has no catalogue or prices
either. Horizons unchanged (§14).

*VERIFY:* a first-ever login offline is refused with "connect once" wording; after one online login
the same account signs in offline; a leaver deactivated in the portal is refused online immediately
and offline at the horizon.

**Also here:** the **connection status** row (network vs server vs revoked — `ConnectivityProbe`
exists and is shared) and the **app-update prompt** (`TillReleaseSettings` +
`PlutusVersion.IsOlderThan` are live and advisory only — ⚠ **there is no self-update for MAUI**, by
Matt's decision, so a till can say it is behind and nothing more).

### Step 21 — two pieces still open · **1½d**

⚠ **Step 21 is marked ✅ and is not finished, so it is stated here rather than buried.** Verified
against the tree 2026-08-12:

| Piece | State |
|---|---|
| Delete `SetupViewModel` + `TransferThirdPartyViewModel` and their views | ✅ **Gone** — `ViewModels/FirstTimeStartUp/` holds only `RecoveryViewModel` |
| Keep `RecoveryViewModel` as the cutover on-ramp | ✅ As planned ([L8](#l8--obsolete-first-run-screens)) |
| Delete `SettingsViewModel.ExecuteDeleteDb` | ✅ Gone |
| Fix `AppViewModel.EmployeeId` = `Employees.Last().Id` | ✅ **No longer crashes** — null is a normal answer now. Its *deletion* is [L9](#l9--appviewmodelemployeeid-and-appviewmodelemployees) |
| ⚠ **Delete `LoginViewModel.EnsureStoreAsync`** | ⬜ **STILL RUNS on every sign-in** (`LoginViewModel.cs:301` → `:348`) and still throws every time. **~½d** — step 14's Meta-cached store header already replaces it, so this is a deletion. ⚠ Two printing call sites reference its five paths in comments (`PosPrinterManager.cs:150`, `TillAgentPrinting.cs:104`) — check them, because **a missing store must not lose the receipt** |
| **Un-enrol request + manager approval** | ⬜ **~1d.** WP4's last piece; there is **no client code at all** (grep for `unenrol` in the app returns nothing). The server side is ready — `POST /api/v1/tills/unenrol-request` got its device-token policy in step 19. ⚠ **`PendingRemoval` is not a stop signal, deliberately**: halting a till the moment someone requests it back would make un-enrolment a way to take a shop's till down. Only `Revoked` stops, and the till learns which from `GET /api/v1/tills/devices/{deviceId}/status` — and it **must poll it**, because device tokens are bearer tokens with **no server-side denylist**, so a revoked till otherwise keeps working until its 12h token expires |

### The cluster with no step at all

| What | ~ | Detail |
|---|---|---|
| **Platform notices** — announcements banner, help tickets, app-update prompt, pick-from-floor | **3–4d** total | All small consumers on the existing 60s cadence, copied from the web till's shapes: `GET /api/v1/announcements/active` (Maintenance/Incident banner only — Info must not show), `GET /api/v1/notifications?unackedOnly=true` + acknowledge, and `/api/v1/support/tickets`. ⚠ **`NoticesClient` is built and appears in the entire AppClient once, in a comment** — it needs a cadence step *and* the XAML, which is why these rows were corrected from 🟡 to ⬜. ⚠ Tickets and pick-note acks need the **operator** token; a device token cannot pass a `perm:*` gate. **Cheapest carried on steps 22 and 24.** *DoD:* a seeded Incident announcement shows within a cycle and Info does not; an unacked pick-note persists across restart until acknowledged; a ticket raised on the till appears in the portal inbox and the reply comes back |

## 4. Smaller rows that ride along, and which step carries each

Real Part B gaps that do not need a step of their own. Listed so none is a surprise when its step
opens.

| Gap | Rides with | ⚠ |
|---|---|---|
| **Pick-from-floor notices** + **announcements banner** | 22 or 24 | Corrected 2026-08-10 from 🟡 to ⬜ — the row claimed "pending only the banner XAML" |
| **Portal-controlled receipt template** | 26 | ⬜ — no schema or parser exists in any client; it is WP3's business, deferred with reason |
| **VAT band on the sale line** (`LineMeta.vatBand`) | 27 | 🟡 only because the **server backfills** any line arriving without one (`VatBandStamp`). MAUI is correct-by-default; it needs to send one only where the till knows something the catalogue cannot — a single-purpose gift-card line is `"standard"` **by the voucher treatment**, not by its catalogue row. ⚠ **Leave it null rather than guessing**: a stated band is never overwritten |
| **Refund-only baskets** | 11b | Partly there — `refundOnly` drives prompt wording and surcharge suppression |
| **Un-enrol request + approval** | 21 | ⬜ |
| **Help / support tickets** | 24 | ⬜ — closes the `support-heavy` churn signal |
| **Connection status** (network vs server vs revoked) | 28 | ⚠ Runs the OTHER way too — the **web till** is 🟡, still on `navigator.onLine` (WP17.3) |
| **App-update prompt** | 28 | ⬜ — advisory only; no self-update exists |
| ⚠⚠ **Remote lock of a lost or stolen till** | **Neither till has it — and it READS as built** | `Device.Locked`/`LockReason` ship, `HeartbeatResult` carries them, `SyncClient` surfaces them — but **nothing sets the flag and nothing enforces it**. `IssueDeviceTokenAsync` refuses only on `Status == Revoked`. ⚠ **Reach for Revoked in a real incident.** Enforcement must land **before** any control that sets the flag, or the switch stays fake. Full detail: [risk 4](#9-risks-this-document-does-not-solve) |

## 5. Where the WEB till is behind (parity runs both ways)

| Row | WP | Note |
|---|---|---|
| **Offline sign-in with an expiry** | WP17.1 | MAUI has tiered horizons (`SharedKernel.OfflineCredentials`). The web till **cannot sign in offline at all** — so the shop that loses broadband loses the till, which is the thing the whole offline design exists to prevent. ⚠ Needs WP15's test runner first: the horizons would be a C2 twin |
| **Roster on a cadence + sign-out on disable** | WP17.4 | **W4 above** — the one that is not cosmetic |
| **Reprint a receipt for a past sale** | WP11 | MAUI has it (till 1.34.0), marked *"REPRINT — not a new sale"*. Web joins the reporting screen that already lists sales |
| **Connection status** | WP17.3 | Web is on `navigator.onLine` — the network interface, not the server. It says "online" in a shop whose broadband is down and cannot tell a revoked till from a dead one. Both tills should move to the shared `ConnectivityProbe` |
| **Reopen a Z-closed day** | **W1** | Server side is live; only MAUI has the screen |
| **Card surcharge** | WP15 | Built on MAUI 2026-08-09; the web till reads `charge`/`minimumCharge` off the legacy wire and ignores them. ⚠ **Kapow's rate is ZERO (confirmed)** and UK consumer surcharges have been banned since **2018-01-13**, so this is a latent trap for a future B2B tenant rather than a live discrepancy |
| **Every C2 twin's TypeScript half is unexecuted** | **WP15** | `package.json` has `dev`, `build`, `preview`, `typecheck` — **no test runner and no test files** beyond the 19 tendering tests added 2026-08-11. `VatLineMathTests` fixes .NET to the numbers `api.ts` produces and **nothing executes `api.ts`**; `LegacySaleBridgeTests` pins item-id derivation to a GUID the TypeScript produced in a 2026-07-24 smoke test; `till/basket.ts basketTotals` is a **third, entirely unpinned** copy of the discount apportionment. Add Vitest (same Vite toolchain, no new build concept) and port the .NET vectors across, then add the C2 row saying what now pins them. ⚠ **Needs Node → the Mac.** ⚠ **Matt's call on timing** — recorded here rather than left unowned, because a twin nobody tests is how two tills come to disagree by a penny on the same basket, for ever, on every VAT return, with nothing flagging it |
| **It shows every store's pick notes** | WP17.4 | `GET /api/v1/notifications` does **not** filter by store — it returns the tenant's 50 most recent — so addressing is the client's job and `App.tsx:130` applies no filter. Latent for single-store Kapow; wrong the moment a second store exists, and wrong in the expensive direction: the shop that *does* hold the stock sees the same note and may assume the other branch took care of it, so the web order ships short. MAUI is the strict one via `NoticesClient.IsForStore`. ⚠ One `.filter()` — but doing it client-side **creates** a C2 twin; **moving the filter to the server deletes it instead, and is probably the better answer** |

## 6. The 15 MAUI ⬜ rows, grouped

Not fifteen problems — **five clusters**, each already owned by a step above:

- **Loyalty / gift cards / customers** (6 rows) → **step 27**
- **Platform notices** — announcements, help tickets, app-update prompt, pick-from-floor (4 rows) →
  no step yet, **~3–4d**, cheapest carried on 22/24
- **Theming + portal-controlled receipt template** (2 rows) → **steps 22 and 26**
- **Users** (1 row) → **step 24**
- **Un-enrol request + manager approval** (1 row) → **step 21, ~1d**
- **Refund-only baskets** (1 row) → **steps 11b / 27**

## 7. How long, honestly

**≈35–40 working days.** Two thirds is **step 27 (12–15d)** and **step 26 (8–10d)**.

⚠ **That is BUILD time, not DONE time.** Every step up to 21 passed its VERIFY, and then the first
hand-run found fourteen faults — six invisible to every test here. **Add hand-running to every row,
and expect the screen to be wrong the first time.** Three separate attempts were needed to fix one
edit form.

⚠ **Treat the total as ±25%, and check before estimating.** On 2026-08-11 **three Part B rows still
said ⬜ for work that had already shipped** (the Bin, VAT bands, step 25e) — a stale ⬜ makes the gap
look BIGGER and gets it re-planned, re-estimated and possibly rebuilt. **A row is as wrong when it is
pessimistic as when it is optimistic. Grep for a ⬜ before believing it.**

⚠ **And check what already exists.** Cash looked like a five-day build and the server turned out to be
finished — the whole step was client-side wiring. **Seven components have now been found built, tested
and called from nowhere**: `OutboxPusher.DrainAsync`, the catalogue browse, `TillStore.SearchAsync`,
`NoticesClient`, `VatBandCache.RefreshAsync`, `OperatorSession.Token`, and a heartbeat version write
assigned on every beat and saved on none. **When a screen looks broken, grep for callers of the thing
that should be doing the work before debugging the thing itself.**

⚠ **The eighth, found 2026-08-13 while writing up the hand-test: `CrashLog.Directory`.** Its own doc
comment says *"Surfaced in the Plutus tab so nobody has to guess"* and **nothing calls it** — so on the
one day the crash log identified two faults in minutes, a tester still had no way to find the file from
inside the app. ⚠ **Note the shape of this one: the comment describes the intent, so reading the code
tells you it is done.** Same trap as `vatBandForTaxId` — a comment is not a caller any more than it is
a pin. **~10 minutes to wire into the Plutus tab, beside the log path it already knows.**

⚠ **The cheap wins are spent.** Almost everything delivered before 2026-08-10 was *wiring* —
components that already existed with no caller. What is left is screen-building, and it does not
compress the same way.

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

**Open on 1.48.0** — fixed in code, unproven on hardware. Full script: [`Test Maui.md`](../Test%20Maui.md).

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

## 9. Risks this document does not solve

Flagged rather than discovered mid-build. 1–3 are resolved and kept so nobody re-opens them.

1. ~~Existing local till data at enrolment~~ — **resolved by default 3**: archive, never merge, never
   delete. Residual risk is an operator skipping the archive step; WP4's first-run flow refuses to
   enrol while an un-archived legacy file exists.
2. ~~Receipt-print vs outbox-commit ordering~~ — **resolved, designed into WP3**: commit first, print
   from the committed payload. A print failure after commit is a reprint problem, not a money problem.
3. ~~Cross-till refund lookup~~ — **resolved, scheduled**: hard-gated part of step 26.
4. ⚠⚠ **Stolen hardware — and the kill switch is half-built, which is worse than absent.** Local-only
   operator login means a stolen till carries cached credentials, so an attacker has offline access up
   to that till's refund ceiling. Traced properly 2026-08-09 **because the pieces look like a solution
   and are not one**: `Device.Locked`/`LockReason` exist, `HeartbeatResult` carries them, `SyncClient`
   surfaces them as `HeartbeatOutcome.Locked`. Two things are missing and together they mean the
   feature cannot be used at all — **nothing sets the flag** (no endpoint, no portal control; `grep`
   for `Locked =` returns nothing outside the migration) and **nothing enforces it**
   (`IssueDeviceTokenAsync` refuses only `Status == Revoked`, so even once set the lock is *advice to
   the till*, and a thief running modified software ignores it). **A kill switch honoured only by the
   client is not a kill switch.** ⚠ **`Revoked` is the only thing that stops a device today — reach
   for that in a real incident.** *Deliberately not decided here:* who may lock a till, whether a lock
   is reversible and by whom, and what a locked till does with unsynced sales. **Small once decided:**
   a check in `IssueDeviceTokenAsync`, an RBAC-gated endpoint, the portal control beside the existing
   device-removal one. **Do the enforcement half FIRST** — an endpoint that sets a flag nothing
   honours repeats the mistake.
5. **GDPR, compounding 4.** WP12's `LoyaltyCache` holds customer PII. `RetentionSweeper` +
   `DeletionSchedule` handle erasure centrally and **nothing propagates that to purge a till-side
   cache**, so a deletion request could be honoured centrally while a copy persists on till hardware.
6. **Fleet updates are hand-waved.** `426 Upgrade Required` is handled client-side (banner, selling
   continues, sync parks) and the heartbeat's version check is advisory — but **how a binary update
   physically reaches hardware across many shop networks is unanswered**, and there is no self-update
   for MAUI by Matt's decision.
7. **Mixed versions in the field** during rollout — some tills outbox-wired, some not, both hitting
   the same backend.
8. **Card terminals are greenfield.** Grepped for Stripe/SumUp/Worldpay/Adyen/Zettle across the
   backend and both MAUI apps: **zero terminal-integration hits.** Not "blocked on a decision" —
   there is no terminal code to build on. The gateway-*awareness* layer exists and MAUI copies the
   display cheaply (WP14); only the terminal drive is greenfield. Affects both tills.
9. **703 uncompiled bindings in the MAUI till** — deferred deliberately, written down so it stays
   deferred rather than forgotten. Debug now validates XAML (`MauiXamlInflator` = `XamlC`), which
   closed the crash class where a bad property only failed when a human opened the tab. Making that
   visible surfaced 703 XC0022/XC0025/XC0103 advisories — **not new**: Release printed the identical
   703 all along, Debug had simply never looked. All one finding: bindings without `x:DataType`, so
   they resolve reflectively at runtime. **Nothing is broken by it today.** It matters when someone
   trims or AOTs the Windows head, because reflective bindings are exactly what trimming removes, and
   it will present as **pages blank in Release only**. ⚠ Not suppressed with `NoWarn` — silencing 700
   warnings the moment they become visible would undo the point of looking. The fix is mechanical
   (`x:DataType` per view, then `MauiEnableXamlCBindingWithSourceCompilation`) and belongs with the
   screen steps, **per view as each is worked**, not as one 703-line sweep.

## 10. What comes OUT afterwards — the legacy-removal register

**Matt, 2026-08-10:** *"As part of bringing MAUI to Parity, the legacy stuff needs hiding and
eventually removing. Can you document what will eventually need removing and I will do that last of
all."*

**Hiding has been done; deleting has not.** Every item below is code that is still compiled and still
shipped, but is either unreachable from the UI or reachable only in a way that cannot affect the
platform.

> ⚠ **Do the deletions LAST, and in the stated order.** Several are load-bearing for each other —
> `Helpers/Database/Database.cs` cannot go until every screen above it has, and the legacy models
> cannot go until that does. Deleting bottom-up produces a build that will not compile and a diff
> nobody can review.

> ⚠⚠ **The legacy `Database.db` file itself is NOT on this list and must not be deleted.** It is the
> shop's pre-cutover sales history and **there is no server copy** (default 3: archive, never delete).
> Removing the *code* that reads it is safe; removing the *file* is not.

| Key | Meaning |
|---|---|
| 🙈 **Hidden** | No longer reachable from the UI. Code still ships. Safe to delete when its turn comes |
| ⚠️ **Live** | Still reachable and still runs. Must be replaced first — the step is named |
| 🔒 **Blocked** | Cannot be deleted yet; something else depends on it. The blocker is named |

### L1 — The legacy-database archive path

**Code:** `SettingsViewModel.ExecuteBackupDb` + `BackupDbCommand`;
`Plutus.Client.Storage.Cutover.ArchiveLegacyDatabase`; `MetaKeys.LegacyArchivedAtUtc`; the archive
gate in `EnrolmentFlow.BlockedReasonAsync`. 🙈 **Hidden** 2026-08-10 (the settings screen's "Database"
section is gone). **Order:** any time.

⚠ **Read this before deleting.** `ExecuteBackupDb` is the only thing that can stamp
`LegacyArchivedAtUtc`, and that stamp is what `EnrolmentFlow.BlockedReasonAsync` looks for before
allowing a till holding an un-archived legacy file to enrol. **The gate is passed `null` today and
does not run**, so removing this changes nothing that currently works — but it **permanently removes
the ability to switch that gate on**, and a real shop migrated off NatApp would then have no on-ramp
for its history. Matt's call, taken 2026-08-10 on the basis that no such migration is planned.

"Restore database" was deleted outright rather than hidden: it overwrote the legacy file from a
user-chosen `.db`, which no screen reads any more, and it crashed on the legacy gate first.

### L2 — Till-side inventory CRUD

**Code:** `ViewModels/MainTill/Inventory/Items/AddEditViewModel.cs` (whole file); `AddEditView.xaml`
+ `.cs`; `ViewAllViewModel`'s `OpenEditItemCommandArg`, `UpdateItemStockCommandArg`,
`CreateNewCategoryCommand`, `OpenAddItemCommand`. 🙈 **Hidden** 2026-08-10. **Order:** before L5.

It could not work in either direction. It writes items into the legacy local database, and the till
client has **no item-write endpoint at all** — so a till-created item reaches no report, no other till
and no VAT return. And since the basket resolves items from the v2 catalogue, a locally-created item
could not be **sold** on the machine that made it. An operator would type a full item in and then be
unable to find it. Items belong to the portal; **step 25 replaced this rather than reviving it**.

### L3 — The legacy permission gate

**Code:** `Helpers/Security/Authorisation.cs` — `IsAuthorised` (both overloads), `LegacyCheck`,
`RequestAuthorisedUserInput`. ⚠️ **Live — still compiled.** **Replaced by** `Services/Security/TillGate.cs` (step 12).
**Order:** after L2 and L4 — those hold the last callers (`AddEditViewModel` ×3,
`ViewAllViewModel` ×1, all now unreachable from the UI but still compiled).

It reads employees and an `AuthActions` table out of the legacy local database, which a
portal-provisioned till does not have. Until 2026-08-10 it did not merely fail — **it crashed the
app**: `GetEmployee` returned null and `emp.EmpAuths` threw, out of `async void` command handlers with
no `catch`. That is what took the till down when Matt pressed "Change printer". It now refuses instead
of throwing, and logs that it was reached at all. ⚠ `RequestAuthorisedUserInput` never assigns the id
it returns, so its `do/while` re-prompts for ever and only Cancel escapes — **supervisor override on
this till has never once succeeded.**

### L4 — Till-side reporting

**Code:** `ViewModels/MainTill/Statistics/SalesReportsViewModel.cs`, `StockOuttakeViewModel.cs` and
their views. ⚠️ **Live — still reachable; warned, not hidden.** **Replaced by step 26.** **Order:** after step 26 ships.

Both read the legacy local database throughout. Since step 11, sales go to the v2 store and the
platform — **not** there — so on a portal-provisioned till these report **zero** for everything sold
since. A report stating "£0.00 takings" about a day the shop took £2,000 is considerably worse than
one that will not open, which is why the tab now carries a warning saying so.

⚠ **Warned rather than hidden, deliberately.** A till migrated from a NatApp install still holds real
history in that file and this is the only way to see it.

⚠⚠ **THESE TWO SCREENS ARE THE ONLY THING KEEPING SYNCFUSION IN THE BUILD.** Matt, 2026-08-10: *"I am
not going to renew Syncfusion, it seems like it can be replaced."* As of till 1.33.0 every Syncfusion
control on a **reachable** screen is gone — quantity box, alterations picker, item list, discount
multi-select. What remains is `SfCartesianChart` + `SfCalendar` here, plus the `XlsIO` export in
`Helpers/FileIO/ExcelHandling.cs` whose **only** caller is `SalesReportsViewModel`. So deleting L4
also deletes `ExcelHandling.cs`, `SyncfusionLicenseProvider.RegisterLicense` in `App.xaml.cs`,
`ConfigureSyncfusionCore` in `MauiProgram.cs`, and every Syncfusion package reference. ⚠ **In that
order, and not before** — removing the licence registration while a licensed control still exists in
the assembly turns a dormant screen into a **trial-dialog** screen, which is worse than leaving it.
Detail: [`syncfusion-footprint.md`](../syncfusion-footprint.md).

### L5 — The legacy database layer

**Code:** `Helpers/Database/Database.cs`; the `Plutus/Data/Database` project (55 files).
🔒 **Blocked by** L2, L4, L7, L8 — and `TillViewModel`, which still uses legacy models for the basket.
**Order:** after everything above.

Still referenced from 12 files. `TillStoreAccess`'s header records why the v2 store has a single
owner: this layer opens its own context at **37 call sites across 21 files**, and reproducing that
shape on a new schema was the thing to avoid. ⚠ `App.xaml.cs:59` calls
`Helpers.Database.Database.LocalDbExist()` in the startup path — **check what that decision does
before removing it**; a start-up branch is not a screen and will not announce itself when it changes.

### L6 — The legacy models

**Code:** `Plutus/Data/Database/Models/` — `ItemModel`, `SaleModel`, `StoreModel`, `EmployeeModel`,
`PaymentMethodModel`, `TaxModel`, `TransactionModel`, `StockModel`, `AuthActions`,
`Emp_AuthActions`, and the rest. 🔒 **Blocked by** L5, and the basket. **Order:** last.

⚠ **`ItemModel` is load-bearing in the till screen today.** `TillViewModel.FindItem` maps v2
`CatalogueItem` rows *into* `ItemModel` because that is what the basket and the item list bind to —
and **MAUI bindings fail silently**, so swapping the bound type blanks the rows rather than failing
the build. **These go when the basket is reshaped (step 11b), not before.** ⚠ Money on these models
is `decimal`; the platform is integer pence end-to-end — recorded in till-design **C2**.

### L7 — `LoginViewModel.EnsureStoreAsync`

**Code:** `ViewModels/LoginViewModel.cs` — `EnsureStoreAsync` and its two legacy `Database` blocks.
⚠️ **Still runs on every sign-in — verified 2026-08-12** (`LoginViewModel.cs:301`). **Order:** step 21, still open (§3).

A 2026-08-09 hotfix that writes API data into the legacy `Stores` table — exactly the bridge default 9
forbids. Step 14's Meta-cached store header replaces it. It is currently **throwing on every sign-in**
with a null `StoreModel` primary key (caught, visible in the crash log) — a fair summary of why it
should go. ⚠ It also creates `Database.db` on every sign-in, which is what made the enrolment gate a
one-way door.

### L8 — Obsolete first-run screens

✅ **MOSTLY DONE — corrected 2026-08-12 against the tree.** `SetupViewModel` and
`TransferThirdPartyViewModel` (self-labelled LEGACY; threw from `async void`) and their views **are
gone**; `ViewModels/FirstTimeStartUp/` now holds `RecoveryViewModel` alone. The register said all
three were still shipping, which was stale.

**Remaining code:** `RecoveryViewModel` + `RecoveryView.xaml`/`.cs`. 🙈 Reachable only as the cutover
on-ramp. **Order:** decide L1 first.

⚠ **`RecoveryViewModel` was KEPT deliberately** by step 21, reframed as the on-ramp feeding
`Cutover.ArchiveLegacyDatabase`. **If L1 is deleted that reason goes with it** and Recovery can go
too — but **decide L1 first, because this depends on it.**

### L9 — `AppViewModel.EmployeeId` and `AppViewModel.Employees`

**Code:** `ViewModels/AppViewModel.cs`. ⚠️ **Live — but no longer a crash.** **Replaced by**
`AppViewModel.SignedInOperator`. **Order:** with L3.

`EmployeeId` is `Employees.Last().Id`, which **used to throw for every portal-roster operator** — the
list is empty. Every legacy gate call site reaches through it, which is the other half of why those
buttons crashed rather than refused. ✅ **Step 21 made null a normal answer** (the property's own
comment records it), so it now refuses instead of crashing — **the deletion is still owed.**
**Nothing new may use it.**

### L10 — `Plutus.Frontend.ClientUI`

**Code:** `Plutus/Frontend/Plutus.Frontend.ClientUI/` and its entry in `Plutus.slnx`.
🔒 **Order:** step 22 removes it from the solution, keeping the directory.

The second MAUI frontend. **Step 22 ports its `Colors.xaml`/`Styles.xaml` into the AppClient verbatim
FIRST** — the theming port must land before the project is dropped, or the till loses its colour
scheme.

### What was deleted rather than listed

So nobody hunts for them.

| Removed | When | Why not merely hidden |
|---|---|---|
| `SettingsViewModel.ExecuteDeleteDb` | Step 21 | Destroyed the shop's entire sales history behind one "are you sure", with no undo and no server copy |
| `SettingsViewModel.ExecuteRestoreDb` | 2026-08-10 | Overwrote the legacy file from any `.db` a user could point at, from a settings menu |
| The five store-detail edit commands | Step 20 (WP6.1) | Wrote the company's own VAT number locally, so it could differ on every till in the estate |
| The store logo | Step 20 | No logo field on the store-info contract — it could only ever be one machine's opinion |
| `TillStore`'s local `Barcodes` fallback | 2026-08-09 | Nothing had ever written that table; its presence implied multi-barcode support the platform has no entity for |

## 11. What is NOT on this list, deliberately

- **Deleting the legacy code.** That is §10, L1–L10, and **Matt does it last of all** — after the step
  that replaces each piece has landed.
- **The legacy `Database.db` file.** Never delete it: it is the shop's pre-cutover history and there
  is no server copy.
- **Anything that would make a legacy write path work again.** Where a screen wrote somewhere nothing
  reads, the answer is the platform endpoint, not a repair.
- **Card terminal integration.** Greenfield for both tills, no provider — risk 8.
- **A portal VAT surface.** Shipped 2026-08-08 (WP2c); the portal owns VAT and no till holds a rule.

---
---

# PART 2 · HOW TO WORK

## 12. Execution protocol

1. **One STEP per working session**, in this document's order. Announce the step, build it, run its
   VERIFY, paste the test output, commit, and **update this document's status AND `till-design.md`
   Part B rows in the same commit** (the CLAUDE.md reflex, D3).
2. **Required reading before any code, in order:** [`repo-runbook.md`](../repo-runbook.md) (its codebase
   pitfalls have each cost a session) → [`till-design.md`](../till-design.md) **Part C2** → §15 below →
   the step body.
3. **NEVER deploy, NEVER push.** Backend changes are verified through `PlutusAppFactory` integration
   tests and a locally-run `Plutus.DBService`. Matt deploys via the runbook when he chooses.
   ⚠ **ETRIE is untouchable.** Never run `ops/keycloak/run-keycloak.sh`.
4. **Verification split.** VERIFY is headless and gates the step. **USER-VERIFY** (real enrolment,
   paper receipts, drawer, scanner, visual theming) goes to §8, does **not** block the next step, and
   **is never claimed as done** — the step is not signed off until Matt ticks it.
5. **Money rules are mutation-checked.** Any step touching VAT, prices, refunds, tenders or totals:
   after its tests pass, **deliberately break the rule, watch a named test fail, restore**. Say so in
   the commit message. **A test never seen to fail is not evidence.**
6. **Line numbers in step bodies are 2026-08-09 anchors, not gospel.** Re-locate by symbol name. If a
   step's premise turns out false (the code already does it / no longer exists), **say so in the
   commit and adapt** — do not force the step.
7. **Versions:** bump the changed component's `versions/*.txt` in the same commit — `till-maui` for
   app changes, `platform` for `src/Plutus.*` library changes, `backend` for server changes.
   `MAJOR.FEATURE.FIX`.
8. **Surfaces in scope:** the MAUI app, `src/*` libraries, the backend, tests. **Web-till TypeScript
   is edit-only-when-a-C2-rule-requires-dual-landing**, every TS edit is flagged ⚠ **NOT
   TYPECHECKED** (no Node on this box) and added to §8. WP15/WP17 web-till work needs the Mac.
9. **Stop-and-report conditions** — the only reasons to halt: an architecture-doc conflict; a package
   not on the allow-list seems required; a schema migration on the LIVE MySQL database seems required
   that this document does not name; anything that would touch ETRIE or deploy. **Everything else has
   an answer here — §13 is the answers.**

**Package allow-list (MAUI side):** what AppClient already references — EF Core Sqlite,
`CommunityToolkit.Mvvm`/`.Maui`, Mapster, the Syncfusion `34.1.32` pins. `Plutus.Client.Core` and
`Plutus.Contracts.Client` are plain net10 class libraries: **SharedKernel + BCL only.** Anything else
= stop and report.

**Toolchain facts (verified on this box):** SDK 10.0.302 with the `maui` workload;
`Plutus.Frontend.AppClient` builds for `net10.0-windows10.0.19041.0`. ⚠ The "Appium UI suite (PR #8)"
named by a superseded plan is **not in this branch** — `Plutus.Frontend.AppClient.Tests` is an
xunit/Moq unit project. **Do not go looking for the Appium suite.**

⚠ **"A local backend" means `PlutusAppFactory`'s in-process `HttpClient`** (shared in-memory SQLite,
token helpers per runbook pitfalls 5–6). `Plutus.Client.Core` accepts an injected `HttpClient`
precisely so the factory client satisfies every DoD. **No local MySQL is required, ever.**

## 13. Binding defaults — the decisions, already made

An agent building from this document follows these **without asking**. **Matt can veto any of them**,
before or after — most are cheap to change.

| # | Default (binding) | Used by |
|---|---|---|
| **1** | **AppClient is the go-forward app.** Harvest from ClientUI exactly two things — `Colors.xaml` (step 22) and the repository *interface shape*, never its implementation — then remove ClientUI from `Plutus.slnx`. Do **not** delete its directory. | Everything |
| **2** | **Offline credentials = synced password hashes verified locally** via `Plutus.SharedKernel.Pbkdf2`, byte-identical to the server. Not an invention — the legacy Kapow DB carried `HashedPassword`+`Salt`. No local-PIN interim step. | WP8 |
| **3** | **Existing local till data at enrolment: archive, never merge, never delete.** A timestamped copy of the legacy SQLite file into the translation agent's input folder, then build the v2 store from the server catalogue. Local sales history lives only in the archive; history queries go to the server. **Enrolment refuses to proceed until the archive step has succeeded.** | WP2, WP4 |
| **4** | **Migrate first, enrol second.** A till enrols only after its store's legacy data has run through the translation agent. | WP2, WP4 |
| **5** | **Legacy `TillController` in `Plutus.DBService`: deprecate, don't delete.** `[Obsolete]` + a doc comment pointing at `Plutus.Tenancy`'s `TillsController`. Removal is a separate cleanup once MAUI is live — deleting mid-retrofit risks the NatApp still trading in the shop. | WP4 |
| **6** | **Card capture stays out of scope** for both tills, pending a provider (risk 8). MAUI copies the web till's gateway-*awareness* display only. | WP14 |
| **7** | **The parity-audit rulings stand**: everything found is IN, homed to a WP. | §21 |
| **8** | **Offline credential horizons are TIERED, and a till never hard-locks out of selling.** §14. | WP8, WP16 |
| **9** | ⚠⚠ **NO BRIDGE TO THE LEGACY LOCAL DATABASE — the MAUI screens cut over to `Plutus.Client.Storage`.** *(Matt, 2026-08-09: "I would not bridge the legacy DB, it is not needed.")* The question arose the moment the till opened and its search found nothing: MAUI's screens read the legacy `Database.db` while the synced catalogue, the outbox and committed sales live in the **v2** store — two different databases. Writing synced items back into the legacy tables would have lit every screen up in an afternoon and left every till carrying two copies of its catalogue on a schema nobody intends to keep. **Decided against.** ⚠ The consequence is honest: **a screen shows nothing until it is ported**, so the till gets *more* visibly incomplete before it gets better. Scope measured 2026-08-09: **37 call sites across 21 files**. | WP2, all screens |
| **10** | **When in doubt, MATCH THE WEB TILL.** `Plutus.Frontend.WebApp/src/api.ts` + `till/*.tsx` are the reference for behaviour, wording, payload shape and arithmetic. If this document and the web till disagree, **the web till wins** and the discrepancy is noted in the commit. Parity IS the requirement; inventing a better answer on one till is how C2 rows are born. | every step |
| **11** | **Online operator auth = `POST /api/Auth/Login`, exactly like the web till.** No new token endpoint. MAUI calls it when online, holds the token in memory for the session; its `pos.sell` scope satisfies `SalesIngest`, and `perm:*` routes resolve from RBAC by the token's userId (runbook pitfall 5). Offline sign-in stays the roster. **Bundled fixes:** `Employee.Active` checked at login; `unenrol-request` gets a device-token policy; `GET /api/v1/sales` and `GET /api/v1/cash-events` gain `pos.*` alternatives. | 19, then 23–27 |
| **12** | ✅ **CONFIRMED — Matt, 2026-08-09: "You should not be able to refund MORE than the price paid for it."** Enforced at **BOTH** gates. At the till: `RefundRules.Authorise` caps at the remainder and refuses past it — ⚠ **no override, supervisor included, may exceed the remainder**; ceilings authorise *up to* what is owed, never beyond. At ingest: `SalesIngestService` re-runs the same `Authorise` against the origin sale's recorded refunds and **quarantines (202)** anything claiming more. ⚠ **Quarantine, not 400** — the money (if any) already left a drawer on a till that broke the rule: a 400 makes the evidence vanish into the till's Failed queue; quarantine preserves it where the portal can see it. | 16, 17 |
| **13** | ✅ **CONFIRMED — Matt, 2026-08-09 ("Do I need more?" — no). Tenders = the fixed `TenderType` set** (Cash, Card, Online, Credit, GiftCard), moved to SharedKernel. Five covers everything the platform takes: the web till exposes exactly these, Online is how webstore orders ingest, Credit is store credit, GiftCard is WP13's redemption. No payment-method roster endpoint. ⚠ A sixth is a cheap **additive** change — one enum member, one till button, one C2 pin — not a redesign. | 8, 9, 13b |
| **14** | **Discounts = manual line discount first** (positive inc-VAT `DiscountPence`, gated `pos.discount` with the operator's ceiling), scaled by `VatLineMath.ForLine`. Catalogue/scheduled discounts wait until a server endpoint exists — there is none. | 9, 12 |
| **15** | **Parked baskets are local-only**, in the v2 `SavedBasket` table, serialised as **contract JSON via `PlutusApiClient.Json` — no Newtonsoft `$type`.** No server sync (the web till parks locally too). `$type` coupling already broke discounted parked baskets once. | 18 |
| **16** | ⚠ **First sign-in of any account on a device must be ONLINE** *(Matt, 2026-08-09)*. That first login mints a **device-local PBKDF2 verifier** (fresh salt); offline sign-in verifies against it. The roster keeps shipping hashes until both tills run verifiers, then the server stops (flagged, separate change, C2 row required). A till that has never been online cannot sign anyone in — deliberately: it has no catalogue or prices either. | 28 |
| **17** | **Reporting series with no server answer are DROPPED, not locally recomputed.** `summary-rich` is in **POUNDS**, everything else in **PENCE** — encode it in the contract type names. Local re-derivation is C2 drift by construction. | 26 |
| **19** | ✅ **CONFIRMED — Matt, 2026-08-13: "If the card machine is down, we cannot refund cards."** A refund goes back **only** to the tender that took the money, capped at what that tender took, **with no exception for a dead card terminal and no supervisor override** — the same shape as default 12 for the sale total. So a part-cash-part-card customer cannot be handed the whole refund in notes, and a card sale cannot be refunded from the drawer at all. ⚠ **This was raised as an owner-level question precisely because it has a shop-floor cost** (a customer sent away until the terminal is back), and the answer is the strict one: the alternative is the oldest till fraud there is, and an honest cash refund of card takings empties the drawer just as effectively. **Do not re-litigate it in code** — if it ever changes it changes here first. | 16, 17, ingest |
| **18** | **Additive feed fields are allowed.** `CatalogueItemDto` gained `Brand`, `Description`, `CostPence?`, `StockQty?` (nullable, so old servers stay compatible). ⚠ **`Barcodes[]` CANNOT be wired and that is settled**: there is no barcode entity in `Plutus.Entities` at all — **`IdOne` IS the barcode**, and multi-barcode items are not something the platform models. `FindByBarcodeAsync`'s alias path is dead **by design, not omission**; adding it is a platform decision, not a till task. `PriceSchedule` is likewise unpopulated but harmless — the effective-dated timeline rides in `BandData` from the feed, so scheduled prices work. The store-info screen **drops the logo** (no contract field). | 10, 20, 25 |

## 14. How long a cached login lasts (the numbers, and why)

Matt asked for a recommendation. **The recommendation is to stop asking for one number**, because
three constraints pull in different directions and any single value loses two of them:

| Constraint | What it wants |
|---|---|
| **Keep selling** | A shop whose till refuses logins during an outage falls back to a cash tin and paper — a *worse* compliance event than a stale staff roster, because it produces no HMRC-attributable records at all |
| **Shrink the theft** | A stolen till holds operators' **platform** passwords at PBKDF2-HMAC-SHA1 / 101,010 iterations — ~13× below current OWASP guidance for that PRF — and they work on the web till too |
| **Reach the leaver** | Nothing can be *pushed* to an offline till (risk 5). **Expiry is the only mechanism that ever revokes a dismissed employee on one**, so this horizon *is* the erasure SLA you can put in a DPA |

Tiering by what the permission can *do* satisfies all three. Ringing up sales is how a shop survives
an outage and is worth almost nothing to a thief — the money lands in the ledger. Refunds, cash-out
and price overrides are how a stolen till becomes cash, and are what a shop can live without for a
few days.

| Lifetime | Value | Why that number |
|---|---|---|
| **Money-out** (refund, void, discount, no-sale, price override, all admin) | **7 days** since the last operator sync | Covers the realistic UK worst case — a Friday-night line fault on an end-of-next-working-day care level over a bank holiday is ~5 days — and is short enough to state as an erasure SLA inside the UK GDPR Art 12(3) one-month window **even if the request lands on day one of an outage** |
| **Selling** (`pos.sell`, `support.tickets`) | **30 days** | The alternative to a stale roster is a shop that cannot trade. Covers the two cases that actually meet this boundary: the spare till from the cupboard, and a convention/pop-up till offline for a planned week |
| **Warning** | from **3 days** | A warning that first appears an hour before the cliff is decoration. Its job is to get someone to plug the cable in while that is still enough |
| **Idle lock** | **15 min** | PCI-DSS 8.2.8's figure, and right on its merits for an unattended shop-floor device. ⚠ It **locks, it does not log out** — the basket survives, unlock is one password entry. That is what makes it cost seconds rather than sales |
| **Absolute session** | **min(12h, business-day rollover, Z-close)** | Matches the server's token TTL. **The rollover is the load-bearing half**: a session spanning two days attributes the incoming shift's sales to the outgoing operator — silently, in exactly the records HMRC would ask about |
| **Server operator token** | **keep 12h** | The TTL is not the problem; **irrevocability** is |

**At every boundary the till degrades, it never bricks.** Past 7 days: sells normally, refunds and
manager functions withheld, screen says so in shop English. Past 30 days: offline sign-in refused,
with a manager break-glass extension as the escape hatch. The floor set is an **allow-list**
(`OfflineCredentials.SellFloor`), so a permission added to the catalogue next year is withdrawn when
stale until someone deliberately says otherwise.

⚠ **Two server-side findings that make these numbers enforceable:**

1. ✅ **`POST /api/Auth/Login` not checking `Employee.Active`** — found 2026-08-08, and it turned out
   to be **already fixed** when step 19 went to bundle it. An offline expiry policy is theatre while
   the online path lets a deactivated user straight back in.
2. ⚠ **There is no server-side session revocation for any principal.** Tokens are HMAC bearer tokens
   checked for signature and `exp` only — no denylist, no DB lookup. Revoking a device or resetting a
   password stops the *next* sign-in and **does not eject a live session**. The cheap fix is a
   per-user `TokenEpoch` integer emitted as a claim and compared per request (one indexed, cacheable
   lookup), turning 12 hours of irrevocability into seconds. **Recommended, not scheduled — Matt's
   call.**

## 15. Pitfalls that have each cost a session

All still live. These are the MAUI/cutover ones; [`repo-runbook.md`](../repo-runbook.md) holds the
platform-wide list and **both apply**.

- **Green build ≠ working EF.** The EF-9 break compiled clean and died at runtime. Same lesson as a
  deploy: verify columns and behaviour, not history tables or build output.
- **`Microsoft.Data.Sqlite` POOLS connections from v6** — disposing a context does not release the
  file. `SqliteConnection.ClearAllPools()` before any copy/move/delete of a database file; the cutover
  archive is the case that matters.
- **EF 9 does not value-generate string keys** the way 3.1 apparently did: always set legacy `Id`s
  explicitly (`Guid.NewGuid().ToString()`).
- **EF 3.1-era `DbSet` + .NET 10 = ambiguous `Where`** (`IAsyncEnumerable` vs `IQueryable`) —
  disambiguate with `.AsQueryable()`.
- **Never mutate `HttpClient.BaseAddress`** — one client per address via `PlutusHttp.TryFor`.
- **`ShellContent` does not inherit Title/Icon from its page** — copy them (`AppShell.Tab(...)`).
- **A viewmodel constructor that can throw takes down whatever constructs it** — `AppShell` builds
  every tab eagerly. Null-guard reads; never dereference app state in a ctor.
- **Debug now validates XAML** (XamlC validate-only) — a bad property fails the BUILD, by design.
- **The device is what "enrolled" means; the till id is a separate, recoverable fact**
  (`GET devices/{id}/status` returns it). **Never tell an enrolled till it isn't paired.**
- ⚠ **Tested components are not a working feature until something calls them.** WP5's entire sync
  spine sat unreferenced while the till showed no stock. **Every VERIFY must include the call site**,
  not just the library. (Seven such components found so far — §7.)
- ⚠ **MAUI bindings fail silently.** A binding onto a member that no longer exists renders blank
  instead of failing. Enumerate bindings before changing a bound type, and hand-run every row type.
- ⚠⚠ **`Services.UIHandeling.Modal` is a NON-REENTRANT gate, and `InputAlertHelper` already goes
  through it.** Wrapping an input alert in `Modal.ShowAsync` at the call site takes the same
  `SemaphoreSlim(1,1)` twice on one flow and **deadlocks for ever** — no exception, no log, and every
  dialog in the app dead behind it. That is finding **U**: it stopped the till taking a sale on
  1.48.0. **Call `LaunchInputAlertAsync` bare; wrap only raw `DisplayActionSheet`/`DisplayAlert`.**
- ⚠⚠ **A `finally` does not run when the `try` body deadlocks.** "`IsBusy` is safe, every set has a
  `finally`" was used to rule `IsBusy` out of finding Q, and it was wrong: the flag stayed set because
  the method never got that far. **When a flag is stuck, ask whether the body can hang — not whether
  the cleanup exists.**
- ⚠ **`ALTER TABLE … ADD COLUMN` has no `IF NOT EXISTS` in SQLite** — the first draft of the v5 step
  threw *"duplicate column name"* at start-up on any store built from the current model: a till that
  would not open. Pinned by `Running_the_upgrade_twice_is_harmless`.
- ⚠ **`TillStore` has TWO hand-written upsert branches.** A field copied into one and not the other
  reaches only brand-new items — exactly the `StockUntracked` bug. `CatalogueUpsertTests` fails if
  either forgets.
- ⚠ **Grep for the ROUTE STRING, not the attribute form.** `grep -rn '"api/v1/sales'` finds both
  forms; `Route("api/v1/sales"` finds one. Getting this wrong once caused a **duplicate endpoint to
  be built and deployed**, and because a constrained parameter (`{saleId:guid}`) outranks an
  unconstrained one, the server served sale detail from a **device-gated** handler in place of the
  operator-gated one for ~20 minutes.
- ⚠ **Ask the running server.** It answered 401-not-404 from the start and that was explained away.
  `/swagger/v1/swagger.json` **through Caddy returns the portal's HTML** — hit
  `http://127.0.0.1:5100/swagger/v1/swagger.json` on the Mac instead.
- ⚠ **A log a person cannot attribute at a glance is worse than no log, because it is believed.** 60
  `JsonException`s read as a till fault and were **the test suite**: `CrashLog` fell back to the
  system temp directory with the *same filename* as a real till's log. The test-host log is now named
  `plutus-NOT-A-TILL-testhost-*.log`.

## 16. Item identity — the seam with the translation agent

[`To do/NatApp-Translation-Agent-Plan-2026-08-05.md`](NatApp-Translation-Agent-Plan-2026-08-05.md)
moves **legacy shop data** into the backend; this document makes **a till talk to** it. They run in
parallel and meet at exactly one point: item identity.

> ### ⚠ The original seam statement was WRONG, and the correction is binding
>
> It said: *"item IDs on a cutover till must equal the item IDs the central migration produced."*
> **They cannot, and they must not be compared.** Verified against the code and the live database:
>
> 1. **The server's catalogue has no item UUIDs at all.** `Items` is still keyed
>    `(IdOne barcode, IdTwo tenant)`. There is nothing to compare a till's GUID against.
> 2. **The only central item UUIDs that exist are random.** `Migration.Kapow`'s `IdRemap.GetOrMint`
>    mints a fresh `Uuid7` per run, in memory, with no export — and only for **historic sale lines**,
>    never the catalogue.
> 3. **So two populations already coexist in live data, by design.** Confirmed in production: barcode
>    `761941391632` carries **two distinct `ItemId` GUIDs across 161 sale lines** — random ones on
>    migrated history, derived ones on web-till sales. Several hundred barcodes are like this.
>
> **The real invariant is the BARCODE, not the GUID.** `StockProjectionConsumer` attributes stock by
> `itemIdOne`; `ItemId` rides along. So:
>
> > **MAUI must derive `ItemId` exactly as the web till does —
> > `DeterministicGuid.ForItem(businessId, itemIdOne)` on the legacy Business id — so the two TILLS
> > agree with each other. It must NOT be checked against migrated history.**
>
> ⚠ **`businessId` ≠ `tenantId`.** `ForItem` is keyed on the legacy *Business* id (Kapow:
> `d5a31aac-159e-9a30-706b-02f9eb935600`, hardcoded as `BUSINESS_ID` in `api.ts:25`) — **not** the
> `TenantId` that `EnrolResult` returns. Deriving item GUIDs from TenantId produces silently wrong
> ids that nothing catches quickly, because stock still moves (lines key on `itemIdOne`). The
> businessId is stored in `Meta` at enrolment; `GET /api/v1/stores/{id}/info` returns it.

**Two consequences for the translation-agent work:**

- If real UUID PKs are ever put on `Items`, those UUIDs **must** be `DeterministicGuid.ForItem`, not
  freshly minted — otherwise the catalogue disagrees with both tills on day one.
- The two-population split is tracked debt, not damage: reports key on `ItemIdOne`. But **8,120 of
  82,965 sale lines carry no barcode at all** (~10%, mostly migrated history) and those can never be
  item-attributed by any report. **Worth knowing before anyone trusts an all-time item ranking.**

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

## 18. The architecture decisions, restated

**Two MAUI projects, and they are not two versions of the same thing.**

| | `Plutus.Frontend.AppClient` | `Plutus.Frontend.ClientUI` |
|---|---|---|
| What it is | Sean's MAUI rework of NatApp, merged at `4494a57` | The earlier, abandoned in-house MAUI port |
| Data layer | `Plutus/Data/Database` — **the legacy NatApp schema** | `Plutus.Entities.Models` — the **backend's** types |
| HTTP | **Zero** at the start | 5 files with `HttpClient` |
| Sync | **None** | `DBAction` outbox + `LocalToServerSync` (buggy) |
| Feature completeness | **The working till** | Missing Settings, StoreOptions, FTSU, add-user |
| Known bugs | All three fixed | All three present |

**AppClient is the go-forward app** (default 1). ⚠ **It is a real trade, not a formality:** AppClient's
data layer is the *legacy* schema — decimal money, string IDs, a flat stock column — which is what the
platform moved away from, while ClientUI already speaks the backend's types. Choosing AppClient made
the v2 local store genuine work rather than something inherited. **Still the right call: a working
till with a schema to migrate beats a schema-correct shell with no till in it.**

**Transport: extend the DB-backed outbox + plain REST. No broker.** Settled across three independent
reviews.

| Option | Score | Why |
|---|---|---|
| **Extend the outbox + plain REST** | **9/10** | Already built, tested and live for the web till. A wiring job, not a design job |
| Client-driven pull/sync, no broker | 7–8 | Re-derives what the outbox gives free (idempotent ingest, dead-lettering) |
| SignalR hub + REST fallback | 4–5 | Solves sub-second push, which a till doesn't need; still needs REST underneath |
| Message broker (RabbitMQ/NATS) | 2 | The architecture doc rejects it outright: *"broker credentials on every till and firewall fights"* |

⚠ **"No broker" is not "no queue."** A durable queue is mandatory and exists on both ends — implemented
as database rows (a local table on the till, the `Outbox` table on the server) rather than a broker
product. The wire between them is plain HTTPS to an idempotent ingest API. *Future, not now:* if the
number of **internal** consumers of sale-ingest grows, a managed broker becomes worth revisiting —
purely to fan one event out to many. It would not change the till edge.

**A macOS/Linux till is a build target, not a port.** The three client libraries are plain `net10.0`,
MAUI-free and package-free, and they carry the VAT rules; only UI and hardware are platform-specific.
Pinned by `Till_libraries_stay_platform_neutral_and_hold_no_VAT_rates_of_their_own`, which fails on an
OS-specific TFM in a till library **or a literal VAT rate in one**.

---
---

# PART 3 · WHAT HAS BEEN COMPLETED

## 19. The one-line answer

**A MAUI till can trade a full shop day.** Sell, price, split-tender, refund to the original method,
reprint a receipt, print through the hardware agent, open a float, X-read, Z-read, **reopen a Z**, add
and edit items, adjust stock, manage categories, bin an item, sign a disabled operator out within 60
seconds, and keep trading with the line down.

What it cannot do yet: **loyalty, gift cards, users, theming, and server-aggregated reporting.**

## 20. The cutover steps — the record

| Phase | Steps | State |
|---|---|---|
| **0 — Foundation** | 1 EF9 · 2 reference · 3 `TillStoreAccess` · 4 enrolment flow | ✅ |
| **1 — Line primitives** | 5 price pair · 6 TaxId + StockUntracked · 7 VAT band store · 8 tender values → SharedKernel | ✅ |
| **2 — The money path** | 9 basket + assembler · 10 v2 lookup · 11 `CommitSaleAsync` · 12 permission gates · 13 sync services · 13b fixed tender set · 14 receipt re-signature · 14b crash sweep | ✅ (⚠ **11b outstanding**) |
| **3 — Returns / park / reprint** | 15 sale read path · 16 `RefundRules` wiring · 17 server refund cap · 18 parked baskets | ✅ |
| **4 — Operator auth** | 19 `/api/Auth/Login` + backend fixes | ✅ |
| **5 — Screens** | 20 WP6 store info · **21 first-run sweep** · **23 WP9 cash** · **25 WP10 inventory** · **26 reprint half** | ✅ (⚠ **21 has two pieces open** — §3; 22, 24, rest of 26, 27 open) |
| **6 — Hardening** | 28 online-first login | ⬜ |

⚠ **Step 25 is DONE, all of it** — including 25e (the VAT band timeline), which was carried as "the
last half-day" for three days before a grep found it already built.

⚠ **Step 21 is marked ✅ and is NOT finished** — `EnsureStoreAsync` still runs and throws on every
sign-in, and the un-enrol round trip has no client code. **Both are in §3, with the pieces that did
land.** Two source documents disagreed about this (one listed step 21 as complete, another had L7/L8
waiting on it); settled against the tree on 2026-08-12.

**Notes worth carrying forward from the delivered steps:**

- **Step 1 (EF Core unified on 9.0.18).** The wall: legacy migrations were *compiled* against EF 3.1
  and died under 9 with `MissingMethodException: MigrationBuilder.CreateIndex(...)`; fixed by
  retargeting `Plutus/Data/Database` to `net10.0`. ⚠ **Residual risk:** the Till/Inventory
  eager-`Include` chains are untested under EF 9 — extend `LegacyDatabaseUnderEf9Tests` with one test
  per Include chain before anything retires them.
- **Step 4 (enrolment).** `TillPlacement` is the single resolver — Meta first, then the legacy
  Preferences value, then the device-status endpoint, back-filling Meta each time — which **replaced
  three separately-written copies** of that recovery block. Placement refreshes on every start, so
  tills enrolled before this self-heal. ⚠ The archive gate is deliberately passed `null` until step 21
  exists to archive: enabling it first would refuse enrolment with no way through, **on every till
  that has ever opened its legacy file — which is all of them**, because the `Database` constructor
  creates one on first touch. ⚠ Still owed from step 3: `TillStoreAccessTests`.
- ⚠ **Step 8 changed shape, and the reason matters.** Declaring `TenderType`/`SaleChannel` **enums**
  in SharedKernel was tried and **reverted**: those names already exist in `Plutus.Entities.Models`
  and **69 backend files import both namespaces**, so every use became `CS0104: ambiguous reference`.
  Renaming the backend's copy would change the CLR type of mapped EF properties and move the model
  snapshot — a `PendingModelChangesWarning` against a live database. `IngestTender.TenderType` is a
  **byte** on the wire, so the values were all a client needed: SharedKernel got
  `Tenders`/`SaleChannels`/`Adjustments` **constants** plus `Tenders.FromMethodName`, and
  `TenderTypeParityTests` pins them to the backend enum **and** to the web till's `tenderTypeFor`.
- ⚠ **The declared VAT rate wobbles further at low prices than the docs say.** `VatLineMath` quotes
  1993–2004bp for a 20% line; that is the range for £10–£20. A £5.00 item resolves to **1990bp** and a
  penny item lands further out still, because the rounding error is a fixed half-penny against a
  smaller base. Correct per C1 rule 2 (the rate comes FROM the pair) — but **anything that
  range-checks a declared rate must scale with the line, not use a flat window.**
- **Step 9 (basket + assembler).** ONE calculation, so the screen total and the payload cannot
  disagree. ⚠ `LineMeta.ItemIdOne` is load-bearing and fails **silently**:
  `StockProjectionConsumer` skips lines without it — **stock quietly stops moving while every sale
  reports success** — so the assembler asserts it. Verified by 19 unit tests **and**
  `SaleAssemblerE2eTests`, which posts an assembled mixed-rate basket to the real `/api/v1/sales` and
  asserts **201 Recorded, not 202 quarantined**. It also fixed a live percent-discount bug:
  `item.Price * Decimal.Parse(amount)` charged **ten times** the price for a "10%" entry.
- **Step 10 (item lookup → v2).** Fixed a live defect: `SearchId` ignored tombstones, so **a binned
  item was still sellable**.
- **Step 11 (checkout → `CommitSaleAsync`).** Sale + outbox row are ONE row in ONE transaction;
  DeviceSeq allocated inside; the per-line stock decrement **deleted** (v2 has no local stock — the
  server attributes movement from `LineMeta.itemIdOne`). ⚠ **Commit BEFORE printing.**
- **Step 12 (permission gates).** `Services/Security/TillGate.cs`; no `IsAuthorised("Till", …)`
  remains. Fixed: the refund threshold summed `bRI.Price` **without quantity** — five £30 returns
  tested as £30. ⚠ A null `SignedInOperator` **blocks**, never silently allows.
- **Step 13b — a fresh portal-provisioned till could not complete a checkout AT ALL.**
  `GenPaymentMethodActions` read the legacy local `PaymentMethodModel` table, which only
  `Database.Init()` ever seeded — so a portal till rendered **zero** payment buttons and the
  `paid != total` loop could never progress. Dev machines never saw it because they were migrated from
  legacy installs. Killed, along with the legacy seed.
- **Step 14 (receipt).** Built from the payload the platform accepted. ⚠ The barcode had been **EMPTY
  since step 11**.
- **Step 15 (sale read path).** A committed sale could not be read back at all
  (`GetPendingAsync` filters Pending), blocking reprint, offline refunds and X/Z at once. ⚠
  `AlreadyRefundedPenceAsync` needed a real **indexed column** on `LocalSale` — the origin id was
  buried in JSON, unqueryable.
- **Step 16 (`RefundRules` wired).** ⚠ Surfaces `WasCapped` — silently refunding less starts
  disputes. `NeedsConnection` and `UnknownSale` stay **different messages**: telling someone to check
  the network when the receipt simply is not ours sends them to reboot a router with a customer
  waiting. Fixed two live defects: `trans.First()` threw when the item was not on the sale, and a
  non-short-circuit `&` across two `TryGetValue`s.
- **Step 21 (first-run screens).** ⚠ Deletes `SettingsViewModel.ExecuteDeleteDb` outright (it
  destroyed the translation agent's only input, with no undo); `ExecuteBackupDb` becomes "Archive
  legacy database" stamping `MetaKeys.LegacyArchivedAtUtc`. **Keeps `RecoveryViewModel`** as the
  cutover on-ramp. Fixes `AppViewModel.EmployeeId` = `Employees.Last().Id`, which **throws for every
  portal-roster operator**. ⚠ Do **not** port `Authorisation.RequestAuthorisedUserInput` — it never
  assigns `authEmpId` and always returns default.
- **Step 23 (cash).** ⚠ **The SERVER was already finished** — the whole step was client-side, and the
  five type names appeared nowhere in the app. ⚠ **Queued, not posted**: a shop opens before its
  broadband does, and a float that failed to post is a day whose banking cannot be reconciled at all.
  ⚠ **One Z per day is enforced LOCALLY as well as server-side**, refusing every type afterwards
  rather than merely a second Z — the server's guard is unreachable offline, which is when it matters.
  ⚠ **`SharedKernel.BusinessDay` extracted** so the drawer and the sales it reconciles against cannot
  disagree about "today". ⚠ On the drain, **409 and 400 are TERMINAL** and kept with the server's
  words: a till that retries them for ever looks healthy while quietly never banking.
- **Step 25 (inventory), slice by slice:** stock column (Number / **∞** untracked / **—** never
  counted — ⚠ it had been **blank on every row, and blank reads as ZERO**) · adjust stock (a new
  till-side `pos.stock.adjust`, Supervisor and up, **never the Cashier**) · categories (the 409
  becomes the **offer** to reassign; ⚠ a test pins that the till never calls the LEGACY delete, which
  cascades and would take every item in the category with it) · the Bin (⚠ the offline-tombstone rule
  was honoured on every read path and **pinned by nothing** — now covered across scan, search and
  browse separately; **restore stays portal-side**) · the VAT band timeline (already built).
- ⚠ **Step 25's stock screen is a LEDGER, not a quantity box.** `qty` is a signed delta and zero is
  refused. The only "set it to N" surface in the v1 API is `POST /api/v1/stock/takes`, which converts
  counted − expected into an adjustment server-side. **A screen whose box holds an absolute number
  must post a TAKE, not a movement** — posting the typed number as a delta would add the count to the
  count.
- ⚠ **The Cashier exclusion on `pos.stock.adjust` is load-bearing, not an oversight.** The person
  minding the shelf and the person who can alter its count must differ, or shrinkage stops being
  visible. A named test fails if that ever changes. ⚠ And the **till** had to be fixed too: its gate
  asked for `pos.stock.adjust` alone while the server accepted **either** that or
  `portal.stock.adjust`, so an **Owner** was refused by the till for something the platform allowed.
  `TillGate.CheckAny` mirrors the server now.
- **The riskiest steps, ranked, for whoever touches them again:** 9 (every penny flows through the
  assembler) · 17 (money, server-side) · 27 (the gift-card VAT fork mis-states silently) · 23 (a
  double Z or wrong variance is a same-day cash dispute) · 12 (a null operator falling open removes
  every ceiling) · 21 (`ExecuteDeleteDb` destroys unarchived history).

## 21. The work packages — the record

| WP | State | What landed |
|---|---|---|
| **0** Toolchain + baseline gate | ✅ | `maui` workload present; AppClient `net10.0-windows` head builds 0 errors |
| **1** Shared contracts + client core | ✅ | `Plutus.Contracts.Client` + `Plutus.Client.Core` (outbox engine, pusher, API client, token provider). ⚠ Not named `Plutus.Contracts` — that name is taken by the legacy repository-interface layer. Architecture test keeps it **MAUI-free and backend-module-free**, mutation-checked. ⚠ **There is no first-class `ItemIdOne` field on the wire** — the barcode rides inside `IngestLine.DiscountsJson`, a metadata envelope, exactly as `api.ts:985–992` builds it. Adding an `ItemIdOne` property to the DTO would serialise to nothing and **stock would silently stop moving** |
| **2** Local store v2 + money/ID sweep | ✅ | `Plutus.Client.Storage`, schema v2 (now v6). The till's local DB is an **operational cache, not an archive** — catalogue, prices, permissions, cursors, the outbox, saved baskets, and a rolling 14-day window of recent sales for reprint and X/Z. `LocalSales` **is** both the outbox (Pending) and the window (Pushed) — one table, one transaction, no dual-write; `PayloadJson` is the contract itself, so the pusher never re-serialises from entities. Money is `long` pence, ids are `Uuid7`. Cutover **archives, never merges** |
| **2b** VAT effective-dating | ✅ corrected + ARMED | First implementation validated each line's `VatRateBp` by **exact membership** — wrong, and it would have quarantined ordinary web-till sales the moment any tenant's bands were seeded (Matt's catch). Replaced by the **pair-based** rule: explained by an in-force band → accept; explained **only** by a retired/not-yet-effective rate → **quarantine** with both rates named; explained by neither → **accept**, because off-band legacy damage is *surfaced, never blocked* (owner's decision) and the VatIntegrity report owns it |
| **2c** Portal VAT surface | ✅ | **The portal is now the source of VAT truth.** `GET /api/v1/vat/bands` ships the whole effective-dated timeline, future points included, so an offline till applies a rate change on the day. A rate change **adds a dated point and is refused in the past**; a *scheduled* one can be cancelled, an *in-force* one cannot. Portal gained a top-level VAT tab — Return · Bands · Corrections · **Rules** (every rule the code applies with its HMRC citation, served from `VatGuidance` so text and behaviour ship together) |
| **3** Outbox + sale ingest | ✅ | `CommitSaleAsync` — one transaction, sequence allocated, **commit before print**. Pusher semantics: `201/200` → Pushed · `202` → **Quarantined, never retried** · `400` → Failed, **skipped so one poison sale cannot block the queue** · network/5xx/timeout → Pending, 5s→5min backoff, retry for ever. Prune Pushed past the window; **never prune Pending or Failed**. Soak: 120 offline sales drain exactly once in order; crash mid-drain records 20 of 20 |
| **4** Enrolment + device identity | ✅ | Server URL + enrolment code → `EnrolResult`; `ClientSecret` in platform `SecureStorage`, **never** the SQLite file (grep-verified). **TWO TOKENS, and every later WP leans on it:** the device token covers `sales.ingest` calls only (sale ingest, heartbeat, catalogue, store info, receipt template, themes); every `perm:*` endpoint resolves permissions from RBAC **by the token's userId**, so a device token can never pass one. ⚠ Remaining: the un-enrol request + approval round trip (step 21) |
| **5 · 5b** Heartbeat + catalogue sync | ✅ | `POST /api/v1/heartbeat` (in-process `TillPresence`, 2/5-min ONLINE/STALE/OFFLINE boundaries — **no per-heartbeat MySQL writes**), `GET /api/v1/catalogue/changes`, `SyncNow`/`Locked`/`LockReason` on `Device`. ⚠ **The cursor is `(ModifiedAt, IdOne)`, not a bumped `BIGINT`** — a counter needs every catalogue write path to remember to increment it, and the audit found **nine** such paths for Items alone plus a raw-SQL purge; `ModifiedAt` is stamped for every `IAuditable` at one choke point. The barcode breaks timestamp ties, which a bulk edit produces by the hundred. ⚠ **The migration carries an index** — without it the feed is a full scan per till per sync. ⚠ **A price write calls `PricingService.TouchItemForSyncAsync`** so the existing feed carries it, and the item ships its **whole price timeline**; three write paths must call it and a test asserts a fourth fails rather than a shop. ⚠ **Store overrides are chosen from the DEVICE in the token, never a query parameter** — a till asking for another store's prices would be charging another shop's prices with no way for the operator to tell. Search went from **three** implementations to one shared `SharedKernel.ItemSearch` |
| **6** Store Information | ✅ | Read-only off `StoreInfoCache`; five local-write commands and the logo gone. Shows "unavailable", **never stale** |
| **8** Operator RBAC + offline login | ✅ (Users screen = step 24) | Roster cached per device, offline sign-in verified with `SharedKernel.Pbkdf2`, ceilings + time windows + staleness tiers at the gate. ⚠ **Windows ship RAW** — `ResolveAsync` pre-evaluates `InWindow` and discards the windows, so building the payload with it would give a Saturday-only supervisor synced on a Wednesday **no permissions at all** until the next sync, silently. The rule moved to `SharedKernel.PermissionGrant.IsActiveAt` and the server delegates to it. ⚠ **The roster is narrowed to people who work here** — company- and tenant-scope assignments sit on every till's ancestor chain, so "everyone RBAC-reachable" would mirror the whole company roster *and its password hashes* onto every counter. **That is a product decision as much as a technical one, and the first thing to revisit if a manager covering another shop cannot sign in.** Supervisor override refuses self-auth, applies the supervisor's ceiling, and names both people |
| **9** Cash | ✅ | Step 23 |
| **10** Inventory + stock ledger | ✅ | Step 25 |
| **14** Payment-gateway awareness | 🔨 | Shared half done — `Client.Core.PaymentGateway.Resolve`, 13 tests, mutation-checked. ⚠ **The rule is `integrated`, not `provider`**: every provider reports `integrated: false` today, so a till reading the *name* would wait for a terminal that never answers. Remaining: the checkout XAML |
| **15** Web-till test runner + C2 pins | ⬜ | §5. Needs the Mac; Matt's call on timing |
| **16** Connectivity + offline credentials | 🔨 | Shared half done — `/api/v1/ping` (anonymous, **touches no database**, so it answers during a MySQL blip and for a till not yet enrolled), `ConnectivityProbe`, `OfflineCredentials` horizons, 42 tests. ⚠ **"Offline" is three different faults wearing one word** and the operator is the person who has to act on the difference: no network (their cable), no server (nothing they can do), or **this till has been revoked** (a manager's job, and no amount of rebooting the router fixes it). ⚠ **Never probe `POST /api/v1/tokens/device`** — rate-limited 5/min per IP, so probing it would make a healthy till report itself revoked, and tills sharing one public IP would do it to each other. Remaining: the MAUI login-screen UI and the web till's half |
| **17** Web till catches up | 🔨 | 17.2 done (the ambiguous VAT-band tie — the only one that puts a wrong number on a VAT return). ⚠ **Worth noting how it hid:** the function's doc comment already *described* the strict rule, so reading the comment would have told you the code was fine. **A comment is not a pin.** Remaining: 17.1, 17.3, 17.4 — §5 |
| **Build guards** | ✅ | Two things that made green mean less than it looked: **CI ran none of the modern suites**, and **Debug never validated XAML** (MAUI inflates at runtime in Debug, so a bad property only failed when a human opened the tab). Both fixed, mutation-checked both ways |

⚠ **A ruling in a table is not a specification.** A 2026-08-08 cross-audit of Part B against the WP
bodies found **seven capabilities ruled IN and never specified anywhere** — no body, no DoD, nothing
anyone could build from — plus **four specified in a body and gated by no DoD**. All were homed and
given DoD lines. **The decision log is not the specification; the bodies are, because they are the
only half anyone builds from.**

## 22. VAT — settled, and not to be re-derived

**The governing principle: ALL VAT GUIDANCE COMES FROM THE PORTAL, DOWN TO THE TILLS.** A till — web
or MAUI — never decides a VAT rule. It receives bands, applies them, and reports what it charged.
Same shape as receipt templates and themes. ✅ **True of the platform since 2026-08-08** (WP2c).
Live rules and their homes: [`till-design.md`](../till-design.md) **C1/C2**.

### The law (HMRC, checked 2026-08-08)

| Class | Rate | Taxable supply? | Input tax recoverable? |
|---|---|---|---|
| Standard | 20% | yes | yes |
| Reduced | 5% | yes | yes |
| **Zero-rated** | 0% | **yes** | **yes** |
| **Exempt** | none | **no** | **NO** |
| Outside scope | none | no | n/a |

⚠ **Zero-rated and exempt are not the same thing**, even though both charge the customer nothing.
Zero-rated is a taxable supply at 0% with full input-tax recovery; exempt is not a taxable supply and
*blocks* recovery of attributable input tax (partial exemption). **Different boxes, different money.**

**Rounding.** HMRC's rounding-*down* concession is **explicitly not appropriate for retailers**
(VATREC12020). Permitted: round up and down to the nearest 1p, or a published/bespoke retail scheme.
Plutus prices **VAT-inclusive**, so the tax-inclusive price is what the customer sees and the net is
derived. **Tax point:** VAT is accounted at the rate in force when the tax point occurs — for retail,
the sale itself, which is why a line is judged against `OccurredAtUtc` and never against "now".

### The four defects fixed 2026-08-08 (Matt: *"Whatever the UK government VAT rules are need to be followed"*)

| Was broken | Fixed |
|---|---|
| **The VAT return summed per-line VAT.** Notice 727 §3.4.1 requires output tax = VAT fraction × takings at each rate; summing thousands of penny-rounded lines understates it | `/api/v1/reports/vat` applies the fraction to takings and reports `vatChargedPence` + `roundingDifferencePence` alongside for reconciliation. **On live Kapow data the return was £10.77 light** |
| **Takings were bucketed by DERIVED rate.** A till computes each line's rate from its price pair, so one 20% band arrived as 1993–2004bp — Kapow's return was split across **six** standard-rate buckets | Takings group by **band**; derived rates snap to the published band within 25bp |
| **Off-band takings would have been folded into a real band** (Kapow has a genuine 2500bp line) | Reported as `unclassified` in its own bucket — never merged, never given an invented rate |
| **Comics were classified Exempt.** Notice 701/10 zero-rates books, comics, magazines; exempt **blocks** input-tax recovery | `VatClass` added (Zero ≠ Exempt at the same 0%). Kapow's 14,740-item band reclassified **zero-rated**. **No money moved** — both are 0% output tax; only the recovery position changes, in Kapow's favour |

**Past returns were restated, not estimated.** `GET /api/v1/reports/vat-corrections` re-runs both
methods over the same rollups per VAT period, reporting Box 1 as filed, Box 1 restated, and the net
error. ⚠ **Periods follow the business's HMRC stagger group**, not calendar quarters — attributing a
correction to the wrong return is the easy way to get this wrong, and it looks fine on screen. At
£10.77 the arithmetic points at an adjustment on the next return, not a VAT652. ⚠ **Plutus does the
arithmetic half only and says so on the screen** — whether the original error was *careless* (which
forces a VAT652 however small) is Matt's and his accountant's judgement, and the software must never
appear to have made it. **It files nothing.**

### The three things a till must never re-derive

1. ✅ **Kapow sells NOTHING exempt** (Matt, 2026-08-08). Every item is standard-rated (5,603) or
   zero-rated (14,740); the reduced band exists but is unused. **Partial exemption does not apply to
   Kapow** — all supplies are taxable, so input tax is recoverable in full. ⚠ `VatClass.Exempt` stays
   in the model because **other tenants will need it** and it is a real UK class, but it must **never**
   be assigned to a Kapow band. Anything that reintroduces it is a bug.
2. ⚠ **`vatRateBp` comes from the PRICE PAIR, always** — `round((unitInc/unitEx − 1) × 10000)`, so
   wobbled values like 2002bp are **correct and expected**. Never send the catalogue's snapped-clean
   band; never use rate arithmetic (`gross × bp/(10000+bp)`), which disagrees with the receipt.
   `vatAmountPence` = `lineGross − lineEx`, where `lineEx` scales the discount by the ex/inc ratio.
   **`api.ts:958–1019` is the reference implementation — mirror it exactly, never "improve" it.**
3. ⚠ **Consistency is STRUCTURAL, not per-client** (Matt asked whether the bands were consistent
   across tills, "also future tills based on Mac and Linux"). `VatBandStamp` backfills the band
   server-side for any line arriving without one, in `SalesIngestService` — the single choke point
   **every channel passes through, including the webstore connector**, whose Woo mapper sent no band at
   all. A till on any platform is therefore correct by default before it implements band awareness.
   ⚠ **A band the client STATED is never overwritten** (the voucher treatment overrides the catalogue
   for gift cards). And `VatAccounting.BandFor` **returns null on a TIE** rather than "nearest, first
   wins" — that silent arbitrary pick would have attributed 0% takings to whichever band sorted first,
   corrupting the exact number the work exists to produce.

## 23. The 2026-08-11 hand-run — all fourteen findings closed

Matt ran a shop day on till 1.41.0 and reported **A–N**. Every one is answered, and six more raised
that evening (**O–T**). Full detail with causes: [`handrun-2026-08-11.md`](../archive/handrun-2026-08-11.md).

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

## 24. Platform work that landed alongside

- **Permissions reach a tenant on BOOT** — `RolePermissionReconciler`. It used to be a manual seed
  step somebody had to remember, and its failure was invisible: the permission simply did not exist,
  so every operator was refused politely. Matt asked why on 2026-08-11 — *"is this not something that
  can be added when the app is compiled, or pushed from the back end?"* — and the honest answer was
  that it did not have to be. ⚠ **Additive only:** it never removes a grant and never overwrites a
  ceiling a shop set for itself, which is what makes running it on every boot safe rather than
  reckless. ⚠ It reads the tenant list **from the database** rather than hardcoding Kapow, which was
  the difference between working everywhere and working on one tenant while the other silently kept
  the old permission set.
- **The heartbeat re-reads the roster**, and a disabled operator is signed out with Matt's wording.
- **The heartbeat checks for updates** — advisory only; there is no self-update for MAUI.
- **`Device.LastSeenUtc`** — "last online" was the enrolment date for every till, for ever. Written
  throttled to one write per till per five minutes; the portal reads **live presence → persisted →
  "never"**. ⚠ And the fix broke the portal's rendering the next day: moving the value from a MySQL
  column (`Kind=Unspecified`, serialised with no suffix) to `DateTime.UtcNow` (`Kind=Utc`, serialised
  **with** a `Z`) made `new Date(value + "Z")` produce `…ZZ` — *"Invalid Date"* on every row. **A fix
  that changes where a value COMES FROM can break a consumer that never changed.** Now an `apiDate()`
  helper on all four date renders.
- ⚠⚠ **The nightly database backups were EMPTY for two days and logged `backup ok`.** Found by
  checking a pre-deploy dump instead of assuming it. Two faults, and it needed both to stay hidden:
  the script connected over **plain TCP**, which the 08-09 password rotation broke, and
  `mysqldump | gzip && mv` takes its status from **gzip**, which succeeds on empty input. The 7-day
  prune would have deleted the last good dump on 2026-08-16 — the deletion and the corruption on the
  same clock. Fixed and proven both ways: a real run gives 63 MB / 101 tables; a deliberately broken
  one exits 1, logs `⚠ BACKUP FAILED`, and leaves the good dump alone.
- **Printing rebuilt on the Plutus Till Agent** (till 1.33.0). ⚠ Matt: *"I still cannot see a printer,
  it says wifi is turned off … The webtill can see the receipt printer fine."* Both true, one cause:
  **the two tills were on different hardware routes.** The web till POSTs a rendered document to the
  agent (`127.0.0.1:9123`), which prints through the ordinary Windows print queue, so every
  driver-installed printer is available. MAUI asked Windows for a `PointOfService` device — a driver
  profile almost no receipt printer ships — and the picker for that selector is generic device chrome,
  which fills an empty list with stock advice about Bluetooth and Wi-Fi Direct radios. ⚠ **"Wireless
  is turned off" was never about the printer.** One route now; OPOS survives one level down, and **if
  no agent is installed everything degrades to the old behaviour by design**.
- **Syncfusion is off every screen an operator can reach** — the licence question closes itself
  ([L4](#l4--till-side-reporting)).

## 25. Where MAUI is AHEAD of the web till

Offline sign-in with an expiry · card surcharge · roster re-read on the heartbeat · sign-out on
disable · reprint a past sale · reopen a Z close.

⚠ **Parity is not a synonym for "catch MAUI up."** Matt, 2026-08-08: *"The tills need to be in parity.
This is the point of the MAUI retrofit. In addition when adding new functionality, it needs to be
added to all tills going forward."* A row where MAUI is ahead is **exactly as much of a parity failure
as the reverse** — §5 is the list, and W1/W4 are the two that matter.

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
