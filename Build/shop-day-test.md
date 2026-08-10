# Shop-day test — the hand-run script

**Build: `D:\tmp\plutus-till-1.31.0\Plutus.Frontend.AppClient.exe`** (unpackaged — no signing, no
install; just run the .exe).

This is the USER-VERIFY script for everything that landed on 2026-08-10. It is ordered as a real
trading day, because that is the order the bugs appear in. **Do them in sequence** — several steps
set up the next one.

⚠ **Sign in as Owner or a Store Manager.** The built-in **Cashier role holds only `pos.sell`**, so a
cashier cannot open a float, take a paid-out, or close the day — that is deliberate, not a fault.

⚠ **If anything crashes or hangs, grab the crash log before restarting.** The Plutus tab shows its
path. `CrashLog` hooks both `AppDomain` and `Microsoft.UI.Xaml.Application.UnhandledException`.

---

## 0. Before the doors open

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 0.1 | Launch the till | Signs in; tabs are **Till · Inventory Managment · Cash · Statistics · Store Information · Settings · Plutus** | No **Cash** tab = you are on an older build |
| 0.2 | **Plutus** tab | Version chip reads **v1.31.0**; connection green | — |
| 0.3 | Wait ~60s, then check the portal's fleet list | The till reports **1.26.0+10fed46** | Versions were NULL on every row until backend 1.8.1 — this is the fix |

## 1. Open the day

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 1.1 | **Cash** tab | Header: *"<today> — open. 0 cash event(s)."* | — |
| 1.2 | **Open float** → `150.00` | Row appears: `HH:mm OpenFloat £150.00 (waiting to send)` | — |
| 1.3 | Wait ~60s, revisit Cash | *"(waiting to send)"* has **gone** | Still waiting = the drain isn't running; check the Plutus tab |

## 2. Sell — the search that never searched

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 2.1 | **Till** tab, type `BAT` in the scan box, Enter | A **picker of matching items** (`Name · barcode · price`) | *"We can't find an item with that ID"* = the search fix isn't in |
| 2.2 | Pick one | It lands in the basket with a price **and a Tax column value** | ⚠ **Blank Tax column** = `VatBandCache.RefreshAsync` isn't running. Give it 60s first — the bands arrive on the tick |
| 2.3 | Scan/type a **real barcode** | Goes straight in, **no picker** | A picker on an exact barcode would be wrong — scanning must never prompt |

## 3. Take the money — the dialog that had no exit

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 3.1 | **Checkout** → **Cash** → amount prompt → **Cancel** | Back to your basket, **intact**, nothing taken | A dark screen you cannot leave = the payment-dialog fix isn't in. This is the big one |
| 3.2 | Checkout → Cash → over-tender (e.g. £20 for £3.30) | Change is offered | — |
| 3.3 | Checkout → **Card** → over-tender | **Refused**, asks again | A card cannot give change |
| 3.4 | Checkout → Cash → `0` | **Refused**, asks again | `0` used to be accepted and re-prompt for ever |
| 3.5 | Checkout properly | Sale completes; receipt prints (if a printer is attached) | — |
| 3.6 | ⚠ **Watch the drawer** | It **physically opens** on the cash sale | ⚠ **THE ONE I CANNOT TEST FROM HERE.** `POSCashDrawer` fell out of its own success path into `throw NotClaimable` — the `return` was missing — so a working drawer reported "in use by another process" on every cash sale |
| 3.7 | If a drawer warning appears, press **Silence**, then take another cash sale | It does **not** come back | "Silence" set a preference nothing read, so it returned for ever |
| 3.8 | Portal → the sale is there | Matches the till | — |

## 4. Give money back

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 4.1 | Add the same item to a fresh basket | — | — |
| 4.2 | **Right-click the basket line → Returns** | A **picker of this till's recent sales** (`date · amount · items`) | Being asked to type a sale UUID = the picker isn't in. ⚠ There is nothing on screen advertising this menu — that is a known gap (step 26) |
| 4.3 | Pick the sale you just made, give a reason | Line becomes a return at **the price actually paid** | Today's catalogue price would be wrong |
| 4.4 | Try to refund it **twice** | Second attempt is **capped or refused** | *"never refund more than was paid"* — binding default 12 |

## 5. Mid-shift

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 5.1 | **Cash** → **Paid out** → `20.00`, reason *"window cleaner"* | Recorded | A missing reason must be refused |
| 5.2 | **Cash** → **X read** → count the drawer | Recorded as a count; **day stays open** | An X must never close the day |
| 5.3 | **Inventory Managment → View all items** | List **fills the window**, sits under the tabs, and you can navigate away | A short scroll box in the top ~230px = the layout fix isn't in |
| 5.4 | **TAP an item** in the list | A sheet: **Add to basket · Edit item · Cancel** | ⚠ Tapping is now the way in. Editing used to live ONLY on a right-click menu with nothing advertising it — asked what happened, Matt's answer was *"I didn't know how to open it"*, which is the honest verdict on that design. Right-click still works too |
| 5.4a | **Edit item** → change the price | Saves; list shows the **new** price | ⚠ Needs a connection and a signed-in operator — item writes go to the platform now, not to a local table. ⚠ Needs `portal.prices.manage`, which **Owner and Company Admin hold and Store Manager does NOT** |
| 5.5 | Check that item in the **portal** | Same new price | If the portal disagrees, stop and tell me |
| 5.6 | **Settings → Change printer** | Printer list, or a polite refusal | ⚠ **It must never close the app.** That was the crash |

## 6. Trading with the line down

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 6.1 | Disconnect the network | — | — |
| 6.2 | Sell something | Completes normally | Selling must never depend on the network |
| 6.3 | **Cash → Paid in** → `5.00`, reason *"change from safe"* | Recorded, `(waiting to send)` | A shop opens before its broadband does |
| 6.4 | Reconnect, wait ~60s | `(waiting to send)` clears; the sale reaches the portal | — |

## 7. Close the day

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 7.1 | **Cash → Z read — close the day** → count | A confirmation naming the day and the amount | — |
| 7.2 | Confirm | Header turns **CLOSED** (red) | — |
| 7.3 | Try **any** cash action again | **Refused** — *"already been closed with a Z read"* | ⚠ Every type, not just a second Z |
| 7.4 | Portal → the day's cash events | Float, paid-out, paid-in, X and Z, with **expected vs counted** | ⚠ The *expected* figure is the **server's** — the till never computes it |

---

## What I most expect to be wrong

Ranked by how likely and how much it matters:

1. **The drawer kick (3.6)** — hardware, never exercised, and the fix was one missing line.
2. **The Tax column (2.2)** — the VAT band cache has *never* been populated on any till; this is its first run.
3. **Item edit (5.4–5.5)** — brand new, and the first thing on the till to WRITE to the platform catalogue.
4. **Offline cash (6.3–6.4)** — the queue and drain are tested headlessly but not on a device.

**None of section 3 is covered by an automated test** — a running UI host is needed and this repo has
none. It is held by review, which is why the hand-run matters more than usual.
