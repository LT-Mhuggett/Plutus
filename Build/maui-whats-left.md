# MAUI retrofit — what is left to change

**Matt, 2026-08-10:** *"I do need to finish the entire MAUI retrofit plan to understand what is left
to change."*

This is that answer, **verified against the tree on 2026-08-10**, not read off the plan prose. It is
the one page to open when the question is "how much is left and what order".

| Document | Answers |
|---|---|
| **This page** | *What is left, in order, and roughly how long* |
| [`till-design.md`](till-design.md) Part B | *Which capability, on which till* — the row-by-row register |
| [`To do/MAUI-Cutover-Plan-2026-08-09.md`](To%20do/MAUI-Cutover-Plan-2026-08-09.md) | *How to build each step* — the bodies and DoDs |
| [`legacy-removal.md`](legacy-removal.md) | *What comes OUT afterwards* — Matt does this last |

---

## The one-line answer

**The plumbing is done. The screens are not.** A MAUI till can now take a sale, price it correctly,
commit it, queue it, drain it, refund against it and print it. What it cannot yet do is most of what
a shop does *around* selling: cash handling, reporting, inventory, loyalty, gift cards, users, and
theming.

**Cutover steps 1–20: done.** **11b + 21–28: remaining.** Roughly **45–60 working days**, dominated
by three: inventory (25), reporting (26) and loyalty/gift cards (27).

---

## ⚠ Read this before estimating anything

Steps 1–20 all passed their VERIFY. Matt then ran the till by hand for the first time on 2026-08-10
and that single day found: an overlay that bricked every screen after the first fast one, a scan box
that had never searched by name, nine buttons that closed the app rather than refusing, a payment
dialog with no exit of any kind, and an item list confined to a 230px box.

**None of it was reachable by any test in this repo.** So the estimates below are for *building* each
step. Add hand-running time to every one of them, and expect the screen to be wrong the first time.

⚠ **Four components have now been found fully built, tested, and called from NOWHERE**:
`OutboxPusher.DrainAsync`, the catalogue browse, `TillStore.SearchAsync`, and — found while writing
this page — **`NoticesClient`, which appears in the entire AppClient exactly once, in a comment.**
When a screen looks broken, `grep` for callers of the thing that should be doing the work *before*
debugging the thing itself.

---

## Order of work

### 0. Step 11b — reshape the basket ⚠ PROMOTED, do it first (~3–4 days)

`BasketItem` to long pence, `BasketReturnItem` collapsed to an `IsReturn` flag, the nine
`is BasketReturnItem` type-tests, both Mapster configs, `BasketDataTemplateSelector`, and every XAML
binding onto those members. ⚠ **Enumerate the bindings FIRST and check each renders — MAUI bindings
fail silently**, so a missed one blanks a column rather than failing the build.

**Why first, and why it is no longer a tidiness job.** `ExecuteCheckoutTransaction` is a ~200-line
`async void` holding the tender loop, cancel handling, the surcharge line, change, and the commit —
**none of it reachable without a UI host**. All three checkout defects fixed on 2026-08-10 shipped,
were found by hand, and are pinned by nothing. It is the only cluster of money-adjacent logic in the
app with no coverage at all. **Extract the tender loop into a testable unit as part of this step.**

It also unblocks [`legacy-removal.md`](legacy-removal.md) **L6** — `ItemModel` is load-bearing in the
till screen purely because the basket binds to it.

### 1. Step 22 — WP7 theming (~3–4 days)

Port ClientUI's `Colors.xaml` verbatim (same `x:Key` names) + `Styles.xaml` + the Syncfusion theme
mapping; then `GetEffectiveThemeAsync` on start and on the 60s cadence, cached so a scheme survives
an offline restart. ⚠ **Receipts ignore the theme entirely** (C1). Removes ClientUI from
`Plutus.slnx` — ⚠ **the port must land before the project is dropped** (legacy-removal L10).
USER-VERIFY: visual, plus a byte-identical receipt under light and dark.

### 2. Step 23 — WP9 cash (~4–5 days)

Float, paid in/out, X-read, Z-read, and the drawer. `CashEventRequest` DTO + client method (both
absent from `Plutus.Contracts.Client` today). ⚠ **This is the one remaining capability a shop
genuinely cannot open without** — everything else on this list has a workaround; a till that cannot
declare a float or run a Z-read cannot be reconciled at close. USER-VERIFY: the drawer physically
kicks on cash events.

### 3. Step 24 — WP8 Users screen (~3 days)

Employee list/create + set password, via the legacy `/api/Employee` and `/api/Auth/SetPassword`
(both still carry the web till). ⚠ Roles and effective-permissions management stay **portal-side** —
the MAUI parity target is the smaller surface. MAUI's current add-user command is a stopgap dialog
reading *"not available in this version yet"*.

### 4. Step 25 — WP10 inventory + stock ledger (~8–10 days) ⚠ largest gap

**This is the one that answers "you cannot edit an item".** Today the till can only *browse* the
catalogue — deliberately, since 2026-08-10: the old Add/Edit/Update-stock screens wrote into the
legacy local database, and the till client has **no item-write endpoint at all**, so an edit reached
no report, no other till and no VAT return. Since the basket now resolves from the v2 catalogue, a
locally-created item could not even be **sold** on the machine that made it. Hiding them was the
honest state; this step is the real fix.

