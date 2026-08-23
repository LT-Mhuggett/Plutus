# Plutus — project instructions

## Tills: read `Build/till-design.md` first, every time

**[`Build/till-design.md`](Build/till-design.md) is the single source of truth for every till
build.** Whenever the work touches a till — the web till, the MAUI app, the hardware agent, the
webstore sales channel, or a till that doesn't exist yet — **read it before writing code, and update
it in the same commit.**

This is not a documentation nicety. It is the mechanism that keeps every till version in sync, and
it exists because both failure modes have already happened here:

**Start at Part A0 — the functional-parity table.** One row per thing a shop needs done, ✅/🟡/⬜/➖ per
till, no implementation detail. It shows the shape of the gap in about a minute. ⚠ **Part B wins on any
disagreement** — A0 is a summary, and a summary is the thing that goes stale first.

⚠⚠ **PARITY NOW INCLUDES LOOK AND FEEL — Matt, 2026-08-19:** *"I need the functionality and look and
feel to be the same across both tills. So if a user swaps between the two, it doesnt matter and they
would understand how to use it."* **This supersedes the 2026-08-17 ruling** (*"parity in FUNCTIONALITY,
not in how the functions operate"*) that used to be quoted here, and it widens the test: an operator who
moves between tills mid-shift must need no retraining. A0 answers "can the till do the thing", which can
no longer be the whole question — so a ✅/✅ row whose two tills look nothing alike is now a gap A0
cannot express, and it belongs in Part B's Notes.

- A feature landed on the web till and the MAUI register silently fell behind → **Part B**, the
  capability register. A capability isn't done until its row exists, with every other till marked
  ✅ or a deliberate ⬜.
- The VAT arithmetic existed only in the web till's TypeScript, so any other till had to re-derive
  it from another language and get it right → **Part C**, the rules register. A rule isn't done
  until C1 says where it lives, and — if it exists more than once — C2 says what stops the copies
  drifting.

**Before writing anything that computes money on a client, read C2 (the drift register).** Two tills
that disagree by a penny on the same basket disagree on every VAT return, forever, and nothing
flags it.

### ⚠ Parity is the default — new functionality ships to EVERY till

**Matt, 2026-08-08: "The tills need to be in parity. This is the point of the MAUI retrofit. In
addition when adding new functionality, it needs to be added to all tills going forward."**

A feature is not finished when it works in the browser. It is finished when its Part B row is
filled in for **every** till — ✅, or a ⬜ whose Notes name the work package that will close it and
why it can wait. There is no third option, and "we'll do MAUI later" counts only when "later" is a
WP number. Build shared logic in `Plutus.SharedKernel` / `Plutus.Client.Core` so parity is the
cheap path, not the disciplined one.

### The reflex

| When you… | Do this |
|---|---|
| Add or change a till capability | Fill its Part B row for **every** till, same commit — see D3 |
| Ship a feature to one till only | Give the others a ⬜ **with a WP number and a reason**; add the WP if none covers it |
| Work on the MAUI till at all | The retrofit is **finished and archived** (2026-08-23). Everything that still binds a build moved into `till-design.md` **Part E** — Matt's rulings, the binding defaults, the pitfalls, the VAT settlement. What is left to PROVE is [`Build/MAUI_finaltest.md`](Build/MAUI_finaltest.md) |
| Write a rule that could live in more than one place | Put it in `Plutus.SharedKernel`; add a row to C1 |
| Find yourself copying logic between TypeScript and .NET | Add a row to C2 saying what pins the copies — or state honestly that nothing does |
| Add a new till surface or platform | Follow D2, then add its column to Part B |
| **Add or change a dialog** on any till | ⚠ **Read `till-design.md` D4 — the dialog contract.** A visible ✕ (use the shared helper: MAUI `DialogHeader`, web `DialogX`), Escape cancels, and ⚠⚠ **the caller must handle "backed out" without dereferencing it** — that is the crash in §0.3b |
| **Build a screen that shows a number** the shop can change | ⚠ **Read `till-design.md` D5 — the live-data contract.** MAUI: `Services.Sync.LiveScreen`, two lines in the constructor, **never a hand-rolled `OnAppearing`** — the pattern is what failed three times. ⚠ `onCadence: false` for a long scrollable table, or it jumps to the top under the operator's hands |
| Discuss tills at all | Cite `till-design.md`, and say if it's out of date |

## The other standing documents

| Document | For |
|---|---|
| [`HANDOVER.md`](HANDOVER.md) | **Next session only — one day long** (Matt, 2026-08-17). Start here for *what to do now*. ⚠ The **rulings and binding defaults** are `till-design.md` **Part E** (they moved there when the retrofit was archived, 2026-08-23); deploy state is `Build/repo-runbook.md`. Do not let the handover grow back into a history. |
| [`Build/repo-runbook.md`](Build/repo-runbook.md) | Build, test, migrate, deploy — and the codebase pitfalls that have each cost a session. Read before writing code. |
| [`Build/plutus-platform-architecture.md`](Build/plutus-platform-architecture.md) | **Wins on any design conflict** with a plan. |
| [`Build/till-design.md`](Build/till-design.md) | Tills — see above. **A0** = functional parity at a glance; **B** = the capability register; **C** = the money rules and what stops them drifting. |
| [`Build/MAUI_finaltest.md`](Build/MAUI_finaltest.md) | ⚠⚠ **THE ONE LIVE MAUI DOCUMENT, since 2026-08-23.** The retrofit is built — what is left is a hand-run. This page says **what is unproven and why**; [`Build/Test Maui.md`](Build/Test%20Maui.md) says **what to click**. ⚠ `MAUI-retrofit.md` is **archived**; its rulings, binding defaults, pitfalls and VAT settlement are `till-design.md` **Part E**. **Do not start a new MAUI plan** — five became one, one became none, and a sixth is how that unwinds. |
| [`Build/table-standard.md`](Build/table-standard.md) | Every data table in all three surfaces. |
| [`Build/index.md`](Build/index.md) | What every other document is for, and which are archived. |
