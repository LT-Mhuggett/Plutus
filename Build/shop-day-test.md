# Shop-day test — the hand-run script

**Build: `D:\tmp\plutus-till-1.36.0\Plutus.Frontend.AppClient.exe`** (unpackaged — no signing, no
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
| 0.2 | **Plutus** tab | Version chip reads **v1.36.0**; connection green | — |
| 0.3 | Wait ~60s, then check the portal's fleet list | The till reports **1.36.0** | Versions were NULL on every row until backend 1.8.1 — this is the fix |

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
| 4.2 | **Right-click the basket line → Returns** | A picker of this till's recent sales, plus **"Sold on another till — look it up in Plutus…"** | Being asked to type a sale UUID = the picker isn't in. ⚠ Refunds must NOT appear in either list — a refund is its own sale with a negative gross, and offering one is what let £13.99 out twice |
| 4.3 | Pick the sale you just made, give a reason | Line becomes a return at **the price actually paid** | Today's catalogue price would be wrong |
| 4.4 | Try to refund it **twice** | Second attempt is **capped or refused** | *"never refund more than was paid"* — binding default 12 |

## 5. Mid-shift

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 5.1 | **Cash** → **Paid out** → `20.00`, reason *"window cleaner"* | Recorded | A missing reason must be refused |
| 5.2 | **Cash** → **X read** → count the drawer | Recorded as a count; **day stays open** | An X must never close the day |
| 5.3 | **Inventory Managment → View all items** | List **fills the window**, sits under the tabs, and you can navigate away | A short scroll box in the top ~230px = the layout fix isn't in |
| 5.3a | ⚠ **Check the A–Z grouping and the search** | Letter headers down the list; typing narrows it; the **column header stays put** while you scroll | ⚠ The list is a plain `CollectionView` now, not Syncfusion. This is the biggest untested swap in the build |
| 5.4 | **Press the Edit button on a row** (or tap the row for a sheet) | The edit prompt opens | ⚠ Tap the SAME row twice — it must open both times. A `CollectionView` won't re-raise selection for a row already selected, so this is the one that would read as a freeze |
| 5.4a | **Edit item** — the order is now: **Tax band → Category → Stock**, then the form | The three sheets come FIRST, and the form that follows SHOWS the tax band (with its **percentage**, e.g. `Standard — 20%`), the category and the stock setting as greyed rows beside the price | ⚠ Reworked in 1.34.0 — Matt: *"Maui edit items is missing category and tax e.g. 20%."* If a sheet is missing entirely you should now get a MESSAGE saying Plutus sent no bands/categories, never silence. ⚠ Needs a connection, a signed-in operator, and `portal.prices.manage` — **Owner and Company Admin hold it, Store Manager does NOT** |
| 5.4b | ⚠ Change the **tax band** on something and save | The ex-tax price on the portal follows the NEW band | ⚠ The ex price is derived by DIVIDING by the band's multiplier. If the portal shows an ex price *higher* than the inc price, the units are inverted — stop and tell me |
| 5.4c | ⚠ If a tax/category sheet does NOT appear | You get a message NAMING the reason — *"this operator isn't allowed to read them"*, *"nobody is signed in on this till"*, *"couldn't reach Plutus"*. **Send me the wording.** | ⚠ Before 1.35.0 every one of those came back as an empty list and the sheet was skipped in silence — a network fault presenting as a fact about your shop |
| 5.5 | Check that item in the **portal** | Every field you changed | If the portal disagrees, stop and tell me |
| 5.6 | **Settings → Receipt printer** | Either the **agent's** version and printer name, or a plain message saying no Plutus Till Agent is running on this PC | ⚠ Rebuilt in 1.33.0. Matt saw *"Wireless is turned off"* — that was Windows' generic device picker complaining about RADIOS because no OPOS printer exists, not about the printer. The till now prints the way the **web till** does, through the agent |
| 5.6a | If an agent is running: **Pair this till** → type the code from its tray window | A test receipt comes out | ⚠ The code is per PC and never leaves it. A wrong code gives "the agent didn't accept that code", not silence |
| 5.6b | **Settings → Print test page** | Paper, or a message | ⚠ This used to do **nothing at all** on a till with no OPOS printer — no paper, no message, indistinguishable from a broken printer |
| 5.6c | **Statistics → Reprint a receipt** → pick the sale from step 3.5 | A copy prints, headed **"REPRINT — not a new sale"** | ⚠ New 1.34.0 (cutover step 26). ⚠ **The drawer must NOT open** — no money is moving. ⚠ The barcode must be the SAME as the original's, or the copy can't be used to find the sale, which is the whole point |
| 5.6d | Reprint a **refund** receipt too | Prints, headed with **both** REFUND and REPRINT | Refunds are reprintable even though they are not re-refundable |
| 5.7 | **Statistics** | *"Today — £x taken over n sales · VAT £x · average basket £x"*, from the PLATFORM | ⚠ The two legacy report buttons are GONE: they read the pre-Plutus database (always zero since cutover) and their Syncfusion charts are unlicensed. Matt is **not renewing** — as of 1.33.0 no Syncfusion control is on any screen you can reach. See `Build/syncfusion-footprint.md` |

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

1. ⚠ **The Syncfusion removals (2.x quantity box, 5.3a item list, alterations)** — four controls
   swapped for MAUI ones in 1.33.0, on screens with **no automated coverage at all**, because that
   needs a running UI host and this repo has none. The quantity box is the one that matters: it is
   on the money path.
2. **Printing through the agent (5.6, 3.5)** — the till has never printed this way. ⚠ If no agent is
   installed on the PC, everything degrades to today's behaviour and nothing breaks; that is the
   design, so "no agent found" is a valid outcome, not a failure.
3. **The drawer kick (3.6)** — hardware, never exercised, and the fix was one missing line. In
   1.33.0 the drawer rides **with** the print job when the agent is in use.
4. **The Tax column (2.2)** — the VAT band cache has *never* been populated on any till; this is its first run.
5. **Item edit (5.4–5.5)** — now writes eight fields to the platform catalogue instead of two.
6. **Offline cash (6.3–6.4)** — the queue and drain are tested headlessly but not on a device.

**None of section 3 is covered by an automated test** — a running UI host is needed and this repo has
none. It is held by review, which is why the hand-run matters more than usual.