Carries five Part B rows: **Inventory CRUD**, **stock as a movement ledger** (MAUI writes a flat
quantity column — concurrent edits are last-write-wins), the **VAT-band consistency guard**,
**category reassign** (MAUI creates locally and has no reassign UI, so the server's 409 has nowhere
to land), and **the Bin + untracked stock** — ⚠ a binned item must stop selling on an *offline* till,
which is what WP5's tombstones (`CatalogueItem.Removed`, built and still unread) are for.

Also here: **"add unknown scan as a new item"**, the flow the web till has and MAUI does not.
USER-VERIFY: scanner round-trip including the unknown-barcode path.

### 5. Step 26 — WP11 reporting + cross-till lookup (~8–10 days) — a rewrite, not a port

⚠ **The Statistics tab currently reads ZERO** for everything sold since cutover step 11, because both
reports read the legacy local database and sales no longer go there. It carries a red warning saying
exactly that, and it is **warned rather than hidden** only because a till migrated from NatApp still
holds real history in that file and this is the only way to see it. This step replaces it with
server-aggregated reporting. Removes [`legacy-removal.md`](legacy-removal.md) **L4**.

### 6. Step 27 — WP12 loyalty, then WP13 gift cards (~12–15 days) ⚠ largest single block

Members, tiers, store credit as a tender, customer attach at sale with auto-discount, member-number
scan-to-attach; then gift cards sell & redeem. ⚠ **Extract `MemberNumbers` from the web till's
implementation** rather than re-deriving it. ⚠ **Gift-card activation posts ZERO VAT** — it is a
liability, not revenue — and the provisioned `GIFT-CARD` item must exist.

### 7. Step 28 — online-first login (~2–3 days)

Local verifier minted at first online login. Closes the last row where the *web* till is ahead is
actually the reverse here — MAUI already has offline sign-in with an expiry and the **web till does
not** (WP17.1). Parity is not a synonym for "catch MAUI up".

---

## Smaller rows that ride along, and which step carries each

These are real Part B gaps that do not need a step of their own. Listing them so none is a surprise
when its step is opened.

| Gap | Rides with | ⚠ |
|---|---|---|
| **Pick-from-floor notices** + **announcements banner** | Step 22 or 23 (cheap once a screen is being touched) | ⚠ **Corrected 2026-08-10 from 🟡 to ⬜.** The row claimed "pending only the banner XAML". `NoticesClient` is referenced **once in the whole AppClient, in a comment** — it needs a cadence step *and* the XAML |
| **Portal-published VAT bands** — the whole timeline, refreshed | Step 25 | ⚠ Caching only *today's* rate is a bug: the timeline is what lets an offline till apply a future-dated change on the day. `Services/Storage/VatBands` gives the app a route to `VatBandCache`, used at basket-add for the band NAME; the cadence refresh is the missing half |
| **VAT band on the sale line** (`LineMeta.vatBand`) | Step 25 | 🟡 only because the **server backfills** any line that arrives without one (`VatBandStamp`). MAUI is correct-by-default; it needs to send it for cases the catalogue cannot know — a single-purpose gift-card line is `"standard"` by the voucher treatment, not its catalogue row |
| **Refund-only baskets** | Step 11b | Partly there — `refundOnly` is computed and drives the prompt wording and surcharge suppression. The reshape settles it |
| **Reprint from a past sale** | Step 26 | 🟡 — the read path landed at step 15 |
| **Portal-controlled receipt template** | Step 26 | ⬜ |
| **Un-enrol request + manager approval** | Step 21 | ⬜ — WP4's last piece |
| **Connection status** (network vs server vs revoked) | Step 28 | ⚠ Runs the OTHER way too: the **web till** is 🟡 here, still on `navigator.onLine`, which reports the network interface and never asks whether the server is there (WP17.3) |
| **Help / support tickets** | Step 24 | ⬜ — closes the `support-heavy` churn signal |
| **App-update prompt** | Step 28 | ⬜ |
| ⚠ **Remote lock of a lost or stolen till** | **Neither till has it — and it READS as built** | `Device.Locked` ships, `HeartbeatResult` carries it, `SyncClient` surfaces it — but **nothing sets it and nothing enforces it**. `IssueDeviceTokenAsync` refuses only on `Status == Revoked`. ⚠ **Reach for Revoked in a real incident.** Enforcement must land *before* any control that sets the flag, or the switch stays fake |

---

## Where the *web* till is behind

Parity is not a synonym for "catch MAUI up". Three rows run the other way:

| Row | Why |
|---|---|
| **Offline sign-in with an expiry** | MAUI has it (WP8 + WP16b, tiered horizons in `SharedKernel.OfflineCredentials`). The web till **cannot sign in offline at all** — WP17.1 |
| **Connection status** | Web is on `navigator.onLine`; both tills should move onto the shared `ConnectivityProbe` — WP17.3 |
| **Card surcharge** | Built on MAUI 2026-08-09. Web deliberately ⬜ — it reads `charge`/`minimumCharge` off the legacy wire and ignores them, and no tenant charges a fee today (UK consumer surcharges have been **banned since 2018-01-13**). Rides WP15's checkout reshape |

⚠ **WP15 needs Node, so it happens ON THE MAC.** It would also clear the queued TypeScript work.

---

## What is NOT on this list, deliberately

- **Deleting the legacy code.** That is [`legacy-removal.md`](legacy-removal.md), L1–L10, and Matt
  does it **last of all** — after the step that replaces each piece has landed.
- **The legacy `Database.db` file.** Never delete it: it is the shop's pre-cutover history and there
  is no server copy.
- **Anything that would make a legacy write path work again.** Where a screen wrote somewhere nothing
  reads, the answer is the platform endpoint, not a repair.
