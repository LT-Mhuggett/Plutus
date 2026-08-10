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

**A MAUI till can now trade a whole day, and print while doing it.** Take a sale, price it, commit
it, queue it, drain it, refund against it, reprint a receipt, open a float, close the day with a
Z-read — and as of 2026-08-10 **add an item, edit an item, and add an unknown scan from the
counter**. What it still cannot do is **loyalty, gift cards, users, theming, reporting, and the
stock ledger**.

**Done: cutover steps 1–20, 23, 26's reprint half, and the first two slices of 25.**
**Remaining: 11b · 21 · 22 · 24 · the rest of 25 · the rest of 26 · 27 · 28 — about 45 working
days.** Two thirds of that is three items: **loyalty + gift cards (27, ~15d)**, **reporting (26,
~9d)** and **the stock ledger + categories + the Bin (rest of 25, ~5d)**.

### ⚠ Four rows are now closed that this page previously listed as open

| Row | Was | Now |
|---|---|---|
| **Item edit / create** | "the till can only browse" | ✅ Full field set — name, brand, description, cost, price, **tax band, category, stock tracking**. The band shows its **percentage** |
| **Add unknown scan as a new item** | ⬜, listed as WP10's | ✅ The till offers it, carrying the barcode, and NAMES the clashing item if the code is taken |
| **Reprint from a past sale** | 🟡 | ✅ Statistics → "Reprint a receipt", marked **"REPRINT — not a new sale"**. 🟡 only for a sale rung on ANOTHER till |
| **Printing at all** | not tracked as a gap | ✅ ⚠ **It was a gap and nobody had written it down.** MAUI used an OPOS device picker that finds nothing on most till PCs; the web till has always printed through the **Plutus Till Agent**. One route now. See `till-design.md` |

### ⚠ And one dependency is gone

**Syncfusion is off every screen an operator can reach** (Matt is not renewing). Quantity box,
alterations picker, item list and the discount multi-select are plain MAUI. What remains is the two
**hidden** legacy report screens and their spreadsheet export — which step 26 deletes anyway, so the
licence question closes itself. [`syncfusion-footprint.md`](syncfusion-footprint.md).

⚠ **The cheap wins are now spent.** Almost everything delivered before 2026-08-10 was *wiring* —
components that already existed with no caller. What is left is screen-building and one server
change, and it does not compress the same way.

⚠ **The pattern that keeps holding.** Cash looked like a five-day build and the server turned out to
be finished — the whole step was client-side wiring. Six components have now been found built,
tested and called from nowhere (`OutboxPusher.DrainAsync`, the catalogue browse,
`TillStore.SearchAsync`, `NoticesClient`, `VatBandCache.RefreshAsync`, `OperatorSession.Token`), plus
a heartbeat write that was assigned on every beat and saved on none. **Check what already exists
before estimating any of the rows below.**

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

### 2. ✅ Step 23 — WP9 cash — **DONE 2026-08-10**

Float, paid in/out, X-read, Z-read, on their own tab beside the Till. The server was already
finished; the whole step was client-side, and the five type names had appeared nowhere in the app.

⚠ **Queued, not posted** (`LocalCashEvents`, schema v4, drained on the 60s tick) — a shop opens
before its broadband does. ⚠ **One Z per day enforced locally too**, because the server's guard is
unreachable offline. ⚠ **The expected figure stays the server's** — only the platform sees the sales
half. ⚠ `SharedKernel.BusinessDay` extracted so the drawer and the sales it reconciles against
cannot disagree about "today".

⚠ **USER-VERIFY still open: does the drawer physically kick?** `POSCashDrawer.InitPOSObject` fell
out of its own success path into `throw NotClaimable` until 2026-08-10, so a working drawer reported
"in use by another process" on every cash sale — and the "Silence" button set a preference nothing
read, so the modal came back for ever. Both fixed; neither has been exercised on hardware.

### 3. Step 24 — WP8 Users screen (~3 days)

