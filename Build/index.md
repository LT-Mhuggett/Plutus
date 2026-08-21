# `Build/` — documentation index

Three places, one rule each. **No document is ever deleted** — a plan just moves right along this line
(runtime debris that lands in a docs folder is not a document, and does get removed):

| Where | What lives there |
|---|---|
| `Build/` (this level) | **Standards and reference** — how things must look and behave. No expiry date. |
| [`Build/To do/`](To%20do/) | **Plans with work still in them.** If it's here, something is unbuilt. |
| [`Build/archive/`](archive/) | **Delivered.** Each carries a banner saying what shipped and what didn't. |

⚠⚠ **CHANGED 2026-08-17 — where the living state lives.** Matt: *"I only want the handover to be for
the following day."* So:

| | Is now |
|---|---|
| [`HANDOVER.md`](../HANDOVER.md) | **ONE DAY LONG.** What to do next session, and nothing else. ⚠ Do not let it grow back into a history — rewrite it; the commits are the record |
| [`To do/MAUI-retrofit.md`](To%20do/MAUI-retrofit.md) **§0** | **The living state** — what is deployed, every one of Matt's rulings, open items, and the lesson about stale status markers |
| [`archive/handover-history-to-2026-08-17.md`](archive/handover-history-to-2026-08-17.md) | The old handover's day-by-day narrative back to 2026-07-28, **verbatim, dropped from nothing**. ⚠ History, not instruction: every "START HERE" in it is superseded and its version numbers were true on their date. Read it for the reasoning — how fourteen faults were found on 2026-08-10, six invisible to every automated test — never for the state |

⚠⚠ **For the MAUI till there is exactly ONE document:
[`To do/MAUI-retrofit.md`](To%20do/MAUI-retrofit.md)** — what remains, how to build it, what is done,
and what comes out afterwards. **Consolidated 2026-08-12 from five** (both `To do/` MAUI plans,
`maui-whats-left.md`, `MAUI-parity.md`, `legacy-removal.md`), at Matt's instruction: *"I do not know
why its splintered into so many."* All five are in [`archive/`](archive/) with banners.

⚠ **It lives in `To do/`, not at this level**, because it has unbuilt work in it — the folder rule
decides, not how often the document is opened. When its last step closes it moves to `archive/`.
⚠ **Before that happens, lift the parts that outlive the retrofit up to this level** — binding
defaults 1–18, the pitfalls, the item-identity seam — per *Keeping this honest* below. Archiving them
unlifted is how the useful half gets buried with the finished half.

Last audited **2026-08-20** against the code, not against the documents' own headers.

