# MAUI parity — what remains, and what is done

> ## 📦 ARCHIVED 2026-08-12 — folded into [`Build/MAUI-retrofit.md`](../MAUI-retrofit.md)
>
> **This page lived for one day.** It was written on 2026-08-12 to end a status board kept in four
> places; that afternoon Matt asked for the whole retrofit in one document —
> *"I do not know why its splintered into so many"* — so its shape (what remains first, what is
> completed at the bottom) became Parts 1 and 3 of `MAUI-retrofit.md`, and the four documents it
> pointed *at* were folded in with it.
>
> ⚠ **Its counted figures were correct on 2026-08-12** (75 Part B rows: 40 ✅ · 15 ⬜ · 7 🟡 · 5 where
> MAUI is ahead) and are carried forward. **Take the numbers from `MAUI-retrofit.md`, not from here.**

**The single page for "where are we with the MAUI till".** Replaces the status boards in
[`archive/MAUI-Cutover-Plan-2026-08-09.md`](MAUI-Cutover-Plan-2026-08-09.md) and
[`archive/MAUI-Retrofit-Plan-2026-08-07.md`](MAUI-Retrofit-Plan-2026-08-07.md), and the
"what's left" half of [`HANDOVER.md`](../../HANDOVER.md).

⚠ **The two plans are NOT retired — they hold the step bodies and DoDs, which is real knowledge.**
What they must no longer be trusted for is STATUS: on 2026-08-12 the cutover plan's board still showed
steps 23 and 25 unticked when both had shipped, which is exactly the confusion this page ends. **Read
them for HOW to build a step. Read this for WHETHER it is built.**

| Where | For |
|---|---|
| **This page** | What remains, in order · what is done · how long |
| [`till-design.md`](../till-design.md) **Part B** | The row-by-row register — which capability, on which till |
| [`archive/MAUI-Cutover-Plan-2026-08-09.md`](MAUI-Cutover-Plan-2026-08-09.md) | Step bodies, DoDs, the execution protocol |
| [`archive/MAUI-Retrofit-Plan-2026-08-07.md`](MAUI-Retrofit-Plan-2026-08-07.md) | The WP bodies behind each step |
| [`legacy-removal.md`](legacy-removal.md) | What comes OUT afterwards — Matt does this last |
| [`Test Maui.md`](../Test%20Maui.md) | The hand-test script to give a person |

**Counted, not estimated — Part B, 2026-08-12: 75 capability rows.**
**40 ✅ both tills · 15 MAUI ⬜ · 7 MAUI 🟡 · 5 where MAUI is AHEAD of the web till.**

---

# WHAT REMAINS

## 0. Open faults — before any new work

| # | What | State |
|---|---|---|
| **Q** | ⚠⚠ **"I could cancel the item, but then searching stopped working."** (Matt, 2026-08-11) | **Cause not found.** Ruled out: `IsBusy` stuck (every set has a `finally`), the cancel handler (touches no shared flag), a dialog awaited inside the store lock (nothing does it). ⚠ A 30s timeout was added to the store gate so this CLASS of failure can no longer hang in silence — but that is a safety net, not a diagnosis. ⚠ **Not reproducible the way it was found**: on 1.48.0 a closed till refuses at the door, so a basket cannot be built on a closed day. **Needs: which search box, and whether the rest of the app still responded.** |
| — | **Hand-run 1.48.0** | ⚠ **Eight till builds have shipped since a person last touched a screen.** Every hand-run so far has found faults no test in this repo could reach — the 2026-08-11 run found fourteen, six of them invisible to every automated test. [`Test Maui.md`](../Test%20Maui.md) |

## 1. Small, and each closes a real inconsistency

| # | What | ~ | Why it matters |
|---|---|---|---|
| **W1** | **Reopen a Z-closed day on the WEB till** | 1d | MAUI has it (till 1.48.0); the web till does not. Matt asked for *"Web and MAUI"*. The two tills currently disagree about whether a closed day can be recovered — a supervisor on the browser is stranded until midnight. Server side is done and live |
| **W2** | **Add item as ONE screen** | ½d | The EDIT screen was rebuilt as a single page (finding K, three attempts). **Add** still opens three questions then a form — the same shape that was wrong for edit. `EditItemPage` is written; this is a create mode on it |
| **W3** | **Portal screen for the expected till version** | ½d | `GET`/`PUT /api/v1/platform/till-release` is live and works; nothing sets it from a UI, so the heartbeat's update check cannot be switched on without curl |
| **W4** | **Web till: roster + permissions on a cadence** (WP17.4) | 1–2d | ⚠ A disabled operator is signed out of MAUI within 60s; the web till has **no proactive check at all** — it signs out only when a request happens to 401, and login tokens are cached 12h. Same change also stops it discarding the whole heartbeat response (`Locked`, `SyncNow`, `CatalogueCursor` are dead there) |

