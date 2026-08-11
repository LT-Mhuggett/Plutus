# Shop-day test — the hand-run script

**Build: `D:\tmp\plutus-till-1.41.0\Plutus.Frontend.AppClient.exe`** (unpackaged — no signing, no
install; just run the .exe).

This is the USER-VERIFY script for everything that landed on **2026-08-10 and 11** — ten builds,
**1.30.0 → 1.41.0**, and not one screen has been touched by a person yet. It is ordered as a real
trading day, because that is the order the bugs appear in. **Do them in sequence** — several steps
set up the next one.

⚠ **Sign in as Owner or a Store Manager.** The built-in **Cashier role holds only `pos.sell`**, so a
cashier cannot open a float, take a paid-out, or close the day — that is deliberate, not a fault.

⚠ **If anything crashes or hangs, grab the crash log before restarting.** The Plutus tab shows its
path. `CrashLog` hooks both `AppDomain` and `Microsoft.UI.Xaml.Application.UnhandledException`.

### One thing must happen on the SERVER first

**Deploy backend 1.9.0** (`D:\tmp\plutus-backend-1.9.0\`). Until then the catalogue feed does not
carry brand / description / cost, so **§5.5a's stock column stays "—"** and searching by brand finds
nothing. Nothing breaks — the columns are nullable — it simply does not switch on. Afterwards press
**Plutus → "Re-download the whole catalogue"**.

✅ **The RBAC re-seed is no longer a manual step.** Matt asked why it had to be
(2026-08-11) and the honest answer was that it didn't — `RolePermissionReconciler` now runs on
every backend boot, so `pos.stock.adjust` reaches Supervisor as soon as 1.9.0 starts. ⚠ It is
additive only: it never removes a grant and never overwrites a ceiling a shop has set itself.
`Plutus.SeedMigrator rbac` still exists for running it against a database out of band.

---

## 0. Before the doors open

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 0.1 | Launch the till | Signs in; tabs are **Till · Inventory Managment · Cash · Statistics · Store Information · Settings · Plutus** | No **Cash** tab = you are on an older build |
| 0.2 | **Plutus** tab | Version chip reads **v1.41.0**; connection green | — |
| 0.3 | Wait ~60s, then check the portal's fleet list | The till reports **1.41.0** | Versions were NULL on every row until backend 1.8.1 — this is the fix |

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
| 5.5a | ⚠ **Check the Stock column** | A **number**, or **∞** for an untracked item, or **—** for one never counted | ⚠ It used to be **blank on every row**, which reads as ZERO — the till was saying the shop holds none of anything. ⚠ **"—" is not "0"**: if you see 0 against something never counted, tell me |
| 5.5b | Tap a row → **Adjust stock…** → *Write some off* → `2` → reason | The column drops by 2 | ⚠ New 1.39.0. ⚠ **It asks HOW MANY, not the new total** — the ledger adds your number to the count. If a prompt ever asks for a total, stop and tell me. ⚠ From 1.40.0 the till accepts EITHER `pos.stock.adjust` OR `portal.stock.adjust`, mirroring the server — so **an Owner or Store Manager can do this WITHOUT the RBAC re-seed**. A **Supervisor** still needs `Plutus.SeedMigrator rbac` to have run |
| 5.5c | Try it as a **Cashier** | Refused politely | Deliberate — the person minding the shelf must not be the one who can alter its count |
| 5.5d | **Categories** button → create one, rename it, then try to **delete a category that has items** | The delete offers to **move the items first**, naming how many | ⚠ New 1.38.0. That refusal is the feature: the LEGACY delete cascades and would take every item in the category — and their sale lines and stock — with it |
| 5.5e | Tap a row → **Move to the Bin…** | Confirms, then the row leaves the list | ⚠ New 1.37.0. ⚠ It withdraws the item from sale on **every** till including offline ones — that is a recall, not a tidy-up. Restoring is portal-side for now |
| 5.6 | **Settings → Receipt printer** | Either the **agent's** version and printer name, or a plain message saying no Plutus Till Agent is running on this PC | ⚠ Rebuilt in 1.33.0. Matt saw *"Wireless is turned off"* — that was Windows' generic device picker complaining about RADIOS because no OPOS printer exists, not about the printer. The till now prints the way the **web till** does, through the agent |
| 5.6a | If an agent is running: **Pair this till** → type the code from its tray window | A test receipt comes out | ⚠ The code is per PC and never leaves it. A wrong code gives "the agent didn't accept that code", not silence |
| 5.6b | **Settings → Print test page** | Paper, or a message | ⚠ This used to do **nothing at all** on a till with no OPOS printer — no paper, no message, indistinguishable from a broken printer |
| 5.6c | **Statistics → Reprint a receipt** → pick the sale from step 3.5 | A copy prints, headed **"REPRINT — not a new sale"** | ⚠ New 1.34.0 (cutover step 26). ⚠ **The drawer must NOT open** — no money is moving. ⚠ The barcode must be the SAME as the original's, or the copy can't be used to find the sale, which is the whole point |
| 5.6d | Reprint a **refund** receipt too | Prints, headed with **both** REFUND and REPRINT | Refunds are reprintable even though they are not re-refundable |
| 5.7 | **Statistics** | *"Today — £x taken over n sales · VAT £x · average basket £x"*, from the PLATFORM | ⚠ The two legacy report buttons are GONE: they read the pre-Plutus database (always zero since cutover) and their Syncfusion charts are unlicensed. Matt is **not renewing** — as of 1.33.0 no Syncfusion control is on any screen you can reach. See `Build/syncfusion-footprint.md` |

## 5z. ⚠ The one worth testing with two people — disable an account mid-shift

New in **1.41.0**. Until now **nothing re-read the roster on any cadence**: it was fetched by the
Plutus tab's manual button, and by a login path that only fires when the roster is *empty*. So a
till left signed in through a shift never re-read permissions at all.

| # | Do | Expect | ⚠ If not |
|---|---|---|---|
| 5z.1 | Sign in on the till as a **test account** (not your own) | Normal | — |
| 5z.2 | In the **portal**, deactivate that account | — | — |
| 5z.3 | Wait up to **60 seconds**, watching the till | It signs itself out and shows **"Your account has been disabled, please speak to your manager"** | ⚠ Longer than ~65s = the roster isn't on the beat. Tell me |
| 5z.4 | Re-activate the account, sign back in | Works normally | — |
| 5z.5 | ⚠ **Now the safety case: pull the network cable while signed in** | ⚠ **NOTHING HAPPENS.** You stay signed in and can keep selling | ⚠⚠ **If the till signs you out when the line drops, STOP AND TELL ME IMMEDIATELY.** "Couldn't ask the server" must never be read as "you're disabled" — that would sign a whole shop out mid-sale on every broadband blip |
| 5z.6 | Change a **permission** in the portal (e.g. give the test account `pos.discount`) and wait 60s | The new permission works on the till without signing out and back in | Permission changes ride the same refresh |

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
1b. ⚠ **The inventory screen's new actions (5.5b–5.5e)** — stock adjust, categories and the Bin all
   landed within a few hours of each other, all reached through action sheets, all untouched by a
   person. ⚠ **The Bin and a stock write-off are both irreversible from the till**, so if a
   confirmation is worded ambiguously that is worth reporting even if it works.
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