Employee list/create + set password, via the legacy `/api/Employee` and `/api/Auth/SetPassword`
(both still carry the web till). ⚠ Roles and effective-permissions management stay **portal-side** —
the MAUI parity target is the smaller surface. MAUI's current add-user command is a stopgap dialog
reading *"not available in this version yet"*.

### 4. Step 25 — WP10 inventory + stock ledger — **~5 days left of ~10**

✅ **Slices 1 and 2 landed 2026-08-10 (tills 1.33.0–1.36.0).** Item **create** and **edit** with the
web till's full field set; **add-unknown-scan** from the counter; the catalogue feed grown to carry
**Brand, Desc and Cost** (schema v5), which closed a silent search-parity gap — `ItemSearch` matches
brand and the till had no brand column, so "Marvel" found nothing here and everything on the web.

⚠ **The backend half of that (1.9.0) is BUILT AND NOT DEPLOYED.**

**What is genuinely left, in order:**

| # | Piece | ~ | ⚠ |
|---|---|---|---|
| 25a | **Stock quantity on the item list** — `GET /api/v1/stock/levels` | 1d | Read-only, gated `portal.reports.view`. The column currently shows **"—"** (not a number) precisely because nothing knows the count — a blank would read as zero |
| 25b | **Adjust stock** — `POST /api/v1/stock/movements` | 1–2d | ⚠ **BLOCKED ON A DECISION, NOT ON CODE** — see below |
| 25c | **Category create / rename / reassign** — `/api/v1/categories` | 1d | The 409 is **reassign-first**: a category with items refuses deletion until they are moved, and the till has nowhere for that refusal to land today |
| 25d | **The Bin** (soft delete + restore) | 1d | ⚠ A binned item must stop selling on an **offline** till — that is what the feed's `Removed` tombstone is for, and it is carried and still unread by any screen |
| 25e | **Portal-published VAT bands, whole timeline** | ½d | ⚠ Caching only *today's* rate is a bug: the timeline is what lets an offline till apply a future-dated change on the day it starts |

#### ⚠⚠ 25b is a PERMISSIONS decision and it is Matt's, not mine

`POST /api/v1/stock/movements` is gated on **`portal.stock.adjust`**, and the RBAC seed gives that to
**Owner, Company Admin, Store Manager** and the legacy "Stock & Items" role — **and to nobody else.**

**Supervisor and Cashier do not hold it.** So a supervisor standing at the counter with a damaged
box cannot write it off, and the till can only offer stock adjustment to a manager.

Three options, and they are genuinely different policies rather than implementations:

1. **Leave it.** Stock adjustment is a back-office job; the till shows counts and does not change
   them. Cheapest, and defensible for a single shop.
2. **Add `pos.stock.adjust`** as a separate till-side code and give it to Supervisor. ⚠ Needs a
   `PermissionCatalogue` entry, an RBAC re-seed, and the server endpoint taught to accept either.
3. **Give Supervisor `portal.stock.adjust`.** One line in the seed — ⚠ but it is a *portal*
   permission, so it also grants category and price-list writes in the portal. Almost certainly not
   what is wanted.

⚠ **It is also a stock LEDGER, not a quantity box.** `qty` is a signed delta and zero is refused;
the only "set it to N" surface in the v1 API is `POST /api/v1/stock/takes`, which converts
counted − expected into an adjustment server-side. **A MAUI screen whose box holds an absolute
number must post a TAKE, not a movement** — posting the typed number as a delta would add the count
to the count.

### 4b. ✅ Refunds are now reachable — the remainder rides step 26 (~1 day)

✅ **Closed 2026-08-10.** `TillStore.ListRecentSalesAsync` + a picker in front of the Returns
dialog: choose the sale from a list of this till's last 20, then give only the reason. Typing an id
stays available. Pinned by five new `TillStoreReadPathTests`.

⚠ **Still to do, and it belongs with step 26:** the picker shows **this till's own sales only**.
Goods bought at another branch still need the sale id typed, because only the server knows that
sale. The same screen serves **reprint from a past sale** (Part B 🟡), so build them together.

