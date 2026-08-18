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
per [`index.md`](../index.md). ⚠ That lift matters: binding defaults 1–21, §15's pitfalls and §16's
item-identity seam all outlive the retrofit, and archiving them unlifted buries them.

## 0. Live state, and every ruling that binds this work

> ⚠ **Moved here from `HANDOVER.md` on 2026-08-17.** Matt: *"move everything relevant from handover.md
> into the Maui-Retrofit.md document… I only want the handover to be for the following day."* The
> handover is now a next-session brief; **this section is where the durable state lives.** The
> day-by-day narrative back to 2026-07-28 is kept verbatim in
> [`archive/handover-history-to-2026-08-17.md`](../archive/handover-history-to-2026-08-17.md) — history,
> not instruction, and every "START HERE" in it is superseded.

### 0.1 What is deployed, and what is only built

| | Version | State — as of **2026-08-18 (evening)** |
|---|---|---|
| ~~Backend 1.17.1~~ | — | ⛔ Superseded by **1.17.2** (row below), 2026-08-18. Kept for the verification rule it records: verify on the DB path (`POST /api/v1/tokens/device`), **not** `/swagger` — which answered 200 throughout the 2026-08-09 outage |
| ~~Web till 1.16.0~~ (`index-DrOux3wm.js`) | — | ⛔ Superseded by **1.17.0** the same evening; it is the rollback copy. Original note: ✅ DEPLOYED 2026-08-18 & verified on all four axes.** ⚠⚠ **THIS IS THE BUILD THAT CAN TAKE A PAYMENT AGAIN.** Checkout had crashed on **every** attempt since 1.10.0 with React error #310 — `CheckoutDialog` called `useMemo` six lines below a guard clause, so it rendered N hooks on mount and N+1 once the payment methods loaded. Found by Matt clicking Checkout; invisible to `tsc`, to `vite build` and to all 205 vitest cases, because **no test in this project mounts a component**. ⚠ `npm run build` now runs **eslint** with `react-hooks/rules-of-hooks` as an error, and the rule was **watched catching the real bug and failing the real build** before being trusted (§W10b). Rollback `current.pre-1.16.0` = 1.15.0 (`index-iU8ZRsnt.js`). ⚠ **1.16.0 is CSS only** — the parked-baskets ✕ was inheriting `width: 100%` from the scan-results list and rendering half the dialog wide. ⚠ **1.14.0 added the ✕ on all 8 web dialogs** (till-design **D4**); **1.15.0** moves the price-adjust arithmetic onto the shared `PriceAdjust` rule — same answers except sub-penny on manual overrides, where the shared one is correct. |
| ~~Web till 1.12.0~~ (`index-fooggnrJ.js`) | — | ⛔ Superseded 2026-08-18 — **it could not take a payment** (see above). Original note: ✅ **DEPLOYED 2026-08-17 & verified on all four axes** — the till host names the hash, 362,039 bytes (not the ~1 KB SPA fallback), `1.12.0` and a string only this change introduced both present, no unsubstituted defines, portal confirmed still on `index-X2HmT_BH.js`. Rollback `current.pre-1.12.0` = 1.10.0 (`index-DBZqCOhi.js`). ⚠ **This is 1.11.0's content plus the store-info fix** — 1.11.0 never shipped as an artefact, so §5b's slices reached the shop inside this build. ⚠⚠ **Deployed ≠ verified by a person**: every §5b register row is still **🟡** and **§W1–§W9 have never been run** |
| ~~Portal 1.9.0~~ | — | ⛔ **NOW DEPLOYED inside 1.10.0**, 2026-08-18 evening. Original note: The opening-hours write path: the advanced-JSON box is validated, Save is gated on the hours being readable, the section forces itself open when the stored value is not, and it has **its own Save button** (it used to say *"press Save in the address row"*). Live is **1.8.0**. ⚠ This is the half that stops unreadable hours being CREATED — the tills can only report them |
| ~~Backend 1.17.4~~ | — | ⛔ Superseded by **1.17.5** (row above) the same evening. Original note: ✅ DEPLOYED & verified — swagger 200 **and** the DB-path probe (`POST /api/v1/tokens/device` with a junk-but-non-empty GUID → **401 "Device not enrolled or revoked."**; ⚠ `Guid.Empty` is rejected *before* the DB, so the runbook's "junk id" must be a real GUID or the probe proves nothing). Rollback `~/PLUTUS/backend.pre-1.17.4`. Carries: the `pos.reports.view` gate on `GET /api/v1/stock/levels` (**without it the new Stock reports read for nobody below Store Manager**), the mandatory credit reason, and the before/after customer-edit audit. ⚠ `appsettings.json` verified byte-identical before the swap. |
| Portal | **1.10.0** (`index-Befe7Xwa.js`) | ✅ **DEPLOYED 2026-08-18 (evening) & verified on all four axes** — right **host** (`admin.`plutus…, not the till's `plutus.…`), the served hash matches the build, **489,161 bytes** (not the ~1 KB SPA fallback), and a string only this change introduced is present while `"goodwill grant"` is **absent**. Rollback `current.pre-1.10.0` = 1.8.0 (`index-X2HmT_BH.js`). ⚠⚠ **This finally ships 1.9.0's opening-hours validation**, which had been built-not-deployed since 2026-08-17 — the portal's advanced-JSON box had no validation at all and its Save lived in another section. Plus the credit reason: the field is marked, the button gated, and nothing substituted. ⚠ The web till was confirmed **untouched** (still `index-DrOux3wm.js`). |
| ~~Backend 1.17.5~~ | — | ⛔ Superseded by **1.17.6** (row below) the same evening. Original note: ✅ DEPLOYED 2026-08-18 (evening, 2nd) & verified on both axes** — swagger 200 and the DB-path probe answering *"Device not enrolled or revoked"*. Rollback `~/PLUTUS/backend.pre-1.17.5`. ⚠⚠ **Carries the half of the §G38 fix that cannot live on the till**: an explicit search on `GET /api/v1/loyalty` now reaches every active customer, so a member who has just been signed up (no tier, no credit) is findable — and can therefore be given a tier. Without this the till still shows nothing. Also everything in 1.17.4. |
| ~~Backend 1.17.6~~ | — | ⛔ Superseded by **1.17.7** the same evening. Original note: ✅ DEPLOYED (evening, 3rd) & verified on both axes** — swagger 200, the DB-path probe answering *"Device not enrolled or revoked"*, and `/api/v1/loyalty` answering 401 (alive, gated). Rollback `~/PLUTUS/backend.pre-1.17.6`. ⚠⚠ **THE LOYALTY LIST NOW SHOWS EVERY ACTIVE CUSTOMER.** Matt: *"I still cannot see them."* Both members were in the database all along (Susan Testerson 0000103, Brian McTesty 0000099) — **the list was hiding them**, because it returned only holders of a Membership or a CreditAccount and a new sign-up is neither. ⚠ My first fix (1.17.5) widened only the SEARCH and kept the default narrow; that still showed nothing to anybody who simply opened the tab, which is what an operator does. ⚠ The argument: `MemberNoAllocator.NextAsync` runs on **every** customer create, so every customer **has a membership number and IS a member** — a tier is an upgrade. "Add member" minting a number while the member list denies them cannot both be right. ⚠ **Fixes the web till too** — `LoyaltyPage.tsx` calls `fetchLoyalty()` with no search and had the identical blind spot. Also everything in 1.17.4/1.17.5. |
| Backend | **1.17.7** | ✅ **DEPLOYED 2026-08-18 (evening, 4th) & verified on both axes.** Rollback `~/PLUTUS/backend.pre-1.17.7`. Adds `createdAtUtc` to the loyalty projection — the **Created** column on both tills is this field, so an older backend leaves it empty everywhere. Carries everything in 1.17.4–1.17.6. |
| Web till | **1.17.0** (`index-b3Er8Ggn.js`) | ✅ **DEPLOYED 2026-08-18 (evening) & verified on all four axes** — right host (`plutus.…`, not the portal's `admin.plutus.…`), served hash matches the build, **362,609 bytes** (not the ~1 KB SPA fallback), and `createdAtUtc` present in the served bundle. Rollback `current.pre-1.17.0` = 1.16.0 (`index-DrOux3wm.js`). ⚠ Carries **Email as its own column and a Created column**, the same two MAUI gained — changed in one commit so the two screens stay comparable. Portal confirmed untouched. |
| till-maui | **1.90.0** | ✅ **BUILT 2026-08-18 (evening, 4th)** at `D:\tmp\plutus-till-1.90.0`; `1.90.0+56746953` = HEAD, the new columns verified inside the binary. ⚠ The only build on the box. ⚠ Needs backend 1.17.7. |
| ~~Backend 1.17.2~~ | — | ⛔ Superseded by **1.17.4** (row above), 2026-08-18 evening. Original note: ✅ DEPLOYED & verified on the DB path (`POST /api/v1/tokens/device` answers, not `/swagger`), report routes reached (401 unauthenticated rather than 404/500), no startup errors. Rollback `~/PLUTUS/backend.pre-1.17.2`. ⚠ Carries the **`pos.reports.view` alternative on the five report endpoints that lacked it** — see B-reporting |
| ~~Portal 1.8.0~~ (`index-X2HmT_BH.js`) | — | ⛔ Superseded by **1.10.0** (row above), 2026-08-18 evening. It is the rollback copy. |
| Agent | **1.4.0** | ✅ Published — the web till's **Settings → Hardware** offers it (HTTP 200, 70,293,789 bytes) |
| ~~till-maui 1.73.0~~ | — | ⛔ **SUPERSEDED and DELETED 2026-08-18** by 1.74.0 (row below) — one build on the box, no ambiguity about which to run. ⚠ Kept as a line because of what it recorded: this row said *"1.72.0, NOT BUILT, disk holds 1.71.0"* for hours while `D:\tmp\plutus-till-1.73.0` existed. **Eighteenth stale marker**, and the cheapest kind to check: `ls /d/tmp/plutus-till-*` |
| platform | 1.47.0 | Ships inside the others |
| **Live DATA** | — | ⚠⚠ **2026-08-17: the NatApp till's sales through 15_08 are IN.** 198 sales / £4,923.86 imported from `Kapow…15_08_2026.db` (delta vs the 23_07 seed), + the 48 items they reference, + the orphaned legacy till got a real row ("Kapow shop till (NatApp)") so the whole history attributes to **store 1** instead of the store-0 bucket. Four-way penny reconciliation green (£562,563.74 / £25,784.71 VAT / 21,888 sales). Dumps: `plutus-pre-20260817-l4-preimport.sql.gz` (+ copy on `D:\tmp`) and `plutus-post-20260817-l4-import.sql.gz`. **Full record: [`NatApp-Translation-Agent-Plan…`](NatApp-Translation-Agent-Plan-2026-08-05.md) §7.** ⚠ Anything the shop sells after 15_08 on the old till needs the **next bridge run** — now a routine documented there |

⚠ **A deploy is verified on the ARTEFACT, never on a 200.** Both hosts SPA-fallback to `index.html`,
so a 200 proves almost nothing: check the host names the new bundle hash, the bundle is the real size
rather than the ~1 KB fallback, the `__APP_VERSION__`/`__BUILD_TIME__` defines are substituted, and a
string only this change introduced is present. That last one caught the 2026-08-09 blank portal, which
both `tsc` and `vite build` passed straight through.

⚠ **`origin` CANNOT be pushed** — it is blocked, not behind. A **151 MB** zip lives in old history
(`3cc9e508`), which `upstream` already has and `origin`, 442 commits behind, does not; GitHub's
pre-receive hook refuses it. HEAD itself is clean — that file is a 134-byte stub and the largest blob
in HEAD is 0.7 MB. Fixing it needs an LFS migration or an orphan branch, i.e. history surgery on a
shared repo. **`upstream` is the off-machine copy meanwhile.**

### 0.2 ⚠⚠ Matt's rulings — the ones that decide what gets built

Chronological. **Each is a decision, not a preference — build against these, and if one looks wrong,
say so rather than quietly doing something else.**

| Date | Ruling | What it settles |
|---|---|---|
| 2026-08-08 | *"The tills need to be in parity. This is the point of the MAUI retrofit. In addition when adding new functionality, it needs to be added to all tills going forward."* | Parity is the DEFAULT. A feature is not done until its Part B row is filled for **every** till — ✅, or a ⬜ naming the work package that will close it |
| 2026-08-08 | *"Each till needs a specific version as they will end up diverging."* | One version file per deployable in `versions/`. ⚠ A shared number would force the web till to claim a release it had no changes in |
| 2026-08-08 | *"When you have enrolled a till, what is the point of seeing the Connect to Plutus tab?"* | Enrolment, not a local database, decides where a till starts |
| 2026-08-11 | *"As part of the heartbeat, the re-read of permissions needs to happen. If a user is disabled, the user needs immediately logging out with an information message."* | The roster rides the 60 s beat; revocation signs the operator out. ⚠ Only from a roster the server ANSWERED with — see `OperatorRevocation` |
| 2026-08-11 | *"No self update for MAUI."* | The update prompt is **advisory**. Nothing may refuse to sell over it. ⚠ The **agent** is the exception — see W5 |
| 2026-08-11 | *"Does the heartbeat from the till check for updates? All tills should do this."* | `ExpectedMauiVersion` / `ExpectedWebVersion` on the beat |
| 2026-08-10 | *"I am not going to renew Syncfusion, it seems like it can be replaced."* | No licensed control on any selling path. ⚠ L4 no longer buys its removal — see the L4 ruling below |
| 2026-08-13 | *"You cannot have a discount greater than the basket."* | Binding default 22a. Checked **before** the permission ceiling — "more than the basket" is true regardless of who is signed in |
| 2026-08-13 | *"All discounts need to be tracked."* | Every discount carries a reason; step-ups carry an authoriser (`DiscountAudit`) |
| 2026-08-14 | *"Base it on roles."* | A discount level **IS** a role's `pos.discount` `MaxPence`. ⚠ A separate tier entity would state a cashier's money limit twice with nothing to notice them disagreeing |
| 2026-08-14 | *"Credits are only ever earned, never purchased, not transferable to cash."* | Loyalty credit is a **DISCOUNT**, not a tender. ⚠ That same line separates it from a gift card, which IS purchased and IS a liability — hence zero VAT on activation |
| 2026-08-16 | *"Can you only deploy new MAUI tills when I ask please."* | Bump `versions/till-maui.txt` per slice, but **build only on request** |
| 2026-08-16 | *"Tables only."* | Reports match the web till's **table behaviour**; no chart. The portal is the home for charts |
| 2026-08-17 | *"Make it %"* | The discount box takes a percent NUMBER — `10` means 10%. Made it a **money** bug, not a label one. Fixed |
| 2026-08-17 | *"I would not install silently, I would inform with a 'Continue or cancel' option… But if they say no, it needs to remind them."* | Agent updates are **asked for** and a decline **returns**. See W5 |
| 2026-08-17 | ⚠⚠ *"Do not drop anything. I have a more recent DB to import and will need to translate where required and retain all legacy sales."* | **L4 is not a deletion.** Legacy history is imported and translated, not discarded — see L4 |
| 2026-08-18 | ⚠⚠ *"Store credit needs to be for a KNOWN customer. Adding credit needs to have a reason and be viewable in the customers history."* | **§5c item 2b, answered.** ⚠ **There is no anonymous store credit at all** — the "no known customer" case is not a supervisor-authorised path, it is **refused**. That is simpler than the plan assumed and closes the question it was blocked on. ⚠ Credit is a **liability the shop owes a named person**; issuing it to nobody creates money the shop cannot reconcile against anybody, and a bearer instrument is what a **gift card** is for (WP13, which already exists). ⚠ **A REASON IS MANDATORY on the way in**, and it is not a local log: it must reach the customer's history where a manager can read it later. So the reason travels on the credit movement, the same shape as `DiscountAudit` — reason mandatory, actor recorded, stored on the record rather than beside it. ⚠ *"viewable in the customers history"* means a **screen requirement as well as a storage one**: a reason nobody can read afterwards is not an audit trail. |
| 2026-08-18 | ⚠⚠ *"Portal shows which reports a till can show. Separate permissions need to be created for viewing them."* | **§5c item 5b, answered — and it is BOTH halves, not one.** ⚠ **(a) The portal curates the SET**: a till shows the reports the portal has published to it, not a catalogue hard-coded into each client. That makes `ReportCatalogue` a *superset the portal chooses from* rather than the answer, on every till. ⚠ **(b) Each report gets its OWN permission**, so "which reports exist here" and "who may read them" are separate decisions — today every report shares `portal.reports.view` / `pos.reports.view`, which is why widening that gate for a Supervisor widened it for **every** report at once (and why the same gate defect has now been fixed four times). ⚠⚠ **This is a new work package, not a §5c slice**: new entries in `PermissionCatalogue`, a per-tenant/per-till published set with storage and an endpoint, a portal screen, and both tills consuming it. ⚠ **It also settles a question nobody asked**: with per-report permissions, a till that is *published* a report it may not *read* must show nothing rather than a refusal — the publish decides the menu, the permission decides the door. |
| 2026-08-18 | ⚠⚠ *"A customer needs to have a unique ID, because people can change emails over time. Audit please."* | **§5c item 6's edit, answered — edit is ALLOWED, and audited.** ⚠ The unique id already exists and always has: `Customer.Id` is a UUIDv7 and `MemberNo` is the human-facing one. **The ruling is that neither the email nor any other editable field is ever the identity** — so changing an email cannot "redirect somebody's account", because nothing resolves a customer by email. That removes the objection the till's missing edit path was built around. ⚠ **"Audit please" is the condition, not an aside**: an edit records who changed what, from what, to what, and when — the `DiscountAudit` shape again, and the same reason (a change to somebody's record that nobody can trace is indistinguishable from a mistake). ⚠ It follows that **email must not be treated as unique** anywhere: two family members sharing an address is ordinary, and a uniqueness constraint on email would refuse a legitimate second member. |

### 0.3 ⚠ Open, and not tracked anywhere else

| | What | Where | Why it is still open |
|---|---|---|---|
| 🟠 | **`LoginViewModel.EnsureStoreAsync` throws on every sign-in** — `InvalidOperationException: Unable to track an entity of type 'StoreModel' because its primary key property 'Id' is null` | `LoginViewModel.cs` | Caught and harmless; the screen it fed is read-only off `StoreInfoCache`. ⚠ It also CREATES the legacy `Database.db` on every sign-in, which is what made the enrolment gate a one-way door. **Goes with step 25**, not 21 — see [L7](#l7--loginviewmodelensurestoreasync) |
| ⚠ | **The UI fixes of 2026-08-10 are held by REVIEW, not tests** | dialogs, navigation, checkout | Nothing in that family can be exercised without a UI host. Weaker than it should be for two overlay bugs in two days — which is why step 11b moved up the order, and why §8's USER-VERIFY list exists |
| ⚠ | **The store-gate deadline convention is unpinned** | `TillStoreAccess.UseAsync` callers | The next caller written without a deadline restores the 2026-08-10 fault in full. Held by convention and a code comment |
| 🔴 | ⚠⚠ **17 input-alert call sites do not handle the operator BACKING OUT, and each one can crash the till** | see the table below | `LaunchInputAlertAsync` returns **null** when the operator leaves without confirming — tapping outside, Escape, or the Cancel button. `data.TryGetValue(…)` on null is a `NullReferenceException`, and most of these sit in **`async void`** methods, so it goes to the dispatcher **unhandled and the app dies**. ⚠ **This is not theoretical — it is the same shape as the 2026-08-18 refund crash**, and it is reachable today by the gesture Matt already uses (*"I know you can click outside of the box to close it"*). **Only `ExecuteAdjustItem` was fixed** on 2026-08-18 (the dialog he reported); the rest were left because several are on money paths and *"what does cancelling mean here"* is a per-flow decision, not a blanket `return`. ⚠ Do NOT "fix" this by making the helper return an empty dictionary: **six call sites detect cancellation by testing for null**, and `SupervisorPrompt` is one of them — an empty dictionary there would fall through into reading a password that was never typed |

### 0.4 ⚠⚠ The lesson this project keeps re-learning

**Nineteen status markers have been found wrong in nine days** (ten by 2026-08-16, six more while
consolidating this document, and three on 2026-08-17), and almost every one failed the same way: a
claim about *behaviour* written from reading a call site instead of following what it calls.

- ✅ rows for work that was not built · ⬜ rows for work that was
- A **➖** ("not applicable") that stopped being true when the architecture moved, while a row eight
  lines below recorded the very move that falsified it
- `FileOperatorStore`'s header explaining that a move was impossible because of an EF 3.1 pin — that
  had been 9.0.18 since the .NET 10 upgrade
- `publish-agent.ps1` silently broken for ten days behind a `latest.json` that read as a current release
- ⚠⚠ **A ✅ THAT WAS TRUE AND USELESS, 2026-08-17.** Part B said MAUI's **Store Information** worked, and
  it did: the endpoint was called, the data arrived, the labels were populated. It was also **drawn in
  light grey on a near-white surface**, so not one field was legible — above an empty grey bar bound to
  the legacy store model and a panel printing a currency *format string*. Matt: *"MAUI looks nothing
  like the webtill."* ⚠ **A capability register cannot catch this**, and pretending otherwise is the
  mistake: *"can the till show its store details"* is answered by data arriving, not by anybody being
  able to read it. **Only a person looking at the screen finds this class of defect** — which is the
  argument for the hand-run, stated by the thing itself rather than by me.
- ⚠ **A "NOT BUILT" over a build that existed** (till-maui 1.73.0, §0.1). `ls /d/tmp/plutus-till-*`.
  The cheapest possible check, not done, for hours.
- ⚠⚠ **A NEW FLAVOUR, 2026-08-17 (W-P6): a ⬜ hiding a capability that was PARTLY built.** "Reprint a
  receipt" read ⬜ on the web till while the button, the sale lookup and the copy-marking had all
  existed for months — only the *printer* was missing. The row was not stale, it was **measuring the
  wrong thing**: a binary marker on a capability with two halves reports the state of whichever half
  the writer looked at. ⚠ It cost nothing here (the fix was one call) but it could as easily have
  been the reverse — a ✅ over a capability missing its money half.

**The rule: grep the callers, check the csproj, run the thing — before believing any marker, including
your own from last week.** ⚠ And **➖ is the most dangerous of the four**: ✅ and ⬜ both invite a check,
➖ invites none.

⚠⚠ **The 2026-08-17 addition to that rule: a test can be as misleading as a marker.** W-P7's
TypeScript money rule had **32 tests copied vector-for-vector from the .NET side** and the
ratio-first mutant still **survived** — `decimal` and `double` round differently, so the vector that
makes the C# test red passes unconditionally in JS. **A mirrored test proves the two files agree about
the cases somebody thought of in the other language.** Mutate the rule and watch it go red, or you
have a suite that reports coverage it does not have.

---

## Where it stands

**Counted from Part B, not estimated — recounted 2026-08-16: 77 capability rows.**
**42 ✅ both tills · 8 MAUI ⬜ · 14 MAUI 🟡 · 7 where MAUI is AHEAD of the web till.**

⚠ **Recounted 2026-08-16 after steps 27 and 26.** The ⬜ column halved because step 27 (loyalty, gift
cards, store credit) and step 26 (reporting, tables, cross-till lookup, receipt template) landed —
but **almost everything they moved went to 🟡, not ✅**, because NO PERSON HAS RUN ANY OF IT. 🟡 here
means "built, tested where testable, unverified on screen". Five status markers were found wrong in
one week; a sixth is not being added by calling untested screens done.

⚠ **Recount, not a re-estimate.** Two rows were added 2026-08-14 (the discount audit trail, ✅ both;
the discount ceiling + step-up, ⬜ on the **web** till), and **refund-only baskets moved ⬜ → ✅ on
2026-08-16 having been built for some time** — the fifth stale marker in a week. ⚠ **The gap is now
as much about the WEB till as MAUI**: seven rows where MAUI leads, against fifteen where it trails.

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
[13 Binding defaults 1–21](#13-binding-defaults--the-decisions-already-made) ·
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
| **W1** | **Reopen a Z-closed day on the WEB till** — ⚠ **folded into [§5b W-P5](#5b-the-web-till-parity-plan--w-p1w-p7)**, which carries the full build notes | 1d | MAUI has it (till 1.48.0); the web till does not. Matt asked for *"Web and MAUI"*. The two tills currently disagree about whether a closed day can be recovered — a supervisor on the browser is stranded until midnight. **Server side is done and live** (backend 1.15.0: `ZReopen`, a compensating event, and `CashDay.IsClosed` where the latest Z-mark wins) |
| **W2** | **Add item as ONE screen** | ½d | The EDIT screen was rebuilt as a single page (finding K, three attempts). **Add** still opens three questions then a form — the same shape that was wrong for edit. `EditItemPage` is written; this is a create mode on it |
| **W3** | **Portal screen for the expected till version** | ½d | `GET`/`PUT /api/v1/platform/till-release` is live and works; nothing sets it from a UI, so the heartbeat's update check cannot be switched on without curl. ⚠ The table is empty, which is correct — the feature is inert until a platform admin PUTs a version |
| **W4** | **Web till: roster + permissions on a cadence** (WP17.4) — ⚠ **folded into [§5b W-P2 + W-P3](#5b-the-web-till-parity-plan--w-p1w-p7)**, which carry the full build notes | 1–2d | ⚠ A disabled operator is signed out of MAUI within 60s; the web till has **no proactive check at all** — it signs out only when a request happens to 401 (`api.ts:50`), and login tokens are cached 12h with their permission set. Same change also stops it discarding the whole heartbeat response (`Locked`, `SyncNow`, `CatalogueCursor` are dead there) |

| **W5** | **Ship the agent with the till, and let the platform say it is out of date** | ~1½–2d | ⬜ **Matt asked for this 2026-08-17** — see the ruling below, which is the part that must not be lost. Two halves: (a) the agent travels in the till package and is installed to a **stable** path, (b) `ExpectedAgentVersion` on the heartbeat beside `ExpectedMauiVersion`. ⚠⚠ **The agent must NOT live inside the versioned till folder.** `plutus-till-1.71.0\` changes every release, and the auto-start registration records a path — putting the agent there would break auto-start **on every upgrade**, which is the 2026-08-17 fault on a schedule. Ship it in `…\agent\`, install it to `%LOCALAPPDATA%\Plutus\Agent\` (no admin, matching the per-user `HKCU` Run key). ⚠ **You cannot overwrite a running exe** — the update needs the agent asked to exit over loopback first, and that dance is the real work, not the copy. ⚠ **The web till cannot install anything** (it is a browser), so browser-only till PCs still need a manual install. ⚠ **This is the one out-of-date signal on the platform that can be ACTIONED rather than merely shown** — the MAUI till has no self-update by Matt's decision, but the agent is a single self-contained exe in a per-user folder that the till can replace. |

> #### ⚠⚠ MATT'S RULING — 2026-08-17: an agent update is ASKED FOR, and a "no" is REMINDED
>
> Verbatim: *"I would not install silently, I would inform with a 'Continue or cancel' option, do not
> want to be doing things when nobody knows. But if they say no, it needs to remind them."*
>
> **So W5(a) is not a silent copy.** Three requirements, and the third is the one most likely to be
> dropped because it is the only one that needs state:
>
> | | |
> |---|---|
> | **Ask** | A **Continue / Cancel** prompt naming what is about to happen. Never a background swap |
> | **Obey a no** | Cancel means the current agent keeps running. It must **not** re-ask on a loop — asking every 60s *is* a silent install with extra steps |
> | ⚠⚠ **Remind** | A declined update **comes back**. It must not be dismissible for ever: an operator who says "not now" during a rush has not said "never", and a till left on an old agent because nobody re-asked is exactly the drift this platform keeps finding |
>
> ⚠ **The reason for the prompt is diagnostic, not courtesy.** Matt's *"do not want to be doing things
> when nobody knows"* is the operative half: a printer that stops working right after a silent agent
> swap is un-attributable, and somebody spends an afternoon on the printer. A prompt makes the
> connection obvious to whoever was standing there.
>
> ⚠ Design note for whoever builds it: "remind" needs a **declined-at** stamp and an interval, and the
> reminder has to survive a restart — otherwise closing the till becomes the way to dismiss it for
> ever, which is a "no" nobody chose. ⚠ And it must never interrupt a live basket; the cadence already
> has `BasketIsOpen` for exactly this.
>
> #### ✅ THE DECISION IS BUILT — `Client.Core/AgentUpdatePrompt.cs` (2026-08-17)
>
> The ruling above is now code, with 18 tests and two mutants killed. **What remains of W5 is the
> mechanics, not the policy.** `Decide(installed, offered, declinedVersion, declinedAtUtc, now,
> basketIsOpen)` → `Ask` | `Nothing`, and it encodes:
>
> - ⚠⚠ **A decline is PER VERSION.** Saying no to 1.4.0 is not saying no to 1.5.0 — blanket would let
>   one "not now" suppress every future update on that till, permanently and invisibly. *(mutant: kills 1)*
> - ⚠⚠ **A decline expires** after `RemindAfter` = **4 hours** — inside the trading day, outside the
>   queue of customers. Shorter is nagging, and a nagged operator dismisses without reading, which is
>   the silent install again with an extra click. *(mutant: kills 2)*
> - ⚠ **A decline with no timestamp asks again.** The failure that costs a shop is an agent that never
>   updates and never mentions it, not one that asks twice.
> - ⚠ **Never mid-basket**, never a downgrade, and **no agent at all is not an update** — "install one"
>   is a different sentence and a different decision.
> - ⚠ Tolerates the `1.4.0+<sha>` informational suffix the artefacts actually carry.
>
> **⬜ Still to build, in order:**
>
> | | ⚠ |
> |---|---|
> | **Persist `declinedVersion` + `declinedAtUtc`** | Two `MetaKeys` entries. ⚠ It **must** survive a restart, or closing the till becomes a permanent "no" |
> | **Ship the agent in the till package** | `plutus-till-<v>\agent\`. ⚠ **Install to `%LOCALAPPDATA%\Plutus\Agent\`, never inside the versioned folder** — that would break auto-start on every release, which is the 2026-08-17 fault on a schedule |
> | **The copy itself** | ⚠ You cannot overwrite a running exe. Ask the agent to exit over loopback, copy, relaunch — this is the fiddly part, not the decision |
> | **`ExpectedAgentVersion` on the heartbeat** | Beside `ExpectedMauiVersion`/`ExpectedWebVersion`, plus the portal field. ⚠ Lets the PLATFORM drive it rather than each till's package |
>
> ⚠ **And part of W5 already existed** — the web till hosts the download (`public/agent/` +
> `latest.json` + the Hardware card). So the browser side is done; W5 is the MAUI half.

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

✅ ✅ **THE MONEY HALF IS DONE — 2026-08-16 (till 1.69.0), and the silent trap was DESIGNED OUT rather than watched for.**

`BasketItem` and `BasketNote` now hold **integer pence** as their source of truth
(`PricePence`/`PriceExTaxPence`), and `CheckoutCommit` reads them straight — the
`Pence.FromDecimal(item.Price)` conversion is gone, along with the comment that had to argue it was
lossless every time somebody touched the path.

⚠⚠ **`Price` STAYS A DECIMAL IN POUNDS, and that is the whole point.** The plan said "`BasketItem` to
long pence"; done literally, the rows' `StringFormat='{0:C}'` bindings would render **£3.30 as
£330.00**, silently, on every row — the trap this step's own notes call out. A pounds-shaped VIEW over
a pence store removes the trap instead of relying on somebody spotting it. **Deliberate deviation from
the plan's wording, in service of the plan's reasoning.**

⚠ Mutation-checked, and this is the one that matters: make the pounds view return pence and **8 tests
go red** — including the parked-basket round trip and three checkout reconciliations. The £330 bug is
now caught by the suite rather than by eye.

✅ **The 28 type tests are down to 9**, and the remaining ones are honest: `IsReturn` is on
`IBasketRecord` with **one** implementation (`this is BasketReturnItem`), and every question of the
form *"is this a return?"* now asks the flag. What still tests the TYPE genuinely needs the subclass's
own data — `ReturnSaleId`, `Reason` — plus `BasketDataTemplateSelector`, which picks the row template
by type and would render the wrong row silently if got wrong.

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

### Step 22 — WP7 theming · ✅ **DONE 2026-08-17 (till 1.73.0)**

> ✅ **Both halves landed.** **7a**: ClientUI's palette ported **verbatim** under the same `x:Key` names
> into `Resources/Styles/Colors.xaml`, merged in `App.xaml` — so it is in place **before** [L10] drops
> that project, and AppClient went from **no theme resources at all** to a full palette. **7b**: the
> pushed theme, on the 60s beat, cached in `MetaKeys.EffectiveTheme` and applied **before the first
> screen** so there is no flash of the stock palette.
>
> ⚠⚠ **Matt, 2026-08-17: *"Changing the theme in the portal needs to be consistent across the web and
> MAUI till."*** That is why the parse-and-validate rule went to `Client.Core/ThemeSlots.cs` rather than
> into the MAUI service — a **C2 twin** of `theme.ts`, 41 tests, two mutants killed. The seven slots are
> XAML keys **named after the slots** (`ThemeAccent`, `ThemeSurface2`, …) rather than remapped onto the
> legacy palette keys: remapping works once and diverges the first time either palette is touched.
>
> ⚠ **`colorsJson` is opaque and the server does not validate it**, so `ThemeSlots` is the only thing
> between a bad value and an unreadable till: six-digit hex only, a bad or absent slot falls back to
> stock rather than a substitute, and ⚠⚠ **a malformed blob still applies the base mode**.
>
> ⚠ **Receipts verified immune** — no print path reads a `Theme*` key. §G31e hand-tests it, because
> printing from a dark scheme once put near-white ink on paper.
>
> ⚠ **L10 is now unblocked**: the palette has landed, so `Plutus.Frontend.ClientUI` can be dropped from
> `Plutus.slnx` without the till losing its colours. That removal is still Matt's to action.
>
> ⚠ 🟡 not ✅ on Part B until §G31 is hand-run — and §G31a is the one that matters: **both tills side by
> side, same assignment, same colours.**

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

### Step 24 — WP8 Users screen · ~~3d~~ → 🟡 **DoD MET 2026-08-16; ~1d left (the roster move)**

> ✅ **The screen is built (till 1.70.0).** The people icon on the **login screen** — list, add
> somebody **with their password**, reset a password. `Services/People/StaffDirectory.cs` (28 tests)
> + three new `PlutusApiClient` methods on the shared `GetLegacyAsync`. **All three DoD lines are
> met**; hand-test §G28 in [`Build/Test Maui.md`](../Test%20Maui.md).
>
> ⚠ **The password is part of ADDING somebody**, not a second errand — a person created without one
> cannot sign in anywhere. If the password half fails the create is still reported, because saying
> it all failed invites a second identical person and the legacy controller will hold both.
> ⚠ The **100-row cap is surfaced**: the list is ordered by `CreatedAt`, so it is the **newest**
> staff who fall off — exactly who this screen is opened for.
> ⚠ Leavers are marked `(left)` and sort last, or somebody sets a password for a person who cannot
> sign in and blames the password.
>
> ⚠ **Found, not fixed:** `StoreOptionsViewModel.AddEmployeeCommmand` (three m's) is bound as
> `AddEmployeeCommand`, with **empty** handlers behind a **commented-out** menu — dead by two routes.
> For the removal sweep.

Employee list/create + set password via the legacy `/api/Employee` and `/api/Auth/SetPassword` —
both still carry the web till, and **no client methods exist, so build them**. ⚠ Roles and effective
permissions stay **portal-side**: the MAUI parity target is the web till's smaller surface, not the
portal's. ~~MAUI's current add-user command is a stopgap dialog reading *"not available in this
version yet"*.~~

#### ✅ STEP 24 IS COMPLETE — the roster move landed 2026-08-17 (till 1.72.0)

`Services/Connectivity/DbOperatorStore.cs` replaces `FileOperatorStore` at all five call sites
(`TillCadence`, `LoginViewModel` ×2, `TillViewModel`, `ConnectionViewModel`). Two roster stores was
drift by construction — one of them is always the stale one, and which won depended on which code
path ran last.

⚠⚠ **THE PLAN'S TARGET WAS WRONG, and this is the useful part to carry forward.** It said
`TillDbContext.Operators`. **A roster cannot go there without dropping two things:**

| Lost | Consequence |
|---|---|
| **`Email`** | `LocalOperator` has no such column, and `OperatorLogin` matches on **email first** — relational storage would have broken the normal way staff sign in |
| **`AsOfUtc`** | Roster-level, with nowhere to live. It is what `OfflineCredentials.Assess` measures staleness from, and it is the **SERVER's** clock by contract *"precisely so a till with a wrong clock cannot decide its own credentials are fresh for ever"*. Substituting a locally-written `UpdatedAtUtc` would have converted a server-anchored horizon into a client-anchored one — silently, with nothing downstream able to detect it |

So the roster is stored as the **whole wire envelope** in `MetaKeys.OperatorRoster`, exactly as
`VatBands` and `ReceiptTemplate` already are. No schema change, no migration on a live till database,
and **nothing dropped** (Matt, 2026-08-17: *"Do not drop anything"*). ⚠ A relational move is still
possible later; it needs `Email` on the entity and a home for the envelope **first**.

⚠⚠ **THE ONE-TIME IMPORT IS THE LOAD-BEARING PART.** `DbOperatorStore.LoadAsync` finds nothing in the
database, reads the old JSON, and adopts it. Without that, a till upgrading while **offline** wakes
with no roster and **nobody able to sign in** — and no way to fetch one, because fetching needs the
network it hasn't got. Hand-tested by **§G30b**, which is the step worth running.

⚠ `FileOperatorStore` and `operators.json` are **kept, not deleted** — the file is the only roster an
offline upgrade can read. It can go once every till has run 1.72.0 online at least once.

⚠ **`FileOperatorStore`'s own header claimed the move was impossible** — that `Plutus.Client.Storage`
was on EF Core 9 against a direct EF **3.1.17** pin, so referencing it failed restore. The app is on
**9.0.18**, the `Database` project is on 9.0.18 including Proxies, and `Plutus.Client.Storage` was
**already a project reference**. The blocker died with the .NET 10 upgrade and the comment outlived
it, which is why this plan and that file disagreed about whether the work could be done at all.

*DoD:* ✅ the employee LIST renders from `/api/Employee`; ✅ a **set-password on an EXISTING employee**
takes effect on the next sign-in; ✅ an employee created on the till can sign in on the web till and
vice versa (⚠ §G28b step 4 — hand-test still to run); ✅ the roster reads from the till database, and
an **offline** sign-in still works on a till upgraded from a JSON roster (⚠ §G30b — hand-test still to
run, and it is the one that would close a shop if wrong).

⚠ ~~**Help / support tickets ride here**~~ — ✅ **DONE 2026-08-16 (till 1.70.0), ahead of this step.**
It needed none of step 24's employee work, so it went with the rest of the notices cluster:
**Settings → Help and support**, `Services/Support/SupportDesk.cs` + `Client.Core/SupportLabels.cs`
+ four `PlutusApiClient` methods (there was no shared half at all). `/api/v1/support/tickets`, gated
`support.tickets`, which every built-in role holds because a lone cashier with a dead till must be
able to shout for help. Uses the operator token, which was already wired.

### Step 26 — WP11 reporting + cross-till lookup · **10–12d** · ⚠ a rewrite, not a port

> ✅ **"TABLES ONLY." Matt, 2026-08-16**, asked whether the rebuild would look and feel like the web
> app. The honest answer was: **the figures yes, the interface no — and two parts were unspecified.**
>
> | | |
> |---|---|
> | **Figures** | ✅ Identical to the penny. Same endpoints, and anything the server cannot answer is **dropped, never locally recomputed** (default 17) |
> | **Table behaviour** | ✅ **BUILT to match** — orderable, searchable, page-sized, paginated, via `SharedKernel.TableSort`. ⚠ Before this, **no MAUI list sorted, paged or page-sized at all** |
> | **The summary chart** | ❌ **NOT built, by decision.** The portal is the right home for charts, and MAUI's chart screens are being deleted with Syncfusion — there is no charting library on this till and none is being added |
> | **OS chrome** | ❌ Never pixel-matches a browser — step 22 already settles that: *"content and branding will"* |
>
> ⚠ **The gap this closed was in the STANDARD, not just the code.** `table-standard.md` opened *"all
> three surfaces"* and MAUI was the fourth — which is why a till that claims to follow it had no
> sortable table anywhere. Corrected, and the rules now live in `SharedKernel.TableSort` so the two
> cannot drift. **+2d on the estimate**, hence 10–12 rather than 8–10.

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

> ⚠⚠ **SCOPE, SETTLED 2026-08-13 — read [`Loyalty Update across all tills.md`](Loyalty%20Update%20across%20all%20tills.md) before starting this
> step.** Matt's loyalty design expands the programme far beyond this step: a configurable credit
> currency, earning rules computed **at ingest**, an append-only loyalty ledger with holds, a rewards
> catalogue, and a member-facing portal. **Step 27 is the PARITY slice, not that programme** — it
> brings MAUI level with what the web till has *today*, and it is the foundation the programme's
> phase B builds on. The programme is platform-first work, phased and sized in the design doc's §17,
> and gated on its decision 1 (discount vs tender — **the accountant's call, expensive to change**).
>
> ⚠ **Binding default 20 applies throughout** (Matt's ruling): tiers are configured in the **portal
> only**; a till **assigns** tiers at Supervisor+ (`customers.manage` — already held); a till **adds**
> members at Cashier+ via the new **`pos.customers.add`** (create-only, `CheckAny` with
> `customers.manage`, online-only, **and the web till's create dialog gains the same gate in the same
> slice** — today it demands `customers.manage`, so a web-till cashier cannot add either).

> ⚠ **Binding default 21 — the programme's economics, decided 2026-08-13.** Matt: *"We already have
> tier'd discount. The credits/Gems value need to be set in the portal. Each credit/gem is worth
> £0.10. Earn one credit/gem for every £10 spent. Use as many credits/gems as you want on an order.
> Need to be able to set an expiry date or never."* → a `LoyaltySettings` row per tenant
> (`PencePerPoint` 10, `SpendPerPointPence` 1000, and `ExpiryChoice` `NotChosen`/`Never`/`AfterMonths`
> + `ExpiryMonths` — ⚠ **the till owner's explicit decision**, gating earning until made, because
> "never expires" is a commercial promise to members and must be *chosen*, not defaulted into), the ledger storing a
> **count of gems not pence** (the rate is a setting and settings change), redemption as a
> **basket-wide discount** reusing `DiscountApportionment.Across` + `VatLineMath.ForLine`, expiry
> **per earn-entry** consumed **oldest-first**, and refunds that claw back the earn *and* restore the
> burn. Full reasoning and the two edge cases in [`Loyalty Update across all tills.md`](Loyalty%20Update%20across%20all%20tills.md) §18. **None of
> this is step 27** — step 27 is parity with today's web till; the economics land in the programme's
> phase A/B on top of it.

⚠⚠ **A LIVE PARITY BUG, found 2026-08-13 while reviewing the design — this is not future work.**
The web till applies the member's tier discount at `TillPage.tsx:122–123`
(`if (m && !m.expired && m.autoDiscountRate > 0) dispatch({type:"applyMemberDiscount", …})`).
`autoDiscountRate` appears **nowhere** in `Plutus.Frontend.AppClient` — MAUI has no customer attach,
so it cannot apply it. **A Gold member is charged 10% more on the MAUI till than on the web till for
the same basket, right now.** "We already have tier'd discount" is true only of the browser. This is
precisely the drift Part B exists to catch, and it is the reason step 27 precedes the programme.

✅ **The backend half is DONE 2026-08-13 — `pos.customers.add` exists, and WP12 is now pure
consumption.** `PermissionCatalogue.PosCustomersAdd`, seeded to **every selling role including the
Cashier** (the only customer capability that reaches it), in `ImpersonationDenied` (creating records
in a customer's tenant is not diagnosis, and it burns a number from their sequence), and
`POST /api/v1/customers` gated **`customers.manage` OR `pos.customers.add`** — the comma-is-OR
`CheckAny` shape from `pos.stock.adjust`. ⚠ **Create-only**, with `PUT` and the membership endpoints
left on `customers.manage`; pinned by `A_cashier_can_ADD_a_member_but_not_edit_one_or_set_a_tier`,
**mutation-checked** (putting the new code on the edit gate is caught). Integration 173 → 174.

⚠⚠ **AND THE WEB TILL NEEDS THE SAME WIDENING BEFORE ANY OF THIS IS VISIBLE.** Its create dialog is
gated `canManageCustomers()` — `pipeline.ts:36`, `customers.manage` alone — so **a web-till cashier
still cannot add a member even though the server now permits it.** Until both tills widen, this is a
permission nothing exercises; it is the same slice per default 20, and Part B carries the web till as
🟡 for exactly this reason.

✅ **DEPLOYED 2026-08-13 — backend 1.17.0 and till-web 1.8.0 are LIVE.** ⚠ **Deploy note, corrected on
the day:** this said the deploy must be followed by `Plutus.SeedMigrator rbac`, on a stale runbook line
claiming RBAC seeding does not run on startup. **It does** — `RolePermissionReconciler` is a hosted
service that reconciles catalogue grants once per boot, and the grants were already present in **both**
tenants before I ran the tool by hand. Running it anyway also fired `MapKapowAuthActionsAsync`, adding
**7 role assignments** — all to one employee who already held `Owner`, so the net effective change was
nil (verified by diffing the new roles' codes against Owner's). Runbook corrected. ⚠ **What IS still
required: a sign-out/in**, because login tokens cache for 12h with the permission set baked in.

✅ **The members'-discount RULE is shared, 2026-08-13 — `SharedKernel/MemberDiscount.cs`.** Extracted
*before* the MAUI screen rather than after, per CLAUDE.md's C2 discipline: a members' discount is money,
and the alternative was MAUI guessing four eligibility conditions from TypeScript. `Applies` (an expired
membership grants nothing), `LineIsEligible` (**not** a return · **not** already discounted — NO
STACKING · **not** a gift card, which is stored value, not a supply), `ForLine` composing both with the
already-shared `LineDiscounts.Percentage`, and `Label` so both tills print the same words on a receipt.
18 tests; **all four exclusions mutation-checked**. C1 rows added.

⚠ **It has no caller yet, deliberately, and that is tracked here so it does not become a ninth
built-and-uncalled component** (§ the standing check). Its caller is the attach screen below. ⚠⚠ **AND
THE LIVE BUG IS NOT FIXED BY IT** — a Gold member is still charged 10% more on MAUI than on the web
till until that screen exists. The rule only guarantees that when the screen lands it will not add a
third answer.

⚠ Also found while extracting: **dropping the return exclusion fails only ONE test**, because
`LineDiscounts.Percentage` independently guards returns. That is defence-in-depth rather than a test
gap — two guards on a money rule is right — but do not "simplify" either away on the grounds that the
other exists.

✅ **The shared CLIENT layer landed 2026-08-13 — `PlutusApiClient` customer methods.** There were
**none at all** before, which is the mechanical reason MAUI has no attach screen. Mirrors `api.ts`
309–418 method for method (binding default 10): `SearchCustomersAsync` (take=10, encoded term),
`GetCustomerAsync`, `GetLoyaltyTiersAsync`, `CreateCustomerAsync`, `SetMembershipAsync`, plus
`CustomerSummaryDto`/`CustomerDetailDto`/`MembershipDto`/`LoyaltyTierDto`. 17 tests pinning the URL
and payload shapes — a client that talks to a slightly different URL fails at a counter, not in a
compiler — and the refusal wording, because "Forbidden" mid-queue tells a cashier nothing and the next
step is to ask a supervisor.

⚠ **Deliberately NOT exposed: customer EDIT.** Editing is `customers.manage`; a cashier holding
`pos.customers.add` must not find an edit call sitting next to the add call (default 20's create-only
line, enforced in the client's own surface rather than only at the server).

⚠ **`credit/redeem` is NOT here yet** — the store-credit tender needs the checkout plumbing
(`CREDIT_PAYID`) alongside it, so it lands with that slice rather than as an orphan method.

⚠ **Three findings while writing the tests, all recorded because each is a trap:**
1. ⚠⚠ **The doc example was WRONG, in five places.** `MemberNumbers`' header claimed
   `482 → "000482K"`; it is **`000482P`** (weights 7,3,1 over `000482` sum to 54, and
   `Crockford32.Alphabet[54 % 32]` is `P`). It came in from `further-enhancements-plan.md` and I
   copied it verbatim when moving the file — then built a test fixture from it, which failed. **A
   wrong worked example in the one place people copy from is a defect**; corrected. Also fixed
   `MemberNumberTests`' "prefixed but too long" fixture, which was invalid for *two* reasons and so
   did not isolate the rule it named.
2. ⚠ **A space is no test of URL encoding.** Removing `Uri.EscapeDataString` entirely SURVIVED,
   because `Uri` escapes a space to `%20` on its own. `&` is the character that matters — raw,
   `search=Marks & Spencer` reaches the server as `search=Marks` plus a stray parameter. The test now
   uses it and the mutant dies. My own comment had named `a&b` while the code tested a space.
3. ⚠ **`Uri.ToString()` unescapes**, so it cannot distinguish an encoded query from a raw one —
   assert on `AbsoluteUri`.

⚠⚠ **A LIVE MAUI DEFECT, FOUND 2026-08-13 WHILE PLANNING HOW THE MEMBERS' DISCOUNT WOULD ATTACH —
FIX IT BEFORE THE ATTACH SCREEN, because that screen adds a second discount to every member's basket
and walks straight into it.**

**Discounts totalling more than the basket make the sale un-completable.** `CheckoutCommit.ApplyAlterations`
computes each alteration's `grosses` **net of the discounts already applied**, and
`DiscountApportionment.Across` rightly refuses a discount larger than the lines it lands on (the
alternative is a negative-gross "sale"). So it **throws** `ArgumentOutOfRangeException`, `CommitAsync`
catches it, and the operator is told *"The sale couldn't be recorded on this till. Nothing has been
taken — try again."*

- ✅ **No money moves** — the one thing it gets right.
- ⚠⚠ **But "try again" never works.** The basket is permanently un-completable, and nothing says a
  discount is the cause or which one to remove. Mid-queue, the only way out is to clear the sale.
- ⚠ **It is reachable today, with no member discount involved.** Nothing caps a discount at the
  basket's value: `TillViewModel` builds each `BasketAlteration` straight from the entered amount
  (~1022–1043) with no check against `SaleIncTax`, and `Alterations` is a collection, so two are
  allowed. Verified: £5 off + £5 off an £8 basket throws with *"A discount of 500p was applied to
  lines worth 300p"*.
- **Pinned** by `DEFECT_discounts_exceeding_the_basket_throw_instead_of_refusing_politely` — which
  asserts **today's** behaviour, so **it will fail when the defect is fixed**, and that is deliberate.
  `A_discount_equal_to_the_whole_basket_is_allowed` pins the boundary as inclusive, so a legitimate
  100% staff discount is not caught by an off-by-one in the fix.

✅ ✅ **CLOSED 2026-08-13 — binding default 22(a) is built AND wired.** `SharedKernel/BasketDiscounts.cs`
(headroom **net of what is already off**, boundary **inclusive**, returns not headroom) now runs in
`TillViewModel.ExecuteAlterTransaction` **before any alteration reaches the basket**, so the operator
is refused while they can still act, with a message naming the headroom and what is already off.

⚠ **The alterations are BUILT FIRST, CHECKED, AND ONLY THEN ADDED.** The two branches produce the same
total by different arithmetic (one rounds per item, the other rounds the sum), so computing "what will
this come to?" separately for the check would be a copy that drifts from the thing it checks. Summing
the real alterations cannot drift. ⚠ It also means **nothing is half-applied** — a refusal after some
items had been altered would leave the operator undoing it by hand in front of a customer.

⚠ **The commit-time throw STAYS as the backstop** (default 12's shape: enforce at both gates), so a
basket assembled another way — a recalled parked basket, a future caller — still cannot produce a
negative-gross sale. The test is renamed `BACKSTOP_discounts_exceeding_the_basket_still_throw_at_commit`:
it no longer documents a defect, it pins that the second gate is still armed.

⚠ **The fix is NOT "cap it silently."** A £5 discount quietly becoming £3 is exactly the silent money
change this codebase exists to prevent. It belongs at the point of **applying** the discount, where
the operator can still act on it — `BasketDiscounts.Authorise` returns the headroom and what is
already off precisely so the message can explain a maximum lower than the basket total.

### ⚠⚠ Four live findings behind default 22 — (b) and (c) cannot be built without them

> ✅ **Since 2026-08-14 the blocking QUESTION is answered ("base it on roles" — see below), so (b) and
> (c) are now ONE piece of work: the discount authorisation has to reach the platform.** Findings 1
> and 4 are fixed; **finding 3 is the remaining work**, and finding 2 is a live money question still
> needing Matt's answer on units.

1. ⚠⚠ **CORRECTED — I GOT THIS WRONG FIRST TIME, AND THE TRUTH IS NARROWER AND MORE USEFUL.** I
   reported *"the operator ceiling is enforced by nothing"* after grepping for `Allows(`. **The
   method is `PermissionResolution.Can(...)`, and it has callers** — `SignedInOperator.Can`,
   `TillGate.Check` (`TillGate.cs:93`), and the Cash / Inventory / Settings / StoreOptions / price-
   override paths all pass amounts. **Ceilings ARE enforced.** The real finding is one line narrower:

   ⚠ **The DISCOUNT gate passes no amount.** `TillViewModel:979` calls
   `TillGate.Check(SignedInOperator, PermissionCatalogue.PosDiscount)` with **no `amountPence`**, so
   it asks *"may this operator discount at all?"* and never *"may they discount THIS much?"* — a
   cashier holding any `pos.discount` grant can take off any amount without stepping up. The comment
   immediately above it already says so: *"`pos.discount` is ceiling-capable precisely so it can be
   handed out with a limit; nothing was asking for it."*

   ✅ **FIXED the same day.** A second `TillGate.Check(..., PosDiscount, requestedPence)` now runs
   **after** the amount is known, so the ceiling bites and the step-up prompt appears. ⚠ **BOTH gates
   stay:** the early one refuses somebody who may not discount at all *before* making them type an
   amount they could never apply; the new one refuses the amount. Removing either brings back a
   defect — the first one's absence is what the comment at ~975 was written about.
2. ⚠⚠ **THE MAUI PERCENTAGE PATH DOES NOT USE THE SHARED RULE.** `TillViewModel` (~1030, ~1046)
   computes `item.Price * Decimal.Parse(input)` directly, under a box labelled **"Percent"**. That is
   *precisely* the bug `LineDiscounts.Percentage` documents itself as existing to prevent —
   *"entering 10 for '10%' multiplied the price BY TEN and the basket cheerfully charged it"* — and
   the shared fix is never called from here. ⚠ **Confirm what units that box expects before changing
   it:** if it wants a fraction the label is wrong, if it wants a percent the maths is, and only one
   of those is a money bug.
3. ✅✅ **CLOSED 2026-08-14 — the wire now carries both, on BOTH tills.** `LineMeta.discountAuthority[]`
   (`reason`, `amountPence`, `requestedBy`, `authorisedBy`, `authorisedByName`), built through
   `SharedKernel.DiscountAudit` → `Client.Core.DiscountAuthorityWire` on MAUI and the matching object
   literal in the web till's `api.ts`. ⚠⚠ **It is NOT on `LineDiscount`, and the original plan below
   was wrong to say it should be** — `discounts[]` projects into legacy `Transaction_Discount` rows
   keyed on a real `DiscountId`, so a manual discount's synthetic id would have FK-failed the
   projection of *every discounted sale*. The clue was in the file the whole time: the members'
   auto-discount is already deliberately omitted from that array for exactly this reason.
   ⚠ **No server change and no migration** — `SalesIngestService` stores `DiscountsJson` verbatim in a
   `longtext` column and only plucks named fields back out with `JsonDocument`, so the field survives
   the round trip and comes back on `SaleLineDto.DiscountsJson`. ⚠ **The reason is mandatory on both
   tills**; MAUI additionally records the supervisor, because it is the only till that has one.
   Details in `till-design.md` Part B + C1/C2. **The original finding, kept because the reasoning still
   applies to anything else added to this envelope:**
   ⚠⚠ **THE WIRE CARRIES NO REASON AND NO ACTOR FOR A DISCOUNT**, so ruling (c) is impossible today.
   `LineDiscount` is `{ id, rate }` and nothing else. The sale records `OperatorUserId` (who rang it)
   and the till — but **not who authorised a discount, nor why**. ⚠ Precedent sits next door:
   `SaleAdjustmentDto` carries a `Reason` for refunds and voids. The change is **additive** (`reason`
   and `authorisedBy` on `LineDiscount`), and ⚠ **the authoriser is the whole point of (b)** — if a
   supervisor approves a cashier's discount and the sale records only the cashier, the approval
   leaves no trace and the audit answers the wrong question.
4. **Discounts over the basket make the sale un-completable** — the defect above; 22(a) is its rule.

### ✅ STEP-UP — Matt chose it, 2026-08-13. ⚠ AND IT IS ALREADY BUILT.

**I was wrong to say no step-up machinery exists.** `TillViewModel.RequestSupervisorOverrideAsync`
(line 533) is a complete step-up and is **already wired into the discount path** (line 984), the
price-override path (587) and one more (1640):

- `SupervisorPrompt.AskAsync()` collects the supervisor's own credentials;
- `OperatorLogin.AuthoriseOverrideAsync(requestedBy, emailOrId, password, permission, amountPence)`
  verifies them **against the synced roster, so it works offline**, and checks the authoriser holds
  the permission **at that amount**;
- ⚠ **self-approval is already refused** — `OperatorLogin.cs:204`, *"the same person authorising
  their own action is…"*;
- it returns `AuthorisedByUserId` / `AuthorisedByName`, and the till logs Permission, RequestedBy,
  AuthorisedBy, AuthorisedByName and AmountPence;
- ⚠ an override that throws is treated as an override that **did not happen**.

**So ruling (b) is mostly built.** What is genuinely missing is only:

1. **Pass the amount to the discount gate** (finding 1) — without it the ceiling never bites, so the
   step-up prompt never appears for a discount however large. Needs the gate moved after the amount
   is entered.
2. ✅ **CLOSED 2026-08-14. It reaches the platform now.** `RequestSupervisorOverrideAsync` used to
   return `bool` — it verified a supervisor, wrote their name to the till's local log and **dropped
   it**. It now returns a `SupervisorGrant`, and the identity lands on the sale. ⚠ The local log
   entry STAYS: it is no longer the audit record, but it is the only trace of an override that
   authorised something which then failed to commit, and that is precisely the sequence somebody
   investigates.
3. ✅ **CLOSED 2026-08-14. The reason is captured, and it is mandatory** — on both tills, refused
   rather than recorded blank. ⚠ **The refusal sits where the discount is APPLIED**, not at commit:
   at commit the money is already on the basket and a customer is waiting, so dropping the sale over
   a missing string would cost more than it is worth. A basket parked before today and recalled after
   sends **no** authority rather than a blank one.

### ✅ ANSWERED — "Base it on roles." Matt, 2026-08-14. Ruling (b) needs NO new entity.

**A discount level IS a role's `pos.discount` ceiling.** `MaxPence` per role already is the level —
Cashier £5, Supervisor £50, Manager unlimited — it is owner-editable in the portal
(`AdminController.cs:306` exposes `maxPence`), a level is "added" by **adding a role**, and the
step-up over a level is `RequestSupervisorOverrideAsync`, which already exists and already refuses
self-approval.

⚠⚠ **The reason this is the right answer, not merely the cheap one: it keeps a cashier's money limit
in ONE place.** A separate "discount tier" entity would state the same ceiling twice — once on the
role, once on the tier — and there is no mechanism that would ever notice them disagreeing. The till
would enforce one and the portal would display the other, which is the C2 failure mode applied to
permissions instead of arithmetic. **Do not build a second home for a money limit.**

⚠ **So of the three gaps below, (b) needs only #2 and #3 — and both are the same wire change as (c).**
#1 was fixed on 2026-08-13.

⚠ **And it constrains how the members' discount is built.** MAUI's shape is a `BasketAlteration`
apportioned at commit, *not* the web till's per-line `discount` field, so a member's basket ends up
with **two** alterations whenever a manual discount is also present. `TargetsOf` already excludes
returns, but **not** already-discounted lines or gift-card lines — both of which
`MemberDiscount.LineIsEligible` excludes — so the member alteration must be **explicitly associated
with the eligible items** rather than left whole-basket, or it lands on lines the shared rule says it
must not touch. That association is also what keeps it clear of the throw above.

**WP12 — the rest.** Screens. Everything below the client is still ⬜ in both MAUI projects. Customer search/attach on the sale screen
(`GET /api/v1/customers?search=`, then a live `GET /api/v1/customers/{id}` for balance and
membership), a create dialog gated **`pos.customers.add` OR `customers.manage`**, a **tier-assign
picker gated `customers.manage`** reading `GET /api/v1/loyalty/tiers`, a store-credit tender
mirroring the web till's synthetic `CREDIT_PAYID`, and a management list off `GET /api/v1/loyalty`.

✅ **DONE 2026-08-13 — `MemberNumbers` is in `Plutus.SharedKernel`.** The format and check character
moved (a backend module MAUI may not reference, and the Crockford check character is a *rule*);
`MemberNoAllocator` **stayed** in `Plutus.Customers`, because handing out the *next* number needs a
tenant-wide counter and is server-only — the rule travels, the sequence does not. The scan-routing
predicate the till needs now exists as **`MemberNumbers.LooksLikeMemberScan`**: prefixed `C` + valid
check character → **customer attach**; prefixed and **invalid** → say the card did not scan cleanly,
rather than searching for an item that cannot exist. ⚠ Deliberately stricter than
`TryCanonicalise`, which still accepts a bare `"482"` for the human reading a card down the phone —
a *scan* of six digits is far more likely to be a product. C1 rows added to `till-design.md`.
Unit 938 → **949**; `platform` 1.30.0 → 1.31.0.

⚠⚠ **AND IT SURFACED A LATENT DEFECT — the member-number ceiling.** `Format` grows a seventh digit
past 999,999, but `TryCanonicalise` keys off length and accepts only `SequenceDigits + 1`, so
sequence 1,000,000 formats as `"10000007"` and canonicalises to **null**: the millionth member of a
tenant would get a card that prints, scans and **resolves to nobody**. The old comment claimed the
growth was safe. Now pinned by `Past_the_sequence_ceiling_a_number_formats_but_cannot_be_read_back`
and documented on `Format`: **the fix is to widen `SequenceDigits`** (both halves read that one
constant, so they widen in step and existing zero-padded numbers are unaffected) — ⚠ **never to
loosen the parser**, which would make a bare **EAN-8** canonicalise as a member number ~3% of the
time. Nobody is near 1,000,000 members; it is recorded so it is a decision and not a surprise.

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
| ⚠⚠ **Delete `LoginViewModel.EnsureStoreAsync`** | ⬜ **BLOCKED — and this row was WRONG TWICE. Corrected against the tree 2026-08-14.** ❌ *"still throws every time"* — **it cannot throw at all**: the whole body sits in a `try` whose `catch` swallows to `CrashLog` precisely so a missing store can never block sign-in (`:394`). ❌ *"this is a deletion, ~½d"* — **deleting it NullReferences two inventory screens**. Step 14's Meta cache replaced the store's *details* for display, but **`Store.Id` is the blocker, not the details**, and two screens dereference it outright: `AddEditViewModel.cs:330` and `ViewAllViewModel.cs:1324` (⚠ the code's own comment names only the first — corrected in the same commit). `Database.cs:43` also uses it but is null-guarded, so it degrades rather than breaks. **It goes with step 25**, when inventory moves off the legacy store and nothing needs a legacy store id — not before. ⚠ Do **not** "fix" this by null-coalescing the two dereferences to `0`: that silently writes stock rows against store 0. ⚠ Two printing call sites reference its five paths in comments (`PosPrinterManager.cs:150`, `TillAgentPrinting.cs:104`) — **a missing store must not lose the receipt**, and both already handle null |
| **Un-enrol request + manager approval** | ✅ **DONE 2026-08-16 (till 1.70.0).** *"Ask for this till to be removed"* sits on the **Plutus tab** beside *Forget this till*, and `TillCadence` step 6 now polls device status on the 60s beat so a **revoked till actually stops** — `DeviceRevocation` (19 tests, mutation-checked both ways) decides, `TillCadence.DeviceRevoked` → `App.ForceSignOut` acts. ⚠ The polling half was the one that mattered: `ConnectivityProbe` already knew how to ask but ran only at sign-in and on the Plutus tab, so a till revoked mid-shift was never told and kept selling for up to its 12h token life. ⚠ Only an explicit `Revoked` stops anything — a failed poll, 401/403, 404 and unknown statuses all keep trading, because closing a shop on a network blip is the worse outage. Original note: WP4's last piece; there is **no client code at all** (grep for `unenrol` in the app returns nothing). The server side is ready — `POST /api/v1/tills/unenrol-request` got its device-token policy in step 19. ⚠ **`PendingRemoval` is not a stop signal, deliberately**: halting a till the moment someone requests it back would make un-enrolment a way to take a shop's till down. Only `Revoked` stops, and the till learns which from `GET /api/v1/tills/devices/{deviceId}/status` — and it **must poll it**, because device tokens are bearer tokens with **no server-side denylist**, so a revoked till otherwise keeps working until its 12h token expires |

### The cluster with no step at all

| What | ~ | Detail |
|---|---|---|
| ✅ ~~**Platform notices**~~ — **ALL FOUR DONE 2026-08-16 (till 1.70.0)** | ~~3–4d~~ | ✅ Banner on the **Till tab** (`Services/Notices/Noticeboard.cs`, cadence step 4c) for pick-notes + announcements; **Settings → Help and support** for tickets; the **app-update prompt was already built** (`ConnectionView.xaml:96`) and its row was a stale ⬜. ⚠⚠ **The one rule this layer added:** a **failed poll does not clear the board** — `NoticesOutcome.Delivered` false means "we could not ask", not "nothing to show", and confusing them takes a live incident banner off the screen the first time the broadband blinks. Marked stale instead, and the stale line shows only alongside something. Mutation-checked both ways. ⚠⚠ **Closing it found the two tills DISAGREED**: `ShowsOnATill` denies `Info` so an unknown severity still shows, but the web till had an **allow-list** — the same incident would appear on MAUI and vanish silently on the browser till. Converged (`api.ts showsOnATill`), pinned both sides. ⚠ `NoticeboardBindingTests` **parses the XAML**, because on this banner a silent binding failure looks exactly like success. Original note below. |
| ~~**Platform notices**~~ — announcements banner, help tickets, app-update prompt, pick-from-floor | ~~**3–4d**~~ | All small consumers on the existing 60s cadence, copied from the web till's shapes: `GET /api/v1/announcements/active` (Maintenance/Incident banner only — Info must not show), `GET /api/v1/notifications?unackedOnly=true` + acknowledge, and `/api/v1/support/tickets`. ⚠ **`NoticesClient` is built and appears in the entire AppClient once, in a comment** — it needs a cadence step *and* the XAML, which is why these rows were corrected from 🟡 to ⬜. ⚠ Tickets and pick-note acks need the **operator** token; a device token cannot pass a `perm:*` gate. **Cheapest carried on steps 22 and 24.** *DoD:* a seeded Incident announcement shows within a cycle and Info does not; an unacked pick-note persists across restart until acknowledged; a ticket raised on the till appears in the portal inbox and the reply comes back |

## 4. Smaller rows that ride along, and which step carries each

Real Part B gaps that do not need a step of their own. Listed so none is a surprise when its step
opens.

| Gap | Rides with | ⚠ |
|---|---|---|
| **Pick-from-floor notices** + **announcements banner** | ~~22 or 24~~ | ✅ **DONE 2026-08-16 (till 1.70.0)** — banner on the Till tab, cadence step 4c. Original note: corrected 2026-08-10 from 🟡 to ⬜ — the row claimed "pending only the banner XAML" |
| **Portal-controlled receipt template** | ~~26~~ | ✅ **DONE 2026-08-15/16 (till 1.67.0+)** — ⚠ **this row was stale**, found 2026-08-16 while writing the handover: `SharedKernel/ReceiptTemplateRules.cs` (the merge rule), `Client.Core/ReceiptTemplateWire.cs` (the parser) and `Services/Printing/ReceiptBranding.cs` (**one** overlay, three print paths) all exist and are wired on the 60s cadence. Original note: ⬜ — no schema or parser exists in any client; it is WP3's business, deferred with reason |
| **VAT band on the sale line** (`LineMeta.vatBand`) | 27 | 🟡 only because the **server backfills** any line arriving without one (`VatBandStamp`). MAUI is correct-by-default; it needs to send one only where the till knows something the catalogue cannot — a single-purpose gift-card line is `"standard"` **by the voucher treatment**, not by its catalogue row. ⚠ **Leave it null rather than guessing**: a stated band is never overwritten |
| **Refund-only baskets** | 11b | Partly there — `refundOnly` drives prompt wording and surcharge suppression |
| **Un-enrol request + approval** | ~~21~~ | ✅ **DONE 2026-08-16** — and with it the device-status poll that makes a revoked till stop |
| **Help / support tickets** | ~~24~~ | ✅ **DONE 2026-08-16** — Settings → Help and support. Closes the `support-heavy` churn signal |
| **Connection status** (network vs server vs revoked) | 28 | ⚠ Runs the OTHER way too — the **web till** is 🟡, still on `navigator.onLine` (WP17.3) |
| **App-update prompt** | ~~28~~ | ✅ **ALREADY BUILT** — found 2026-08-16 on `ConnectionView.xaml:96`, fed by `TillCadence.UpdateAvailable`; the ⬜ was stale. Advisory only; no self-update exists |
| ⚠⚠ **Remote lock of a lost or stolen till** | **Neither till has it — and it READS as built** | `Device.Locked`/`LockReason` ship, `HeartbeatResult` carries them, `SyncClient` surfaces them — but **nothing sets the flag and nothing enforces it**. `IssueDeviceTokenAsync` refuses only on `Status == Revoked`. ⚠ **Reach for Revoked in a real incident.** Enforcement must land **before** any control that sets the flag, or the switch stays fake. Full detail: [risk 4](#9-risks-this-document-does-not-solve) |

## 5. Where the WEB till is behind (parity runs both ways)

> ⚠⚠ **THE IMPLEMENTATION PLAN FOR CLOSING THIS TABLE IS [§5b](#5b-the-web-till-parity-plan--w-p1w-p7),
> written 2026-08-17 at Matt's request so an agent can execute it with no questions.** This table
> stays as the register; §5b is the work.

| Row | WP | Note |
|---|---|---|
| **Offline sign-in with an expiry** | WP17.1 | MAUI has tiered horizons (`SharedKernel.OfflineCredentials`). The web till **cannot sign in offline at all** — so the shop that loses broadband loses the till, which is the thing the whole offline design exists to prevent. ⚠ Needs WP15's test runner first: the horizons would be a C2 twin |
| **Roster on a cadence + sign-out on disable** | WP17.4 | **W4 above** — the one that is not cosmetic |
| **Reprint a receipt for a past sale** | WP11 | MAUI has it (till 1.34.0), marked *"REPRINT — not a new sale"*. Web joins the reporting screen that already lists sales |
| **Connection status** | WP17.3 | Web is on `navigator.onLine` — the network interface, not the server. It says "online" in a shop whose broadband is down and cannot tell a revoked till from a dead one. Both tills should move to the shared `ConnectivityProbe` |
| **Reopen a Z-closed day** | **W1** | Server side is live; only MAUI has the screen |
| **Card surcharge** | WP15 | Built on MAUI 2026-08-09; the web till reads `charge`/`minimumCharge` off the legacy wire and ignores them. ⚠ **Kapow's rate is ZERO (confirmed)** and UK consumer surcharges have been banned since **2018-01-13**, so this is a latent trap for a future B2B tenant rather than a live discrepancy |
| ~~**Every C2 twin's TypeScript half is unexecuted**~~ | ~~**WP15**~~ | ⚠ **STALE — CORRECTED 2026-08-17.** The web till HAS a test runner: `"test": "vitest run"`, vitest 3, and **four test files** (`pipeline`, `notices`, `till/discountReason`, `till/tendering`) — 45 tests, verified green on the Mac this session. So the blocker named here is gone; what remains is that **most twins are still not covered**, which is a smaller and different problem. ⚠ Node lives only on the Mac, so any TS test run needs it. Original note: `package.json` has `dev`, `build`, `preview`, `typecheck` — **no test runner and no test files** beyond the 19 tendering tests added 2026-08-11. `VatLineMathTests` fixes .NET to the numbers `api.ts` produces and **nothing executes `api.ts`**; `LegacySaleBridgeTests` pins item-id derivation to a GUID the TypeScript produced in a 2026-07-24 smoke test; `till/basket.ts basketTotals` is a **third, entirely unpinned** copy of the discount apportionment. Add Vitest (same Vite toolchain, no new build concept) and port the .NET vectors across, then add the C2 row saying what now pins them. ⚠ **Needs Node → the Mac.** ⚠ **Matt's call on timing** — recorded here rather than left unowned, because a twin nobody tests is how two tills come to disagree by a penny on the same basket, for ever, on every VAT return, with nothing flagging it |
| ~~**It shows every store's pick notes**~~ | ~~WP17.4~~ | ✅ **FIXED SERVER-SIDE 2026-08-09 (WP17.4) — this row is stale, corrected 2026-08-17.** `GET /api/v1/notifications` now filters by the caller's store, **derived from the device token and never from the query string** — which deleted the C2 twin rather than pinning it, because a per-client filter is a rule every future till would have to re-implement correctly. Pinned by two `PickNotesE2eTests`, mutation-checked. Original note: `GET /api/v1/notifications` does **not** filter by store — it returns the tenant's 50 most recent — so addressing is the client's job and `App.tsx:130` applies no filter. Latent for single-store Kapow; wrong the moment a second store exists, and wrong in the expensive direction: the shop that *does* hold the stock sees the same note and may assume the other branch took care of it, so the web order ships short. MAUI is the strict one via `NoticesClient.IsForStore`. ⚠ One `.filter()` — but doing it client-side **creates** a C2 twin; **moving the filter to the server deletes it instead, and is probably the better answer** |

## 5b. The WEB-TILL PARITY PLAN — W-P1…W-P7 · ✅ **ALL SEVEN DONE 2026-08-17 (web 1.11.0)**

> ✅⚠ **COMPLETE IN CODE, NOT DEPLOYED, AND NOT SEEN BY ANYBODY.** All seven slices are built,
> typechecked and tested on the Mac — **178 vitest cases** (from 45 when this plan was written) and
> **19 mutants** run across them, **18 killed and one that needed a new test vector to kill** (W-P7 —
> read that one, it applies to every mirrored-test file in this repo). Live is still **web 1.10.0**;
> the hand-test sections **§W1–§W8** in [`Test Maui.md`](../Test%20Maui.md) have never been run.
> Every register row flipped to **🟡**, none to ✅, and that is the honest state: *"built, tested where
> a machine can reach, never seen by a human."*
>
> | Slice | What it closed | State |
> |---|---|---|
> | **W-P1** | Stop trading when the device is revoked | ✅ built · 🟡 |
> | **W-P2** | The cached operator roster (the spine) | ✅ built · 🟡 |
> | **W-P3** | Discount ceiling + supervisor step-up | ✅ built · 🟡 |
> | **W-P4** | Sign in with the network down | ✅ built · 🟡 |
> | **W-P5** | Cash events offline + reopen a Z-closed day | ✅ built · 🟡 |
> | **W-P6** | Reprint a past receipt through the printer | ✅ built · 🟡 |
> | **W-P7** | The card surcharge, with the fee's VAT | ✅ built · 🟡 |
>
> **Next on this plan: nothing.** What remains is Matt's — **deploy 1.11.0** when he asks, then the
> hand-run. ⚠ The eight §5 rows all read 🟡 now, so the plan's own definition of done is met and its
> value from here is as the record of *why* each rule is the way it is.

> **Matt, 2026-08-17: write this so it can be completed with no questions.** So: every decision is
> already made and written down here, every rule to copy names its exact source file, and every
> wording an operator sees is given **verbatim**. If something in the code contradicts this plan,
> **stop and re-read the named source file — the code wins**, then correct this plan in the same
> commit (that is how sixteen stale markers happened; do not add a seventeenth).
>
> **Scope:** the 8 rows in §5 where the web till is behind MAUI. ~**8–10 days** honest. Everything is
> **TypeScript in `Plutus/Frontend/Plutus.Frontend.WebApp/src/`**, so everything needs **the Mac** —
> there is no node on the Windows box (runbook § Frontend build).
>
> **Do them in this order.** W-P2 is the spine — W-P3, W-P4 and the gating half of W-P5 all build on
> its cached roster. W-P6 and W-P7 are independent and can go any time.

### ⚠⚠ W-P0 — ground rules for every slice (read once, apply to all)

1. **Read [`till-design.md`](../till-design.md) first and update it in the same commit** (CLAUDE.md
   rule). Each slice below says exactly which Part B / A0 rows flip and what the C2 register gains.
   Flip web ⬜ → **🟡**, never straight to ✅ — 🟡 means "built, tested where a machine can reach,
   unverified on a screen", and no person will have run it yet.
2. ⚠⚠ **Every rule copied from .NET is a C2 twin.** For each one: (a) mirror the named source file
   **exactly** — same numbers, same edge answers, same wording; (b) write **vitest** tests against
   the same cases the .NET tests pin (the .NET test file is named per slice); (c) add a **C2 row**
   in `till-design.md` saying what pins the pair. The web till HAS a test runner: `"test": "vitest
   run"` — 45 tests when this was written, **178 after W-P7**; run on the Mac with `npx vitest run`,
   typecheck with `npx tsc --noEmit`. ⚠⚠ **And mirroring the .NET vectors is NOT sufficient** — see
   W-P7's survived mutant: `decimal` and `double` round differently, so a vector that pins the rule in
   C# can pass unconditionally in TypeScript. **Mutate the TS rule and watch it go red**, or the
   mirrored test is decoration.
3. ⚠ **Fail open on polls, fail closed on money.** A failed network call never stops the till, never
   clears a banner, never signs anybody out. Only an explicit server answer changes state. This rule
   is load-bearing in W-P1, W-P2 and W-P4 and each names its version of it.
4. **Storage:** durable till state goes in **IndexedDB** via `offline.ts` (`meta` store for blobs, a
   new store needs a **DB version bump** in `openDB` — copy the existing `objectStoreNames.contains`
   guard pattern). `localStorage` only for what already lives there (theme, agent snapshot).
5. **Every poll added to `App.tsx`'s cadence block** copies the existing shape: `const t =
   window.setInterval(...)` + cleanup in the effect's return, `.catch(() => undefined)`, 60_000ms.
6. **Deploy** (only when Matt asks; bump `versions/till-web.txt` per slice regardless): build on the
   Mac per runbook § Frontend build — **tar the whole project, never just `src/`** (`vite.config.ts`
   is source; a stale one blanked the portal on 2026-08-09), ⚠⚠ **never `rsync --delete`**
   (`public/agent/` holds a 70 MB exe that is NOT in git — deleting it kills the agent download),
   `PLUTUS_APP_VERSION=$(cat versions/till-web.txt) npm run build`, then **before copying**: grep
   `dist/assets/*.js` for `__APP_VERSION__|__BUILD_TIME__` (must be absent), check the bundle is
   ~340 KB not the ~1 KB SPA fallback, grep for one string only this slice introduced. Back up
   `current` → `current.pre-<version>`, copy `dist/.` in, re-run the same three checks against the
   served URL, then `curl` ETRIE's `/health` → must be 200.
7. **Hand-test steps** go in [`Test Maui.md`](../Test%20Maui.md) under a new `§W` heading ("WEB till
   checks — needs the deployed web till, not the MAUI build"). That document already compares the
   two tills side by side (§G25, §G31), so this is its idiom.
8. ⚠ **Auth:** the web till holds a **device credential** (`getDeviceCredential()` in `pipeline.ts`,
   null when un-enrolled) and mints device tokens (`getDeviceToken()`); operators get a session
   token from `POST /api/Auth/Login` (`api.ts login`). Every feature below that needs the device
   token must **no-op quietly when un-enrolled** — an un-enrolled browser till is a legitimate state.

---

### W-P1 — a revoked web till STOPS TRADING · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built as specified.** `src/deviceStanding.ts` — `checkStanding` / `mustStop` / `REVOKED_MESSAGE`
> mirroring `DeviceRevocation.cs`, polled on its own 60 s cadence in `App.tsx`, latched in IndexedDB
> (`deviceRevoked` via the new `putTillState`/`getTillState` pair — same `meta` store, **no DB version
> bump needed**), blocking screen with a **Check again** action.
>
> **21 vitest cases** mirroring `DeviceRevocationTests.cs` one for one; **both mutants killed** — a
> failed poll stopping the till kills 10, folding `PendingRemoval` into the stop kills 2.
> `tsc --noEmit` clean. Web till suite **66 tests** (was 45).
>
> ⚠ **Two things the plan did not say, decided while building:**
> - **The poll effect has NO `session` guard** and its own cadence, deliberately: a stolen till is
>   revoked while it sits at a login screen, which is exactly when nobody is signed in. Putting it in
>   the `[session]` block would have made the feature useless in its main case.
> - **The block renders BEFORE the login gate.** Gating after login would let somebody sign in to a
>   machine the platform has finished with — a revocation means the hardware stops being a till, not
>   that one operator stops using it.
>
> ⚠ The latch **clears on any other explicit answer**, so re-enrolling in the portal un-blocks the
> till without anybody clearing browser storage by hand.
>
> **Registers done:** Part B revoked-till row web ⬜→🟡 · A0 "Stop trading when it is revoked" web
> ⬜→🟡 · **C2 row added** (`DeviceRevocation ↔ deviceStanding.ts`, both sides pinned) ·
> `versions/till-web.txt` → **1.11.0** · hand-test **§W1a–d**. ⚠ **NOT DEPLOYED** — deploy only when
> Matt asks.

### ~~W-P1 — a revoked web till STOPS TRADING · ~1d~~ *(original brief, kept for the reasoning)*

**Why:** the web till's connection state is `navigator.onLine` — the network interface, not the
server. A lost or stolen browser till keeps selling until its 12 h token dies, because device tokens
have **no server-side denylist**. MAUI closed this 2026-08-16; the endpoint and the decision rule
already exist.

**Mirror source:** `src/Plutus.Client.Core/DeviceRevocation.cs` — copy `Check` and `MustStop`
**exactly**. Its .NET tests: `tests/Plutus.Tests.Unit/DeviceRevocationTests.cs` (19 cases — the
vitest file mirrors all of them).

**Build:**
- `src/deviceStanding.ts`: `checkStanding(code: number, body: {status?: string} | null)` returning
  `"trading" | "removalRequested" | "revoked"`, plus `mustStop(...)`.
- Poll `GET /api/v1/tills/devices/{deviceId}/status` (gate: `sales.ingest` — the **device token**
  works; same auth as the heartbeat) on the 60 s cadence in `App.tsx`, only when
  `getDeviceCredential()` is non-null.
- On `mustStop`: `signOut()` (it already clears the session), set a blocking full-page state that
  survives reload (IndexedDB `meta` key `deviceRevoked`), and show — **verbatim**:
  *"This till has been removed in Plutus and can no longer be used. Speak to your manager — it can
  be re-enrolled from the portal."*
- The blocked page's only action is a "check again" that re-polls; an answer other than an explicit
  revocation clears the flag.

**⚠⚠ Rules that bind (each mutation-killed on the .NET side):**
- **Only the literal status `"Revoked"` (case-insensitive) stops the till.** A failed poll, a
  **401/403**, a **404** and an unknown status ALL keep trading — stopping a shop on a network blip
  or a routing mistake is a worse outage than the one this prevents. Approving a removal sets
  `Status = Revoked`, it does not delete the row, so the explicit answer always arrives.
- **`PendingRemoval` trades.** Halting on a *request* makes un-enrolment a way to take a shop down.
- The message blames **the till**, never the account — otherwise somebody tries login after login
  and concludes the staff accounts are broken.

**DoD:** revoke the device in the portal → the web till blocks within 60 s **without a reload**; a
pulled network cable for 10 minutes changes nothing; reject (not approve) a removal request → still
trading throughout. **Registers:** Part B "A REVOKED TILL STOPS TRADING" web ⬜→🟡; A0 "Stop trading
when it is revoked" web ⬜→🟡; C2 row `deviceStanding.ts ↔ DeviceRevocation` (pinned both sides).
**Version:** bump `versions/till-web.txt` minor.

---

### W-P2 — the roster spine + a disabled operator is signed OUT · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built as specified.** `src/roster.ts` — typed wire shapes (`OperatorRoster` / `TillOperator` /
> `OperatorGrant`), `refreshRoster()` on the 60 s cadence using the **device token** (the endpoint is
> gated `sales.ingest`, so it works with nobody signed in — which is what lets W-P4 sign the *first*
> person in offline), the whole envelope cached in IndexedDB `operatorRoster`, and `isStillPermitted`
> mirroring `OperatorRevocation.Check`. Sign-out uses Matt's sentence verbatim.
>
> **11 vitest cases**, mutation-checked on the load-bearing line. `tsc` clean. Suite **77 tests**.
>
> ⚠ **Verified before building, because the whole check depends on it:** the web till's
> `session.employeeId` and the roster's `TillOperatorDto.UserId` are **the same identity** — both are
> the employee's `Id` (`AuthController` reads `reader.GetGuid(0)`; `TillOperatorsController` sends
> `UserId: e.Id`). Had they differed, this check would have signed out every operator in the shop.
>
> ⚠ **Two details the plan did not specify, decided while building:**
> - **Matched case-insensitively** on the user id. A Guid can arrive in either case from either end,
>   and a casing mismatch would sign out *everyone* while the roster was perfectly correct — a fault
>   indistinguishable from the server having emptied the roster.
> - **The message goes before `signOut()`**, because `signOut()` reloads the page in password mode, so
>   anything after it never runs.
>
> ⚠ `refreshRoster` **validates the envelope shape before caching** (`asOfUtc` a string, `operators` an
> array). A truncated body would otherwise replace a good roster with rubbish — and W-P4's offline
> sign-in depends on that cache being trustworthy.
>
> **Registers done:** Part B disabled-operator row web ⬜→🟡 · A0 row web ⬜→🟡 · **C2 row added**
> (`OperatorRevocation ↔ roster.ts`, both sides pinned) · hand-test **§W2a–c** and **§W3** (the
> side-by-side comparison). ⚠ **NOT DEPLOYED.**

### ~~W-P2 — the roster spine + a disabled operator is signed OUT · ~1–1½d~~ *(original brief)*

**Why:** the web till signs out only **reactively** (`api.ts:50`: `if (res.status === 401)
signOut()`) and login tokens are cached **12 h with their permission set** — so a disabled operator
keeps a working session until something happens to 401. MAUI drops them inside 60 s. ⚠ **This slice
is the spine: W-P3 (ceiling), W-P4 (offline sign-in) and W-P5's gating all read the roster it
caches.**

**Mirror sources:** `src/Plutus.Client.Core/OperatorRevocation.cs` (the decision — its header is the
rule) and `src/Plutus.Client.Core/OperatorLogin.cs` (what the roster is for). Wire shape:
`src/Plutus.Contracts.Client/OperatorContracts.cs` — `TillOperatorsResult(TillId, AsOfUtc,
Operators[])`, each `TillOperatorDto(UserId, DisplayName, Email, CredentialHashBase64,
CredentialSaltBase64, Grants[])`, each `OperatorGrantDto(Code, MaxPence, ValidFromUtc, ValidToUtc,
DaysOfWeekMask, WindowStartLocal, …)`.

**Build:**
- `src/roster.ts`: fetch `GET /api/v1/tills/{tillId}/operators` (gate `sales.ingest` — **device
  token**; the till id is on `getDeviceCredential()`), store the **whole envelope verbatim** in
  IndexedDB `meta` under `operatorRoster`. Refresh on the 60 s cadence. ⚠ Store the envelope, not
  rows — `AsOfUtc` is roster-level and W-P4's staleness horizons are measured from it; it is the
  **server's** clock on purpose, so never substitute a client timestamp.
- `revocation check`: after each successful fetch, if a session is active and the signed-in
  operator's `userId` is not in the roster → `signOut()` + show — **verbatim**:
  *"Your account has been disabled, please speak to your manager"* (Matt's wording, 2026-08-11).
  ⚠ The web till's session must carry `employeeId` for this — it already does (`session.ts`).

**⚠⚠ The load-bearing rule, copied exactly from `OperatorRevocation`:** **a roster that could not be
fetched is NOT an empty roster.** Null (failed fetch) → carry on, always. An **empty roster the
server actually sent** → revoke, correctly. Get this backwards and every broadband hiccup signs the
whole shop out mid-sale, on the flakiest sites first, with a message accusing the operator of being
disabled.

**DoD:** disable an operator in the portal → their web-till session ends within 60 s with Matt's
exact wording; pull the cable for 10 minutes mid-session → nothing happens; the roster envelope is
visible in IndexedDB with `asOfUtc`. **Registers:** Part B "A disabled operator is signed OUT" web
⬜→🟡; A0 "Sign an operator out the moment they are disabled" web ⬜→🟡; C2 row
`roster.ts ↔ OperatorRevocation` — the null≠empty rule pinned by vitest on the TS side.

---

### W-P3 — the discount ceiling + supervisor step-up · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built as specified.** `src/permissions.ts` mirrors `PermissionResolution.Can` +
> `PermissionGrant.IsActiveAt` + `EffectivePermission.Merge`; `DiscountDialog` resolves `pos.discount`
> from the **W-P2 cached roster** (so it works offline), offers the step-up, and refuses
> self-approval. **22 vitest cases**, two mutants killed (intersection-instead-of-union on ceilings;
> the gate failing open on an unknown code). Suite **99 tests**, `tsc` clean.
>
> ⚠ **`plannedDiscountPence` was added to `basket.ts` rather than re-deriving the amount** — it calls
> the real `lineDiscountPence` engine, so what the gate checks is what will be applied. MAUI makes the
> same choice for the same reason (default 22): a second derivation drifts, and only on baskets that
> do not divide evenly. ⚠ It applies the **same exclusions as the reducer** (returns, gift cards), or
> it would over-state the figure and refuse a legal discount.
>
> ⚠⚠ **The predicted falsification happened and is corrected.** Part B's discount-audit row said
> *"absent means no step-up was required, which on this till is true of every discount today"* — the
> web till now **writes `authorisedBy`**, so that sentence is false and the row says so. ⚠ "Absent"
> now covers **two** histories (no step-up needed, and every web-till discount before 2026-08-17), and
> still must not be read as "unauthorised".
>
> ⚠ **Two rules the plan implied but did not spell out, both enforced:** the authoriser must
> **themselves** pass the gate for the amount (otherwise "step up" becomes "ask anyone at all"), and an
> operator with **no** `pos.discount` grant gets a sentence rather than a dead button.
>
> ⚠ **`offlineLogin.ts` was created here, not in W-P4** — the plan said to, since the step-up needs the
> PBKDF2 verify. It carries the four load-bearing parameters (**101010 · SHA-1 · 512 bits · UTF-8**)
> with the reason SHA-1 is deliberate written next to them, and a constant-time-ish byte compare.
> W-P4 extends this file rather than starting one.
>
> **Registers done:** Part B ceiling row web ⬜→🟡 (+ the audit row's note corrected) · A0 row web
> ⬜→🟡 · A0's "web till cannot" list updated · **C2 row added** (`PermissionResolution ↔
> permissions.ts`, marked MONEY) · hand-test **§W4a–f**, including **§W4f — the limit must survive the
> network dropping**. ⚠ **NOT DEPLOYED.**

### ~~W-P3 — the discount ceiling + supervisor step-up · ~1–1½d~~ *(original brief)*

**Why:** the web till has **no client-side permission model at all** — `session.ts` holds token,
employeeId, name. A web cashier can take off **any amount**; the only enforcement is server-side at
ingest. MAUI gates on `pos.discount` with the operator's `MaxPence` and steps up to a supervisor.
Matt, 2026-08-14: *"Base it on roles"* — a discount level **IS** a role's `pos.discount` `MaxPence`.

**Mirror sources:** `src/Plutus.SharedKernel/Permissions.cs` → `PermissionResolution.Can` (:262) —
how grants resolve to an effective permission + ceiling (time windows and day masks included);
`Plutus/Frontend/Plutus.Frontend.AppClient/Services/Security/TillGate.cs` — the gate UX (allowed /
needs-override / refused); `src/Plutus.SharedKernel/DiscountAudit.cs` — the step-up record.
**Do NOT re-derive grant resolution** — mirror `Can`'s cases, including its answers for expired
grants and out-of-window times.

**Build:**
- `src/permissions.ts`: `effectiveFor(grants, code, nowLocal)` → `{allowed, maxPence}` mirroring
  `PermissionResolution.Can`. Source of grants: the signed-in operator's row in the W-P2 roster.
- In `till/DiscountDialog.tsx`, before the reason prompt: resolve `pos.discount`. Over the ceiling →
  offer step-up. Step-up = a second operator enters **email + password**, verified against the
  cached roster with the W-P4 PBKDF2 verify (build that function in this slice if W-P4 has not
  landed; it is ~20 lines — parameters below), and that operator must themselves pass the gate for
  the amount.
- ⚠⚠ **Self-approval is refused** — same rule `DiscountAudit` pins on MAUI: the authoriser's
  `userId` must differ from the requester's.
- ⚠⚠ **The web till must now WRITE `authorisedBy`** into `LineMeta.discountAuthority[]` on stepped-up
  discounts. Part B's discount-audit row currently records that the web till writes **no**
  `authorisedBy` *"— on that till no step-up is possible, so 'absent' is true"*. **This slice makes
  that sentence false: update that row's note in the same commit.**
- ⚠ Order of gates is unchanged and matters: **the money rule first** ("more than the basket" is
  true regardless of who is signed in), then reason, then ceiling/step-up — matching MAUI.

**DoD:** a Cashier with a £5 `MaxPence` grant is refused a £10 discount and offered step-up; the
supervisor's approval lands in `discountAuthority[]` with their `userId`; self-approval refused with
its own message; an operator with no `pos.discount` grant cannot open the discount dialog at all.
**Registers:** Part B "Discount ceiling + supervisor step-up" web ⬜→🟡 **and** the discount-audit
row's note corrected; A0 "Hold a cashier to a discount limit" web ⬜→🟡; C2 row
`permissions.ts ↔ PermissionResolution` (⚠ this one is MONEY — vitest mirrors the .NET cases
exactly).

---

### W-P4 — offline sign-in, with the same expiry horizons · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built as specified.** `offlineLogin.ts` gains the horizons, the three trust tiers, the sell
> floor, `assess`, `allowedWhileOffline`, `sessionExpiresAt` and `signInOffline`; `LoginPage` falls
> back on a transport failure. **30 vitest cases**, mutation-checked (a money-out permission slipped
> into the sell floor kills 3). Suite **129 tests**, `tsc` clean.
>
> ⚠⚠ **THE ONE THING THE PLAN COULD NOT PRE-DECIDE, AND IT IS A SECURITY BOUNDARY.** §5b said "network
> failure only — a 401 must NOT fall back", but the web till's `login()` threw an indistinguishable
> `Error` for a 401 and for a dead network, so *there was nothing to branch on*. Added
> `AnsweredError` + `serverAnswered(err)` in `api.ts`: every HTTP answer is tagged, `fetch`'s
> `TypeError` is not. ⚠ **`navigator.onLine` is explicitly NOT used for this** — it reports the network
> interface and says "online" in a shop whose broadband is down, which would have made the guard
> useless in the exact case it exists for. §W5b hand-tests it.
>
> ⚠ **Staleness is measured from the roster envelope's `asOfUtc`.** MAUI uses
> `LocalOperator.UpdatedAtUtc` because its store is relational; the web till caches the envelope, and
> the envelope's stamp is the same fact — the *server's* clock, not the browser's. Recorded because the
> two look like different anchors and are not.
>
> ⚠ The offline session carries an **empty token** deliberately, so `perm:*` endpoints stay unreachable
> while queued sales keep flowing on the **device** token — the same split as MAUI.
>
> ⚠ **The horizon is checked BEFORE the password**: past 30 days the answer is the same whatever is
> typed, and "wrong password" would send somebody to reset a password that was never the problem.
>
> **Registers done:** Part B offline-sign-in row web ⬜→🟡 · A0 row web ⬜→🟡 · **C2 row added**
> (`OfflineCredentials` + `Crypto.Pbkdf2` ↔ `offlineLogin.ts`) · hand-test **§W5a–d**, with **§W5b** as
> the security case. ⚠ **NOT DEPLOYED.**

### ~~W-P4 — offline sign-in, with the same expiry horizons · ~2–3d~~ *(original brief)*

**Why:** the web till **cannot sign in offline at all** — the shop that loses broadband loses the
till, which is the thing the whole offline design exists to prevent.

**Mirror sources:** `src/Plutus.SharedKernel/OfflineCredentials.cs` — the horizons, **exactly**:
`MoneyOutMaxAge 7d · SellMaxAge 30d · WarnAfter 3d · IdleLock 15min · MaxSession 12h`, assessed from
the roster's **`AsOfUtc`** (server clock), with `SurvivesStaleness`/`SellFloor` deciding which
permissions outlive the 7-day money-out horizon. Its tests:
`tests/Plutus.Tests.Unit/OfflineCredentialsTests.cs`. Verification:
`src/Plutus.SharedKernel/Crypto.cs` `Pbkdf2` — ⚠⚠ **the parameters are load-bearing and deliberate**:
**101010 iterations · SHA-1 · 64-byte hash · 32-byte salt · UTF-8 password**. SHA-1 is not a mistake
— it preserves the legacy till hash byte-for-byte (the file says so), and a browser that used SHA-256
would refuse every valid password. WebCrypto does this natively:
`crypto.subtle.importKey("raw", utf8(password), "PBKDF2", false, ["deriveBits"])` →
`crypto.subtle.deriveBits({name:"PBKDF2", hash:"SHA-1", salt, iterations:101010}, key, 512)`, then
compare against base64-decoded `CredentialHashBase64`.

**Build:**
- `src/offlineLogin.ts`: on `login()` network failure (⚠ **network failure only** — a 401 is an
  answer and must NOT fall back, or a disabled operator signs in offline past their own refusal),
  verify against the W-P2 cached roster: match `email` case-insensitively or `userId`, PBKDF2 as
  above, then `assess(asOfUtc, now)` mirroring `OfflineCredentials.Assess`.
- The session it mints is **local-only** and marked so; expiry = `SessionExpiresAtUtc`'s rule
  (12 h max, 15 min idle lock); past `WarnAfter` (3d) show the staleness warning; past
  `MoneyOutMaxAge` (7d) money-out permissions (refunds, paid-out, discounts…) are refused with the
  staleness message while `SellFloor` permissions keep working to 30d; past 30d no offline sign-in.
- ⚠ An offline session **cannot** call `perm:*` endpoints (no platform token) — queued sales still
  flow (device token), which is exactly MAUI's shape.
- Login failure wordings — mirror `OperatorLogin`'s distinctions (no roster / unknown operator /
  wrong password / no credential set): each gets its own sentence, because *"wrong password"* for
  all of them once sent someone hunting a typo that did not exist.

**DoD:** enrol + sign in online once → pull the cable → sign in offline succeeds; wrong password
refused; a roster 8 days old refuses a refund but still sells; 31 days refuses sign-in; **a 401 from
the server never falls back to offline verify**. **Registers:** Part B "Offline sign-in with an
expiry" web ⬜→🟡; A0 "Sign somebody in with the network down" web ⬜→🟡; C2 rows
`offlineLogin.ts ↔ OfflineCredentials` (the five numbers pinned on both sides) and
`offlineLogin.ts ↔ Crypto.Pbkdf2` (the four parameters pinned on both sides). ⚠ Security posture:
hashes cached client-side = the same posture as MAUI's roster cache, already documented on
`FileOperatorStore` — cite it, don't re-argue it.

---

### W-P5 — cash events offline + reopen a Z-closed day · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built as specified.** `cashOutbox.ts` (the queue + drain) and `cashRules.ts` (the three
> decisions), a `cashOutbox` store on **DB v3**, drained on the beat and on the `online` event,
> "waiting to send" on the Cash page, and a **Reopen this day…** action gated `pos.cash.reopen`.
> **17 vitest cases**, both money mutants killed. Suite **146 tests**, `tsc` clean.
>
> ⚠ **The decisions were split into `cashRules.ts` rather than left inside the queue.** The queue is
> IndexedDB and needs a browser; every way this feature can lose or hide money is a *decision*, and a
> decision tests without one. Same split as `deviceStanding`/`roster`/`permissions`.
>
> ⚠ **Two orderings that are load-bearing, both decided while building:**
> - **Sales drain BEFORE cash.** A Z waits for its own day's sales, so draining cash first would just
>   stall on a Z that the sales drain is about to unblock.
> - **Queue BEFORE sending.** A browser that dies between the POST and the local write would otherwise
>   lose the money; the other order cannot.
>
> ⚠ **The expected drawer is still never computed locally** — `expectedPence` is set only when the
> platform ANSWERED, so an offline X/Z shows the count without inventing an expectation. That rule was
> already load-bearing on MAUI and is unchanged.
>
> ⚠ **The local Z guard refuses every type after a Z, not merely a second Z** — the server's guard
> cannot be consulted with the line down, which is exactly when it matters. ⚠ And a **ZReopen** is the
> one exception, because locking the escape hatch inside the thing it unlocks would strand a till until
> midnight.
>
> ⚠ **`ZReopen` was added to `pipeline.ts`'s `CashEventType`** — the server has accepted it since
> backend 1.15.0; only the client type was missing.
>
> **Registers done:** Part B Z-reopen row + the Cash row's web half web ⬜→🟡 · A0's two rows web ⬜→🟡 ·
> **C2 row added** (`CashPushService ↔ cashRules.ts`) · hand-test **§W6a–d**, with **§W6b** as the money
> case. ⚠ **NOT DEPLOYED.**

### ~~W-P5 — cash events offline + reopen a Z-closed day · ~2d~~ *(original brief)*

**Why:** `CashPage.tsx` posts online-only through `pipeline.ts postCashEvent` — a float taken while
the line is down is a day that cannot be reconciled. And the server has had `ZReopen` live since
backend 1.15.0 (**W1**) with nothing on the web till calling it, so a browser till Z-closed by
mistake is stranded until midnight.

**Mirror source:** `Plutus/Frontend/Plutus.Frontend.AppClient/Services/Sync/CashPushService.cs` —
copy its decision rules, not its shape: **queued-not-posted** (IndexedDB store `cashOutbox`, ⚠ DB
version bump in `openDB`); **one Z per business day enforced locally** (refusing every type after
it, not merely a second Z — the server cannot be consulted with the line down, which is when it
matters); on drain **409 and 400 are TERMINAL and keep the server's words** (a till that retries
them forever looks healthy while never banking); ⚠⚠ **a Z waits for its own day's PENDING sales to
drain first** (expected = float + cash takings + ins − outs, so a Z that overtakes queued sales
reports a shortage equal to every penny not yet sent — and Pending only, never Failed, or one
refused sale blocks the till's close forever); **the expected figure is the SERVER's, never
computed locally**.

**Build:** wrap `postCashEvent` in a queue-first path; drain on the cadence and on the `online`
event alongside `drainOutbox()`; surface "(waiting to send)" per queued row and clear it when the
drain reports. Then **ZReopen**: add `"ZReopen"` to `CashEventType` in `pipeline.ts`; a "Reopen this
day…" button on `CashPage` visible only when the day is closed, gated `pos.cash.reopen` via W-P3's
`effectiveFor` (Supervisor+ hold it); ⚠ **a reason is REQUIRED** — refuse an empty one; the reopen
posts **online-only** (it is an audited supervisor action, not drawer money — do not queue it).

**DoD:** open a float with the cable pulled → it queues, shows "(waiting to send)", and lands when
the line returns; a Z with queued sales waits and says so; a 409 shows the server's words and stops
retrying; Z-close a day → reopen with a reason as a Supervisor → trading resumes; a Cashier does not
see the reopen button. **Registers:** Part B "Reversing a Z close" web ⬜→🟡 and the Cash row's
web-half note updated; A0 "Record cash movements with the network down" and "Reopen a day closed by
mistake" web ⬜→🟡; C2 row `cash outbox rules ↔ CashPushService`.

---

### W-P6 — reprint a receipt for a past sale · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built — and the brief was wrong about the starting point, which is the interesting part.**
> `reporting/SaleDetailDialog.tsx` has had a **Print copy receipt** button for months, and
> `receiptData()` already marked the copy (`${id} (COPY)` on the sale id). The reporting page already
> found the sale. What it did *not* have was a **printer**: it called `window.print()`, so a counter
> with a thermal printer could not hand a customer paper.
>
> ⚠⚠ **So the ⬜ was not stale — it was measuring the wrong thing.** The hard halves (find the sale,
> mark the paper) were done; the missing half was one call. The register said "cannot reprint" and the
> code said "reprints, badly". **Seventeenth marker corrected this way**, and a new flavour of it:
> not a ⬜ that should have been ✅, but a ⬜ hiding a *partly* built capability. ⚠ The reflex that
> found it is the same one: **open the file before believing the row.**
>
> **Built:** `printCopy(d)` → `agentAvailable()` → `printDocument(receiptToDocument(receiptData(d),
> agent.columns ?? 42, **false**))`, and the browser dialog kept as the fallback for no-agent /
> refused. ⚠ **`false` is `openDrawer`** — no money is moving, same rule MAUI states.
> ⚠ **A reprint must never be held up by hardware**, so every failure falls through to the browser
> rather than refusing.
>
> ⚠ **The web till reprints CROSS-TILL sales and MAUI cannot.** Its list is `GET /api/v1/sales` — the
> whole business, server-side — where MAUI reads its own SQLite. The trade runs the other way too:
> MAUI's works with the line down. Both halves of that are now in Part B.
>
> ⚠⚠ **ONE RULE IS NOT CONVERGED, and it is recorded as unconverged rather than quietly counted as
> done.** MAUI prints `** REPRINT — not a new sale **` above the first rule; the web till marks the
> **sale-id line only**. Same purpose, weaker signal — the id is small print. The honest fix is
> `receiptDoc.ts` taking an `isReprint` flag, which touches the shared document builder → **WP15**.
> C2's reprint-marking row now says so, and **§W7a asks Matt to judge it on real paper**.
>
> **Registers done:** Part B reprint row web ⬜→🟡 (with the cross-till note) · A0's two reprint rows
> web ⬜→🟡 · the A0 ⬜-list entry struck through · C2 marking row rewritten as a live divergence ·
> hand-test **§W7a–c**. ⚠ **NOT DEPLOYED.**

### ~~W-P6 — reprint a receipt for a past sale · ~1d~~ *(original brief)*

**Why:** MAUI has had it since till 1.34.0; the web till cannot hand a customer their paper again.

**Mirror source:** `Plutus/Frontend/Plutus.Frontend.AppClient/Services/Printing/ReceiptReprint.cs` —
the **money rules**, not the dialogs: ⚠⚠ **the copy is MARKED "REPRINT — not a new sale"** (this
till's refund flow accepts a sale found by a receipt barcode, so two identical papers for one
purchase is the shape of a double refund — which has happened here once already); ⚠ **the drawer
does NOT kick** (no money is moving, and a drawer that opens on a reprint teaches operators the
drawer means nothing).

**Build:** the reporting page already lists sales (`api.ts fetchSales`, :705). Add a per-row
"Reprint" action → fetch the single sale (`GET /api/v1/sales/{saleId}` — add `fetchSale(id)` beside
`fetchSales` if absent; it needs the **operator** token like the rest of reporting) → build the
receipt through the existing `till/receiptDoc.ts` path with the REPRINT marking and `drawer: false`
→ print through the agent exactly as a sale receipt prints. Cross-till reprint comes free — the
platform's projection is the authority for another till's sale.

**DoD:** reprint yesterday's sale → paper says REPRINT, drawer stays shut, figures match the
original; a sale rung up on the MAUI till reprints from the web till. **Registers:** Part B "Reprint
a receipt for a past sale" web ⬜→🟡 (and the cross-till row's web half); A0 rows "Reprint a receipt
for an earlier sale" + "Reprint a sale rung up on another till" web ⬜→🟡.

---

### W-P7 — the card surcharge · ✅ **DONE 2026-08-17 (web 1.11.0)**

> ✅ **Built as specified.** `src/till/surcharge.ts` — `feePence`, `pairFor`, `surchargeLine`,
> `cardIsTendered`, `hasSurcharge` and the last-known-good cache — wired into `CheckoutDialog`, with
> the member auto-discount exclusion in `basket.ts`. **32 vitest cases** using the .NET test's own
> vectors. Suite **178 tests** (from 146), `tsc` clean.
>
> ⚠⚠ **A MUTANT SURVIVED, AND IT IS THE MOST USEFUL THING THIS SLICE PRODUCED.** The brief said "the
> vitest file mirrors them", and mirroring them is exactly what left the rule unpinned.
> `CardSurchargeVatTests` forces products-before-division with `3 × 10000 ÷ 12000 = 2.5`: `decimal`
> computes the ratio as `0.8333…3`, so ratio-first gives 2.4999… and the .NET test goes red. **A
> double rounds that ratio UP** (`0.8333333333333334`), so ratio-first gives 2.5000000000000004 and
> the copied vector **passes**. The ratio-first mutant survived the whole suite.
> **`45 × 70 ÷ 100 = 31.5`** is the vector that discriminates in JS (ratio-first: `45 × 0.7 =
> 31.499999999999996`) and it is now in the file.
>
> ⚠ **A copied vector is not a copied guarantee** — arithmetic is only pinned where the *host
> language's* rounding can go wrong, and that is a different set of inputs per language. Worth
> applying to every other "mirrors the .NET tests" file in this repo. C2's new row states it.
>
> ⚠ Searched exhaustively afterwards (gross 50p–£50, flat fee 15–60p, every ex/gross ratio from
> pure-20% to pure-zero-rated): **no divergence anywhere in the money range.** The rule was
> unprotected, never wrong — said plainly so nobody re-audits old takings looking for a penny.
>
> ⚠ The other **five mutants died first time**: truncating the percent half, letting the fee ride on
> returns, counting a gift card as a card, dropping the once-per-sale guard, and drifting the
> `CARD-SURCHARGE` spelling between `surcharge.ts` and `basket.ts` (the last one is why
> `isCardSurcharge` is exported — the two literals are pinned by a test, not an import, because
> `surcharge.ts` already imports `basketTotals` from `basket.ts` and the reverse would be a cycle).
>
> ⚠⚠ **BUILT AT CHECKOUT, NEVER STORED IN BASKET STATE — the one deliberate mechanical difference
> from MAUI.** MAUI adds the fee line to `Basket`. The web till's basket is **persisted to
> localStorage**, so a fee line there would survive a cancelled checkout, a park/recall and a switch
> to cash: a phantom fee on a cash sale is money nobody authorised. Derived in the dialog it cannot
> outlive the screen that priced it. ⚠ The member-discount exclusion went in **anyway** — the reducer
> is the one place that can be sure, and MAUI (which *does* put the line in its basket) needs exactly
> that rule, so a reader comparing the two must not find it on one side only.
>
> ⚠ **The web equivalent of "picking a card method" is "a card row holds money"**, and the fee folds
> into what is owed exactly as `TenderLoop` does it (`total += surcharge; outstanding += surcharge`).
> ⚠ **No circularity** — the fee is a function of the goods, not of the amounts, so **rest** settles
> in one press. ⚠ **Once per sale** falls out of the basket being the base, so a split across two
> cards cannot be charged the flat half twice.
>
> ⚠ **`vatBand` is deliberately absent from the fee line** (`taxId: -1`, which no published band can
> claim). The provisioned item sits on the **zero** band server-side, and sending `vatBand: "zero"`
> beside a blended 1905bp rate would state that no VAT is due on a line declaring some — on a
> standard-rated basket that reads as zero-rated output tax. MAUI reaches the same place by sending
> `VatBandKey: null` on every line.
>
> ⚠ **Fails CLOSED**: `feePence`/`pairFor` throw on a negative setting or an impossible ex total, and
> the dialog catches it, **refuses the sale** and names the problem. Completing without the fee takes
> the wrong money silently; letting the throw escape would blank the till and lose the basket.
>
> ⚠ **Still dormant for Kapow** (rate zero → no line). **§W8a exists to prove exactly that**, and it
> is the only part of §W8 that applies to normal trade.
>
> **Registers done:** Part B "Card surcharge" web ⬜→🟡, with the decayed-deferral note retired ·
> **A0 row added** ("Charge the tenant's card fee, with the fee's VAT following the basket") · C1's
> second-implementation column filled · **C2 row added**, carrying the mutation lesson · hand-test
> **§W8a–f**, with **§W8c** as the VAT case. ⚠ **NOT DEPLOYED.**

### ~~W-P7 — the card surcharge · ~½–1d~~ *(original brief)*

**Why:** the tenant can set a card surcharge and the web till **ignores it** — it reads
`charge`/`minimumCharge` off the legacy wire and does nothing. MAUI computes it at checkout
(2026-08-09). Two tills in one shop: one adds the fee, one doesn't. ⚠ Kapow's is **zero**, so
nothing visibly changes for Matt — the DoD needs a test tenant value.

**Mirror source:** `src/Plutus.SharedKernel/CardSurchargeVat.cs` — `FeePence(surchargeBp,
flatPence, basketGrossPence)` and `PairFor(...)` (⚠ **the VAT pair follows the basket the fee rides
on** — that is the whole reason `PairFor` exists; do not invent a flat 20%). The fee line's item id
is the constant **`CARD-SURCHARGE`** (`ItemIdOne`). Its tests:
`tests/Plutus.Tests.Unit/CardSurchargeVatTests.cs` — the vitest file mirrors them. Config source:
`ActiveGatewayDto.SurchargeBp` / `SurchargeFlatPence` (`src/Plutus.Contracts.Client/
PaymentContracts.cs`) — and `CheckoutDialog.tsx` **already calls `fetchActiveGateway()`**, so extend
the `ActiveGateway` interface in `api.ts` with the two fields rather than adding a fetch.

**Build:** `src/till/surcharge.ts` twin of `FeePence` + `PairFor`; in `CheckoutDialog`, when a card
tender is selected and either config value is non-zero, add the `CARD-SURCHARGE` line (both zero =
no line, the default). ⚠⚠ **The member auto-discount must EXCLUDE the surcharge line** — MAUI's
`MemberDiscountBasket` excludes it explicitly (a discounted fee under-collects the surcharge);
`till/basket.ts` ~:155 is where the web till's exclusions live (returns, already-discounted, gift
cards — add the surcharge). Reference for the checkout wiring: MAUI's `CheckoutCommit.cs` surcharge
section.

**DoD:** with a test tenant set to 50bp + 20p flat, a £20 card sale gains the fee line with the
right VAT pair, cash sales don't, a member's discount skips it, and both tills produce **the same
fee to the penny** on the same basket. **Registers:** Part B "Card surcharge" web ⬜→🟡; C2 row
`surcharge.ts ↔ CardSurchargeVat` (⚠ MONEY — mirrored tests mandatory).

---

### What ✅ looks like for the whole package — ✅ **met 2026-08-17, except the deploy**

All 8 §5 rows and their A0 mirrors at 🟡; a `§W` section in `Test Maui.md` covering each DoD's
hand-checks; **five new C2 rows** (device standing, operator revocation, permissions/ceiling,
offline credentials + PBKDF2, surcharge — plus the cash-rules row) each stating what pins the pair;
`versions/till-web.txt` bumped per slice; deployed **only when Matt asks**, verified on the
four-axis artefact check every time. ⚠ 🟡 → ✅ happens only after a person runs §W — no exceptions,
that is what 🟡 is for.

✅ **Done:** the rows, the A0 mirrors, **§W1–§W8**, and **six** C2 rows (the five above plus
cash-rules; the surcharge row landed with W-P7 and carries the mutation lesson).
⬜ **Outstanding, both Matt's:** `versions/till-web.txt` says **1.11.0** and the build has **not been
deployed** (live is 1.10.0), and **no §W section has been run by a person**. Nothing in this plan is
waiting on more code.

## 6. The 15 MAUI ⬜ rows, grouped

Not fifteen problems — **five clusters**, each already owned by a step above:

- **Loyalty / gift cards / customers** (6 rows) → **step 27**
- **Platform notices** — announcements, help tickets, app-update prompt, pick-from-floor (4 rows) →
  no step yet, **~3–4d**, cheapest carried on 22/24
- **Theming + portal-controlled receipt template** (2 rows) → **steps 22 and 26**
- **Users** (1 row) → **step 24**
- **Un-enrol request + manager approval** (1 row) → **step 21, ~1d**
- **VAT-band consistency guard** (1 row) → **WP10**

✅ **Refund-only baskets left this list on 2026-08-16** — built, tested and hand-run; the row had
been ⬜ against a Notes cell that described the *server* half. **Grep before believing a marker.**

## 7. How long, honestly

**≈35–40 working days.** Two thirds is **step 27 (12–15d)** and **step 26 (8–10d)**.

⚠ **That total does NOT include the expanded loyalty programme** ([`Loyalty Update across all tills.md`](Loyalty%20Update%20across%20all%20tills.md),
2026-08-13). Step 27's 12–15d is the **parity slice** — MAUI level with today's web till, plus gift
cards. The programme (credit currency, earn-at-ingest, ledger with holds, rewards, member portal) is
platform-first work sized separately in the design doc's §17 at roughly **45–55d across four phases**,
of which only phase B's MAUI half (~5–6d) would land in this document as a new step.

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

> ### ⚠ A REMOVAL SWEEP IS OWED, AND IT IS NOT THE SAME AS THIS REGISTER
>
> **Matt, 2026-08-16:** *"I think we need to go through at one point and check what can be removed
> from MAUI."*
>
> **This register lists what was ALREADY KNOWN to be legacy when it was written (L1–L10).** It is not
> the answer to *"what in this app is now dead?"* — the retrofit has since replaced whole screens,
> and things fall out of use without anybody noticing. Four examples found by accident in one
> session on 2026-08-14/16, none of them in L1–L10:
>
> - ~35 lines building `sale.Transactions` / `sale.Refunds` on **every checkout**, read by nothing
> - `sale.Notes`, copied at checkout and read back at print time, for no reason
> - a **duplicate** `GetReportSummaryAsync` that no caller used
> - `SaleFinder`, a whole class, unused within an hour of being written
>
> ⚠ **Each was found by tripping over it, not by looking** — which is exactly why a deliberate sweep
> is worth doing rather than trusting that the register is complete.
>
> **What the sweep should actually do** (~1–2d, and best AFTER the remaining steps land, or it will
> be redone):
> 1. Every `public`/`internal` type in `Plutus.Frontend.AppClient` with **no reference** outside its
>    own file or tests — the `SaleFinder` shape.
> 2. Every **legacy-model write** whose value is never read back — the `sale.Transactions` shape.
>    ⚠ Grep for assignments into `Database.Models` types from the till path.
> 3. Every **XAML view with no route** into it, and every viewmodel only that view constructs.
> 4. Duplicate client methods hitting **one endpoint** two ways.
> 5. ⚠ The **`Plutus.Frontend.ClientUI`** project (L10) — still in `Plutus.slnx`, still building.
>
> ⚠⚠ **A "no references" grep is a starting list, NOT a verdict.** MAUI resolves things by NAME at
> runtime — XAML `x:Class`, `{Binding}` paths, `MessagingCenter` subscriptions, Shell routes — and
> none of those are compile-time references. **Anything the sweep proposes gets checked against the
> XAML and the messaging centre before it goes**, or a screen renders blank in Release and nobody
> finds out until a hand-run.

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

**Code:** `ViewModels/MainTill/Statistics/SalesReportsViewModel.cs`, `StockOuttakeViewModel.cs`,
`StatisticsViewModel.cs` and their views.

⚠️ **HIDDEN 2026-08-16 (till 1.68.0) — MARKED FOR DELETION, NOT DELETED.** Matt: *"Can you hide this
and mark it for deletion but NOT delete it."* The tab registration in `AppShell.xaml.cs` is commented
out with the reasoning beside it; **every file remains in the build.** Replaced by the **Reports**
tab (step 26), which reads the platform.

> ### 🛑 ANSWERED 2026-08-17 — **NOTHING IS DROPPED. L4 IS NOT A DELETION.**
>
> Matt, verbatim: *"Do not drop anything. I have a more recent DB to import and will need to
> translate where required and retain all legacy sales."*
>
> **So the question below is settled in the direction that keeps the code.** The screens stay hidden
> and stay in the build. ⚠ **Do not delete these files, and do not delete Syncfusion on the strength
> of L4** — the Syncfusion removal has to find another route or wait.
>
> ⚠⚠ **AND THIS IS NOW A WORK PACKAGE, NOT A CLEAN-UP.** A newer legacy DB is coming in and its sales
> must be **translated** into the v2 store and **retained**, not merely left readable in a hidden
> screen. That is an import/migration job with real money in it:
>
> | Needs deciding when it starts | Why it matters |
> |---|---|
> | **Where legacy sales LAND** | Translated into the v2 store (so the Reports tab and every VAT return see them), or kept legacy-side and read separately? Only the first makes them count |
> | **VAT band per legacy line** | Legacy rows carry a `TaxId`, not a published band. `VatBandStamp` backfills on ingest — ⚠ but it stamps *today's* mapping, and a 2024 sale may need the rate that applied **then** (`VatBandCache` holds the timeline, so this is answerable) |
> | **Idempotency** | An import re-run must not double-count takings. Legacy ids have to map deterministically, the way `DeterministicGuid.ForItem` already does for catalogue rows |
> | **Money representation** | Legacy is `decimal`; v2 is integer pence. `Pence.FromDecimal` exists **for exactly this** and its header says so — "the one place decimals legitimately still arrive… reading the old Kapow-schema database during cutover" |
> | **What "translate" covers** | Items, tax rows, employees, customers, discounts — a sale references all of them, and a sale whose item id resolves to nothing is a line nobody can read |
>
> ⚠ **Do not begin this by writing an importer.** The first job is to look at the DB Matt has and
> record what is actually in it — row counts, date range, which tables, whether ids collide with
> live ones. An importer written against a guessed schema is how history gets silently mangled, and
> unlike most bugs here **this one is not reversible once the takings are wrong.**
>
> ### ✅ THE IMPORT RAN — 2026-08-17, from the 15_08 backup
>
> **Every row above got its answer, and the record lives in
> [`NatApp-Translation-Agent-Plan-2026-08-05.md`](NatApp-Translation-Agent-Plan-2026-08-05.md) §7**
> (that plan was the mechanism, as Matt confirmed on 2026-08-08). The shape of it:
>
> - **Looked FIRST, imported second** — the delta between the 23_07 and 15_08 backups was measured
>   and proven purely additive (0 changed rows, 0 lost rows) before any code was written.
> - **198 sales / £4,923.86**, 2026-07-24→2026-08-15, into SalesV2 via the existing
>   `Migration.Kapow` mapper — now **incremental** (LegacyRef delta, stable till/device identity,
>   DeviceSeq continuation, quarantine dedupe, `--verify`-before-`--apply`, closed-period guard).
> - **Where legacy sales land** → the v2 store, same as the 21,646 before them. **VAT per line** →
>   the file's own `VatId → Vats.Rate` (the 2026-07-24 `VatReconstructed` rule; source-header VAT
>   £131.97 vs recorded £140.33, difference explained in §7). **Idempotency** → LegacyRef, proven:
>   same backup twice = "nothing to import", on t1 AND live. **Money** → `KapowMoney.ParsePence`.
>   **"Translate" covered** → the 48 items the new sales reference (insert-only); 2 till-side price
>   changes and stock deliberately NOT taken — Matt's "do not overwrite anything new".
> - **Rehearsed on `plutus_t1` first** (re-cloned same-day), which caught a real defect before live.
> - **Four-way penny reconciliation after**: SalesV2 == ΣSaleLines == SalesRollups == VatRollups =
>   £562,563.74 / £25,784.71 VAT / 21,888 sales. The orphaned `d4fa2572…` till got its real
>   `Till`+`TillDetails` row ("Kapow shop till (NatApp)", store 1), so the whole shop history now
>   attributes to the shop instead of the store-0 bucket.
>
> ⚠ **L4's screens still stay** — the hidden tab remains the only reader of a migrated till's local
> pre-cutover file, and Matt's "do not drop anything" stands. What changed is that the PLATFORM now
> holds the till's sales through 2026-08-15, so the gap the screens papered over is three weeks
> smaller. The **final** bridge run happens at cutover, from a backup taken after the till stops.

⚠⚠ **THE ORIGINAL QUESTION, kept for the reasoning.** Deleting these screens deletes the **only**
reader of the pre-cutover legacy file. A till migrated from NatApp still holds real history there and
nothing else in the app can show it. **The question was: does anyone still need pre-cutover history
ON A TILL, given the platform holds everything since?** → **Answered: yes, and more than that — it
must be imported and translated.** **Do not answer it by quietly deleting the files.**

⚠ **Hidden is already most of the benefit**: nobody can now reach a screen that reports £0.00 for a
day the shop took £2,000. What deletion additionally buys is the Syncfusion removal below.

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

⚠ **What it costs to keep, measured 2026-08-18:** Syncfusion is **76 MB of the till's 264 MB publish
output (29%)**, all of it reachable from no screen an operator can open, and the full removal would
make the artefact **a third smaller**. That is the price of L4's ruling, and it is a fair price —
recorded so the decision stays informed, **not** as an argument to reopen it. The sequencing, the
tiers that do NOT need L4 touched, and the 15 MB of `DocumentFormat.OpenXml` the till never calls are
in [`Shrink MAUI Build.md`](Shrink%20MAUI%20Build.md). ⚠ That page is **packaging only** — this
document remains the authority on whether these screens live.

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
⚠️ **Still called on every sign-in** (`LoginViewModel.cs:301`). **Order:** ⚠ **NOT step 21 — it is
blocked until step 25.** Corrected 2026-08-14; see below.

A 2026-08-09 hotfix that writes API data into the legacy `Stores` table — exactly the bridge default 9
forbids. Step 14's Meta-cached store header replaces it **for display**. ⚠ It also creates
`Database.db` on every sign-in, which is what made the enrolment gate a one-way door.

⚠⚠ **THE DELETION IS BLOCKED, AND TWO EARLIER DESCRIPTIONS OF THIS WERE WRONG. Settled against the
tree 2026-08-14:**

- ❌ *"throwing on every sign-in"* — **overstated.** It **cannot throw out at all** (the body is one
  `try` with a swallowing `catch` at `:394`, deliberately, so a missing store never blocks sign-in),
  and it does not even reach the `db.Add` that was blamed unless the till has **no** local store row
  *and* the API answers: `:352` returns immediately once `Store` is set, `:359` returns if a row
  already exists. So on a till that has signed in once, it is a no-op.
- ❌ *"~½d, it is just a deletion"* — **deleting it NullReferences two inventory screens.** The
  blocker is **`Store.Id`, not the store's details**, which is why the Meta cache did not free it:
  - `ViewModels/MainTill/Inventory/Items/AddEditViewModel.cs:330` — `App.GetViewModel().Store.Id`
  - `ViewModels/MainTill/Inventory/Items/ViewAllViewModel.cs:1324` — same, ⚠ **and this one was
    missed** by the code's own comment, which named only `AddEditViewModel`
  - `Helpers/Database/Database.cs:43` — also uses it, but behind a null guard, so it degrades

⚠ **Do not "unblock" this by null-coalescing those to `0`.** That writes stock rows against store 0
— a silent data change dressed up as a null fix. **It goes with step 25**, when inventory leaves the
legacy store and nothing needs a legacy store id.

⚠ **This is the fourth status marker in this document found wrong in a week.** The pattern is the
same every time: a claim about *behaviour* written from reading a call site rather than following
what it calls. **Grep the callers before believing a ⬜ or a ✅.**

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
| **20** | ✅ **CONFIRMED — Matt, 2026-08-13: "Tiers need to be set on the portal, but you need to be able to assign and change a tier on the tills IF you have the correct permissions. Supervisor to change tiers. Till operator to add new loyalty members."** Three rules: **(a)** tiers are *configured* in the **portal only** — no till creates or edits a tier. **(b)** *Assigning/changing* a member's tier at a till is **Supervisor and up** — `customers.manage`, which Supervisor already holds, so this is screen work only. **(c)** *Adding* a new member at a till is **Cashier and up** via a new **`pos.customers.add`**, with `POST /api/v1/customers` accepting either it or `customers.manage` (the `CheckAny` shape from `pos.stock.adjust`). ⚠ **Create-only, deliberately** — a cashier may add but not alter: editing a member's email quietly redirects their account, and a tier changes every future basket. ⚠ **Changes the WEB till too** — its create dialog is gated `customers.manage` alone today, so a web-till cashier cannot add either; both tills gain the gate in the same slice. ⚠ Adding is **online-only on every till**: member numbers come from a tenant-wide counter, and two offline tills would mint the same one. Expanded design: [`Loyalty Update across all tills.md`](Loyalty%20Update%20across%20all%20tills.md) §14. | 27 |
| **21** | ✅ **CONFIRMED — Matt, 2026-08-13: "We already have tier'd discount. The credits/Gems value need to be set in the portal. Each credit/gem is worth £0.10. Earn one credit/gem for every £10 spent. Use as many credits/gems as you want on an order. Need to be able to set an expiry date or never."** The programme's economics, and every till reads them from the server: a per-tenant **`LoyaltySettings`** row (`PencePerPoint` 10, `SpendPerPointPence` 1000, `ExpiryChoice` `NotChosen`/`Never`/`AfterMonths` + `ExpiryMonths`), following the `GiftCardSettings` idiom where **absence is the gate** — ⚠ **expiry is the TILL OWNER's explicit decision** (Matt, 2026-08-13), so `NotChosen` blocks earning and *"never"* is **chosen, not defaulted into**; two fields rather than a bare nullable so *"the owner chose never"* is distinguishable from *"nobody has decided"*. ⚠ **The brand term is configurable and lives ONLY in `NameSingular`/`NamePlural`** — code, tables, columns and DTOs use the neutral `Point`, and MAUI carries the two strings with its synced `LoyaltyCache` rather than a resource file, because a hardcoded "gems" on a till button or receipt is a bug the second tenant finds. ⚠ **Expiry is computed ON READ from the date** (the `GiftCardLedger.cs:57` precedent — nothing sweeps gift cards at all), so a balance is right **even if no job has run**; a sweeper writes idempotent `Expire` rows **only** for the accounting record, valued at each batch's **own** rate. Four consequences are binding because each is a silent-wrongness risk: **(a)** the ledger stores a **count of gems, never pence** — the rate is a portal setting, and a pence balance would either re-value all history or fail to, depending on which figure was written. **(b)** Redemption is a **basket-wide discount** of `gems × PencePerPoint`, reusing `DiscountApportionment.Across` + `VatLineMath.ForLine`, so a mixed-VAT basket apportions right for free; ⚠ `Across` **throws** above basket value, so the till caps the offer or ingest quarantines the sale. **(c)** Expiry is **per earn-entry**, consumed **oldest-expiring-first**, and changing `ExpiryMonths` **never retro-expires**. **(d)** ⚠⚠ A refund must **claw back the earn** *and* **restore the burn** — the earn alone leaves a gem printer (buy £1,000, refund, keep 100 gems); the burn alone loses the member gems they paid with. Balance may go negative; redemption blocks while it is. ⚠ **Earn base:** gross inc-VAT **actually paid** — after tier discount, after redemption, **excluding gift-card activation** (a liability, not a supply — earning there pays out twice). Rounding **floors per sale**. Reasoning and edge cases: [`Loyalty Update across all tills.md`](Loyalty%20Update%20across%20all%20tills.md) §18. ⚠ **GEMS ARE GRANDFATHERED** (§18.8): each batch carries `PencePerPointAtEarn`, consumption is **oldest-first**, so a rate change touches future earns only and the old-rate cohort liquidates itself. Two consequences bind the tills: the member-facing figure is **money, not a count** (*"you have £23.50 in gems"* — a count has no single value once batches differ, and a money-denominated redemption makes oldest-first value-neutral to the member), and ⚠⚠ **NO TILL EVER HOLDS `PencePerPoint`** — it receives a money balance and sends a money redemption, so this is **a C2 twin that never gets created**, and MAUI's offline `LoyaltyCache` hint is correct by construction since a cached *value* needs no rate to interpret. A redemption is **one ledger row per source batch** (a refund must restore the same batches at the same rates). The portal still previews, audits and type-to-confirms a rate change (§18.7) — the hard `danger` warning moves to shortening `ExpiryMonths`, the one edit that still destroys value. ✅ **NO LONGER GATED — §14 decision 1 settled by Matt, 2026-08-14: a redemption is a DISCOUNT**, *"only ever earned, never purchased, not transferable to cash"*. ⚠ That reasoning, not the Clubcard precedent, is what to cite: nothing was ever owed, so there is no liability for a tender to discharge — which is precisely the line that separates a gem from a **gift card**, and the reason the two must never share a code path. | programme phase A/B, not 27 |
| **22** | ✅ **CONFIRMED — Matt, 2026-08-13, three rulings on discounts.** **(a)** *"You cannot have a discount greater than the basket."* **(b)** *"Discount levels should be a setting that is configurable by the owner, and over certain levels (which can be added and configurable) need approval from a supervisor or higher."* **(c)** *"All discounts need to be tracked — till, logged-in employee and reason."* ⚠ **(a) IS BUILT** — `SharedKernel/BasketDiscounts.cs`, the same shape as `RefundRules` (a verdict plus amounts, with **no money baked into a string** — `RefundDecision` already settled that formatting is a client concern, wrong the first time a tenant trades in another currency and unlocalisable for MAUI's `I18N_L10N`; I had written `£` into it and that decision caught me). Headroom is **net of what is already off**, because the commit-time apportioner judges it that way and a gate that disagrees passes a basket through one and throws at the other. ⚠ **The boundary is INCLUSIVE** — a 100% staff discount is legitimate, and only *more* than everything is refused. Returns are **not** headroom. 15 tests, mutation-checked. ⚠ **(b)'s open question is ANSWERED — Matt, 2026-08-14: "Base it on roles."** A discount **level IS a role's `pos.discount` ceiling** (`MaxPence`): Cashier £5, Supervisor £50, Manager unlimited. So *"configurable by the owner"* is `AdminController.cs:306`, which already edits `maxPence`; *"levels which can be added"* is **adding a role**; and *"over certain levels need approval from a supervisor or higher"* is the step-up that `RequestSupervisorOverrideAsync` already performs. ⚠⚠ **No new entity, no new portal screen, and — critically — no SECOND place a money limit lives.** A parallel "discount tier" table would have meant a cashier's ceiling was stated twice, and the day the two disagreed the till would enforce one and the portal display the other. **Roles were already the answer; the ruling makes it the answer on purpose.** ⚠ What (b) still needs is therefore **only** what the gate could not say: the authorisation must reach the **platform** — see (c), which is the same wire change. **(c) is NOT built.** | 27, and the discount path generally |
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

---

## 0.3b ⚠⚠ The input-alert back-out audit (2026-08-18)

`LaunchInputAlertAsync` returns **null** when the operator leaves a dialog without confirming —
tapping outside, Escape, or the Cancel button. Every ⚠ row below dereferences that null, most inside
an **`async void`**, so the exception reaches the dispatcher unhandled and **the till dies**.

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
| 2 | **Checkout must match the web till** | The web till's one-screen tender (every method listed, live Paid/Remaining, named refusals) vs MAUI's sequential prompts. ⚠ The ARITHMETIC is already shared and pinned (`TenderLoop` ↔ `tendering.ts`, C2) — this is the SCREEN, which is cutover step 11b and was always going to be the bigger half | **3–4 d** |
| 2b | ⚠⚠ **Store credit must come from a KNOWN customer, or be gated with a recorded reason** | ✅ **RULING GIVEN AND MOSTLY ALREADY TRUE — 2026-08-18 (backend 1.17.4 + portal 1.10.0).** Matt: *"Store credit needs to be for a KNOWN customer. Adding credit needs to have a reason and be viewable in the customers history."* ⚠ **So there is NO anonymous-credit path to build** — the plan assumed a supervisor-authorised exception and the answer is that it is simply refused. Credit is a liability the shop owes a **named** person; a bearer instrument is what a **gift card** is (WP13, already built). ✅ **SPENDING credit was already correct on MAUI** and needed nothing: the tender is only offered when `CreditAvailablePence > 0`, which requires an attached customer, and `TryRedeemStoreCreditAsync` refuses defence-in-depth if the customer is detached mid-checkout — *"committing a sale carrying credit nobody owns would be money from nowhere"*. The ruling confirms that design rather than changing it. ⚠⚠ **WHAT WAS ACTUALLY WRONG WAS THE REASON, AND IT WAS WRONG IN TWO PLACES.** `POST /customers/{id}/credit/issue` did `body.Reason?.Trim() ?? "grant"` and the portal sent `issueReason \|\| "goodwill grant"` — so credit could be granted with **no reason anybody typed**, and the history then showed a plausible-looking word that means nothing. ⚠ **That is worse than a blank, because it READS as an audit trail** — the same class of fault as a substituted figure. ✅ The endpoint now **refuses** a missing, empty or whitespace reason (all three reached the old default), and the portal marks the field, gates the button and no longer substitutes. ✅ **The history was already there** — `GET /customers/{id}/credit` returns the entries with their reasons and the portal's *Credit history* table already shows a Reason column, so *"viewable in the customers history"* was met; the round-trip is now **asserted** rather than assumed. ⚠ **Known-customer is structural on issue**: the route carries the id and an unknown one is a 404, which the test pins too. ⚠ 1 new integration test, **watched going red** (a reasonless grant returned `OK`). ⚠ It also exposed a suite fragility worth knowing: WP13.5 throttles **50 rps per tenant** and every test shares Kapow, so adding requests anywhere can rate-limit a **different** test — a sibling failed reporting `TooManyRequests` while asserting `BadRequest`. Both now use the retrying `Send` helper that two other test classes already carry. ⚠ **Not built, and not asked for: an add-credit path on the till.** Credit is granted in the portal; if a till ever needs one it inherits the same mandatory reason from the endpoint | **done** |
| 3 | **Click the price in the row to adjust it** | ✅ **DONE 2026-08-18 (till 1.86.0).** The **price cell is now the way in** — tapping it opens the adjust box, which is what the web till does (its price cell is a `linklike` button). ⚠ Before this, **Adjust existed only on the right-click / long-press context menu** with nothing on screen to say so: the identical discoverability fault already reported about editing an item (*"I didn't know how to open it"*, 2026-08-10). ⚠ An adjusted line is now **marked with a `*`**, as the web till marks it — money-visibility, not decoration, because a £5 item retyped to 50p otherwise looks exactly like an item that costs 50p (finding W's lesson on a different control). ⚠⚠ **NOT a literal in-cell text box, and that is a decision worth challenging.** A `Button` or an `Entry` in the cell would match the web till's *implementation*, but either needs its colours resolved from the theme, and **a price that renders invisible on the money screen is the fault that cost build 1.74.0** — so the existing `Label` and its binding are untouched and the gesture sits on a wrapper. If the tap turns out not to fire inside a `ViewCell` on WinUI, the price still renders exactly as before and the context menu still works: **feature-absent beats price-invisible.** ⚠ Unverifiable by machine — **§G42a** is the only check. Parity here is in FUNCTIONALITY (Matt, 2026-08-17), and the function asked for was *"click the price"* | **done**, well inside 1–1½ d |
| 3b | ⚠⚠ **A MONEY BUG FOUND BESIDE IT: a second scan joined a hand-adjusted line AT THE ADJUSTED PRICE** | ✅ **FIXED 2026-08-18 (till 1.86.0).** Ring a £5 item, adjust it to 50p, **leave the line selected**, scan the same item again — and the second unit joined that line at **50p**. The shop sold it for a tenth of its price, silently, with the receipt as the only evidence. ⚠ **The fault was TWO PATHS, not a missing check.** MAUI's search path compared the price pair (and so behaved correctly by arithmetic accident); its *selected-line fast path* checked only the item id and "not a return". The web till has never done this — `basket.ts` excludes `!l.adjusted`. ⚠ Found by **reading the web till's reducer instead of ours** while doing item 3, which is the lesson: the recorded rule is *when Matt says a screen does not match, check the other screen before defending ours*, and it pays on logic as well as layout. ✅ The rule now lives in `SharedKernel.BasketMerge` (**C1**), both MAUI call sites ask it, and its TypeScript twin is pinned by a **shared ten-case vector table** (**C2**) — `BasketMergeTests` and `till/basketMerge.test.ts`, same figures, **and both sides were RUN** (10 green in C#, 10 green in vitest on the build Mac; 224 green there overall). ⚠ Two mutants watched dying: removing the `adjusted` guard, and dropping the ex-half of the price comparison. ⚠⚠ **The `adjusted` flag is tested BEFORE the price pair** — adjusting a line to exactly its catalogue figure would otherwise pass the arithmetic and re-merge, which is the accident the old code relied on. ⚠ Honest weakness recorded in C2: the TS side is a **copy under test**, because the web till's predicate lives inside a `find(…)` inside `addLine` and is not exported. **Exporting it is the real fix and is not done.** ⚠ **§G42d** is the hand-check, and it is written as a money check | **found and fixed** |
| 5 | **Reports do not match the web till** | ✅ **ALL THREE GAPS CLOSED 2026-08-18 (till 1.85.0 + backend 1.17.3).** ⚠ First, the gap was smaller than this row said and in a different place. The web till has **eight** subtabs (Summary, Custom, VAT, Items sold, Category sales, Best sellers, Stock, Negative stock); MAUI had **five**. ⚠ *"Custom"* is **not** a missing report — it is the web till's separate custom-range takings tab, and MAUI's From/To pickers already apply a range to **every** report, which subsumes it. So the real gaps were **Stock**, **Negative stock** and **drill-down**. ✅ **Stock and Negative stock are in** — and the framework did what it promised: `ReportCatalogue` took **one entry each**, no screen, no viewmodel, no XAML (Matt, 2026-08-16: *"Can I check that you are writing the framework that new reports can just be dropped into MAUI?"* — this is the first time that claim has been tested by a new report, and it held). ⚠⚠ **AND THE GATE WAS WRONG AGAIN.** `GET /api/v1/stock/levels` was `portal.reports.view` only, which no Supervisor or Cashier holds — **the fourth time this same defect has been fixed**, and the third fix is recorded in a comment on `POST /api/v1/stock/levels/bulk` **six lines above the endpoint it missed**. Every fix so far landed on the endpoint that happened to be in use rather than on the rule; the two URLs are now in `TillHardeningE2eTests`, added **first** and watched returning 403. ⚠ Also honest: **no date range applies to stock** (it is a fact about *now*, a sum of the movement ledger) and the report **says so on screen**, because the From/To pickers sit above it. ⚠ 5 new tests, and the one that matters was **mutation-checked** — dropping `filter=negative` makes Negative stock list the whole shop, which under that heading reads as *"everything is below zero"*; the mutant was watched dying. ✅ **AND DRILL-DOWN IS IN — the one that turns a report into an answer.** A new **Sales** report lists every sale in the range across **every till** (looking for a sale you did not ring up is the usual reason for looking), and tapping a row opens it: lines with their VAT and their discounts, how it was paid **including change given**, what has already been refunded or voided **and why**, and the totals. ⚠ A refund is itself a sale with a **negative** gross and is shown as one. ⚠ **Read-only** — refunding stays on the till screen where the ceiling and the cross-till cap live; a second door to the money with none of that behind it is not a convenience. ⚠ `TillTable` gained an **opt-in** row tap (default null), so the four tables that predate this cannot change behaviour, and only rows carrying a `DrillSaleId` do anything — a tap on a VAT bucket is inert **and silent**. ⚠⚠ Found on the way: `GetAsync<T>` in the shared client **never caught `JsonException`**, hidden because every report until now deserialised an OBJECT and `{}` is valid for one; the first caller to want a JSON **array** threw, and the test written for exactly that (`No_report_throws_when_the_server_answers_with_nonsense`) caught it. ⚠ Fixing it too widely then broke `A_dead_network_is_an_outcome_not_an_exception` — a dead line must carry its REASON — so the catch was narrowed to deserialisation alone. **Two tests I wrote caught two of my own mistakes in one slice**  | **done**, inside the 2–3 d estimate |
| 5b | ⚠ **"I thought we had built this where reports approved in the portal are pushed to each till version?"** | ⚠⚠ **ANSWERED 2026-08-18, AND IT IS A NEW WORK PACKAGE — NOT A §5c SLICE.** Matt: *"Portal shows which reports a till can show. Separate permissions need to be created for viewing them."* **Both halves, and they are independent.** ⚠ **(a) THE PORTAL CURATES THE SET.** A till shows what the portal has published to it, so `ReportCatalogue` stops being the answer and becomes **a superset the portal chooses from** — on every till. Needs: storage for a published set (per tenant, overridable per till), an endpoint the till reads on the cadence, a portal screen, and both clients consuming it instead of rendering their whole catalogue. ⚠ **(b) EACH REPORT GETS ITS OWN PERMISSION.** Today all eight share `portal.reports.view` / `pos.reports.view`, which is exactly why widening that gate for a Supervisor widened it for **every** report at once — and why the same gate defect has now been fixed **four** times (`/api/v1/sales`, `/reports/summary`, the five report endpoints, `/stock/levels`). Needs new `PermissionCatalogue` entries, `RbacSeeder` defaults per built-in role, and the endpoints re-gated one by one. ⚠⚠ **A QUESTION THE RULING SETTLES WITHOUT BEING ASKED**: a till *published* a report its operator may not *read* must show **nothing**, not a refusal — **the publish decides the menu, the permission decides the door.** A greyed-out row would leak what other roles can see. ⚠ **Nothing is built yet, and deliberately so**: half a permissions model is worse than none, because the gaps look like grants. ⚠ Sizing now that it is understood: **(b) ≈ 1–1½ d** (mechanical, and it makes the four-times-fixed gate defect structurally impossible), **(a) ≈ 2–3 d** across backend, portal and two tills. **Do (b) first** — it is smaller, it is the security half, and (a) without it just hides reports rather than protecting them | **decision made · ≈ 3–4½ d, not started** |
| 6 | **Loyalty on MAUI is unusable** | ✅ **DONE 2026-08-18 (till 1.89.0) — and it is now the WEB TILL'S screen.** Matt: *"Ensure the webtill and maui are inline."* So the web till's `LoyaltyPage.tsx` was read first and MAUI matched to it, rather than the other way round. ⚠ **Columns went from three to the web till's six, in its order**: Customer (with the **email under the name**, exactly as it renders it), Member no., Tier, **Discount**, **Renews**, Credit. The two that were missing are not decoration — *"why didn't they get their 10%?"* cannot be answered from a screen showing neither the rate nor the renewal. ⚠ Empty cells are **"—"**, the web till's `<span className="muted">—</span>`: a blank reads as a screen that failed to load. ✅ **EDIT EXISTS AT LAST** — the web till has had a per-row Edit since it was written and MAUI had none. ⚠⚠ **It was absent ON PURPOSE and the ruling removed the reason**: `PlutusApiClient`'s own header said it *"deliberately exposes no customer edit"* because an email change was thought to redirect an account. Matt, 2026-08-18: *"A customer needs to have a unique ID, because people can change emails over time. Audit please."* Identity is `Customer.Id`; nothing resolves a customer by email; the server records `before`/`after`. ⚠ **A row TAP, not a button column** — the web till has room for a per-row action and a till screen does not; hand-run 1 proved a gesture fires inside a `ViewCell`. Silent for an operator without `customers.manage`, because the web till simply does not render the button for them. ⚠ `prefillWithPlaceholder: true` puts the current values in as **real editable text** — without it they are grey hints, `InputResults` reads `entry.Text`, and changing only the phone would submit an empty name. That is **finding K** (*"I can ONLY change the tax"*), and this is the first edit form written since it was fixed. ⚠ **The tier stays a separate action**, unlike the web till's combined dialog: `InputAlert` has no picker, and the web till's own comment explains why create-then-tier must remain two separately-gated calls. Same capability, one more tap — **parity in FUNCTIONALITY** (Matt, 2026-08-17). ⚠ `UpdateCustomerAsync` added to the shared client, which never had one | **done** |
| 7 | ⚠ **Nothing updates unless you navigate away and back** | ✅ **DONE 2026-08-18 (till 1.83.0).** ⚠⚠ **This was the THIRD report of one fault, and the first two fixes are why it came back.** 2026-08-11: *"The open float was 'Waiting' and never updated. I navigated away and back onto the cash tab and it had updated."* Then finding N: today's takings read at sign-in, a day of trading never moving them. Then this. Each fix was correct **and local**, so the next screen inherited nothing — and pitfall 17 already *said* "when you find one stale screen, go and look for its siblings straight away", which nobody did. **A documented pattern was the thing that failed**, so it is now a shared class: `Services/Sync/LiveScreen.cs`, two lines in a page constructor, no `OnAppearing` override and nothing to remember to unsubscribe. ⚠ **The screen that could not update AT ALL was Store Information** — loaded in its constructor, no refresh of any kind, so a portal correction could not reach the till until somebody signed out and back in. That is almost certainly the concrete case behind the report, and it is the same screen whose opening hours were argued about the day before: **a till that cannot be shown a corrected value is indistinguishable from a portal that never saved it.** ⚠ Cash and Statistics were **rewritten onto the shared class though they already worked** — so that the rule has no exceptions, which is the only thing that stops the fourth report; §G39b is the regression check on those two. ⚠⚠ **A judgement call worth challenging: the long tables deliberately do NOT tick** (`onCadence: false` — Items, Loyalty, Reports). Rebuilding a 500-row `ObservableCollection` every 60 seconds sends a `CollectionView` back to the top under the operator's hands: **a worse fault than the staleness, and a self-inflicted one.** They still reload on arrival and on an explicit Search. ⚠⚠ **NONE of this is machine-testable** — a MAUI `Page` cannot be *constructed* in the test project (`BindableObject` needs a live WinUI3 dispatcher; it is why 3 tests are skipped), so `LiveScreen` has no unit test and cannot have one. **§G39 is the only check that exists**, and it is written knowing that |
| 8 | **"Choose bag item" in Store Information** | It sets which item the till's quick **Bag** button rings up. ⚠ **The web till has the identical setting — in Settings** (`prefs.ts bagBarcode`, Settings → Till). So MAUI's is in the **wrong place**, and it moves to Settings with item 9. Not a mystery feature, a misfiled one | **folded into 9** |
| 9 | **Settings must match the web till** | ⚠ Matt: *"most of the MAUI Plutus tab would move into settings"* — **agreed, and it is the right instinct**: the web till has one Settings page with sections (Till, Printer, Hardware, Device), while MAUI splits device/platform concerns into a separate "Plutus" tab. Merging them is mostly moving existing panels + the bag item from Store Information | **2 d** |
| 10 | ⚠⚠ **Colour customisation reaches the WEB till and barely touches MAUI** | 🟡 **STARTED 2026-08-18 (till 1.87.0) — the first XAML in MAUI that consumes a theme slot.** ⚠⚠ **AND IT UNCOVERED A SEPARATE DEFECT: two styles were referenced twenty times and defined NOWHERE.** Every basket row label carries `Style="{DynamicResource ListItemDetailTextStyle}"`, and no such key existed in the application — `DynamicResource` to a missing key **applies nothing, silently**, so the money screen's rows have never been styled at all. There was no `Styles.xaml` in the project. ✅ Both styles now exist and take their ink from `ThemeInk`, so **fixing the dead references and starting the theming are one change**, with a blast radius of exactly the elements that already asked for those keys. ⚠ The till page now takes its background from `ThemeSurface`, because **ink and surface must be applied as a PAIR** — setting a text colour without owning the colour behind it is how Store Information shipped unreadable (1.74.0), and how a dark-mode device would render near-black ink on near-black. ⚠ **CORRECTION TO THIS ROW'S OWN CLAIM:** it said the six brushes in `Colors.xaml` are built with `StaticResource` "which captures the colour at parse time" — that is only half right. `Theming.Apply` **already rebuilds every brush** on each change (its own comment records the hazard), so the definitions are harmless; the real gap was, as the rest of the row said, that **no XAML consumed a slot**. Acting on the wrong half of that would have been a wasted change. ⚠⚠ **NEW GUARD, and it is the durable part**: `XamlResourceTests` walks every XAML file and fails if a referenced resource is undefined, or if a theme slot is reached by `StaticResource` (frozen at parse time = half a themed screen). **Watched going red** against a renamed style. This is the first automated check of any kind on MAUI's XAML. ⚠ **WHAT REMAINS: cash, inventory, reports, loyalty and settings still paint with platform defaults**, and the accent/line slots are not consumed anywhere yet. The pattern and the guard now exist, so the rest is mechanical — but it is per screen and each one wants the surface-with-ink pairing | **~1 d left** of 1½–2 d |

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
