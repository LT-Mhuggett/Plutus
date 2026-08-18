# Handover — next session

> ⚠ **This document is ONE DAY LONG, on purpose.** Matt, 2026-08-17: *"I only want the handover to be
> for the following day."*
>
> Everything durable — deploy state, every ruling, open items, the plan, the estimates — lives in
> **[`Build/To do/MAUI-retrofit.md`](Build/To%20do/MAUI-retrofit.md) §0**. The day-by-day narrative back
> to 2026-07-28 is kept verbatim in
> [`archive/handover-history-to-2026-08-17.md`](Build/archive/handover-history-to-2026-08-17.md).
> **Do not grow this file back into a history.** Rewrite it; the commits are the record.

**Written:** 2026-08-18 · **Head:** `git log -1` · **Suites:** unit **1358** · MAUI **605** · web till **224** · integration **174** · architecture **17** · lint clean — all green

> ⚠⚠ **THE ARCHITECTURE SUITE IS IN THAT LIST NOW BECAUSE IT HAD BEEN RED SINCE 2026-08-16 AND NOBODY
> KNEW.** It was not in the suite line, so it was not what anybody ran before committing — two days of
> green-looking commits over a failing convention test. **Run all five.** ⚠ It is also now the only
> automated check on MAUI's XAML (`XamlResourceTests`), which is where the silent failures live.

---

## ⏰ START HERE — the build exists, and no person has ever run it

✅ **Till 1.92.0 is BUILT; backend 1.17.8, web till 1.17.0 and portal 1.10.0 are DEPLOYED.** ⚠⚠ **Hand-run 1 happened** — see `Test Maui.md`, last section: three sections passed and **four faults came back, all now fixed in this build**. Double-click:

```
D:\tmp\plutus-till-1.87.0\Plutus.Frontend.AppClient.exe
```

⚠ **1.87.0 and 1.88.0 are now deleted** — 1.89.0 is the only build on the box. The artefact reads **`1.92.0+25c5707b`** = HEAD, and this session's work is confirmed **inside the
binary** rather than merely committed — six strings only these changes introduced were found in it.
⚠ It will say the till isn't enrolled; that is expected for an unpackaged build (runbook § MAUI till
build) — enrol it as a fresh till.

⚠ **§5c item 6 is now done too** — the Loyalty tab is the web till's screen: six columns in its order, and an **Edit** MAUI has never had. Hand-run **§G44**.

⚠⚠ **THE BACKEND DEPLOY IS NOT OPTIONAL FOR THIS BUILD.** Without 1.17.4 the new **Stock** and
**Negative stock** reports read for nobody below Store Manager. It is deployed and verified on both
axes (swagger 200 **and** the device-token probe answering *"Device not enrolled or revoked"*, which
is the one that proves the DB path). Rollback: `~/PLUTUS/backend.pre-1.17.4`.

⚠ **Portal 1.10.0 is deployed too** (`index-Befe7Xwa.js` on **admin.**plutus…, verified by hash, size
and content; rollback `current.pre-1.10.0`). It carries **1.9.0's opening-hours validation**, which had
been built-not-deployed since 2026-08-17, **plus** the mandatory credit reason.

**Six §5c items landed today, in order:**

| Build | What it fixes |
|---|---|
| 1.82.0 | ⚠⚠ **A member could not be added ANYWHERE.** Add member / Set tier came off the till screen (correctly) but the Loyalty tab had neither — and before that they had 403'd for every operator since they were written. Both now on the Loyalty tab, moved not copied |
| 1.83.0 | **Nothing updated unless you navigated away and back** — the third report of one fault. Now one shared `LiveScreen`; **Store Information could not refresh at all** and is the likely case behind the report |
| 1.84.0 | **Stock** and **Negative stock** reports — and the `pos.reports.view` gate wrong for the **fourth** time, on the endpoint six lines below the comment recording the third fix |
| 1.85.0 | ⚠⚠ **DRILL-DOWN** — a **Sales** report across every till; tap a row to see its lines, VAT, how it was paid and what has gone back. *"The one that turns a report into an answer"* |
| 1.86.0 | **Tap the price to adjust it** — and a **money bug** beside it: a second scan joined a hand-adjusted line **at the adjusted price** |
| **1.87.0** | **The basket rows are styled at last** (two styles referenced 20× and defined nowhere), and MAUI **consumes its first theme slot** |

**Testable now, and NONE of it has been run: §G38** (add a member — it has never once worked) ·
**§G39** (screens refresh while you watch) · **§G40** (Stock / Negative stock) · **§G41** (drill-down)
· **§G42** (tap the price — ⚠ **§G42d is a money check**) · **§G43** (the styled basket, ⚠ **look at
§G43a first**).