<details>
<summary>What was wrong, for the record</summary>

**Matt, 2026-08-10: *"In MAUI I cannot do a refund?"*** — the refund *rule* is built and shipped
(cutover steps 15–17). What is missing is how you find the sale.

The flow today: add the item to the basket → **context menu on the basket line → "Returns"** → a
dialog asks for **the original sale ID** and a reason. `ReturnLookup` then prefers the SERVER record
(goods bought on till B and returned at till A is ordinary retail, and only the platform knows what
has already been given back elsewhere), falling back to this till's own record inside the 14-day
window.

⚠ **Two things make it unusable in practice, and neither is the refund logic:**

1. **There is no way to list or search past sales on the till.** `TillStore.FindLocalSaleAsync`
   takes a `Guid` and there is no browse, no "today's sales", no receipt search — verified by grep,
   nothing of the sort exists. The sale ID's only source is the barcode on the printed receipt, so
   **a till without a printer cannot refund anything.**
2. **"Returns" is a context-menu item on a basket row** — a right-click on Windows. Nothing on the
   screen suggests it exists.

Neither needs new platform work: `GET /api/v1/sales` already answers, and step 15 built the read
path.

</details>

### 5. Step 26 — WP11 reporting + cross-till lookup (~8–10 days) — a rewrite, not a port

⚠ **The Statistics tab currently reads ZERO** for everything sold since cutover step 11, because both
reports read the legacy local database and sales no longer go there. It carries a red warning saying
exactly that, and it is **warned rather than hidden** only because a till migrated from NatApp still
holds real history in that file and this is the only way to see it. This step replaces it with
server-aggregated reporting. Removes [`legacy-removal.md`](legacy-removal.md) **L4**.

#### ⚠ Splitting reports per store and per till — Matt asked 2026-08-10. It WORKS, with one catch.

Verified against live data the same day: three sales rang up on Matt's till and landed as
`SalesRollups` **StoreId 4 · TillId 019fe244… · 3 txns · £97.94**. So the answer to *"does all of
this information get sent?"* is **yes** — nothing needs to change on the till.

- **Per till** is solid. `SalesV2.TillId` is on every sale and is **server-authoritative**, derived
  from the enrolled device at ingest rather than trusted from the payload — a till cannot claim to
  be another one. `DeviceId` is there too, so two devices on one till are separable.
- **Per store** works *today* through `SalesRollups.StoreId`.

⚠ **But the store is DERIVED, not recorded.** `SalesV2` has **no `StoreId` column**;
`RollupProjection.ResolveSpineAsync` resolves till → store → company at the moment the rollup is
written. Rollup rows already written keep the store they were written with, so ordinary history is
safe — **but `RollupRebuilder.RebuildAsync` re-derives from the till's CURRENT store**, and a rebuild
is exactly what you run after a projection bug or to fold in migrated rows.

So: **move a till between stores, then rebuild, and every historical sale it ever took moves with
it.** Yesterday's takings change shop. Nothing errors and nothing flags it.

This is cheap to close *now* and expensive later: stamp `StoreId` onto `SalesV2` at ingest (the
device already resolves the till, and `TillPlacement` already re-reads placement on every start), and
have the rebuild read the stamped value instead of re-resolving. Matt's call — he has said **no
changes for now**, and with one store live the exposure is currently nil. Recorded here so the
decision is deliberate rather than discovered during a year-end.

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
| **Reprint from a past sale** | ✅ **Done 2026-08-10** | Statistics → "Reprint a receipt", from this till's last 20. ⚠ Marked **"REPRINT — not a new sale"** above the first rule, and the marker's POSITION is pinned by a test: the refund flow accepts a sale found by a receipt barcode, so two identical papers for one purchase is the shape of a double refund. ⚠ Same barcode as the original — the marking distinguishes the paper, never the sale. **Step 26 still owns the cross-till case** |
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