## 2. The cutover steps still open

| Step | What | ~ | Notes |
|---|---|---|---|
| **11b** | **Reshape the basket** — `BasketItem` to long pence, `BasketReturnItem` → an `IsReturn` flag | **4d** | ⚠ **Do it before the screens.** `ExecuteCheckoutTransaction` is a ~200-line `async void` holding the tender loop, cancel handling, the surcharge line, change and the commit — **the only money-adjacent cluster in the app with no test coverage at all**. Every checkout defect so far was found by hand. ⚠ Enumerate the XAML bindings FIRST: MAUI bindings fail silently, so a missed one blanks a column rather than failing the build. Unblocks `legacy-removal` L6 |
| **22** | **WP7 theming** — portal-pushed colour schemes | 3–4d | Port ClientUI's `Colors.xaml` verbatim (same `x:Key` names) + `Styles.xaml`; `GetEffectiveThemeAsync` on start and on the 60s cadence, cached so a scheme survives an offline restart. ⚠ Receipts ignore the theme (C1). Removes ClientUI from the solution — the port must land BEFORE the project is dropped (`legacy-removal` L10) |
| **24** | **WP8 users** — employee list/create + set password | 3d | Via the legacy `/api/Employee` and `/api/Auth/SetPassword`, which still carry the web till. ⚠ Roles and effective permissions stay PORTAL-side; the MAUI target is the smaller surface. MAUI's current add-user is a stopgap dialog reading *"not available in this version yet"* |
| **26** | **WP11 reporting** + cross-till lookup | **8–10d** | A rewrite, not a port. The first slice landed (today's takings from the platform, and reprint). ⚠ What remains: items sold, VAT, best sellers, and the **cross-till sale lookup** — a refund for goods bought at another branch still needs the sale id typed. The same screen serves reprint-from-any-till, so build them together |
| **27** | **WP12 loyalty, then WP13 gift cards** | **12–15d** | ⚠ **The largest single block, and two thirds of everything left with step 26.** Members, tiers, store credit as a tender, customer attach at sale, member-number scan. Gift cards: sell and redeem. ⚠ Gift-card VAT is settled and must not be re-derived — activation posts ZERO VAT (it is a liability, not revenue) |
| **28** | **Online-first login** | 2–3d | Hardening. Default 16 |

## 3. Where the WEB till is behind (parity runs both ways)

| Row | WP | Note |
|---|---|---|
| **Offline sign-in with an expiry** | WP17.1 | MAUI has tiered horizons (`SharedKernel.OfflineCredentials`). The web till **cannot sign in offline at all** |
| **Card surcharge** | WP15 | Built on MAUI 2026-08-09; the web till reads `charge`/`minimumCharge` off the legacy wire and ignores them. ⚠ **Kapow's rate is ZERO (confirmed)**, and UK consumer surcharges have been banned since 2018-01-13, so this is a latent trap for a future B2B tenant rather than a live discrepancy |
| **Reprint a receipt** | WP11 | MAUI has it; web joins the reporting screen that already lists sales |
| **Roster on a cadence + sign-out on disable** | WP17.4 | See W4 above — the one that is not cosmetic |
| **Connection status** | WP17.3 | Web is on `navigator.onLine`; both tills should move to the shared `ConnectivityProbe` |

## 4. The 15 MAUI ⬜ rows, grouped

Not fifteen problems — **five clusters**, each already owned by a step above:

- **Loyalty / gift cards / customers** (6 rows) → step 27
- **Platform notices** — announcements, help tickets, app-update prompt, pick-from-floor (4 rows) → no step yet, ~3–4d
- **Theming + portal-controlled receipt template** (2 rows) → step 22
- **Users** (1 row) → step 24
- **Un-enrol request + manager approval** (1 row) → ~1d
- **Refund-only baskets** (1 row) → step 27

---

## How long, honestly

**≈35–40 working days.** Two thirds is **step 27 (12–15d)** and **step 26 (8–10d)**.

⚠ **That is BUILD time, not DONE time.** Steps 1–21 all passed their VERIFY, and then the first
hand-run found fourteen faults — six invisible to every test here. **Add hand-running to every row,
and expect the screen to be wrong the first time.** Three separate attempts were needed to fix one
edit form.

⚠ **Treat the total as ±25%, and check before estimating.** On 2026-08-11 **three Part B rows still
said ⬜ for work that had already shipped** (the Bin, VAT bands, step 25e) — a stale ⬜ makes the gap
look BIGGER and gets it re-planned. **Grep for a ⬜ before believing it.**

⚠ **And check what already exists.** Cash looked like a five-day build and the server turned out to be
finished — the whole step was client-side wiring. **Seven components have now been found built,
tested and called from nowhere**: `OutboxPusher.DrainAsync`, the catalogue browse,
`TillStore.SearchAsync`, `NoticesClient`, `VatBandCache.RefreshAsync`, `OperatorSession.Token`, and a
heartbeat write assigned on every beat and saved on none.

---
---

# WHAT HAS BEEN COMPLETED

**A MAUI till can trade a full shop day.** Sell, price, split-tender, refund to the original method,
reprint a receipt, print through the hardware agent, open a float, X-read, Z-read, **reopen a Z**,
add and edit items, adjust stock, manage categories, bin an item, sign a disabled operator out
within 60 seconds, and keep trading with the line down.

## Cutover steps

| Phase | Steps | State |
|---|---|---|
| **0 — Foundation** | 1 EF9 · 2 reference · 3 `TillStoreAccess` · 4 enrolment flow | ✅ |
| **1 — Line primitives** | 5 price pair · 6 TaxId + StockUntracked · 7 VAT band store · 8 tender values → SharedKernel | ✅ |
| **2 — The money path** | 9 basket + assembler · 10 v2 lookup · 11 `CommitSaleAsync` · 12 permission gates · 13 sync services · 13b fixed tender set · 14 receipt re-signature · 14b crash sweep | ✅ (⚠ **11b outstanding**) |
| **3 — Returns / park / reprint** | 15 sale read path · 16 `RefundRules` wiring · 17 server refund cap · 18 parked baskets | ✅ |
| **4 — Operator auth** | 19 | ✅ |
| **5 — Screens** | 20 WP6 · 21 store info · **23 WP9 cash** · **25 WP10 inventory** · **26 reprint half** | ✅ (22, 24, rest of 26, 27 open) |
| **6 — Hardening** | 28 | ⬜ |

⚠ **Step 25 is DONE, all of it** — including 25e (the VAT band timeline), which this page carried as
"the last half-day" for three days before a grep found it already built.

## The 2026-08-11 hand-run — all fourteen findings closed

Matt ran a shop day on till 1.41.0 and reported **A–N**. Every one is answered, and six more raised
that evening (**O–T**). Full detail with causes in
[`handrun-2026-08-11.md`](../handrun-2026-08-11.md).

| | What was wrong |
|---|---|
| **A** | The till **crashed** when you clicked into the Inventory search box — focus alone rebuilt a grouped collection, and the search path had no exception guard |
| **B** | ⚠⚠ **A sale could be taken after the day was Z-closed, and the platform ACCEPTED it** — 201, onto a day already counted and banked. The rule existed only on the cash path. Now gated on the till AND the server (quarantine, not reject — the money is real) |
| **C** | An item just SOLD could not be re-added — a dangling basket selection incremented a detached ghost |
| **D/E** | *"Something went wrong"* on an over-payment — it was a **crash**, not wording. Refusals now say what is wrong |
| **F** | Split payments — ⚠ **they already worked on both tills**; nothing needed writing. The web till gained its first-ever tests (19) while checking |
| **G** | Refunds now offer **only the tender the sale was paid with** |
| **H** | Stock adjustments report — the ledger always had it; there was no way to READ it |
| **I** | A counted drawer now says **SHORT/OVER**; the platform always knew and never told anyone. ⚠ The Z now waits for its own day's sales |
| **J/N** | Screens did not redraw — the Cash tab showed "(waiting to send)" against money already banked, and **today's takings were read once, at sign-in** |
| **K** | ⚠ Edit item — **three attempts**. Not a clipped form; the container was the constraint. Now one page |
| **L** | There is **no delete-item** and never was — item delete is structurally impossible (`ItemController` is CRU, not CRUD) |
| **M** | Tabs renamed to match the web till |
| **O–T** | Reopen a Z close · a closed till refuses at the door · "Locations & Tills" · the `0.0.0` version bug · "Last online" was the enrolment date |

## Platform work that landed alongside

- **Permissions reach a tenant on BOOT** — `RolePermissionReconciler`. It used to be a manual seed
  step somebody had to remember, and its failure was invisible: the permission simply did not exist.
- **The heartbeat re-reads the roster**, and a disabled operator is signed out with Matt's wording.
- **The heartbeat checks for updates** — advisory only; there is no self-update for MAUI.
- **`Device.LastSeenUtc`** — "last online" was the enrolment date for every till, for ever.
- ⚠⚠ **The nightly database backups were EMPTY for two days and logged `backup ok`.** Found by
  checking a pre-deploy dump instead of assuming it. Fixed, and proven both ways.
- **Syncfusion is off every screen an operator can reach** — the licence question closes itself.

## Where MAUI is AHEAD of the web till

Offline sign-in · card surcharge · roster re-read on the heartbeat · sign-out on disable ·
reprint a past sale. ⚠ Parity is not a synonym for "catch MAUI up".
