# Plutus — project instructions

## Tills: read `Build/till-design.md` first, every time

**[`Build/till-design.md`](Build/till-design.md) is the single source of truth for every till
build.** Whenever the work touches a till — the web till, the MAUI app, the hardware agent, the
webstore sales channel, or a till that doesn't exist yet — **read it before writing code, and update
it in the same commit.**

This is not a documentation nicety. It is the mechanism that keeps every till version in sync, and
it exists because both failure modes have already happened here:

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
| Write a rule that could live in more than one place | Put it in `Plutus.SharedKernel`; add a row to C1 |
| Find yourself copying logic between TypeScript and .NET | Add a row to C2 saying what pins the copies — or state honestly that nothing does |
| Add a new till surface or platform | Follow D2, then add its column to Part B |
| Discuss tills at all | Cite `till-design.md`, and say if it's out of date |

## The other standing documents

| Document | For |
|---|---|
| [`HANDOVER.md`](HANDOVER.md) | The **living state**: what's deployed, what's open, rollback tags. Start here. |
| [`Build/repo-runbook.md`](Build/repo-runbook.md) | Build, test, migrate, deploy — and the codebase pitfalls that have each cost a session. Read before writing code. |
| [`Build/plutus-platform-architecture.md`](Build/plutus-platform-architecture.md) | **Wins on any design conflict** with a plan. |
| [`Build/till-design.md`](Build/till-design.md) | Tills — see above. |
| [`Build/MAUI-parity.md`](Build/MAUI-parity.md) | ⚠ **THE single page for MAUI status** — what remains (top) and what is done (bottom). Replaces the status boards in both `To do/` plans and `maui-whats-left.md`, which disagreed with each other. |
| [`Build/legacy-removal.md`](Build/legacy-removal.md) | What comes **out** of the till afterwards, in dependency order. Matt does this last. |
| [`Build/table-standard.md`](Build/table-standard.md) | Every data table in all three surfaces. |
| [`Build/index.md`](Build/index.md) | What every other document is for, and which are archived. |