⚠⚠ **§G42a IS THE ONE MOST LIKELY TO FAIL AND IT IS FINE IF IT DOES.** Whether a tap gesture fires
inside a `ViewCell` on WinUI could not be verified by any test in this project. The right-click
**Adjust** menu still works either way — just report it, because the fix would be a different control.

⚠ **Install agent 1.4.0 as well.** Web till → Settings → Hardware → *Download the agent (v1.4.0)*, or
`tools\Plutus.TillAgent\publish-out\PlutusTillAgent-1.4.0.exe`. ⚠⚠ Put it in
`%LOCALAPPDATA%\Plutus\Agent\` and run it from **there**, not Downloads — §G29 and §G31 need it, and
Downloads is the fault it fixes.

## The hand-run. 69 sections, and none of it has ever been run

**[`Build/Test Maui.md`](Build/Test%20Maui.md)** — the RUN ORDER table at its top is the order. About
**2½ hours**, or ~15 minutes for §A alone if that is all there is.

⚠⚠ **The last hand-run findings were 2026-08-13.** MAUI has gone from 1.49.x to **1.77.0** with nobody
looking at a screen. For scale: 2026-08-10 found **fourteen** faults, **six invisible to every
automated test in the project**; 2026-08-13 found five more including two money holes. **Every hand-run
so far has found something the tests could not.**

⚠ **Part B now has ZERO MAUI ⬜ rows** — 50 ✅ both · **17 🟡** · 0 ⬜ MAUI · 8 ⬜ web till. Functional
parity is closed *on paper*; those 17 🟡 are the ones no human has seen. **Running §G validates
seventeen rows; there is no remaining MAUI build work that would move any.**

⚠⚠ **And as of 2026-08-17 the WEB till's eight ⬜ rows are 🟡 too** (§5b W-P1…W-P7 — see the table
below). So **every capability row on both tills is now ✅ or 🟡**, and there is no build work left on
either till that would move one. **The only thing that turns 🟡 into ✅ is a person at a screen** —
which makes the hand-run the single highest-value thing anybody can do to this project right now, and
§W the newest, least-looked-at part of it.

---

## What a person needs to decide next (nothing is blocked on it today)

| | |
|---|---|
| ✅ **The legacy DB import RAN (2026-08-17)** | **198 sales / £4,923.86** (24 Jul→15 Aug, the shop's real trade) imported from the 15_08 NatApp backup into SalesV2, + the 48 items those sales reference. Rehearsed on `plutus_t1`, then live; **four-way penny reconciliation green** (SalesV2 == ΣSaleLines == SalesRollups == VatRollups = £562,563.74 / £25,784.71 VAT / 21,888 sales); same-backup-twice = no-op, proven. Pre+post dumps kept. The orphaned legacy till now has a real row — **the whole shop history attributes to store 1**. Full record + how to run the next bridge: [`NatApp-Translation-Agent-Plan…`](Build/To%20do/NatApp-Translation-Agent-Plan-2026-08-05.md) §7. ⚠ **Three things deliberately NOT done, yours to decide**: 2 items changed price on the till after 23_07 (portal keeps its values — update there if wanted); **stock untouched** (3 weeks of till+web trade against one pile — a stock count is the honest fix); 15 sale notes/16 price-override records dropped, as the original ETL always did |
| **`origin` history surgery** | `origin` cannot be pushed — a 151 MB blob in old history that `upstream` already has. Needs an LFS migration or an orphan branch. `upstream` is the off-machine copy meanwhile |
| **Whether W5's mechanics come next** | Its policy half is built (`AgentUpdatePrompt`); the packaging, exe swap and `ExpectedAgentVersion` are not |
| 🔴🔴 **FIXED in web 1.13.0 — THE WEB TILL COULD NOT TAKE A PAYMENT** | Matt, 2026-08-18: *"When I try to checkout on the webtill, I get… Minified React error #310"*. **Checkout crashed on every attempt, and had since 1.10.0** — `CheckoutDialog` called `useMemo` **six lines below a guard clause**, so it rendered N hooks on mount and N+1 once the payment methods loaded, and React refused to render. Not an edge case: the dialog always mounts before the methods arrive, so it was **the only path**. ⚠ It arrived with finding Y (`e6c6b65d`) — correct arithmetic, placed the wrong side of a guard clause — and survived my 1.12.0 deploy because **nothing automated could see it**: `tsc` passes (it is type-correct), `vite build` passes, and all 205 vitest cases pass since **no test in this project mounts a component**. ⚠ **Hard-refresh (Ctrl+F5) before retrying** — the old bundle caches. ⚠ `npm run build` now fails on a hooks violation (`react-hooks/rules-of-hooks`), and I **watched the rule catch this exact bug and fail the real build** before trusting it — §W10b says how to re-prove that. **§W10a is now the first thing to run after any web-till deploy: ring one item, take cash, take card, refund one.** |
| ✅ **Web till 1.12.0's content is all still live** — go and look at **Store Information** | The opening-hours message now distinguishes **three** states, and which one you see *is* the answer to *"why don't my hours show?"*: a week of times (fine), *"Not set"* (**nothing is stored** — they were never saved), or *"the portal has hours but this till can't read them: …"* (**stored and malformed** — the message names the fault). Before this build all three printed the middle one. ⚠ Also live: all seven §5b slices, which never shipped under their own 1.11.0 label. **§W9a** is the two-minute version of this test |
| ⚠ **The portal fix is BUILT, NOT DEPLOYED** (1.9.0) | The tills can only *report* unreadable hours; the portal is where they get **created**. Its advanced-JSON box had **no validation at all**, its simple editor showed a malformed value back as *every day unticked*, and its Save button lived in a different section — so hours could be saved unreadable, or set and never saved, and both look identical to *"not set"*. Now validated, Save-gated, and with its own button. ⚠ **Say the word and I'll deploy it** — same four-axis verification, rollback copy taken first |
| ✅ **MAUI 1.74.0 built — Store Information rebuilt** | Three cards matching the web till, same labels, **legible labels** (every one was light grey on near-white — the data was all there and none of it readable), no grey legacy bar, no currency-format-string panel, no stray *Bag* button, and Store id / Till id which it never showed. Now inside **1.75.0**. |
| 🔴 **FIXED in 1.75.0 + backend 1.17.2 — the Reports tab could not read ANY report** | Matt, 2026-08-18: *"it is saying 'This report couldn't be read. You may not have permissions to see it. Or the till is offline'"*. **Two independent faults, and the message named neither correctly.** ① `ReportsViewModel` asked with the **device** token; `perm:*` resolves RBAC by the token's `NameIdentifier`, which on a device token is the **device id** — so the lookup asked "what may this DEVICE do" (nothing) and returned **403 for every operator, whatever their role**. Not a permission to grant — the wrong identity was asking. ⚠ `OperatorTokenProvider` was built for exactly this at step 19 and had **one** call site that this screen never used — the **fifth** component here found fully built and wired to nothing. ② **Five of the six report endpoints lacked the `pos.reports.view` alternative**; only `/reports/summary` got it at step 26. So a **Supervisor** — who by design holds *no* portal permission yet can Z-close a day — still could not read the takings they counted against. Both fixed, the gate pinned by an integration test **watched going red** first. ⚠ **The message now separates "no live sign-in" from "offline"** — the old one blamed the network for an expired session |
| ⚠⚠ **§5b W-P1…W-P7 — all seven done** | **[§5b](Build/To%20do/MAUI-retrofit.md) W-P1…W-P7 complete in code.** A revoked browser till stops; a disabled operator is signed out inside 60 s; a cashier has a discount limit with a supervisor step-up; the till **signs in, sells and takes cash with the line down**; a Z-closed day can be reopened; a past receipt reprints **on the thermal printer** (cross-till, which MAUI cannot do); and the tenant's **card fee** is charged with the fee's VAT following the basket. **178 vitest tests** (was 45), `tsc` clean, **19 mutants run — 18 killed, 1 that needed a new vector**. ⚠ Nothing on that plan is waiting on more code: what is left is **deploy** (your call) and the **hand-run**, `Test Maui.md` **§W1–§W8**. ⚠ Not to skip: **§W4f** (the discount limit must survive the network dropping), **§W6b** (a Z must wait for its own day's sales) and **§W8a** (with no card fee set, the checkout screen must look *exactly* as it did yesterday) |

---

## The standing documents

| | For |
|---|---|
| ⚠⚠ [`Build/To do/MAUI-retrofit.md`](Build/To%20do/MAUI-retrofit.md) | **THE plan.** §0 is the live state and every ruling; §3 the open steps; §10 what comes out afterwards. There is no sixth place to look |
| [`Build/Test Maui.md`](Build/Test%20Maui.md) | The hand-run script — 69 sections, run order at the top |
| [`Build/till-design.md`](Build/till-design.md) | **Part A** the functional-parity table · **Part B** the capability register · **Part C** the money rules and what stops them drifting. Read before writing anything that computes money on a client |
| [`Build/repo-runbook.md`](Build/repo-runbook.md) | Build, test, migrate, deploy — and the 19 pitfalls that have each cost a session |
| [`Build/plutus-platform-architecture.md`](Build/plutus-platform-architecture.md) | **Wins any design conflict** with a plan |
| [`Build/index.md`](Build/index.md) | What every other document is for, and which are archived |