> ### 🧹 What the 2026-08-20 audit changed
>
> Matt: *"Archive anything in there that is complete… merge documents that are similar and perform a
> general cleanup."* `To do/` held **eight** documents while this index described **five**, so the map
> had stopped matching the territory — which is the one failure an index cannot afford.
>
> **Archived — complete:** [`Discount plan.md`](archive/Discount%20plan.md) and
> [`Multi-barcode plan.md`](archive/Multi-barcode%20plan.md) (both built **and** deployed;
> only their hand-runs remain, and those live in `Test Maui.md`) ·
> [`plutus-catalogue-sync-design.md`](archive/plutus-catalogue-sync-design.md) (a greenfield **design
> study**, never a plan — and its central recommendation was deliberately rejected, which its banner
> now says in full) · [`shop-day-test.md`](archive/shop-day-test.md) (pinned to MAUI **1.43.0** with the
> till on **1.109.0** — sixty-six builds stale).
>
> **Merged:** `syncfusion-footprint.md` → [`Shrink MAUI Build.md`](archive/Shrink%20MAUI%20Build.md)
> **§4**. Two documents were describing the same 76 MB from opposite ends, and the removal *order* in
> one depended entirely on the licence hazard explained in the other. Reading either alone was the
> failure mode.
>
> **Deleted:** `To do/plutus-till-2026-08-08.log` — 125 bytes of runtime debris (two startup lines from
> till v1.1.0), untracked and referenced by nothing.
>
> ⚠⚠ **AND THE RULE THIS AUDIT PROVED ON ITSELF.** Below, *"a plan's own status line is the least
> reliable thing in it"*. Both plans archived today said **"Nothing is deployed yet"** in their status
> lines while being live in the shop, and `shop-day-test.md` said it was *"the fuller reference"* long
> after the document it deferred to had grown seventeen times larger. **Every stale claim found today
> was a document describing its own status or its own relationship to another document.** Neither is
> knowable from inside the document. Check both from outside, or do not write them.
>
> ### 🔎 A second pass, same day — four more proposed for archiving, and **only one of them was**
>
> Matt: *"I think the following files are stale and can be archived… Can you confirm this please."*
> Checked against the code. **One was; three were not, and two of those would have been damaging:**
>
> | Proposed | Verdict |
> |---|---|
> | `kapow-db-gap-analysis.md` | ✅ **Archived.** Findings F1/F2/F3/F5 fixed, F4 consciously rejected, §5 superseded by the NatApp doc, and it analyses a backup two snapshots old |
> | [VAT-FixLater-Report](VAT-FixLater-Report-2026-07-23.md) | ⛔ **Kept — it is exactly current.** The live endpoint answers **47**, the same 47 items, the same 20/27 split: **nothing corrected in 28 days.** A live worklist, not history. ⚠ It *did* have a real fault — 27 rows labelled `Exempt` where the live band is `Zero rated (books)` — now fixed |
> | [Loyalty Update across all tills.md](To%20do/Loyalty%20Update%20across%20all%20tills.md) | ⛔ **Kept — ZERO implementation.** Every one of its own types returns **0 references** and `IngestSaleRequest` still has no `CustomerId`, so no sale can be earned against. **Not stale — untouched.** ~45–55 days of unbuilt design; archiving it would file an unstarted programme under *Delivered* |
> | [MAUI-retrofit.md](To%20do/MAUI-retrofit.md) | ⛔⛔ **Kept — the single most active document in the repo.** **≈10–15 working days left** (re-costed 2026-08-20; its own §6/§7 said 35–40 and were three days stale), and **§0 is the living state** — its deploy table was updated hours before this was proposed. `CLAUDE.md` mandates reading it for any MAUI work, in three places. Archiving it would file the **current deploy state** under *Delivered* and bury §0.3b — **17 dialogs that can crash a till on back-out** |
>
> ⚠⚠ **THE DISTINCTION WORTH KEEPING: "OLD" IS NOT "STALE".** A document nobody has touched for weeks
> because the work has not started is **live and unbuilt**; a document nobody has touched because its
> subject was finished is **archivable**. Loyalty and the retrofit are the first kind — the retrofit had
> been *edited that day*. Test the claim, not the calendar. And note the reverse case too:
> `VAT-FixLater-Report` is dated **2026-07-23** and is nonetheless accurate to the row — the oldest
> filename in `Build/` was the most current document in it.

---

## Standards & reference — these stay

