# Handover — next session

> ⚠ **This document is ONE DAY LONG, on purpose.** Matt, 2026-08-17: *"I only want the handover to be
> for the following day."*
>
> Everything durable — deploy state, every ruling, open items, the plan, the estimates — lives in
> **[`Build/To do/MAUI-retrofit.md`](Build/To%20do/MAUI-retrofit.md) §0**. The day-by-day narrative back
> to 2026-07-28 is kept verbatim in
> [`archive/handover-history-to-2026-08-17.md`](Build/archive/handover-history-to-2026-08-17.md).
> **Do not grow this file back into a history.** Rewrite it; the commits are the record.

**Written:** 2026-08-17 · **Head:** `git log -1` · **Suites:** unit 1310 · MAUI 598 · web till 45 — all green

---

## ⏰ START HERE — the build exists, and no person has ever run it

✅ **Till 1.73.0 is BUILT and verified.** Double-click:

```
D:\tmp\plutus-till-1.73.0\Plutus.Frontend.AppClient.exe
```

The artefact reads `1.73.0+7cf5f2dc`, which is HEAD. It is the **only** till build on the box — 1.71.0
was deleted so there is no question which to run. ⚠ It will say the till isn't enrolled; that is
expected for an unpackaged build (runbook § MAUI till build) — enrol it as a fresh till.

⚠ **Install agent 1.4.0 as well.** Web till → Settings → Hardware → *Download the agent (v1.4.0)*, or
`tools\Plutus.TillAgent\publish-out\PlutusTillAgent-1.4.0.exe`. ⚠⚠ Put it in
`%LOCALAPPDATA%\Plutus\Agent\` and run it from **there**, not Downloads — §G29 and §G31 need it, and
Downloads is the fault it fixes.

---

## The hand-run. 69 sections, and none of it has ever been run

**[`Build/Test Maui.md`](Build/Test%20Maui.md)** — the RUN ORDER table at its top is the order. About
**2½ hours**, or ~15 minutes for §A alone if that is all there is.

⚠⚠ **The last hand-run findings were 2026-08-13.** MAUI has gone from 1.49.x to **1.73.0** with nobody
looking at a screen. For scale: 2026-08-10 found **fourteen** faults, **six invisible to every
automated test in the project**; 2026-08-13 found five more including two money holes. **Every hand-run
so far has found something the tests could not.**

⚠ **Part B now has ZERO MAUI ⬜ rows** — 50 ✅ both · **17 🟡** · 0 ⬜ MAUI · 8 ⬜ web till. Functional
parity is closed *on paper*; those 17 🟡 are the ones no human has seen. **Running §G validates
seventeen rows; there is no remaining MAUI build work that would move any.**

---

## What a person needs to decide next (nothing is blocked on it today)

| | |
|---|---|
| **The legacy DB import** | ⚠ Matt has *"a more recent DB to import"* and everything must be **retained and translated** — [L4](Build/To%20do/MAUI-retrofit.md) is now a work package, not a deletion. ⚠ **Do not start by writing an importer**: look at the DB first and record what is in it. An importer against a guessed schema mangles history, and that one is not reversible once the takings are wrong |
| **`origin` history surgery** | `origin` cannot be pushed — a 151 MB blob in old history that `upstream` already has. Needs an LFS migration or an orphan branch. `upstream` is the off-machine copy meanwhile |
| **Whether W5's mechanics come next** | Its policy half is built (`AgentUpdatePrompt`); the packaging, exe swap and `ExpectedAgentVersion` are not |

---

## The standing documents

| | For |
|---|---|
| ⚠⚠ [`Build/To do/MAUI-retrofit.md`](Build/To%20do/MAUI-retrofit.md) | **THE plan.** §0 is the live state and every ruling; §3 the open steps; §10 what comes out afterwards. There is no sixth place to look |
| [`Build/Test Maui.md`](Build/Test%20Maui.md) | The hand-run script — 69 sections, run order at the top |
| [`Build/till-design.md`](Build/till-design.md) | **Part A** the functional-parity table · **Part B** the capability register · **Part C** the money rules and what stops them drifting. Read before writing anything that computes money on a client |
| [`Build/repo-runbook.md`](Build/repo-runbook.md) | Build, test, migrate, deploy — and the 18 pitfalls that have each cost a session |
| [`Build/plutus-platform-architecture.md`](Build/plutus-platform-architecture.md) | **Wins any design conflict** with a plan |
| [`Build/index.md`](Build/index.md) | What every other document is for, and which are archived |
