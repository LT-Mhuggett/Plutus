# `Build/` — documentation index

Three places, one rule each. Nothing is ever deleted — a plan just moves right along this line:

| Where | What lives there |
|---|---|
| `Build/` (this level) | **Standards and reference** — how things must look and behave. No expiry date. |
| [`Build/To do/`](To%20do/) | **Plans with work still in them.** If it's here, something is unbuilt. |
| [`Build/archive/`](archive/) | **Delivered.** Each carries a banner saying what shipped and what didn't. |

**The living state of the project is [`HANDOVER.md`](../HANDOVER.md), not this folder.** Ask it
what's deployed, what broke, and where the rollback tags are. Ask these documents *why*.

Last audited **2026-08-07** against the code, not against the documents' own headers.

---

## Standards & reference — these stay

| Document | What it's for |
|---|---|
| [plutus-platform-architecture.md](plutus-platform-architecture.md) | The platform architecture (v3). **Wins on any conflict** with a plan. |
| [plutus-operator-platform-requirements.md](plutus-operator-platform-requirements.md) | What the operator platform must do, with a built-status appendix. |
| [repo-runbook.md](repo-runbook.md) | Build, test, migrate, deploy — plus the ten codebase pitfalls that have each cost a session. Read before writing code. |
| [table-standard.md](table-standard.md) | The shared `DataTable` contract (sort / search / 25-50-100 / paging) every table in all three surfaces must use. |
| [till-design.md](till-design.md) | **THE SINGLE SOURCE OF TRUTH FOR EVERY TILL BUILD.** What the surfaces are (A), what each till can do (B), and where every shared rule lives (C). **Any till work reads it first and updates it in the same commit** — that is what keeps till versions in sync. ⚠ **C2, the drift register, is the section to read before writing anything that computes money on a client.** Consolidated 2026-08-08 from the old `till-parity.md` + `till-anatomy.md`. |
| [maui-whats-left.md](maui-whats-left.md) | **What is left of the MAUI retrofit, in order, with rough effort.** Verified against the tree 2026-08-10, not read off the plan prose. Open this when the question is "how much is left"; the cutover plan holds the *how* for each step and till-design Part B holds the row-by-row register. |
| [shop-day-test.md](shop-day-test.md) | **The hand-run script.** A trading day in order — open a float, sell, refund, X-read, edit an item, trade offline, Z-close — with what to expect and what a failure looks like at each step. Ordered deliberately: several steps set up the next. |
| [syncfusion-footprint.md](syncfusion-footprint.md) | **What Syncfusion actually holds up, and what can replace it.** Answers "can we drop it?" — for REPORTING yes and already done; for the till no, because the quantity box on the checkout path is Syncfusion. Verified control-by-control 2026-08-10, with effort for a full removal. |
| [legacy-removal.md](legacy-removal.md) | **What comes out of the MAUI till, and in what order.** Everything hidden during the retrofit but still compiled — the archive path, till-side inventory CRUD, the legacy permission gate, till-side reporting, the legacy DB layer and its models. Matt does the deletions **last of all** (2026-08-10); this is the list he does them from, with the dependency order and the two items that must NOT be deleted. |
| [kapow-db-gap-analysis.md](kapow-db-gap-analysis.md) | The source NatApp SQLite database, table by table. Still the reference for any migration tooling. |
| [VAT-FixLater-Report-2026-07-23.md](VAT-FixLater-Report-2026-07-23.md) | The legacy VAT rows deliberately **not** auto-repaired (owner's decision). Live: the portal shows this list at Reporting → VAT. Shrinks as items are corrected. |
| `secrets.local.md` | Local credentials. **Gitignored — never commit.** |

## To do — plans with work still in them

In [`To do/`](To%20do/). Two documents, deliberately: seven overlapping native-till plans were
consolidated into one on 2026-08-07.

| Document | What's left |
|---|---|
| [MAUI-Retrofit-Plan-2026-08-07.md](To%20do/MAUI-Retrofit-Plan-2026-08-07.md) | **The MAUI plan — written for autonomous execution, no questions.** Make Sean's `Plutus.Frontend.AppClient` a platform client. **In progress: WP0–WP4 + WP2b done** (the transport spine — enrol, trade offline, drain exactly once); **WP2c next**. §3b is the progress board and the resume point; §2a carries the VAT rules; §10 the item-ID seam. WP0–WP13 with DoDs, **binding defaults** (§9, veto-able), USER-VERIFY gates where a human must click. |
| [NatApp-Translation-Agent-Plan-2026-08-05.md](To%20do/NatApp-Translation-Agent-Plan-2026-08-05.md) | **The data plan** — translates the legacy NatApp database into the current schema (confirmed by Matt 2026-08-08 as the migration mechanism). Runs in parallel with the retrofit; they meet at **item identity**, corrected in the retrofit's §10 — the barcode is the invariant, not the GUID, and the central catalogue has no item UUIDs to compare against. |

Nothing on the web till, the portal or the backend is outstanding as a plan.

## Archived — delivered

Each carries a banner explaining what shipped and what was deliberately left.

| Document | Outcome |
|---|---|
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

## Archived — superseded

Not delivered: **folded into a later document**, which is named in each banner. Kept for the
reasoning and the options that were weighed and rejected.

| Document | Folded into |
|---|---|
| [MAUI-Backend-Sync-Plan-2026-08-01.md](archive/MAUI-Backend-Sync-Plan-2026-08-01.md) | The MAUI retrofit plan — this was its backbone. ⚠ Two of its instructions are now actively wrong; the banner says which. |
| [plutus-maui-build-spec.md](archive/plutus-maui-build-spec.md) | Same. Its ⏸ pause is void — the upstream code it waited for landed at `4494a57`. |
| [till-retrofit-2026-07-25.md](archive/till-retrofit-2026-07-25.md) | Same (its MAUI column). The web-POS column closed in July. |
| [Migration-2026-07-22-plan.md](archive/Migration-2026-07-22-plan.md) | Nothing outstanding — its remaining workstreams were all ClientUI, which is being retired. |
| [BugFix-2026-07-22-plan.md](archive/BugFix-2026-07-22-plan.md) | Implemented in AppClient; unfixed in ClientUI, which is being retired. |
| [OfflineMode-2026-07-23-plan.md](archive/OfflineMode-2026-07-23-plan.md) | Its §4.3 credential fork is still an open decision in the retrofit plan. |

## Data (gitignored)

- `seed-data/` — the live Kapow till backup used by the seed/ETL migrator. **Real business data.**
- `snapshots/` — T0.1 endpoint regression snapshots (contain real sale figures).
- `archive/Database.db*` — regenerable local dev artifacts, unrelated to the archived documents.

---

## Keeping this honest

A new plan starts in `To do/`. When it's finished, don't just tick its boxes — **move it to
`archive/` and give it a banner** saying what actually shipped, what was gated, and what was
deliberately dropped. A plan left in `To do/` reads as outstanding work forever; one archived
silently loses the reasoning that made it worth writing.

Two things that keep this from rotting:

- **If a plan contains something standing** — a convention, a runbook, a table anyone will need
  next year — lift it up to this level *before* archiving, as [repo-runbook.md](repo-runbook.md)
  was lifted out of `operator-portal-plan.md`. Otherwise the useful part gets buried with the
  finished part.
- **Audit against the code, not against the headers.** Of the documents reviewed on 2026-08-07,
  two still said *"Plan only — no code changed yet"* when much of the work had shipped, and one
  said the opposite. A plan's own status line is the least reliable thing in it.