| Document | What it's for |
|---|---|
| [plutus-platform-architecture.md](plutus-platform-architecture.md) | The platform architecture (v3). **Wins on any conflict** with a plan. |
| [plutus-operator-platform-requirements.md](plutus-operator-platform-requirements.md) | What the operator platform must do, with a built-status appendix. |
| [repo-runbook.md](repo-runbook.md) | Build, test, migrate, deploy — plus the **21** codebase pitfalls that have each cost a session. Read before writing code. ⚠ This row said *"the ten pitfalls"* until 2026-08-20, when there were already twenty-one — **do not write a count you will not come back and update.** |
| [table-standard.md](table-standard.md) | The shared `DataTable` contract (sort / search / 25-50-100 / paging) every table in **all four surfaces** must use — web till, management portal, operator console **and the MAUI till**. ⚠ This row said *"three"* until 2026-08-20, repeating the exact error the document itself was corrected for on 2026-08-16: the omission is **why the MAUI till has no sortable, searchable or paged table anywhere** while claiming to follow this standard. |
| [till-design.md](till-design.md) | **THE SINGLE SOURCE OF TRUTH FOR EVERY TILL BUILD.** What the surfaces are (A), what each till can do (B), and where every shared rule lives (C). **Any till work reads it first and updates it in the same commit** — that is what keeps till versions in sync. ⚠ **C2, the drift register, is the section to read before writing anything that computes money on a client.** Consolidated 2026-08-08 from the old `till-parity.md` + `till-anatomy.md`. |
| [Test Maui.md](Test%20Maui.md) | ⚠⚠ **THE ONLY HAND-TEST DOCUMENT — and the one to hand somebody else.** Written for a person who does not know the codebase: **§A** the reported faults and what each looked like when broken · **§B** a full shop day · **§C** the two-person safety cases · **§D** what is deliberately not built, so nobody reports the plan as a bug · **§E** work added late and never tested · **§W** the web-till checks · **§G54–§G67** one section per feature. Written for Matt on 2026-08-11: *"can you create a 'Test Maui.md' document that I can refer to, and that I can get anybody else to use to test too."* ⚠ **`shop-day-test.md` was a second hand-test script and is archived** (2026-08-20) — it was pinned to till 1.43.0. **Do not start a second one again**; add a §G section here. |
| [VAT-FixLater-Report-2026-07-23.md](VAT-FixLater-Report-2026-07-23.md) | The legacy VAT rows deliberately **not** auto-repaired (owner's decision). Live view: portal → Reporting → VAT (`GET /api/v1/reports/vat-integrity`). ⚠⚠ **RE-VERIFIED AGAINST THE LIVE SERVER 2026-08-20 and it is CURRENT, not history** — the endpoint answers **47**, the same 47 items, the same 20/27 split. **Nothing has been corrected in 28 days**, so this is a live worklist and does not archive. ⚠ Its band column was fixed the same day: 27 rows said **Exempt** where the live band is **Zero rated (books)** — verified as the identical id set. Not cosmetic: both are 0%, they differ on a VAT return, and correcting an item from the old table would have set the wrong one with every total still balancing. |
| `secrets.local.md` | Local credentials. **Gitignored — never commit.** |

## To do — plans with work still in them

In [`To do/`](To%20do/). **Three documents**, and the count has now been wrong in **both** directions in
one day — which is the argument for checking it rather than trusting it:

- it said **five** while the folder held **eight** (the two plans built on 2026-08-20 and the catalogue
  design study were never listed);
- then it said five while the folder held **three**, because `Shrink MAUI Build.md` and
  `Migrate back end to Linux.md` were moved to `archive/` and the index was not told.

⚠⚠ **AND THE SECOND MOVE FOOLED ME INTO A BAD REPAIR.** `Migrate back end to Linux.md` vanished from
`To do/`, I ran `git status` **filtered to `To do/`**, saw ` D`, called it an accidental deletion and
restored it from git — creating a **duplicate**, because the file had been *moved* and was sitting in
`archive/` all along. The `??` line naming its new home was in the output I had filtered out.
**When a tracked file disappears, look for where it went before concluding it is gone** — `git status`
without a path filter says so in the next line.

⚠ **If you add a document to `To do/`, add its row here in the same commit.** A plan the index does not
list is a plan nobody finds, and an index that undercounts its own folder is worse than no index —
it reads as authoritative.

⚠ **`Shrink MAUI Build.md` is not a sixth MAUI document** and must not become one. It holds artefact
size, the Syncfusion footprint and removal order — no capability status, no deploy state, no rulings.
If a status claim ever appears in it, fold it into `MAUI-retrofit.md` and delete it: that is precisely
how the five consolidated documents splintered in the first place.

| Document | What's left |
|---|---|
| [MAUI-retrofit.md](To%20do/MAUI-retrofit.md) | ⚠⚠ **THE ONE MAUI DOCUMENT.** Part 1 what remains (open faults, the open steps with bodies and DoDs, the ⬜ rows, the risks, and the L1–L10 legacy-removal register Matt actions last) · Part 2 how to work (protocol, binding defaults 1–18, offline horizons, pitfalls, the item-identity seam) · Part 3 what is done (the step and WP records, VAT, the hand-run). **≈10–15 working days left** — re-costed 2026-08-20. ⚠ Steps 26 and 27, once two thirds of the estimate, have both LANDED; §6/§7 said 35–40 for three days after §0 was corrected, which is how it came to read as unfinished when it is nearly done. Consolidated 2026-08-12 from five documents, all now archived. |
| [Loyalty Update across all tills.md](To%20do/Loyalty%20Update%20across%20all%20tills.md) | **The loyalty programme design** (Matt, 2026-08-13) — a configurable credit currency ("gems"), earning rules, an append-only loyalty ledger with holds, rewards, tiers with auto-qualification, and a member-facing portal. §16 pins the surfaces (**every till + webstore + both portals**) and the rule that **earning is computed at ingest, never on a till**; §17 is the gap analysis against what is already live and the four-phase sizing (~45–55d). ⚠⚠ **NOT STARTED — nothing in it is implemented** (verified against the code 2026-08-17: every one of its own types returns zero references, and `IngestSaleRequest` still carries no `CustomerId`, so no sale can be earned against). ✅ Its decision 1 (discount vs tender) was **settled 2026-08-14 — a discount** — so it is no longer gated; ⚠ but **9 of its 21 decisions are still open or merely proposed**, including refund symmetry, which is a cash-out exploit if got wrong. ⚠ Retrofit step 27 is the **parity** slice this builds on, not this programme. |
| [NatApp data translation agent and scripts.md](To%20do/NatApp%20data%20translation%20agent%20and%20scripts.md) | **The data migration — now a RUNBOOK as much as a plan, and it has been EXECUTED TWICE.** The bridge run (top-up, 2026-08-17, §7) and the **full replace** from the 19_08 backup (2026-08-20, §8, which carries the no-questions rerun script `~/PLUTUS/natapp-replace.sh` on the Mac). ⚠ **Which one to reach for:** a periodic top-up while the till still trades = the **bridge** (never touches what exists); a fresh backup that should *become* the truth = the **replace**. ⚠ **It stays in `To do/` because the one that matters has not happened**: the cutover run, a replace taken after the physical till's last sale. Meets the retrofit at **item identity** ([`MAUI-retrofit.md`](To%20do/MAUI-retrofit.md) §16) — the barcode is the invariant, not the GUID. |

Nothing on the web till or the portal is outstanding **as a plan**. ⚠ Two smaller items are tracked
inside `MAUI-retrofit.md` §0 rather than as plans of their own, and neither should be forgotten because
of where it lives: the **13 portal dialogs still missing a close ✕** (§0.3e, ≈half a day, mechanical)
and the **17 MAUI input-alerts that crash on back-out** (§0.3b, the serious one).

## Archived — delivered

Each carries a banner explaining what shipped and what was deliberately left.

| Document | Outcome |
|---|---|
| 🆕 [Discount plan.md](archive/Discount%20plan.md) | **Archived 2026-08-20 — built AND deployed the same day.** Scheduled discounts ("Wednesday Warhammer") and operator-applied ones, across every till. DP1–DP5 live. ⚠⚠ **The one thing to know: a rule does nothing until somebody ticks *Apply it automatically*** — all six pre-existing rules had `AutoApply = 0`, so the feature deployed working perfectly and applying nothing, and was reported as broken. Configuration, not a defect. ⚠ Two of its own §3 statements were **wrong and the code was right** (the window boundary; a `decimal Amount` meaning pounds-or-fraction, which the architecture guard refused) — the banner says which. Only the hand-run (§G64/§G65) remains. |
| 🆕 [Multi-barcode plan.md](archive/Multi-barcode%20plan.md) | **Archived 2026-08-20 — built AND deployed, in two halves, and the split is the lesson.** 1.19.0 shipped the entire spine — entity, migration, endpoints, feed field, alias resolution at three doors, both offline caches — **and no way for anybody to use it**; Matt found that within the hour. 1.20.0 added the screens. ⚠⚠ **A plan whose last package is a sync feed has no package for the screen. A feature is delivered when somebody can use it, not when the data path works.** Endpoints exercised live against the real server. Only §G66/§G67 remain. |
| 🆕 [shop-day-test.md](archive/shop-day-test.md) | **Archived 2026-08-20 — superseded by [Test Maui.md](Test%20Maui.md); do not run it.** Pinned to MAUI build **1.43.0** with the till on **1.109.0**. ⚠ Its own signpost was half right and half rotted: *"give them `Test Maui.md` instead — this page stays the fuller reference."* The first clause held; the second stopped being true when the other document grew seventeen times larger, and nobody noticed. **Two hand-test documents is one too many.** |
| [plutus-sonnet-build-spec.md](archive/plutus-sonnet-build-spec.md) | Phases 0–2 — foundations, the T1.3 sale contract, web-POS cutover, device enrolment. |
| [plutus-implementation-plan.md](archive/plutus-implementation-plan.md) | Phases 0–12. Open items are externally gated (payment adapter, Woo outbound go-live), not unbuilt. |
| [plutus-operator-platform-plan.md](archive/plutus-operator-platform-plan.md) | Phases 13–18. WP16.4 dunning and WP17.2 gateway health have their config layers live and wait only on a provider account. |
| [operator-portal-plan.md](archive/operator-portal-plan.md) | OP1–OP4. Its §0 runbook was extracted to [repo-runbook.md](repo-runbook.md). |
| [portal-till-refresh-plan.md](archive/portal-till-refresh-plan.md) | P1–P6. Only the cosmetic P1.3 legacy-table sweep is left. |
| [further-enhancements-plan.md](archive/further-enhancements-plan.md) | FE1–FE9, including the hardware agent — now verified on real hardware. |
| [loyalty-usability-plan.md](archive/loyalty-usability-plan.md) | LP1–LP3, plus the flagged `localStorage` follow-up. |
| [WebApp-2026-07-23-plan.md](archive/WebApp-2026-07-23-plan.md) | The web till, Phases 1–5. Phase 0's B2C blocker was routed around, not solved. |
| [VAT-Investigation-2026-07-23-plan.md](archive/VAT-Investigation-2026-07-23-plan.md) | Guardrails live; the repair list lives on in VAT-FixLater-Report. |
| [woo-sku-audit-2026-07-26.md](archive/woo-sku-audit-2026-07-26.md) | Point-in-time audit that fed WP6.0. Figures will have drifted. |
| 🆕 [Shrink MAUI Build.md](archive/Shrink%20MAUI%20Build.md) | **Archived 2026-08-20 — tier C delivered (till 1.110.0).** Syncfusion and `DocumentFormat.OpenXml` are out of the MAUI till: **264 MB → 169 MB (−36%)**, 0 Syncfusion DLLs, and the stale licence key deleted. ⚠ It absorbed `syncfusion-footprint.md` the same day, so the licence hazard, the swap costs and the removal order are all in its §4. ⚠⚠ **Its §3 is struck through as a WRONG "verified by grep"** — it claimed `DocumentFormat.OpenXml` was uncalled and one-line removable; `ExcelHandling.cs` was built on `SpreadsheetDocument` and it would not have compiled. ⬜ **Two things it still names**: the 86 native `.mui` folders (3.8 MB, unmovable — see its §6) and **signing the MSIX**, which is the real answer to *"why 299 files in the root"* and ≈half a day. |
| 🆕 [Migrate back end to Linux.md](archive/Migrate%20back%20end%20to%20Linux.md) | ⚠ **Moved to archive on 2026-08-20 — but note it is UNBUILT, so this is the one archive entry that does not fit the folder rule.** *"Nothing needs redeveloping"*: the backend is plain `net10.0` with no native dependencies and no OS branching, so `osx-arm64` is a publish flag. The real cost is **the MySQL auth channel** (the unix-socket workaround for `caching_sha2_password` cannot cross a machine boundary), four macOS-coupled ops scripts, supervision, ICU and the proxy split from ETRIE. **~1–2 days, operational, none of it started.** ⚠ **If that move was not deliberate it belongs back in `To do/`** — a plan in `archive/` reads as delivered, and this one is not. |
| 🆕 [kapow-db-gap-analysis.md](archive/kapow-db-gap-analysis.md) | **Archived 2026-08-20 — its job is done.** The source NatApp SQLite database, table by table. **F1** (colliding timestamp sale ids), **F2** (no VAT on the sale), **F3** (money as decimal TEXT) and **F5** (stock a mutable counter) are all fixed in the platform — F3 mechanically, by the architecture suite's pence guard. ⚠⚠ **F4, *"the item primary key is the barcode"*, was deliberately NOT fixed and never will be** — `Item.IdOne` is the identity, and the behaviour F4 wanted arrived instead as additive alias rows. ⚠ **§5's migration order is superseded** by the NatApp document, which has been executed twice and carries the rerun script. ⚠ It analyses the `23_07` backup when `seed-data/` now holds `15_08` and `19_08`, so **every figure in it is a July count**. ✅ Kept because **§4 is still the only map of the legacy schema in this repo**, and the cutover has not happened. |
| 🆕 [handrun-2026-08-11.md](archive/handrun-2026-08-11.md) | **Matt's first real shop-day run of the post-Syncfusion till** (reported on 1.41.0, fixed in 1.42.0) — his findings in his order, with triage. ⚠ **Listed here for the first time on 2026-08-20**: it had been in `archive/` unlisted since it was written, which is how a document becomes invisible. Its own opening line is the point — *"this page exists because the last hand-run's findings lived in a chat message and had to be re-derived twice."* Findings from it that closed are in `till-design.md` Part B; read this for **what a real operator noticed first**, which is a different list from what the tests cover. |

## Archived — superseded

Not delivered: **folded into a later document**, which is named in each banner. Kept for the
reasoning and the options that were weighed and rejected.

| Document | Folded into |
|---|---|
| 🆕 [syncfusion-footprint.md](archive/syncfusion-footprint.md) | [Shrink MAUI Build.md](archive/Shrink%20MAUI%20Build.md) **§4**, 2026-08-20 — all of it: the licence hazard into §4.0, the swap table into §4.5, the hand-run watch-list into §4.6. ⚠ **Two documents were describing the same 76 MB from opposite ends**, and the removal ORDER in one was load-bearing *because of* the trial-dialog hazard explained in the other. ⚠ It moved into `To do/` rather than staying at this level because the work is **unbuilt** — the folder rule decides. ⚠ **The line worth carrying:** `SfNumericEntry`'s `Minimum="1"` lived in the control's markup, so it left with the control — and a quantity of 0 rings a line that charges nothing and looks exactly like a sale. **A rule in a control's markup leaves with the control.** |
| 🆕 [plutus-catalogue-sync-design.md](archive/plutus-catalogue-sync-design.md) | Nothing — **it was a greenfield DESIGN STUDY, never a plan**, and it is archived rather than promoted to reference for one reason: ⚠⚠ **its central recommendation was deliberately REJECTED and following it would break the platform.** §3.1 proposes `item_id UUID PRIMARY KEY` with the barcode demoted to an alias row, and calls barcode-as-primary-key *"the single most common design mistake in POS databases"* — which is **exactly what Plutus does, knowingly**: `Item.IdOne` seeds a frozen deterministic-GUID vector, is half a composite PK with five FK families on it, and sits on every historical sale line. Re-keying was never a migration; it was a rewrite of the sale history. ✅ **What WAS adopted** (aliases resolving to one item, both codes live through a supplier changeover, snapshot sale lines, change-log delta pull, single-writer) is in the banner, alongside what was not (global barcode PK — Plutus is multi-tenant; `pack_qty` case barcodes — **not built**). It produced [Multi-barcode plan.md](archive/Multi-barcode%20plan.md). |
| [MAUI-Cutover-Plan-2026-08-09.md](archive/MAUI-Cutover-Plan-2026-08-09.md) | [MAUI-retrofit.md](To%20do/MAUI-retrofit.md), 2026-08-12. The 28-step execution order, its bodies and DoDs and binding defaults 10–18. ⚠ **Not delivered** — steps 11b, 22, 24, 26, 27, 28 were still open; they are Part 1. ⚠ Its status board was wrong when archived (23 and 25 shown unticked after shipping) — read it as history. |
| [MAUI-Retrofit-Plan-2026-08-07.md](archive/MAUI-Retrofit-Plan-2026-08-07.md) | Same. The WP bodies and DoDs, binding defaults 1–9, the risk register, the item-identity seam. It had itself replaced six earlier documents. |
| [maui-whats-left.md](archive/maui-whats-left.md) | Same. Superseded twice in two days, which is the argument for one document. |
| [MAUI-parity.md](archive/MAUI-parity.md) | Same. It lived one day — its remains-first / completed-at-the-bottom shape became Parts 1 and 3. |
| [legacy-removal.md](archive/legacy-removal.md) | Same, **§10, verbatim**. ⚠ **NOT delivered — the deletions L1–L10 have not been done**; Matt still does them last. Archived because the register belongs beside the steps that unblock each row. |
| [MAUI-Backend-Sync-Plan-2026-08-01.md](archive/MAUI-Backend-Sync-Plan-2026-08-01.md) | The MAUI retrofit plan — this was its backbone. ⚠ Two of its instructions are now actively wrong; the banner says which. |
| [plutus-maui-build-spec.md](archive/plutus-maui-build-spec.md) | Same. Its ⏸ pause is void — the upstream code it waited for landed at `4494a57`. |
| [till-retrofit-2026-07-25.md](archive/till-retrofit-2026-07-25.md) | Same (its MAUI column). The web-POS column closed in July. |
| [Migration-2026-07-22-plan.md](archive/Migration-2026-07-22-plan.md) | Nothing outstanding — its remaining workstreams were all ClientUI, which is being retired. |
| [BugFix-2026-07-22-plan.md](archive/BugFix-2026-07-22-plan.md) | Implemented in AppClient; unfixed in ClientUI, which is being retired. |
| [OfflineMode-2026-07-23-plan.md](archive/OfflineMode-2026-07-23-plan.md) | Its §4.3 credential fork is still an open decision in the retrofit plan. |

## Data (gitignored)

- `seed-data/` — three live Kapow till backups (~102 MB) used by the seed/ETL migrator. **Real business
  data: a retailer's entire trading history and customer list.** ⚠ **Checked 2026-08-20 and it was
  protected only INCIDENTALLY** — by the `*Database*.db` filename rule, because the supplier happens to
  put "Database" in the filename. A CSV export or a renamed `.sqlite` dropped in there would have been
  committed. `Build/seed-data/` is now ignored as a **directory**, which is how `snapshots/` was always
  done. ⚠ Nothing had leaked; nothing in the folder was ever tracked.
- `snapshots/` — T0.1 endpoint regression snapshots (contain real sale figures).
- `archive/Database.db*` — regenerable local dev artifacts, unrelated to the archived documents.

---

## Keeping this honest

A new plan starts in `To do/`. When it's finished, don't just tick its boxes — **move it to
`archive/` and give it a banner** saying what actually shipped, what was gated, and what was
deliberately dropped. A plan left in `To do/` reads as outstanding work forever; one archived
silently loses the reasoning that made it worth writing.

Five things that keep this from rotting. The first two are original; the last three were each learned
the hard way on **2026-08-20**, when eight documents sat in a folder this index said held five.

- **If a plan contains something standing** — a convention, a runbook, a table anyone will need
  next year — lift it up to this level *before* archiving, as [repo-runbook.md](repo-runbook.md)
  was lifted out of `operator-portal-plan.md`. Otherwise the useful part gets buried with the
  finished part.
- **Audit against the code, not against the headers.** Of the documents reviewed on 2026-08-07,
  two still said *"Plan only — no code changed yet"* when much of the work had shipped, and one
  said the opposite. A plan's own status line is the least reliable thing in it.
- ⚠⚠ **A DOCUMENT CANNOT KNOW ITS OWN STATUS, OR ITS OWN RELATIONSHIP TO ANOTHER DOCUMENT.** Every
  stale claim found in the 2026-08-20 audit was one of those two. Two plans said *"Nothing is deployed
  yet"* while live in a shop; `shop-day-test.md` said it was *"the fuller reference"* long after the
  document it deferred to had grown seventeen times larger; and this index said *"five documents"* over
  a folder of eight. **Deploy state belongs in exactly one place** — `MAUI-retrofit.md` §0.1 — and a
  plan should point at it rather than restate it.
- ⚠ **A "keep these in sync" note is not a mechanism.** The same day, a twin-file comment that had
  asked two frontends to stay byte-identical was found to have been violated for months, costing every
  portal dialog its close ✕. It is now `FrontendTwinTests`. **Where a claim can be checked
  mechanically, check it mechanically** — that applies to documents as much as to code.
- ⚠ **Two documents on one subject is one too many, and the tell is a cross-reference you cannot drop.**
  `syncfusion-footprint.md` and `Shrink MAUI Build.md` cited each other four times; the removal *order*
  in one was only load-bearing because of the licence hazard in the other. If a reader must hold two
  documents open to act safely, **merge them**. Same for hand-test scripts: there is now exactly one.
