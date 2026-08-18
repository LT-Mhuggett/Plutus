# Test Maui — the MAUI till hand-test script

**For till 1.73.0.** Anyone can run this. You do not need to know the codebase, and you should not
need to ask anyone what a step means — if a step is unclear, that is a bug in this document, so please
say so.

**Time:** about **2½ hours** for everything now — this grew a lot between 1.54 and 1.72. About 15
minutes for §A alone, which is still the part worth doing if that is all the time you have.

---

## ⚠ RUN ORDER — do it in this order, not the order the sections are numbered

The sections grew by date, so the newest work is at the bottom. **These four come first**, because
each one can make everything after it look broken, and knowing they are clean tells you what a later
failure actually means.

| # | Do | Why first |
|---|---|---|
| **1** | **§G27 — a removed till must stop, a network blip must not** | Security behaviour with two halves that fail in **opposite** directions. Before 1.70.0 a revoked till kept selling for up to 12 hours |
| **2** | **§G24 — money on every basket row** | The basket's money changed underneath. If a £3.30 item shows **£330.00**, stop and report it; nothing else is worth testing until that is right |
| **3** | **§G30b — sign in after an upgrade with the network DOWN** | The roster moved stores in 1.72.0. If the import failed, this is a shop that cannot open — and it only shows up offline |
| **4** | **§A0 — can the till take a sale at all?** | It could not, on 1.48.0. Everything else assumes it can |
| **5** | **§A4b — refund a sale paid two ways** | The money one. Fixed across several builds and never yet run by a person |
| **6** | **§F7b — a percentage discount** | 1.72.0 money fix. Typing `10` used to try to take **£200** off a £20 item |
| **7** | **§G1 — does the bottom of the till screen still look right?** | A row was added to that grid and everything under it renumbered. MAUI bindings fail **silently** — a blank button means the renumber is wrong |

**Then the rest, in order:** §A → §B → §F (discounts) → §G (members, gift cards, reports, receipts)
→ §C (needs two people) → §E.

⚠ **Almost everything in §F and §G has NEVER been run by anyone.** It was built between 2026-08-14
and 2026-08-17 and is marked 🟡 — "built and tested where a test can reach, unverified on screen".
That is why this document exists.

⚠⚠ **The last hand-run findings were 2026-08-13** (fixed in 1.49.x–1.50.0). MAUI has gone from there
to **1.72.0** with nobody looking at a screen. For scale: the 2026-08-10 hand-run found **fourteen**
faults, **six of them invisible to every automated test in the project**; 2026-08-13 found five more,
including two money holes. **Every hand-run so far has found something the tests could not.**

⚠ **Write down anything odd even if no step asks about it.** Every one of the fourteen faults found
on 2026-08-11 came from somebody noticing something, not from a step asking the right question.

---

## Before you start

| | |
|---|---|
| **Run** | ✅ `D:\tmp\plutus-till-1.81.0\Plutus.Frontend.AppClient.exe` — just double-click it. Nothing to install. **BUILT 2026-08-18 and verified: the artefact reads `1.81.0+a5d571c6`, which is HEAD** — and the rebuilt **Store Information** screen is confirmed inside the binary (its four new strings are present; the deleted Region panel's `CurrencyDisplayArg` binding is gone). It is the **only** till build on the box — 1.73.0 was deleted so there is no question which to run. ⚠ **1.73.0 could not pass §W9d/§G33** — it is the build whose Store Information Matt photographed, with every field label light-grey on near-white. ⚠ It will say the till isn't enrolled; that is expected for an unpackaged build (runbook § MAUI till build) — enrol it as a fresh till. ⚠ **Nothing older.** Each of the builds before it fails a step in this document: **1.48.0** cannot take a sale (A0) · **1.49.0** crashes on a card overpay (A4) · **1.49.1** says nothing during a split payment (A5) · **1.49.2** lets a closed day take items from the item list (A8) · **1.49.3/1.50.0** let a split-paid refund go on one card (A4b) · **1.53.0 and earlier** let a discount be given with no reason recorded (§F). |
| **Agent** | ⚠ **Agent 1.4.0 is REQUIRED for §G29**, and it fixes "start automatically" not working after a reboot. Get it from the **web till → Settings → Hardware → Download the agent (v1.4.0)**, or from `tools\Plutus.TillAgent\publish-out\PlutusTillAgent-1.4.0.exe`. ⚠⚠ **Copy it to `%LOCALAPPDATA%\Plutus\Agent\` and run it from THERE — not from Downloads.** Auto-start records the path it was launched from; a Downloads copy gets cleaned up or renamed `… (1).exe`, and then the till boots and starts nothing. That is the fault this build fixes, and running it once from a permanent folder repairs a stale registration. |
| **Portal** | `https://admin.plutus.huggett.dscloud.me` |
| **Web till** (for comparing) | `https://plutus.huggett.dscloud.me` |
| **Sign in as** | any operator with **Supervisor** or above — some steps need permission to change stock |

**Press Plutus → "Re-download the whole catalogue" once before you begin.** An existing till only
receives *new fields* for items that change after it, so this forces the backfill. Skip it and some
rows will look wrong for reasons that are not bugs.

### How to record what you find

For each step, write down **what you did**, **what you expected**, **what happened**. That third one
is the valuable part — "it didn't work" cannot be fixed, "I pressed Confirm and the dialog closed
with nothing saved" can.

⚠ **Anything that surprises you is worth writing down even if this document does not ask about it.**
Every one of the fourteen faults found on 2026-08-11 came from someone noticing something odd, not
from a test asking the right question. Six of them were invisible to every automated test in the
project.

⚠ **If the app closes, that is always a bug.** Note what you were doing and carry on.

---

# §A — the reported faults, and whether they are really fixed

The specific fixes in **1.42.0–1.52.0**, from the hand-runs of 2026-08-11 and 2026-08-13. Each one was
reported by a real person using the till. **If you only have 15 minutes, do this section.**

⚠ **A0, A4, A4b, A5 and A8 are the newest** — all checkout or closed-day faults found on
2026-08-13, and A4 has broken the till twice in two different ways. **§E** covers three things from
2026-08-11 that still nobody has tested.

## A0. Take a payment — does the "Amounts" box actually appear?

**⚠⚠ DO THIS FIRST. On 1.48.0 the till could not take a sale at all**, so nothing past the checkout
has been tested on this build line.

1. Add any item to the basket. Press **Checkout**.
2. Choose **Cash**.

**✅ Expected:** the tender sheet closes and the **"How much…" amount box appears immediately**. Type
the amount, press Confirm, and the sale completes.

**Then, and this is the second half of the test:**

3. **Type something into the scan box** — a name like `BAT`, or a barcode — and press Enter.

**✅ Expected: it searches, exactly as it did before the sale.**

**❌ What was wrong on 1.48.0:** choosing Cash or Card made the tender sheet disappear and **nothing
else happened** — no amount box, no error, no crash. The till just sat there. And from that moment
**the scan box silently stopped searching**, because the stuck sale left the screen marked "busy" for
the rest of the session. Two symptoms, one cause: a dialog that waited for itself for ever.

⚠ **Also worth trying, since they use the same machinery:** a **card** payment, a **split payment**
across two tenders, a **refund**, and pressing **Cancel** at both prompts — the basket must survive
Cancel with nothing taken. And a discount, a category change, or the Settings printer picker: those
dialogs were all dead behind the same fault.

## A1. Edit an item — does the form arrive filled in?

**This is the most important step in the document.** It was reported twice and diagnosed wrongly
twice before it was fixed.

1. Go to **Inventory Management → View all items**.
2. Right-click any item (or press its **Edit** button).

**✅ Expected: ONE screen opens with everything on it** — Barcode (shown, not editable), Name, Brand,
Description, Cost, Price, a **Tax band dropdown**, a **Category dropdown**, and a **stock switch** —
with the item's current values **already filled in**. Change what you like, press **Save**.

**❌ What was wrong, three times over:** pressing Edit used to open a **"Tax band" question**, then a
Category question, then a Stock question, and only then a form — whose editable boxes rendered as
grey ghost text so they looked empty. It read as "I can only change the tax", which is exactly what
was reported. In 1.46.0 the boxes were filled in and the questions were numbered "step 1 of 4"; the
report came back **unchanged**, because numbering a wrong shape only tells you how far there is to go.

⚠ **Things worth trying here:** change **only the price** and save. Change the **tax band** and check
the list shows it. Press **Cancel** and confirm nothing changed.

⚠ **Adding a NEW item still uses the old question-then-form flow** (titled "New item — step 1 of 4").
That is known, not a new bug — say if it bothers you and it gets the same treatment.

## A1b. Change a price — does the VAT follow it? ⚠ NEW in 1.70.0

Same screen as A1. This is a **money** step: the ex-VAT price is what every VAT return is built from,
and you never type it — the till works it out from the band.

1. **Inventory Management → View all items**, edit any **standard-rated (20%)** item.
2. Set the **Price** to exactly **£10.00**, leave the band alone, **Save**.
3. Find the item in the portal (or re-open it) and look at its **ex-VAT price**.

**✅ Expected: £8.33.** Not £12.00, and not £10.00.

4. Now set the price to **£7.99** and save again. **✅ Expected: £6.66.**

**❌ If you see £12.00**, the till multiplied where it should have divided — stop and say so, because
every item saved since would be wrong by 44%.

⚠ **Why this is worth two minutes of your time:** free-typed ex-prices once corrupted **47 live
items** — a £7.99 item carrying a £799.00 ex price — and with them every VAT figure downstream. The
till no longer lets that number be typed at all, which is the actual fix; this step just confirms the
arithmetic that replaced it.

⚠ **One case you cannot easily test by hand**, so it is covered by tests instead: an item with **no
VAT band** whose stored prices are already nonsense. The till used to carry that nonsense into the new
price; it now refuses to, saves the item as VAT-free, and warns you that giving it a band is the fix.
If you ever do see that warning, the item it names genuinely needs a band.

## A2. Click into the Inventory search box

1. **Inventory Management → View all items**. Let the list finish loading.
2. Click into the **search box**. Do not type anything yet.
3. Now type `bat`.

**✅ Expected:** nothing dramatic. The list filters as you type.
**❌ The bug:** the app **closed** the instant you clicked into the box, before typing. Reproducible
every time.

## A3. Sell something, then add the same item again

1. Sell an item — anything. Complete the sale.
2. Now scan or search for **that same item** and add it to a new basket.

**✅ Expected:** it goes into the basket normally.
**❌ The bug:** it silently did nothing — no line, no error, and the scan box cleared as though it had
worked. It only ever affected the item you had *just* sold, which made it look like that item was
broken rather than the till.

## A4. Overpay by card, and underpay in cash

⚠⚠ **This step has now broken the till twice, in two different ways. It is the one to be fussy about.**

1. Put something in the basket — say £3.30.
2. Checkout → **Card** → type **20.00**.

**✅ Expected:** a message telling you a card cannot give change, then it **asks again** — and **your
basket is still there** throughout.

3. Type **3.30** and complete the sale.
4. Now try **Cash** → type **1.00** on a £3.30 basket.

**✅ Expected:** it tells you how much is still to pay and asks again. This is normal — see A5.

**❌ What was wrong, twice:** on 1.42.0 it said *"Something went wrong"* — not a wording choice, a
crash. On **1.49.0 the app closed outright**: the refusal message is a dialog, and it was being built
on a background thread, which Windows refuses. ⚠ **A normal sale was fine on that build** — only the
refusal path was affected, which is exactly why it survived to be found by hand.

⚠ **So the thing to watch for here is the app vanishing**, not the wording. If it closes, say what you
had typed and which tender you had chosen.

## A4b. ⚠⚠ Refund a sale that was paid TWO ways — the money one

**Fixed across 1.49.3–1.51.0 and never yet tested by a person. This is the one to be most careful about.**

1. Sell something for **£4.40**, paid **£2.00 cash + £2.40 card** (A5 does exactly this).
2. Now return that item. Choose **Card** and try to put the whole **4.40** back on it.

**✅ Expected:** it refuses, and the refusal says where the rest goes — *"That is more than card took on
this sale, so it cannot all go back that way. Refund what this method paid, then pick the other one for
the rest."* ⚠ **The amount box should already be filled in with 2.40**, not 4.40.

3. Accept **2.40** on the card, then pick **Cash** for the remaining **2.00**.

**✅ Expected:** the refund completes as one refund with two tenders, in the proportions the customer
actually paid.

⚠ **Also worth trying:** refund a **card-only** sale and see whether cash is offered at all (it should
not be), and refund a **cash-only** sale to cash (which should be entirely normal).

⚠ **Also try a sale rung up on ANOTHER till** if you have two enrolled — 1.51.0 asks the platform how it
was paid, so the same caps should apply. If instead the refund "succeeds" and then shows as quarantined
in the portal, say so — that is the fallback working and the cap not.

**❌ What was wrong up to 1.49.3:** both methods were offered — correctly — and **neither was capped**, so
the whole £4.40 could go back on the card: credited £2.40 more than it ever took, the £2.00 left in the
drawer, the till balancing, and no report anywhere disagreeing. ⚠ **The server did not catch it either.**

⚠ **Reported by Matt on 2026-08-13 (finding Y).** The shared rule, the server gate and the MAUI screen
all landed the same day; the web till still has no such restriction, and neither till can cap a
refund against ANOTHER till's sale yet.

## A5. Split a payment across two methods

**What you are really checking here is whether the screen TELLS you what it has taken.**

1. Basket of **£4.40**. Checkout → **Cash** → **2.00**.
2. It takes you back to the tender list. **Read the heading.**

**✅ Expected:** the heading says **`Paid £2.00 — £2.40 left to pay`**. The first time round it should
have read **`Payment Method — £4.40 to pay`**.

3. **Card** → **2.40**.

**✅ Expected:** the sale completes as **one** sale with **two** payments, and the receipt shows both.

**❌ What was wrong on 1.49.1:** the heading said only *"Payment Method"* — the same on the second pass
as on the first — so there was nothing to say the £2 had been taken or that £2.40 was left. It read
exactly like a till that had swallowed £2. ⚠ **The money was never lost** (a test has pinned that since
the tender loop was written); the screen simply never said so, which is its own fault and arguably a
worse one, because it invites taking the money twice.

⚠ **Try a THREE-way split too** — £2, £1, £1.40 — and check the running total is right at each step.
And try **Cancel** halfway through: nothing should be taken, and if you have already put cash in the
drawer, hand it back — a cancelled sale records nothing at all, deliberately.

## A6. Open a float and stay on the Cash tab

1. **Cash** tab → **Open float** → enter **150.00**.
2. **Stay on the Cash tab.** Do not navigate away. Watch the line for up to a minute.

**✅ Expected:** the line first reads **"(waiting to send)"**, then clears itself within about a
minute.
**❌ The bug:** it said "waiting" for ever. The money *had* been sent — only the screen was stale — and
the only way to find out was to leave the tab and come back.

## A7. Close the day with the WRONG money — the money test

1. On the same day: **Cash → Open float £150.00**, then **Paid out £20.00** (any reason).
2. So the drawer should hold **£130.00**. Deliberately count it wrong: **Z read → 110.00**.
3. Confirm. **Stay on the Cash tab** for up to a minute.

**✅ Expected:** the Z line turns **red** and reads something like
**"£20.00 SHORT — Plutus expected £130.00"**, and the summary at the top of the screen says the
drawer is £20.00 short.

4. Now open the **portal → Dashboard**.

**✅ Expected:** a **⚠ Drawers out of balance** pill showing £20.00 short. Clicking it opens Banking.

**❌ The bug:** the till said nothing at all. The platform had always known, and the person who
counted the drawer was never told.

⚠ **Then check the opposite:** on another day, count **more** than expected. It should say **OVER**.
An over drawer is not good news — it is a sale rung up wrong or money in the wrong till.

## A8. Try to sell after closing the day — by BOTH routes

1. With the day **Z-closed** (from A7), go to the **Till** tab and try to ring up a sale.

**✅ Expected:** it refuses **before taking any money**, with *"This day has been closed with a Z read…"*,
and your basket is left intact.

2. ⚠ **Now the second route, which is where it was still getting through:** go to **Inventory
Management → View all items**, pick any item and use **Add to till**.

**✅ Expected:** the same refusal. Nothing reaches the basket.

**❌ The bugs, in order:** first the sale went through entirely and the platform accepted it against a
day already counted and banked. Then the till refused a **scan** but happily accepted the same item from
the **item list** — the rule had been put on the door that was reported and not on the other door to the
same basket (Matt, 2026-08-13; fixed in 1.49.3).

⚠ **Also try a refund** against that closed day. It should refuse too.

⚠ **Known and not yet done:** the *"Add to till"* button is not greyed out — it refuses when pressed
rather than looking unavailable. Matt asked for the greyed-out version with "Till closed" beside it, and
that rides step 25's screen.

---

# §B — the rest of a shop day

Work through these in order; they mirror a real day.

## B1. Refunds go back the way they were paid

1. Sell something by **Card**. Complete it.
2. **Refund** it (Till → Returns → pick the sale from the list).

**✅ Expected:** the refund offers **Card only**. No cash option.
**Why it matters:** buy on a card and return for notes is the oldest till fraud there is, and it also
empties the drawer by accident.

3. Now sell something with a **split** (cash + card) and refund it.

**✅ Expected:** both methods are offered, because both were used.

## B2. Reprint a receipt

1. **Reporting → Reprint a receipt** → pick one of the last 20 sales.

**✅ Expected:** it prints, and the copy is clearly marked **"REPRINT — not a new sale"** above the
first line. The barcode is the **same** as the original's.
⚠ The drawer must **not** open — no money is moving.

## B3. Today's takings update while you watch

1. **Reporting** tab. Note the "Today" figure.
2. Ring up a sale, then come **back** to Reporting — or better, ring one up and **stay** on the tab.

**✅ Expected:** the figure moves within about a minute.
**❌ The bug (nobody reported this one):** it was read **once, when you signed in**, and never again.
A takings total that is hours stale looks exactly like a correct one — which is why it is worth
checking deliberately.

## B4. Stock

1. **Inventory Management → View all items.** Check the **Stock** column has numbers in it — not
   blanks. **∞** means "not tracked", **—** means "never counted". A blank is a bug.
2. Right-click an item → **Adjust stock…** → write off **1** with a reason.
3. Portal → **Inventory → Stock adjustments** (bottom of the page).

**✅ Expected:** your write-off is listed, with **your name**, the reason, and the quantity in red.

## B5. Move an item to the Bin — and get it back

1. Right-click an item → **Move to the Bin…**
2. Read the confirmation. It should say plainly that **nothing is deleted** and that it can be
   restored from the portal.
3. Confirm. The success message should be titled **"Done"** — not "Hmm".
4. Portal → **Inventory → Bin** → **Restore** it.

⚠ **There is no way to delete an item from a till, and there never should be** — deleting one would
take its sale history with it. The Bin is the reversible withdrawal-from-sale.

## B6. Categories

1. Inventory Management → **Manage categories**.
2. Try to **delete** a category that still has items in it.

**✅ Expected:** it does not just refuse — it **offers to move the items** somewhere else first, tells
you how many, and then lets you delete the empty category.

## B7. The tab names match the web till

**✅ Expected, left to right:** **Till · Cash · Inventory Management · Reporting · Store Information ·
Settings · Plutus**

Open the web till side by side and compare. The same person uses both, and two tills whose tabs read
differently are two products to be trained on.
⚠ **Loyalty is deliberately absent** on MAUI — it does not exist there yet, and an empty tab is worse
than a missing one. **Plutus** is extra: it is enrolment and diagnostics.

## B8. Printing

1. **Settings → Receipt printer.** Pair with the Plutus Till Agent (the tray app) using its code.
2. Print a test receipt.
3. Ring up a **cash** sale.

**✅ Expected:** the receipt prints, and the **drawer opens as the paper starts moving**.
⚠ **USER-VERIFY, never yet confirmed on hardware: does the drawer physically kick?** If it does not,
that is a genuine finding — please say so.

---

# §C — two people, ten minutes (worth doing once)

## C1. Disable an account mid-shift

⚠ **This is a safety case, not a feature.** You need a second person, or a second device.

1. Sign in on the till as a test operator. **Start putting items in a basket.**
2. On the **portal**, have someone disable that operator's account.
3. Wait up to a minute.

**✅ Expected:** the till signs that person out with:
**"Your account has been disabled, please speak to your manager"**
The basket is abandoned. Any sales already completed are untouched and still send.

## C2. ⚠ The one that matters most — pull the network cable

1. Sign in. **Unplug the network** (or turn off the Wi-Fi).
2. Wait two minutes. **Keep using the till.**

**✅ Expected: NOTHING HAPPENS.** You stay signed in and you can keep selling.

**❌ If the till signs you out when the line drops, stop and report it immediately.** That would mean
"couldn't ask the server" is being read as "you are disabled" — which would sign a whole shop out
mid-sale on every broadband blip. It is pinned by a test, but this step is what would catch it on real
hardware.

3. Ring up two or three sales while offline.
4. Plug the network back in. Wait a minute.

**✅ Expected:** the sales send on their own. **Plutus** tab shows the queue clearing.

---

# §D — what has NOT been built yet

**These are not bugs. Do not report them.** They are on the plan and each has a work package.

⚠ **REWRITTEN 2026-08-16.** Six things this list used to name are now BUILT — loyalty, gift cards,
store credit, customer attach, the fuller reports, and refunding another till's sale. Leaving them
here would have told you not to report them, so you would not have tested them. **They are now in
§F–§G and you should test them.**

| Not in MAUI yet | Where it is |
|---|---|
| Colour themes pushed from the portal | step 22 — **deliberately last**, see below |
| Users — add an employee, set a password | step 24 |
| Announcements, help tickets, update prompts | platform notices, no step yet |
| Un-enrolling a till from itself | step 21 |
| Restoring an item from the Bin **on the till** | portal-side, by design |
| Reopening a closed day **on the WEB till** | MAUI only for now |
| **Adding** a new item as one screen | still the old question-then-form flow; the EDIT screen is the new one |
| The old **Statistics** tab | **hidden on purpose** — it read the pre-cutover local database and showed £0.00 for everything since. Use **Reports** |

---

# When you are done

Write your findings up **in the order you hit them**, with the three lines each (did / expected /
happened). Send them over however is easiest — they get recorded in
[`To do/MAUI-retrofit.md`](To%20do/MAUI-retrofit.md) §1 and worked through from there. (Older runs live
in `archive/handrun-2026-08-11.md`.)

⚠ **If the app closes, the crash log is worth more than any description**, and it writes itself:

```
%LOCALAPPDATA%\User Name\com.MBH.NatApp.Plutus\Data\logs\plutus-till-<today>.log
```

(Yes, `User Name` is a literal folder — it comes from the app manifest.) **Attach it, or paste the last
20 lines.** That file identified both 2026-08-13 faults in minutes: one of them named the offending
thread outright. ⚠ **It is not shown anywhere in the app** — `CrashLog.Directory` exists to surface it
and nothing calls it, so this path is currently the only way to find it.

⚠ **Please include the things that worked**, not only the failures. "A7 said £20.00 SHORT in red,
correct" is what lets a fix be closed; without it, it stays open and gets re-tested for weeks.

⚠ **And say which version you ran.** It is the small grey line at the bottom of the **Plutus** tab,
which should read **`MAUI till v1.52.0`**. (It is also on the sign-in screen.) ⚠ **If it says anything
else, stop and say so** — 1.48.0 cannot take a sale and 1.49.0 crashes on a card overpay, so a run on
either of those will just re-find faults that are already fixed. Two of the fourteen findings on
2026-08-11 took much longer to settle than they needed to, partly because nobody could be certain
which build had produced them.

⚠ **Cross-check it against the portal.** In **Locations & Tills**, your till's row should show the
**same** number next to Active. If the portal says something different — or says **v0.0.0** — that is
worth reporting on its own: the web tills read v0.0.0 there until 2026-08-11, because they were built
on a machine where the version file could not be found and the build fell back to zero in silence.

---

# §E — added late, never yet tested by anyone

## E0. The three things you asked for on 2026-08-13 (till 1.52.0)

1. **A visible way to refund.** On the **Till** tab, look beside the scan box for **"↩ Return an item"**.
   Press it with nothing selected — it should tell you what to do. Then scan an item, tap its line, and
   press it again: you should land in the same Returns flow the right-click menu gives.
   ⚠ **This is not the web till's shape yet** — the web till starts from the *sale*, MAUI starts from the
   *basket*. Step 26 is where they converge. Say if the wording doesn't make that obvious.
2. **Opening hours.** **Store Information** should now list Monday–Sunday with times, or **Closed** for a
   day with none. ⚠ If every day says nothing at all, the hours are probably **unset** — check
   Portal → Locations & Tills → edit the store, then reopen the tab.
3. **Today's takings say when they were read.** On **Reporting**, the line should end **· as at HH:MM**.
   Ring a sale and watch: within a minute the figure and the time should both move.

## E0b. In the portal (1.8.0)

1. **Dashboard:** if any drawer is out of balance, that tile should now be **amber**, not grey like the
   others. ⚠ Amber deliberately, not red — it is something to look into, not a failure.
2. **Inventory:** there should be a **Stock adjustments** tab of its own, between *Stock ledger* and
   *Categories*. It is also still at the bottom of the ledger page, which is intended.

## The 2026-08-11 additions, still untested

## E1. ⚠ Reopen a closed day — the one that unblocked testing

1. **Cash → Z read**, count anything, confirm. The day is now closed.
2. Go to the **Till** tab and try to add an item.

**✅ Expected:** it refuses immediately with **"Till closed"** and tells you a supervisor can reopen
it. ⚠ It must refuse **before** you get a basket — the old behaviour let you build a basket, run the
whole payment flow, and fail at the last moment with no way out.

3. **Cash → "Reopen the day — reverse a Z read"**. Give a reason.

**✅ Expected:** the day reopens, and **the Z read is still listed** in the day's history. Both the
close and the reopening stay on the record with your name against them.

4. Add an item now — it should work normally.
5. Close the day again, then reopen again. Both should work.

⚠ **Sign in as a Cashier and check the reopen is REFUSED.** It is Supervisor and above deliberately:
the person who counted the drawer must not be the only one who can quietly un-count it.

⚠ **This is MAUI only.** The web till cannot reopen a day yet — known, and next on the list.

## E2. The item editor is now ONE screen (see A1)

Covered in A1 above, but worth repeating because it changed again after 1.46.0: pressing **Edit**
should open a single page with everything on it, not a series of questions.

⚠ **Adding a NEW item is still the old question-then-form flow.** Known. Not a bug report.

## E3. Last online, in the portal

**Locations & Tills** → your till's row.

**✅ Expected:** "never" until the till next beats, then a real time that updates. ⚠ Until today that
column showed the **enrolment date** for every till — so if it says something days old and your till
is running, that is worth reporting.

---

# F. Discounts — the reason, and who said yes (**NEW in 1.54.0**, still unrun on 1.54.1)

**Why this section exists.** Matt, 2026-08-13: *"All discounts need to be tracked — till, logged-in
employee and reason."* The till and the employee were already recorded. **The reason was recorded
nowhere**, on any till — and when a supervisor authorised a discount above a cashier's limit, their
name went into a log file **on that till and nowhere else**. Re-image the till and the answer to
*"who approved this?"* is gone.

⚠ **This section is the most likely place to find a new bug in 1.54.1**, because it adds a dialog to
a chain that already had two. Four separate faults came out of exactly this shape on 2026-08-10.

Put **a few items** in the basket first — say £8 worth. Then press **Alterations**.

## F1. A discount now asks WHY

Pick a discount, tick a line, enter an amount, confirm.

**✅ Expected:** a box asking **"Why is this discount being given?"** Type something — `damaged box` —
and confirm. The discount applies exactly as it always did.

## F2. An empty reason is REFUSED

Do F1 again, but leave the reason box **empty** and confirm.

**✅ Expected:** *"Every discount has to say why it was given. Try again and type a short reason."*
**Nothing comes off the basket.**

## F3. Spaces are not a reason

Do F1 again, and type **only spaces** into the reason box.

**✅ Expected: refused, exactly as F2.** ⚠ **This is the one most likely to be wrong** — a box that
accepts `"   "` looks like it is working and produces a report column full of blanks, which is worse
than no column at all because it looks answered.

## F4. Cancelling the reason leaves the basket alone

Do F1 again and press **Cancel** at the reason box.

**✅ Expected:** no discount, no error message, **basket unchanged**. You can carry on and complete
the sale normally.

## F5. Over your limit: the reason comes FIRST, then the supervisor

Sign in as a **Cashier** (someone whose discount limit is small — check the portal if unsure) and try
a discount **larger than that limit**.

**✅ Expected:** it asks **why first**, and *then* asks a supervisor to sign in.

⚠ **That order is deliberate, not accidental.** The supervisor should be approving *a reason*, not a
bare number. If the supervisor prompt comes first, that is a bug — report it.

**Then:** have a supervisor sign in and approve. The discount applies.

## F6. A supervisor cannot approve their own discount

At the supervisor prompt in F5, enter **the same person's** credentials as the operator who is signed
in.

**✅ Expected: refused** — *"A discount can't be authorised by the person giving it."*

## F7. The record actually leaves the till

Complete a discounted sale from F1 (and, if you can, one from F5 with a supervisor).

Then look the sale up — the **portal**, or the web till's sale history.

**✅ Expected:** the sale carries the reason you typed, and for the F5 one, **the supervisor's name**.

⚠ **This is the whole point of the section.** Everything above can pass while the record never leaves
the machine — which is exactly the state 1.53.0 was in. If the reason is on the screen but not on the
sale, say so.

## F7b. ⚠⚠ A PERCENTAGE discount — **the money fix, NEW in 1.72.0**

⚠⚠ **This is the one to do carefully.** Until 1.72.0 the box labelled **Percent** did the wrong
arithmetic: typing **10** for "10% off" multiplied the price **by ten**. A £20 item tried to take
**£200** off.

⚠ **Nobody was ever overcharged** — the money rule refuses a discount bigger than the basket, so it
was caught every time. But the operator was told *"that's more than the basket"* rather than *"you
typed the wrong number"*, and **no percentage discount could be applied at all**. If you ever tried a
percentage and gave up, this is why.

**You need a percentage discount set up** (`Type` = percent) in the till's discount list. ⚠ On a
portal-provisioned till the legacy discount list is empty and you will be told *"There are no
discounts set up for this till yet"* — that is a separate known gap, not this fix.

1. Put **one item at £20.00** in the basket.
2. Apply the **percentage** discount. In the box, type **10**.
3. Give a reason, confirm.

**✅ Expected: £2.00 comes off.** The basket reads **£18.00**.

**❌ If it refuses, saying the discount is more than the basket, the fix has not taken** — that is
exactly the old behaviour, because `£20 × 10` is £200.

4. Now check the **VAT**. On a standard-rated item, **Sale ex tax** must drop too — by about **£1.67**,
not by £2.00 and not by nothing.

⚠ **That fourth step is the one worth not skipping.** The inc and ex figures are discounted
separately, and the ex one is what every VAT return is built from — if it moves by the wrong amount,
the till and the web till will disagree on every VAT return for that basket, and nothing flags it.

**Then the edges:**

| Type | Expect |
|---|---|
| **100** | The whole £20 comes off. 100% is legitimate |
| **0** | Nothing comes off |
| **12.5** | £2.50 off — fractions are allowed |
| **10%** (with the sign) | Accepted — same as `10` |
| **150** | ⚠ **Refused, saying it isn't a percentage between 0 and 100** — NOT "more than the basket" |
| **-10** | ⚠ **Refused.** It must **not** quietly apply 10% off — the old code did |
| **abc** | Refused politely, basket untouched |

⚠ **And check what the box is PRE-FILLED with.** If your discount is set up as 10% in the list, the
box should open showing **10** — not `0.1`. The old table stored it as a fraction, so a box showing
`0.1` would mean somebody typing what they see gets a **0.1%** discount.

## F8. The things that must NOT have changed

These worked before and must still work — they are refused **before** the reason box, so you should
never see it:

| Do | Expect |
|---|---|
| Discount **more** than the basket is worth | Refused, naming the maximum. ⚠ **No reason box** |
| £5 off, then £5 off again on an £8 basket | Second refused: *"£5.00 is already off"* |
| Discount **equal** to the whole basket | **Allowed** — 100% off is legitimate |
| Discount on a basket holding **only returns** | Refused: nothing to discount |

⚠ **If a reason box appears for any row in this table, that is a bug** — it means the money rule
stopped running first, and an operator is being asked to justify a discount that can never apply.

## F9. ⚠ Receipt notes — new in 1.54.1, and easy to miss

The receipt's notes used to be copied into a legacy sale model at checkout and read back at print
time. In 1.54.1 they come straight off the basket. Same words, one hop instead of two — **but this
is a printed-output change, so it needs eyes.**

1. Add an item. Add an **operator note** to the basket if your till offers one.
2. Apply a **discount** (with a reason, per §F1).
3. Complete the sale and **take the receipt**.

**✅ Expected:** the receipt shows the note **and** the discount's label, in basket order, exactly as
it did on 1.53.0. No blank lines where a note used to be.

**❌ What to report:** a missing note, a missing discount line, an empty line, or the same note twice.

---

# G. Members and the tier discount — **NEW in 1.56.0**

**Why this section exists, and it is a money one.** The browser till has applied a member's tier
discount for months. The MAUI till could not — it had no way to attach a customer at all. So **a Gold
member has been charged 10% more on this till than on the web till for the same basket**. 1.56.0 is
the fix, and nobody has run it.

⚠ **This adds a bar to the top of the till's totals area and renumbers every row under it.** MAUI
bindings fail *silently* — a missed one renders **blank** rather than erroring — so **G1 is really a
check that the rest of the screen still works.**

You will need a customer in the portal with a **tier** (e.g. Gold at 10%). Set one up first if there
is not one.

## G1. ⚠ FIRST — does the bottom of the till screen still look right?

Open the **Till** tab and just look at it, before doing anything else.

**✅ Expected:** an **Add member** button, and below it the **Sale ex tax** / **Sale inc tax** figures,
the **Alter Transaction** / **Bag** / **Store Transaction** / **Checkout** buttons and **Cancel
Transaction** — all present, all with their normal text.

**❌ What to report:** any button with **blank text**, a missing total, two controls on top of each
other, or a button that has moved somewhere odd. That is the renumber being wrong, and it is the most
likely fault in this build.

## G2. Attach a member by searching

1. Press **Add member**. Type part of a name, or a phone number, or an email.
2. If more than one matches, pick one from the list.

**✅ Expected:** the button is replaced by the member's **name and tier** — e.g. `Jo Bloggs — Gold 10%`
— and a **Remove member** button. A message says how much came off the basket.

⚠ Put a few items in the basket **first**, so there is something to discount.

## G3. The discount actually appears, and it is right

With a member attached and, say, a £10 item in the basket:

**✅ Expected:** a line in the basket reading **`Gold 10%`** for **−£1.00**, and the **Sale inc tax**
total drops by exactly that.

⚠ **Check the arithmetic on an awkward basket:** three items at **£3.33** at 10% should come off as
**99p**, not £1.00. It is worked out per line, like the browser till.

## G4. Scan a membership card

Scan a member's card into the **scan box** (or type the `C…` number and press Enter).

**✅ Expected:** it attaches that member — it does **not** search for an item, and it does **not** say
"item not found". The box clears.

⚠ **Also try the same number from Inventory → Add to till**, if your till offers it. Both doors must
behave the same; they did not at first.

## G5. ⚠ The exclusions — this is the money-correctness step

| Do | Expect |
|---|---|
| Attach a member, then **discount one line by hand** | The member's 10% covers the OTHER lines only. **No stacking** on the hand-discounted one |
| Attach a member to a basket holding a **return** | The returned line gets **no** member discount |
| Attach a member, then pay by **card** (if your till charges a card fee) | The fee gets **no** member discount |
| Add more items **after** attaching | The `Gold 10%` line **updates** to cover them |
| **Remove** items after attaching | It updates again, and never goes negative |

⚠ **The fourth row is the one most likely to be wrong** — the discount is rebuilt every time the
basket changes, and a mistake there makes it silently collapse to nothing while the line still shows.

## G6. Remove the member

Press **Remove member**.

**✅ Expected:** the `Gold 10%` line disappears and the total goes back up. ⚠ **Any discount YOU
applied by hand must still be there** — only the member's comes off.

## G7. An expired membership

Attach a member whose membership has **expired** (set a past renewal date in the portal).

**✅ Expected:** the bar says so — `Jo Bloggs — Gold (expired, no discount)` — and **no discount is
applied**. ⚠ It must not say "Gold 10%" and then charge full price; that is the operator telling a
customer something the receipt contradicts.

## G8. Sell it, and check the receipt and the record

Complete a sale with a member attached.

**✅ Expected:** the receipt shows the `Gold 10%` line. Look the sale up in the portal afterwards —
the discount should be recorded against the lines it applied to, with the reason `Gold 10%`.

⚠ **Compare one basket against the web till** if you can — same member, same items. **The two totals
must match to the penny.** That is the whole point of this section.

## G9. Add a new member at the till — **NEW in 1.57.0**

Signed in as a **Cashier** (not a supervisor):

1. Press **Add member**. Fill in a name; email and phone are optional.

**✅ Expected:** the member is created, given a membership number, and **attached to the sale
straight away**. The message tells you the number, and says *"A supervisor can set their tier."*

⚠ **There must be NO tier picker in that dialog.** That is deliberate, and it is the browser till's
scar: creating a member and setting a tier are two different permissions, so offering both to a
cashier produced a member who WAS created followed by a "forbidden" error — and the natural retry
made a duplicate.

2. Try it with the till **offline** (pull the network).

**✅ Expected:** it refuses, saying the membership number comes from Plutus and **nothing has been
saved**. That is correct, not a bug — two offline tills would mint the same number.

## G10. Set a member's tier — Supervisor and up

1. Signed in as a **Cashier**, with a member attached: is there a **Set tier** button?

**✅ Expected: no.** A cashier does not see it at all.

2. Sign in as a **Supervisor**, attach a member, press **Set tier**, pick one.

**✅ Expected:** the tier is set, and the member's bar updates — e.g. `Jo Bloggs — Gold 10%`. If there
are items in the basket, **the discount appears immediately**.

⚠ If no tiers exist yet it should say they are created in the portal, rather than showing an empty
list.

## G11. Store credit as a payment — **NEW in 1.58.0**

You need a member with a **store-credit balance** (add one in the portal).

1. Put **£10** of items in the basket. Attach that member.
2. Press **Checkout**.

**✅ Expected:** the payment list now offers **Store credit** alongside Cash and Card — *only*
because a member with a balance is attached.

3. Choose **Store credit** and pay **part** of the sale, say £4. Then pay the rest by cash.

**✅ Expected:** the split works exactly like a cash/card split; the sale completes.

4. Check the member's balance in the portal.

**✅ Expected:** it has gone down by **exactly** what you spent, once.

## G12. ⚠ The store-credit refusals — these are the money ones

| Do | Expect |
|---|---|
| **Detach** the member, then checkout | **No** Store credit button at all |
| Attach a member whose balance is **£0** | **No** Store credit button |
| Try to spend **more** than the balance | Refused, and it says how much is actually there |
| Do a **refund** with a member attached | **No** Store credit button — a refund never goes back onto credit |
| Pull the **network**, then try to pay with credit | Refused, saying the till must be online. **Nothing taken**, basket intact |

⚠ **The last row matters most.** Store credit is spent on the server *before* the sale is recorded,
so if that fails the sale must not happen at all. Check the basket is still there and that **no sale
appears** in the portal afterwards.

## G13. Pay with a gift card — **NEW in 1.59.0**

You need an **active** gift card with a balance. Generate and sell one on the web till if you have
none — MAUI cannot sell them yet (that half is still being built).

1. Put items in the basket. **Scan the gift card** into the scan box.

**✅ Expected:** a message naming the card and its balance — *"K7QP-2M9W-XT4R-8 — £20.00 — take it as
payment at checkout."* ⚠ It must **not** add a line to the basket. Nothing is being sold.

2. **Checkout.**

**✅ Expected:** **Gift card** appears in the payment list. Pay part or all of the sale with it, and
the rest by cash if needed.

3. Check the card's balance afterwards (web till or portal).

**✅ Expected:** down by exactly what you spent, once.

## G14. ⚠ The gift-card refusals

| Do | Expect |
|---|---|
| Scan a card that has **not been sold** (off the rack) | *"That gift card hasn't been sold yet"* — and **no** Gift card button at checkout |
| Scan a **spent**, **expired** or **cancelled** card | It says which of those it is — not just "not valid" |
| Scan a **made-up** number | *"That gift card wasn't recognised"* — and it must **not** be searched for as a product |
| Try to spend **more** than the balance | Refused, with the server's own wording naming what is left |
| Do a **refund** with a card scanned | **No** Gift card button — a refund never goes back onto a card |
| Pull the **network** and try to pay by card | Refused, nothing taken, basket intact, **no sale in the portal** |

⚠ **The first row is the important one.** A card on the rack has a real number and scans perfectly.
Accepting it would hand over goods against money nobody ever paid.

⚠ **Also try a product barcode beginning with G.** It must be treated as a **product**, not sent off
as a gift-card lookup.

## G15. Sell a gift card — **NEW in 1.60.0**

You need an **unsold** card (generated in the portal, not yet sold).

1. **Scan it** into the scan box.

**✅ Expected:** it asks **"Amount to load"** — it knows the card has not been sold yet, so it assumes
you are selling it. ⚠ It must **not** say "that card hasn't been sold yet"; that message is for a
card someone tries to *pay* with.

2. Enter **£20** and confirm.

**✅ Expected:** a **Gift card** line appears in the basket at **£20.00**.

3. Complete the sale.

4. Now **scan the same card again**.

**✅ Expected:** it is now **active**, holding **£20.00**, offered as payment.

## G16. ⚠ Gift-card VAT — the one that cannot be seen on screen

This is the step that matters most and shows least. Sell a card as above, complete the sale, then
look the sale up in the **portal**.

**✅ Expected, if your voucher treatment is "multi"** (mixed VAT rates — the usual case):
the gift-card line declares **£0.00 VAT**. The card is stored value, not goods; the VAT is charged
later on whatever it is spent on.

**✅ Expected, if your treatment is "single"** (everything one VAT rate): the line declares VAT
**inside** the £20 — about £3.33 at 20% — and the customer still pays exactly £20.

**❌ What to report:** VAT declared on a "multi" card. That means the shop would pay VAT twice on the
same money — once when the card was sold, again when it was spent.

⚠ **If no voucher treatment has been set in the portal**, selling must be **refused** with a message
saying so. It must never guess: that choice decides which VAT period the money lands in.

## G17. A member discount must not touch a gift card

Attach a member with a tier discount, then put **a gift card and an ordinary item** in the basket.

**✅ Expected:** the discount comes off the **item only**. The gift card is **never** discounted —
selling £20 of spendable value for £18 hands over £20 of purchasing power, which then gets spent on
already-discounted goods.

## G18. The Loyalty tab — **NEW in 1.61.0**

There is a new **Loyalty** tab. It lists everyone who is a member **or** holds store credit.

1. Open it.

**✅ Expected:** a list of members showing **name · member number**, their **tier and rate**
(e.g. `Gold 10%`), and any **store-credit balance**. A count at the top.

2. Type a name, part of an email, or a **member number** into the search box and press Search.

**✅ Expected:** the list narrows. A member number should find exactly that person.

3. Find a member whose membership has **expired**.

**✅ Expected:** their tier reads **`Gold (expired)`** — not `Gold 10%`. ⚠ Showing a rate beside a
lapsed member is how an operator promises a discount the till will not give.

4. **Pull the network** and open the tab.

**✅ Expected:** it says members can only be listed while online. ⚠ It must **not** show an empty
list — that reads as "this shop has no members", which is a confident wrong statement.

⚠ **This tab is a LOOKUP.** You cannot create a tier here — tiers are made in the portal — and you
assign one from the **Till** tab with a member attached (§G10).

## G19. The table controls — **NEW in 1.62.0**, and they are new to this till entirely

⚠ **No MAUI screen has ever had a sortable or paged table.** The Loyalty tab is the first. If these
work here they get rolled out across reporting, so it is worth being fussy.

On the **Loyalty** tab, with a decent number of members:

| Do | Expect |
|---|---|
| Tap the **Member** header | Sorts by name. Tap again — reverses. The marker changes **⇅ → ▲ → ▼** |
| Tap the **Credit** header | Sorts by amount. ⚠ **£100 must come after £9**, not before — that is the whole point |
| Change **Show** to 50 or 100 | More rows per page, and it jumps back to page 1 |
| Press **Next** / **Prev** | Moves a page. The counter reads **"26–50 of 240"** |
| Get to the **last page** | **Next** goes grey — it must not vanish, or the row jumps about |
| Search (the box at the top) | Re-queries Plutus, and the table returns to **page 1** |

⚠ **The one most likely to be wrong is sorting by Credit.** If it puts £100 before £9 it is sorting
the text rather than the number.

⚠ **Also check "Till 2" sorts before "Till 10"** anywhere a numbered name appears. Plain sorting gets
that backwards and it is the most visible way a table looks broken.

## G20. The Reports tab — **NEW in 1.64.0**

⚠ **There is a new Reports tab, and the old Statistics tab is still there.** That is deliberate for
now: Statistics reads the *legacy local* database, so on a till migrated from NatApp it still shows
real pre-cutover history — but it shows **£0.00 for everything sold since the cutover**, because
sales stopped being written there. Reports reads Plutus and is the one to trust.

1. Open **Reports**. It opens on **Takings** for the **last 7 days**.

**✅ Expected:** real figures — the same ones the web till's Reporting page shows for that range.
A totals line above the table (orders and money).

2. Change the report in the picker: **Takings · VAT · Items sold · By category · Best sellers**.

**✅ Expected:** each loads. Two of them — **By category** and **Best sellers** — have never existed
on this till before.

3. Change the **from** / **to** dates.

**✅ Expected:** it reloads for the new range.

4. ⚠ **Set 'to' EARLIER than 'from'.**

**✅ Expected:** it says so. It must **not** show an empty table, which would read as "no trade".

5. Sort, search and page each report (see §G19).

**✅ Expected:** all four table behaviours work on **every** report, because they all use the same
table.

## G21. ⚠ Reports vs the web till — the figures must MATCH

Open the same date range on the **web till's Reporting page** and on **MAUI's Reports tab**.

| Check | Expect |
|---|---|
| **Takings** total for the range | The same to the penny |
| **VAT** totals | The same |
| **Items sold** total | The same |

⚠ **This is the whole point of the rebuild.** MAUI used to compute reports from its own local
database, so it could only ever show what *this* till had sold — and since the cutover, nothing at
all. Both surfaces now ask Plutus the same question.

⚠ **If "Items sold" shows a warning like "Showing 2,000 of 5,000 lines"** that is correct behaviour,
not a bug — the server caps the rows and the till now says so rather than showing a short total
silently.

## G22. Reprint a receipt from ANOTHER till — **NEW in 1.66.0**

This is the half a customer actually asks for: they bought it at the other counter and want their
receipt.

1. Ring up a sale on **till B** (or the web till).
2. On **till A**, go to reprint a receipt.

**✅ Expected:** the list shows **this till's** recent sales, and at the bottom
**"Sold on another till — look it up in Plutus…"**.

3. Choose that, and pick the sale you rang up on till B.

**✅ Expected:** it prints. The paper shows the right items, quantities, prices and totals, is marked
as a **copy**, and carries the sale's barcode.

⚠ **Check the figures against till B's original receipt** if you still have it — they must match.

4. ⚠ **Pull the network and try the same thing.**

**✅ Expected:** it says the sale isn't on this till and Plutus couldn't be reached. It must **not**
print a blank or half-empty receipt — a customer would take that as proof of a purchase nobody can
find.

⚠ **Your own till's sales must still reprint with the network down.** That is the common case and it
does not need Plutus at all.

## G23. The receipt now obeys the PORTAL — **NEW in 1.67.0**

Until now MAUI's receipt layout was hardcoded. It now uses the store's template from the portal, the
same one the web till uses.

1. In the **portal**, open the store's receipt template. Set a **header line** ("Kapow! Comics"), a
   **footer line** ("No refunds after 30 days"), and turn **Show VAT number** off.
2. Wait a minute on the till (it refreshes on the 60-second cycle), or restart it.
3. Sell something and print.

**✅ Expected:** the receipt shows your header at the top and your footer at the bottom, and has **no
VAT number**.

4. Turn **Show VAT number** back on and print again.

**✅ Expected:** the VAT number is back.

5. ⚠ **Clear the template's store name and address in the portal**, then print.

**✅ Expected:** it falls back to the **store's own** name and address — **not** a blank space where
they were. A receipt has to identify the trader.

6. Compare with a receipt printed from the **web till** for the same shop.

**✅ Expected:** the same header, footer, address and toggles. Same shop, same paper.

7. ⚠ **Pull the network and print.**

**✅ Expected:** it still uses the last template it saw — **not** a reverted hardcoded layout. A
customer's copy must not change shape because the broadband dropped.

⚠ **Reprints too** (§G22): a reprint and a cross-till reprint use the same layout as the original.

## G24. ⚠ MONEY ON EVERY BASKET ROW — **the most important check in 1.69.0**

The basket's money moved from decimal pounds to integer pence underneath. It is **designed** so this
cannot go wrong, and it is still the thing to check first, because if it HAS gone wrong every price
on screen is **100× too big** and nothing errors.

1. Add an item costing, say, **£3.30**.

**✅ Expected:** the row reads **£3.30**. ❌ **If it reads £330.00, stop and report it** — that is the
one failure this change could have caused.

2. Check the **Sale ex tax** and **Sale inc tax** totals at the bottom.

**✅ Expected:** sensible money, matching the rows.

3. Now check **each row type** shows the right price:

| Row | Check |
|---|---|
| An ordinary **item** | Its price |
| A **return** (goods going back) | Its price, and the totals go DOWN |
| A **note** (e.g. gift wrap) | No money, or its own |
| A **discount** | Shows as **negative** |

4. **Park** a basket with a few items and a discount, then **recall** it.

**✅ Expected:** every price is exactly what it was. ⚠ Parked baskets are serialised, so this is where
a money change shows up as corruption rather than as a wrong-looking number.

5. Complete a sale and check the **receipt** and the **portal** agree with the screen.

⚠ **If G24 is clean, the rest of §A and §F–§G are far less likely to surprise you.**

## G25. ⚠ The noticeboard — pick notes and platform announcements — **NEW in 1.70.0**

The web till has had this since Phase 6. MAUI had the rules and no screen, so **nothing on this till
has ever shown a pick-from-floor note.** A banner now sits at the **top of the Till tab**.

⚠ **It is on the selling screen on purpose.** A pick note means a web customer just bought something
that is physically on your shelf, and it is a race against somebody selling the last one over the
counter. On the Plutus tab it would be a notice nobody reads.

### G25a. A pick note arrives and can be acknowledged

1. In the portal (or the Woo site), cause a **web order** for an item that is in shop-floor stock.
2. Watch the **Till tab**. Within **60 seconds**, a blue bar appears at the top:
   **🛒 Pick from the shop floor** with the order details and a **Done — acknowledged** button.
3. Press **Done — acknowledged**.

**✅ Expected:** the bar disappears **immediately** — not in a minute's time — and does not come back
on later polls. Check the web till: the same note should be gone there too.

**❌ If it reappears a minute later**, the acknowledgement did not reach Plutus. Say so.

### G25b. It must not show OTHER shops' notes

If you have more than one store: cause a web order fulfilled by a **different** store.

**✅ Expected: nothing appears on this till.** ⚠ This one is worth doing because getting it wrong is
invisible — staff go hunting for stock that was never on their shelves, and the shop that really has
it assumes someone else dealt with it.

### G25c. An announcement shows, and cannot be dismissed

1. In the portal, publish an **Incident** announcement, then a **Maintenance** one.

**✅ Expected:** within 60 seconds a **red** bar (⛔ incident) and an **orange** bar (🛠 maintenance)
appear above any pick notes, with **no Done button** — announcements are not yours to dismiss, the
server decides when they stop.

2. Publish an **Info** announcement.

**✅ Expected: it does NOT appear on the till.** Info is portal-only — the banner interrupts someone
mid-transaction, and filling it with release notes teaches people to ignore the incident too.

3. Compare against the **web till**: the same three should behave identically. ⚠ They did **not**
before 1.70.0 — the web till used a different rule and would silently hide any severity it had not
been taught, which is why this comparison is here.

### G25d. Pull the network cable while a notice is showing

With a notice on screen, **unplug the network**.

**✅ Expected: the notice STAYS**, and a small grey line appears under it —
*"⚠ Plutus can't be reached — these notices may be out of date."*

**❌ If the banner vanishes, that is a bug**, and an important one: an incident notice explaining why
the card machine is failing must not disappear the moment the line drops. Plug back in — within 60
seconds the grey line goes and the notice is current again.

⚠ **And check the empty case:** with no notices at all, the top of the Till tab should look exactly
as it did in 1.69.0 — no empty strip, no grey warning line.

## G26. ⚠ Help and support — ask Plutus, and read the reply — **NEW in 1.70.0**

⚠ **Before 1.70.0 the MAUI till had no route to support at all.** A shop on this till could not raise
a ticket from the till, and could not read a reply.

Go to **Settings**. Under **Help**, press **Help and support**.

### G26a. Raise a ticket

1. Choose **Raise a new ticket**.
2. Fill in **What's it about?** and **What's happening?**, press **Send to Plutus**.
3. Answer the urgency question — **No — it can wait**.

**✅ Expected:** *"Plutus has your ticket."*

4. Check the **portal's** support screen: the ticket is there, severity **Problem**.
5. Now raise a second one and answer **Yes — we can't trade**.

**✅ Expected:** that one arrives as **Urgent**.

⚠ **Try sending with the description empty.** It must refuse and say a ticket needs both — a subject
alone makes somebody at Plutus ask what the problem is, which costs you another day.

### G26b. Read a reply and answer it

1. From the **portal**, reply to one of the tickets.
2. Back on the till: **Settings → Help and support**.

**✅ Expected:** the ticket list shows each ticket **with its status on the line** — *Open*, *Waiting
on you*, *Closed*. Tap it.

**✅ Expected:** the conversation reads **oldest first**, and you can tell **who said what** — your
name against yours, **Plutus** against theirs.

3. Press **Reply**, type something, **Send reply**.

**✅ Expected:** *"Plutus has your reply."* — and it appears in the portal.

### G26c. A closed ticket takes no reply

Close a ticket in the portal, then open it on the till.

**✅ Expected:** it reads *"This ticket is closed. Raise a new one if you still need help."* and there
is **no Reply button** — rather than a Send that the server would refuse.

### G26d. ⚠ With the network unplugged

Unplug the network and press **Help and support**.

**✅ Expected:** it says Plutus can't be reached and that **nothing has been sent**.

**❌ If it ever says a ticket was sent while offline, that is a serious bug.** Tickets are deliberately
NOT queued: a shop that believes it has reached support is worse off than one that knows it has not.

## G27. ⚠⚠ A removed till must stop — and a REQUEST must not — **NEW in 1.70.0**

⚠⚠ **This is a security step, and it is the most important one in 1.70.0.** Until now, revoking a
till in the portal did **not** stop it: device tokens have no server-side denylist, so a lost or
stolen till carried on selling for **up to 12 hours**. Nothing on the till ever asked.

⚠ **Both halves matter, and the second one more.** A till that stops when it shouldn't is a shop
that can't trade.

### G27a. Ask for this till to be removed

1. **Plutus tab** → **Ask for this till to be removed** → confirm.

**✅ Expected:** *"Plutus has your request. A manager approves it in the portal — this till keeps
working until they do."*

2. **Now go and sell something.**

**✅ Expected: the sale goes through completely normally.** The till is `PendingRemoval`, which is
**not** a stop signal.

**❌ If the till stops trading here, that is a serious bug** — it would mean anybody able to press
that button can close a shop.

3. Check the **portal**: the till appears in the removal queue.
4. **Reject** the removal in the portal. Within a minute the Plutus tab shows the till Active again.

### G27b. ⚠⚠ Now actually revoke it

1. In the portal, **approve** the removal (or revoke the device directly).
2. **Leave the till alone and watch it — do not touch the Plutus tab.**

**✅ Expected: within about a minute the till signs itself out on its own**, showing:
*"This till has been removed in Plutus and can no longer be used. Speak to your manager — it can be
re-enrolled from the portal."*

3. Try to sign in again.

**✅ Expected: it refuses.**

**❌ If the till keeps selling, say so immediately** — that is the whole point of this step, and it
is exactly what happened on every build before 1.70.0.

⚠ **Re-enrol it from the portal afterwards** so the till is usable for the rest of your testing.

### G27c. ⚠ And the case that must NOT stop it — pull the network cable

With the till working normally, **unplug the network** and leave it for a few minutes.

**✅ Expected: the till carries on completely normally.** It may say it can't reach Plutus; it must
**never** sign itself out or claim it has been removed.

**❌ If a network drop ever signs the till out, that is a worse bug than the one G27b tests** — it
would close a shop every time its broadband hiccuped, and it would hit the worst-connected shops
first.

## G28. ⚠ Users — add somebody, and set a password — **NEW in 1.70.0**

⚠ **Before 1.70.0 the people icon said "User management is not available in this version yet"** — and
before that it closed the app. This is the whole screen.

On the **login screen**, press the **people icon** (top corner). That is where the web till keeps it
too, and it is the only place it is any use: the person who needs it is standing at a till nobody can
get into.

### G28a. See who works here

**✅ Expected:** a list of your staff — name and email — with **Add somebody** at the top.

⚠ **People who have left are marked `(left)` and sit at the bottom.** Check one is. Without that mark
somebody sets a password for a person who cannot sign in and blames the password.

### G28b. Add somebody, and have them sign in

1. **Add somebody.** Fill in first name, last name, email, and a password **twice**.
2. **✅ Expected:** *"…can now sign in on any till."*
3. **Sign in as them on THIS till.** ✅ It works.
4. ⚠⚠ **Now sign in as them on the WEB till.** ✅ It works there too — same person, same password.

**That fourth step is the real test.** One staff list across both tills is the point of the whole
retrofit; if they can only sign in on one, say so.

⚠ **Things worth trying:**
- Type a **name** into the email box → it must refuse, and say the email is what they sign in with.
- Type **two different passwords** → ✅ it must say *"Those two passwords don't match"*, **not** that
  the password is too short. (Being told the wrong thing here makes people lengthen a password that
  was never the problem.)
- Type a **short** password → it must say how many characters are needed.

### G28c. Reset an existing person's password

1. Pick somebody from the list. Enter a new password twice.
2. **✅ Expected:** *"Done. … uses the new password next time they sign in."*
3. Sign in as them with the **new** password. ✅ Works.
4. Check the **old** password no longer works.

⚠ **"Next time they sign in" is exact.** Somebody already signed in on another till **stays** signed
in — their session is a token that does not get cancelled. That is expected, not a bug.

### G28d. What is deliberately NOT here

**Roles and permissions are not on the till, and that is on purpose.** They live in the portal, where
the change is audited and whoever makes it can see the whole estate. If you expected to set someone's
role here, say so — but it is a decision, not an omission.

⚠ **If your business has more than 100 staff**, the till shows the first 100 and **says so**. Check
you get that warning rather than a silently short list.

## G29. The portal should now know which agent this till has — **NEW in 1.71.0**

⚠ **Before 1.71.0 every MAUI till read "agent unknown" on the portal's Locations page**, beside
browser tills that reported properly — which looks like a missing agent rather than missing
reporting. MAUI knew the version all along and never said.

1. With the till running and paired to an agent, wait **a minute or two**.
2. Open the **portal → Locations** and find this till.

**✅ Expected:** it shows the **agent version** and the **printer name**, with a recent "reported at"
— the same as a browser till.

3. **Turn the receipt printer off.** Within a couple of minutes the portal should show the printer as
   **offline**.
4. Turn it back on. It should go healthy again.

⚠ **It does not report every minute, deliberately** — only when something **changes**, or every **6
hours** to confirm it is still alive. So don't expect the "reported at" time to tick over constantly;
expect it to jump when you change something.

⚠ **A till with no agent at all should also show** — as "no agent", not as blank. That is a fact about
the shop's kit, not a gap.

## G30. ⚠⚠ The roster moved into the till database — **NEW in 1.72.0**

The list of who may sign in used to live in a JSON file beside the database. It now lives **in** the
till database. Two roster stores was drift by construction: one of them is always the stale one, and
which one wins depended on which code path ran last.

⚠⚠ **The dangerous case is an upgrade with the network DOWN**, because the cached roster *is* offline
sign-in. If the upgrade lost it, the shop would open to a till nobody can get into — and no way to
fetch a new roster, because fetching needs the network it hasn't got.

### G30a. Sign in normally after upgrading

1. On a till that has been used before (so it has a roster), run **1.72.0**.
2. Sign in as usual — **by email**.

**✅ Expected: it just works**, first time, with no re-sync.

⚠ **Email specifically.** The old file kept an email against each person; the natural place to put a
roster in the database has no email column, so this step is checking the move did not quietly cost
you email sign-in. If you can only get in with a user id, say so.

### G30b. ⚠⚠ The one that matters — upgrade OFFLINE

1. Sign in on the older build once, so the roster is cached.
2. **Unplug the network.**
3. Close the till and start **1.72.0** — still offline.
4. Sign in.

**✅ Expected: you get in.** The new build finds no roster in the database, notices the old file, and
adopts it.

**❌ If it says the till has no staff on it, stop and report it** — that is a shop that cannot open,
and it is the whole reason this step exists.

### G30c. Nothing was thrown away

The old `operators.json` is **deliberately left in place** — it is the only roster an offline upgrade
can read, so it stays until every till has run 1.72.0 online at least once. You should not be able to
tell from the till; this note is here so nobody "tidies" it away.

⚠ **After a "Forget this till"**, both the database roster and the old file are cleared — check that
signing in afterwards is refused, or a forgotten till would still hold the staff list of the till it
used to be.

## G31. ⚠⚠ The portal's theme — and it must MATCH the web till — **NEW in 1.73.0**

⚠ **A till does not choose its colours; it is told them.** There is deliberately no theme picker on the
till: an operator changing it locally would make the portal's assignment a suggestion, and two tills in
one shop would stop matching for reasons nobody could see from the portal.

⚠⚠ **Matt's requirement is the point of this step: the same assignment must look the same on the web
till and on MAUI.** So test them side by side, not one after the other.

### G31a. Assign a scheme and watch both tills

1. In the **portal → Locations**, assign a colour scheme to **this store**.
2. Open the **web till** and the **MAUI till** next to each other.
3. Within **60 seconds** both should change.

**✅ Expected: the same colours on both** — the band/buttons, the page background, the card and dialog
backgrounds, the text, and the borders.

**❌ If they differ, say which is which and photograph both.** That is the exact failure this step
exists to catch, and it means the two tills read the portal's blob differently.

### G31b. The resolution order — a till override beats the store

1. Assign a **different** scheme to **this till specifically**.

**✅ Expected: the till-level scheme wins** on both tills, within a minute.

2. **Clear** the till-level override.

**✅ Expected:** both fall back to the **store's** scheme.

3. Clear **every** override.

**✅ Expected: both return to the stock Plutus colours** — a blue-ish accent (`#2c698d`), light
background. ⚠ Not "whatever was set last" — this is the check that the fallback is real.

### G31c. Light and dark

Set the scheme's base mode to **dark**, then **light**, then **system**.

**✅ Expected:** dark and light force themselves on both tills; **system** follows the device's own
setting. Change Windows to dark mode with the theme on "system" and the till should follow.

### G31d. ⚠ Colours survive a restart with the network off

1. With a scheme assigned, **unplug the network**.
2. Close the MAUI till and start it again.

**✅ Expected: it opens in the shop's colours**, not the stock palette — and **without flashing** the
stock palette first.

**❌ If it opens stock-coloured, the cached theme is not being applied** — which on a themed estate
looks like the assignment stopped working.

### G31e. ⚠⚠ THE RECEIPT MUST IGNORE THE THEME

With a **dark** scheme applied, **print a receipt**.

**✅ Expected: normal black-on-white paper.**

**❌ If the print comes out faint or near-white, stop and report it.** Printing from a dark scheme once
put near-white ink on paper — receipts are deliberately immune to theming, and this is the check.

## G32. Show a receipt on screen — **NEW in 1.73.0**

For a customer who wants to see what they were charged when paper is not an option.

1. **Unset the receipt printer** (Settings → Receipt printer), or unplug it.
2. Reprint a past receipt (Plutus tab → reprint, or the sale picker).

**✅ Expected:** instead of the old dead end, it offers **"Show on screen"** — and shows the receipt:
shop name, the lines, the total, the tenders, and the sale number.

3. Set the printer back up and reprint with the **printer switched off** so the print fails.

**✅ Expected:** the failure message also offers **"Show on screen"**.

⚠ **Check the figures against the paper copy if you have one.** The screen renders the *same document*
the printer gets, so the totals must be identical. If they are not, that is serious — say so.

⚠ **Two things it is NOT, by design:** the columns will not line up perfectly (it is not a monospaced
screen — the figures are the point), and it does not show bold or double-height, because a thermal
printer's emphasis has no honest text equivalent. Neither is a bug; report anything else.

---

# §W — WEB till checks

⚠ **These need the DEPLOYED WEB TILL**, not the MAUI build — `https://plutus.huggett.dscloud.me`.
They are here rather than in a separate document because several of them are **side-by-side
comparisons** with MAUI, and this is the document that already does that (§G25, §G31).

⚠ **Parity runs both ways.** Everything in §W is something MAUI could already do and the browser till
could not. Matt, 2026-08-08: *"The tills need to be in parity."*

## W1. ⚠⚠ A revoked web till must STOP — and a network blip must not stop it — **NEW in web 1.11.0**

⚠⚠ **Before this, revoking a browser till did nothing.** Its whole idea of "connected" was the
browser's `navigator.onLine` — the **network cable**, not the server — so a lost or stolen web till
carried on selling for up to **12 hours**, and it could not tell a revoked till from a dead one.

### W1a. Revoke it, and watch it stop on its own

1. Sign in to the **web till** and leave it sitting on the Till tab. **Do not touch it.**
2. In the **portal**, revoke that till's device (or approve a removal request for it).
3. Wait up to **a minute**.

**✅ Expected: the till blocks itself, with no reload**, showing:
*"This till has been removed in Plutus and can no longer be used. Speak to your manager — it can be
re-enrolled from the portal."*

4. **Press F5.**

**✅ Expected: it is STILL blocked.** ⚠ That is the point — a revoked till must not come back by
reloading. It must not offer a login box either.

5. Re-enrol the till in the portal, then press **Check again**.

**✅ Expected:** it lets you back in.

### W1b. ⚠⚠ And the case that must NOT block it — pull the network

With the web till working normally, **unplug the network** (or switch the Wi-Fi off) and leave it for
**five minutes**. Keep using it — add items to a basket.

**✅ Expected: nothing happens.** It may say it is offline; it must **never** block or claim it has
been removed.

**❌ If a network drop blocks the till, stop and report it** — that is a worse fault than the one W1a
tests, because it would close a shop every time its broadband hiccuped, and it would hit the
worst-connected shops first.

### W1c. A removal REQUEST must not block it

1. From the **portal**, raise (but do **not** approve) a removal request for this till.
2. Use the till — ring up a sale.

**✅ Expected: it keeps trading completely normally.**

**❌ If it blocks, that is serious**: anyone able to request a removal could close a shop.

### W1d. Compare against MAUI

Do W1a on the **MAUI** till too (that is §G27). **✅ Both tills should behave identically** — same
wording, same "still blocked after a restart", same "a network drop changes nothing".

## W2. ⚠⚠ A disabled operator is signed OUT of the web till — **NEW in web 1.11.0**

⚠⚠ **Before this the web till only noticed when something happened to 401** — and login tokens are
cached for **12 hours** carrying their permissions. So a disabled operator kept a working browser till
for the rest of the day. MAUI has put them out inside 60 seconds since till 1.41.0.

⚠ The server already refuses a deactivated employee at **login**. What this tests is the person who
was **already signed in** when they were disabled — the case that actually happens.

### W2a. Disable them mid-session

1. Sign in to the **web till** as an ordinary operator. Leave it on the Till tab.
2. In the **portal**, deactivate that employee.
3. Wait up to **a minute**. **Do not touch the till.**

**✅ Expected:** the till says *"Your account has been disabled, please speak to your manager"* and
returns to the login screen.

⚠ **Check the wording.** It must name the **account**, not the till — *"you have been logged out"*
sends somebody to reboot the machine instead of to their manager. It should read identically to MAUI
(§C1).

4. Try to sign in as them again. **✅ Expected: refused.**

### W2b. ⚠⚠ And the case that must NOT sign anybody out — pull the network

1. Sign in. **Unplug the network.**
2. Leave it for **five minutes**, using the till — add items, park a basket.

**✅ Expected: nothing happens.** You stay signed in.

**❌ If a network drop signs you out — especially with the "account has been disabled" message — stop
and report it immediately.** That is a worse fault than the one W2a tests: it would sign out every
shop whose broadband hiccups, blame the operator's account for it, and hit the worst-connected shops
first.

### W2c. Nobody is dropped when the roster is simply empty of *others*

Deactivate a **different** employee (not the one signed in). **✅ Expected: your session is untouched.**

## W3. Compare W1 and W2 against MAUI, side by side

Worth ten minutes: run §G27 (revoked till) and the disabled-operator check on **MAUI** with the web
till open beside it. **✅ Both should behave the same way, in the same wording, at the same speed.**
Anything that differs is a parity bug even if both behaviours look reasonable on their own.

## W4. ⚠⚠ A cashier's discount LIMIT, and the supervisor step-up — **NEW in web 1.11.0**

⚠⚠ **Before this the web till had no permission model at all.** A cashier on the browser till could
take **any amount** off a basket — the only thing in the way was the server at ingest. MAUI has gated
this since 2026-08-14. Matt, 2026-08-14: *"Base it on roles"* — a discount level **IS** a role's
`pos.discount` limit.

**Set up first:** in the portal, give a **Cashier** role a `pos.discount` limit of **£5**, and make
sure a **Supervisor** account has a higher limit (or none).

### W4a. Inside the limit — nothing should change

Sign in as the **Cashier**. Put a £20 item in the basket and take **£3** off with a reason.

**✅ Expected: it applies exactly as before.** ⚠ If an ordinary small discount has become harder, that
is a bug — the gate should be invisible until it bites.

### W4b. Over the limit — refused, and offered a supervisor

Now take **£10** off.

**✅ Expected:** it says that is over your limit, **names your limit (£5.00)**, and offers *"Get a
supervisor to authorise…"*. The Apply button stays disabled.

### W4c. The supervisor authorises

1. Press **Get a supervisor to authorise…**
2. Enter the **Supervisor's** email and password. ⚠ The password box must be **masked** — a
   supervisor's password must not be readable over the shoulder of the cashier whose discount it is.
3. **Authorise.**

**✅ Expected:** it confirms the discount is authorised, and Apply becomes available.

4. Complete the sale, then look at it in the **portal**.

**✅ Expected: the supervisor's name/id is recorded against the discount**, alongside the reason and
the requesting cashier. ⚠⚠ **This is the point of the whole step** — a discount over a limit with
nobody's signature on it is exactly what binding default 22(b) exists to prevent.

### W4d. ⚠⚠ The refusals that matter

| Try | ✅ Expected |
|---|---|
| The **cashier** authorising their own over-limit discount (their own email + password) | **Refused** — *"You cannot authorise your own discount"*. ⚠ An operator who can sign for their own discount has no limit at all |
| A supervisor's email with the **wrong password** | Refused, and no discount applied |
| An email **not on this till** | Refused |
| A **second cashier** (also limited to £5) authorising the £10 | ⚠ **Refused** — the authoriser must clear the amount themselves, or "step up" becomes "ask anyone at all" |

### W4e. An operator with NO discount permission

Remove `pos.discount` from the Cashier role entirely and sign in again. Open the discount dialog.

**✅ Expected:** it says the account isn't allowed to give discounts — ⚠ **a sentence, not a dead
button**.

### W4f. ⚠ It must work with the network down

Sign in, then **unplug the network**, then try an over-limit discount.

**✅ Expected: the limit still applies and the step-up still works** — the gate reads the cached
roster, exactly as MAUI does. ⚠ If limits vanish when the line drops, that is the worst possible
failure of this feature: it would mean unplugging the network removes every discount limit in the shop.

## W5. ⚠⚠ Sign in to the web till with the network DOWN — **NEW in web 1.11.0**

⚠⚠ **The web till could not do this at all.** So the shop that lost its broadband lost its till — the
exact outage the whole offline design exists to prevent. MAUI has had it since WP8.

⚠ **The shape to expect, and it is deliberate:** selling stays alive for a long time, money-out expires
quickly, and **the till never hard-locks**.

### W5a. The basics

1. Sign in to the web till **online** once (this downloads the staff list).
2. **Unplug the network.**
3. **Reload the page** and sign in with the same email and password.

**✅ Expected: you get in.** Ring up a sale — it queues, exactly as it did before.

4. Try a **deliberately wrong password**. **✅ Expected: "Wrong password."** — and you stay out.
5. Try an email that is **not** on this till. **✅ Expected:** a different message, saying no account
   matches. ⚠ Two different sentences matter: *"wrong password"* for both once sent somebody hunting a
   typo that did not exist.

### W5b. ⚠⚠ THE SECURITY ONE — a disabled operator must NOT get in offline

1. **Online**, deactivate an operator in the portal.
2. Still **online**, try to sign in as them. **✅ Expected: refused** by the server.
3. Now **unplug the network** and try again with the same correct password.

**✅ Expected: still refused.**

**❌ If they get in offline, stop and report it immediately.** That would mean a disabled account can
sign in past its own refusal simply by pulling a cable — the till must only fall back to its cached
staff list when it could not **reach** the server, never when the server **said no**.

### W5c. What a stale till loses, and what it keeps

You cannot easily age a till by a week by hand, so this is covered by tests — but if you ever see a
till that has been off the network for a while, this is what should happen:

| Offline for | ✅ Expected |
|---|---|
| under 3 days | normal, no warning |
| **3–7 days** | signs in, **warns** that refunds and manager functions stop after 7 days |
| **7–30 days** | ⚠ **still sells normally**; refunds, cash out and manager functions are withdrawn |
| **over 30 days** | offline sign-in refused, pointing at a temporary code |

⚠ **The till never locks you out of SELLING inside 30 days.** If a stale till ever refuses to sell,
that is a bug — it would close a shop over a connection problem.

### W5d. The session ends at the end of the business day

Sign in late in the evening (say after 22:00). **✅ Expected:** the session ends at **midnight**, not 12
hours later. ⚠ A session spanning two business days puts yesterday's operator on today's X/Z
breakdown, and after a shift change attributes the new person's sales to the old one.

## W6. ⚠⚠ Cash with the network down, and reopening a closed day — **NEW in web 1.11.0**

⚠⚠ **The web till posted cash online-only.** A float taken while the line was down was simply lost —
and **a shop opens before its broadband does.** The money moves whether or not Plutus hears about it,
so a float that failed to post is a day whose banking cannot be reconciled at all. MAUI has queued
these since till 1.44.0.

### W6a. Take a float with the cable out

1. **Unplug the network.** Go to **Cash**.
2. Open a float of **£50**.

**✅ Expected:** it is accepted, and says it is **recorded on this till and waiting to send** — ⚠ NOT an
error. If it reports a failure the operator will record it again, and the day will be £50 out.

3. Record a **paid out** of £5 with a reason. **✅ Expected:** also queued.
4. **Plug the network back in.**

**✅ Expected:** within a minute the "waiting to send" count clears on its own. Check the **portal** —
both events are there, with their original times.

### W6b. ⚠⚠ A Z close waits for that day's sales

1. **Unplug the network.** Ring up **two sales** (they queue).
2. Still offline, do a **Z close**, counting the drawer correctly.
3. **Plug back in** and watch.

**✅ Expected:** the sales go first, **then** the Z — and the variance is **correct**.

**❌ If the Z reports you short by the value of those two sales, stop and report it.** That is the
failure this rule exists for: the expected drawer is float + **cash takings** + ins − outs, so a Z that
overtakes its own sales accuses the person who counted correctly. ⚠ And because a Z is final on the
server, it would then reject those sales — putting the day's real takings into quarantine behind their
own close.

### W6c. Nothing else goes on a closed day

After a Z (online), try to record a **paid in**.

**✅ Expected: refused**, saying the drawer is closed for today — ⚠ and refused **at the counter**, not
accepted-then-rejected tomorrow. The Z button should be unavailable too.

### W6d. Reopen a day closed by mistake

⚠ The server has supported this since backend 1.15.0; **nothing on the web till could call it.**

1. Z-close the day.
2. As a **Supervisor**, press **Reopen this day…**
3. Try it with an **empty reason**. **✅ Expected: refused** — *"no reason given"* in an audit trail is
   worse than no trail, because it looks like a record.
4. Give a reason and reopen.

**✅ Expected:** trading resumes, and you can record cash again.

5. Check the **portal**: ⚠⚠ **both** the close **and** the reopen must be there — *"closed 17:32,
   reopened 17:41, by X, because Y"*. The close must **not** have vanished: deleting it would erase
   that somebody counted and banked that drawer.

6. Sign in as a **Cashier** and Z-close again. **✅ Expected:** no reopen button — a supervisor can, they
   cannot. ⚠ The person who counted the drawer must not be the only one who can quietly un-count it.

7. ⚠ **Reopen with the network down.** **✅ Expected: refused** — this one is deliberately online-only.
   It is an audited supervisor action, not drawer money: if it cannot reach Plutus, it has not happened.

## W7. Reprint a receipt on the web till — **NEW in web 1.11.0**

⚠ Half of this has existed for months and nobody could use it in a shop: Reporting → a sale →
**Print copy receipt** rendered a marked copy, but only through the **browser's** print dialog. A
counter with a thermal printer had no way to hand a customer their paper again.

### W7a. The copy comes off the receipt printer

Needs the **hardware agent running** and a receipt printer configured.

1. Sell something. Note the total and the sale id on the paper.
2. Go to **Reporting → Custom → a date range covering today**, and open that sale.
3. Press **Print copy receipt**.

**✅ Expected:** paper comes out of the **thermal printer** — no browser print dialog at all — and the
screen says *"Copy receipt printed."*

4. ⚠⚠ **THE DRAWER MUST STAY SHUT.** No money is moving. A drawer that opens when nothing is happening
   teaches operators that the drawer opening means nothing, which is worse than it never opening.
5. Compare the two papers: every figure must match — items, discounts, VAT, tenders, change.
   ⚠ The figures come from the **stored sale**, so a price change since then must not move them.
6. ⚠ The copy must say **(COPY)** on the sale id, and the **barcode must be the same** as the
   original's. Marking distinguishes the *paper*, never the *sale* — a copy with a different barcode
   could not find its sale, which is the only reason anybody asks for one.

⚠ **Known divergence, on purpose (till-design C2):** MAUI prints a banner — `** REPRINT — not a new
sale **` — above the first rule. The web till marks the sale-id line only. Same purpose, weaker
signal; converging them is a WP15 job because it touches the shared document builder. **Say so if it
bothers you on real paper** — that is the decision this test is here to inform.

### W7b. ⚠ It falls back rather than failing

1. **Stop the hardware agent** (or unplug the printer and stop the agent).
2. Reprint the same sale.

**✅ Expected:** the **browser** receipt view opens, exactly as before this change. A reprint must never
be held up by hardware — the customer is standing there.

3. Restart the agent and reprint again. **✅ Expected:** back to the thermal printer.

### W7c. ⚠⚠ A sale rung up on the MAUI till, reprinted from the browser

1. Ring up a sale **on the MAUI till** and let it reach the server (Plutus tab → queue empty).
2. On the **web till**, find that sale in Reporting and reprint it.

**✅ Expected:** it prints. ⚠ This is the one thing the web till does here that **MAUI cannot** — its
list is `GET /api/v1/sales`, the whole business, where MAUI reads its own SQLite. The trade runs the
other way too: MAUI's reprint works with the line down and this one needs the server. Try it offline
and expect the sale not to be found — that is correct, not a fault.

## W8. ⚠⚠ The card fee (surcharge) on the web till — **NEW in web 1.11.0**

⚠⚠ **DORMANT FOR KAPOW.** The rate is zero, so nothing appears and nothing changes. **W8a exists to
prove exactly that**, and it is the only part of this section that applies to normal trade.

⚠ UK consumer card surcharges have been **banned since 2018-01-13**. This is for tenants where a fee
is lawful (some B2B), and it exists on the web till because MAUI has charged it since 2026-08-09 —
two counters in one shop taking different money for the same basket is what parity means here.

### W8a. With no fee set, nothing changes at all

1. Sell something, pay by **card**.

**✅ Expected:** no fee line, no fee message, the total is the goods total. ⚠ If anything about the
checkout screen looks different from yesterday, **stop and report it** — this whole slice is supposed
to be invisible until somebody sets a rate.

### W8b. Set a rate, then take a card payment

In the **portal → Company → Card payments**, set **1.69% + 20p**. Then, on the web till:

1. Ring up **£10.00** of standard-rated goods. Open checkout. **✅ Expected:** heading says
   **£10.00** — the fee is not charged for existing, it is charged for paying by card.
2. Type **10.00** in the **card** row.

**✅ Expected:** the heading becomes **£10.37**, a line appears saying *"Card fee £0.37 added — £10.00
of goods + £0.37"*, and **£0.37 still to pay**.

3. Press **rest** on the card row. **✅ Expected:** it fills **10.37** and Complete becomes available.
   ⚠ The fee must **not** move again — it is a percentage of the **goods**, not of what has been
   tendered, so this settles in one step. If it climbs each time you press rest, stop.
4. Complete. **✅ Expected:** the receipt lists **Card surcharge £0.37** as its own line, and the total
   matches what the card was charged.
5. ⚠ Clear the card row and pay **cash** instead. **✅ Expected:** the fee disappears and the total goes
   back to £10.00.

### W8c. ⚠⚠ THE VAT ONE — the fee follows the goods

This is the whole reason the rule is shared rather than written twice.

1. **Zero-rated basket** (a children's book, most food): ring up £10.00 of zero-rated goods, pay by
   card. **✅ Expected:** the fee is charged, and it carries **no VAT** — check the portal's VAT report
   for today: the fee must add **nothing** to output tax. ⚠ A hardcoded 20% would take 6p of VAT that
   HMRC says is not due, and **every total on the receipt would still add up**.
2. **Mixed basket**: £10 standard + £10 zero-rated, pay by card. **✅ Expected:** the fee's VAT sits
   **between** the two — apportioned by value, not the standard rate.
3. ⚠ Compare against the **MAUI till** with the same basket and the same setting. **✅ Expected: the
   same pence.** Both tills price this from `CardSurchargeVat` / `surcharge.ts`, and a penny of
   disagreement here is a penny of disagreement on every VAT return afterwards.

### W8d. Once per sale, never on a refund

1. **Split across two cards**: £5 on card, £5.37 on card. **✅ Expected:** the **flat 20p is charged
   once**, not twice. The fee for the sale is one figure, however many times a card is used.
2. **A refund** (return an item, nothing sold): pay it back to card. **✅ Expected: no fee.** A refund
   attracts no surcharge — there is no supply for the fee to follow.
3. **Gift card / store credit**: pay entirely with one. **✅ Expected: no fee.** Neither is an acquirer
   transaction, so there is no cost to pass on. ⚠ *"Gift card"* contains the word "card"; if a fee
   appears here the tender mapping is wrong.
4. ⚠ **A mixed basket that nets NEGATIVE** (£1 sold, £500 returned) paid by card **does** attract a
   fee, on **both** tills. It looks odd and it is deliberate — MAUI's rule is *"any sale line at
   all"*, and inventing a different answer on the web till would be the divergence. Report it if it
   bites in real trade and it gets changed **in both places**.

### W8e. ⚠ The fee with the line down

1. Take a card payment with a fee set, online. Then **pull the cable** and start a new sale.

**✅ Expected:** the same fee is still charged — last-known-good, from `localStorage` (MAUI caches it
in its Meta store for the same reason). ⚠ A fee that vanished offline and came back online would make
two identical baskets total differently an hour apart, and the operator would wear the argument.

2. On a till that has **never** connected: **✅ Expected: no fee.** Charging nothing beats guessing.

### W8f. ⚠ Set a rate the portal would not allow

Only reachable by editing the setting directly in the database — a negative `SurchargeBp`.

**✅ Expected:** the sale is **REFUSED** at checkout with a sentence naming the problem, and the till
stays usable so the operator can take cash. ⚠ Fail **closed** on money: completing without the fee
takes the wrong money silently, and a blank screen loses the basket as well as the sale.

## W9 / G33. Store Information — **both tills, and the portal** (web 1.12.0 · till 1.74.0)

⚠⚠ **This section exists because of two different faults with one symptom.** Matt, 2026-08-17:
*"webtill does not show the opening hours set in the portal"* and *"MAUI looks nothing like the
webtill."* The first was a **message** that could not tell three states apart; the second was a
**screen** that was unreadable while every register row said the capability worked.

### W9a. ⚠⚠ THE DIAGNOSIS — what the web till now says about your hours

On the **web till**, go to **Store Information**.

Whatever it says, it is now one of exactly three things, and which one it is *is* the answer:

| It says | It means | Do this |
|---|---|---|
| A week of days and times | The portal's hours are stored and readable | Nothing — check the times match the portal |
| *"Not set — add opening hours in the management portal"* | **Nothing is stored.** The field is genuinely empty | Set them in the portal (W9c) — and note that they were never saved before |
| *"The portal has opening hours for this store, but this till can't read them: …"* | **Something IS stored and it is malformed.** The message names the fault | W9c — the portal now shows the same fault |

⚠ **Before this build all three printed the middle message.** If you now get the third one, that is
the original report explained: the hours were saved, as text no till could parse.

### W9b. The rest of the web till's card, unchanged

Business (name, VAT number) · Store (name, address, contact number) · Opening hours · then Store id
and Till id. **✅ Expected:** exactly as it was — this change touched only the hours.

### W9c. ⚠⚠ THE PORTAL — where hours could be saved unreadable

⚠ Needs **portal 1.9.0**, which is **built and NOT deployed** at the time of writing. Skip this
section on the live portal; it will not behave as described.

**Locations → this store → Opening hours.**

1. If the stored hours are unreadable, **✅ Expected:** the section is **already open**, the summary
   says *"the tills can't read these"*, the **advanced JSON view** is showing (not the simple editor
   with everything unticked, which is what it used to do), and the fault is named underneath.
2. Type something broken on purpose — `{mon: "09:00-17:30"}` — into the advanced box.
   **✅ Expected:** *"The tills won't be able to read this: …"*, and **Save is disabled**.
   ⚠ It used to save happily. That is how this started.
3. Fix it — `{"mon":[{"open":"09:00","close":"17:30"}]}`. **✅ Expected:** *"✓ Readable by the
   tills"*, Save enabled.
4. Press **Save opening hours** — ⚠ **the button inside this section**, which is new. It used to say
   *"press Save in the address row"*, a button in a different part of the card above a collapsed
   section, so hours could be set and never saved. **Hours set and not saved look identical to hours
   never set**, which is a third way to produce the original report.
5. Reload the portal. **✅ Expected:** the hours are still there.
6. Back on the **web till**, reload. **✅ Expected:** the week, matching the portal.

### W9d. ⚠⚠ MAUI's Store Information — rebuilt

On the **MAUI till**, **Store Information**.

**✅ Expected — it should now look like the web till:**

1. Heading, then *"Read-only here — edit these details in the management portal…"* — the web till's
   own words.
2. **Three cards**: Business · Store · Opening hours, in that order, with the same field labels.
3. ⚠⚠ **EVERY LABEL MUST BE LEGIBLE.** The old screen drew every field label in light grey on a
   near-white surface — the data was all there and none of it readable. This is the specific thing to
   check, and it is the whole reason this section exists.
4. **Gone, and none of it should come back:** the grey top bar; the empty store block inside it; the
   **Region** panel with *Start of Week* and a currency format string like `£###,###.##/-£###,###.##`;
   the bare **Bag** button under an invisible heading.
5. Below the cards: **Store id** and **Till id**. ⚠ The till id is what you get asked for on a support
   call; MAUI showed it nowhere.
6. The bag setting now reads *"Quick-sell bag: which item the till's Bag button rings up. This till
   only."* with a button naming the current item. Press it, give a real barcode, **✅ Expected:**
   accepted. Give nonsense, **✅ Expected:** *"we can't find an item with that ID"*.
7. ⚠ **Set a theme in the portal while this screen is open.** **✅ Expected:** the card colours and the
   text follow it. The old screen assigned fixed colours, which a theme change could not move —
   half a themed screen is worse than none.
8. ⚠ Compare the hours with the web till's, **word for word**. Both read the same field through the
   same rule (`OpeningHours` / `openingHours.ts`), so a difference here is a real divergence.

### W9e. With the line down

1. Pull the cable, restart the till, open **Store Information**.

**✅ Expected:** the last-known details, including the hours. ⚠ Never the legacy local record, and
never blank — a null answer must not clear the cache, or the next receipt has no shop on it.

2. On a till that has **never** connected: **✅ Expected:** *"Unavailable — this till hasn't been told
its store details yet. It fills in on the next connection."* — and the Store id / Till id rows still
render, because they come from this machine.

## G34. ⚠⚠ The Reports tab — **fixed in till 1.75.0 + backend 1.17.2**

⚠ **On 1.74.0 and every build before it, EVERY report on this tab answered "This report couldn't be
read. You may not have permission to see it, or the till is offline."** Matt found it on 2026-08-18.
Two independent faults, and the message named neither correctly: the till was asking as the **device**
(a device holds no permissions, so 403 regardless of who is signed in), and five of the six endpoints
refused a Supervisor even with the right identity.

⚠ **This needs backend 1.17.2 or later.** Against an older backend, four of the six will still refuse.

### G34a. Every report opens

**Reports** tab. For **each** entry in the Report dropdown — **Takings · VAT · Items sold · By
category · Best sellers · Negative stock** — pick it, set a range that has trade in it (**01/08/2026 →
18/08/2026** works; the shop's real sales through 15 Aug are now imported) and press **Refresh**.

**✅ Expected, for every one:** a table with figures, or the honest **"Nothing in this range."**
⚠⚠ **NOT** *"This report couldn't be read."* — if you see that on any of the six, note **which**, and
whether it says *permission* or *no live sign-in*: those are now different messages naming different
causes.

### G34b. ⚠ Takings should agree with the platform, to the penny

1. **Takings**, 01/08/2026 → 18/08/2026.
2. Compare the total against the **portal's** Reporting → Summary for the same range.

**✅ Expected: the same figures.** Both read `SalesV2` through the same rollups. ⚠ August now contains
**131 imported sales (£3,087.52)** from the old shop till, so this range is no longer near-empty —
which is what makes it a real check rather than a comparison of two zeros.

### G34c. ⚠⚠ THE ONE THAT WAS ACTUALLY BROKEN — a Supervisor must see the takings

⚠ This is the case the fix exists for, and the one a manager account will NOT exercise: **Owner,
Company Admin and Store Manager all hold `portal.reports.view`**, so they were only ever blocked by
fault ①. A **Supervisor** holds `pos.reports.view` and *no portal permission at all*.

1. Sign in as a **Supervisor** (or have one created in the portal → Users).
2. Open **Reports → Takings**, then **VAT**.

**✅ Expected: both read.** ⚠ A supervisor can **Z-close a day**, so being refused the takings they
just counted against is the specific nonsense this fixes.

3. Sign in as a **Cashier**. **✅ Expected: refused** — and that is CORRECT, not a bug: a cashier holds
   neither reporting permission. ⚠ Check the wording tells them it is a *permission* matter.

### G34d. The two refusals must be distinguishable

1. **Sign out**, then reach the Reports tab (or let a session expire overnight and press Refresh).
   **✅ Expected:** *"Reports are read as the signed-in operator, and this session has no live sign-in…
   Sign in again"* — ⚠ **it must NOT blame the network.** The old message said "or the till is
   offline", which sent you to check a cable over an expired token.
2. **Pull the network cable** and press Refresh. **✅ Expected:** a message about being unable to reach
   Plutus — and ⚠ **never an empty table**, because "no rows" on a reporting screen is a statement
   about the shop's trading, and making it when we could not ask is a confident lie.

## W10. ⚠⚠ THE WEB TILL COULD NOT TAKE A PAYMENT — fixed in web 1.13.0

⚠⚠ **Checkout crashed on EVERY attempt, and had done since 1.10.0.** Matt, 2026-08-18: *"When I try to
checkout on the webtill, I get… Minified React error #310"* — *"Rendered more hooks than during the
previous render."* `CheckoutDialog` called `useMemo` **six lines below a guard clause**, so it rendered
N hooks on mount and N+1 once the payment methods loaded, and React refused to render.

⚠ **Not an edge case — the only path.** The dialog always mounts before the methods arrive.

⚠ **Nothing automated could have caught it**: `tsc` passes (it is type-correct), `vite build` passes,
and all 205 vitest cases pass because **no test in this project mounts a component**. A person
clicking Checkout found it. **§W10a is now the first thing to run after any web-till deploy.**

### W10a. Take a payment — the two-minute smoke test

1. Ring up any item. Press **Checkout**.

**✅ Expected:** the tender screen, with the methods listed. ⚠⚠ **If the screen is blank or the console
shows React error #310, STOP** — that is this fault back, and the till cannot trade.

2. Type the full amount in **Cash** and Complete. **✅ Expected:** the sale completes, receipt shows.
3. Do it again by **card**. ⚠ With Kapow's zero surcharge the total must not change (§W8a).
4. ⚠ **Return an item and refund it** — the refund path is where the offending hook lived
   (`refundCapacities`), so it is the half most worth re-checking.

### W10b. ⚠ The guard that now stops it recurring

`npm run build` runs **eslint** first, with `react-hooks/rules-of-hooks` as an **error**.

**To prove it still works** (worth doing once, after any dependency change): move a `useMemo` below the
`if (!methods) return` in `CheckoutDialog.tsx` and run `npm run build`.

**✅ Expected: the build FAILS** with *"React Hook useMemo is called conditionally."* Verified that way
on 2026-08-18 — the rule was watched catching the real bug and failing the real build before being
trusted. ⚠ A linter nobody has seen fail is a linter nobody knows is wired up.

## G35. ⚠⚠ Refund and price-adjust — **fixed in till 1.76.0**

⚠ **On 1.75.0 and earlier, pressing Refund with nothing selected CRASHED the till** — and pressing it
before selecting a line is what anybody does first. Matt found it on 2026-08-18. A second crash was
uncovered while fixing the adjust dialog: **cancelling the price-adjust box also killed the app.**

### G35a. Refund with nothing selected — the crash

1. Empty basket, nothing selected. Press **Refund**.

**✅ Expected:** the dialog *"Which item is coming back?"* explaining to scan the item, tap its line,
then press again. ⚠⚠ **The till must NOT close.** If it vanishes, this fault is back.

2. Add an item but **do not tap its line**. Press **Refund** again. **✅ Expected:** the same guidance.
3. Now **tap the line**, press **Refund**. **✅ Expected:** the return flow starts and asks which sale
   it came from.
4. Mark a line as a return, then press **Refund** on that same line. **✅ Expected:** *"Already going
   back"* — not a crash, and not silence.

### G35b. ⚠ The adjust box now has a visible way out

1. Add an item, tap its line, choose **Adjust**.

**✅ Expected:** the price box shows **Confirm _and_ Cancel**. ⚠ Cancel is new — previously the only
exits were tapping outside or Escape, neither of which the box advertised.

2. Press **Cancel**. **✅ Expected:** the box closes, **the price is unchanged**, and ⚠⚠ **the till
   stays alive** — cancelling used to crash it.
3. Do it again, this time **tapping outside** the box. **✅ Expected:** same — closed, unchanged, alive.
4. Press **Escape**. **✅ Expected:** same again.
5. Now adjust for real: type a new price, **Confirm**. **✅ Expected:** the line's price changes, and
   the receipt/basket total follow.

### G35c. ⚠ Cancel now appears on EVERY input dialog — check a few

The Cancel button was added to the shared helper, so all 24 dialogs that had none now show one. Worth
a quick look at: **Cash → paid in/out**, **Inventory → edit item**, **Store Information → the bag
setting**, **Loyalty → add member**.

**✅ Expected:** each shows Cancel, and pressing it leaves everything untouched.

⚠⚠ **BUT — REPORT ANY CRASH HERE IMMEDIATELY.** Only the adjust dialog's back-out was fixed;
**17 other call sites still mishandle a cancelled dialog** and may take the till down. The full list is
in `MAUI-retrofit.md` **§0.3b**, and the risky ones are **Refund**, **selling a gift card**, **Cash**,
the **supervisor prompt** and **checkout**. If one of those dies when you cancel, that is a known and
recorded fault, not a new mystery — tell me which, and it gets fixed with the right meaning for that
flow rather than a blanket "do nothing".

## G36. The ✕ on every dialog — **till 1.77.0 · web 1.14.0**

⚠ Matt, 2026-08-18: *"add x's to all relevant boxes and write it into the till-design.md so that it is
not missed in future."* The rule is now **`till-design.md` Part D4 — the dialog contract**; this section
is how you check it holds.

### G36a. MAUI — every box has a ✕, top-right

Open each and look for the **✕** in the top-right corner, then press it:

| Dialog | How to reach it |
|---|---|
| **Adjust price** | add an item → tap its line → Adjust |
| **Alterations** | tap a line → Alterations ⚠ **this one had NO exit at all before 1.77.0** |
| **Returns** | tap a line → Refund → pick a sale |
| **Cash paid in / out** | Cash tab → Paid in |
| **Bag item** | Store Information → the bag button |
| **Edit item / stock** | Inventory → an item → Edit |
| **Add member** | Loyalty → Add |
| **Supervisor override** | trigger any gated action as a Cashier |

**✅ Expected, every time:** a ✕ top-right; pressing it closes the box, changes nothing, and **the till
stays alive**.

⚠⚠ **REPORT ANY CRASH.** The ✕ is deliberately wired to the **Cancel** path, which is the safe one —
but **17 call sites still mishandle a back-out** (`MAUI-retrofit.md` §0.3b), and the risky ones are
**refund, gift-card sale, cash, the supervisor prompt and checkout**. If one dies, that is a *known and
recorded* fault, not a new mystery: tell me which and it gets the right per-flow fix.

### G36b. ⚠ The three exits must all behave the same

On the **Adjust price** box, one at a time: press **✕** · press **Cancel** · tap **outside** · press
**Escape**.

**✅ Expected: all four do the same thing** — close, leave the price alone, no crash. ⚠ They are not the
same code path (D4 rule 5: the ✕ and Cancel return blanked fields, the other two return nothing at
all), which is exactly why all four are worth pressing.

### G36c. Web till — the same ✕, same corner

`https://plutus.huggett.dscloud.me` — open each and press the ✕: **Checkout** · **Apply discount** ·
**Parked baskets** · **Return item** · **Sale detail** (Reporting) · **Add user** (Users) · **Edit
item** (Inventory) · **Add member** (Loyalty).

**✅ Expected:** ✕ top-right, closes cleanly, nothing submitted.

⚠⚠ **THE ONE TO CHECK HARDEST IS CHECKOUT.** Several of these dialogs are HTML forms, where a button
defaults to *submit* — so a mis-built ✕ would **complete the sale** instead of closing the box. It is
explicitly `type="button"`; pressing ✕ on a filled-in checkout must leave the basket **unsold**.

⚠ Also check a **long title** does not run underneath the ✕ (`Refund — £1,234.56` is the longest).

## G37. ⚠⚠ Adjusting a price — **till 1.79.0 / web 1.15.0**. This one is a VAT check.

⚠ Until 1.79.0 MAUI asked for the **ex-VAT price AND the inc-VAT price** in one dialog and wrote both
onto the line. The sale line's declared VAT rate is derived from that pair, so two hand-typed numbers
*became* the VAT on the sale — £10.00 ex against £10.50 inc declared **5% on a 20% item**, and nothing
anywhere would have told you. It now asks for one number, like the web till.

### G37a. One field, and the VAT follows the item

1. Add a **standard-rated (20%)** item — say £12.00. Tap its line → **Adjust**.

**✅ Expected:** ONE box, labelled **Price (inc VAT)**, pre-filled with £12.00. ⚠ If you see two boxes,
you are on an old build.

2. Type **£6.00**, Confirm. **✅ Expected:** the line shows £6.00, and **Sale Ex. Tax shows £5.00** —
   the ex half derived, the 20% kept.
3. Complete the sale, then check the **portal → Reporting → VAT** for today. **✅ Expected:** that sale
   contributes **£1.00** of VAT, at the standard band — not some rate nobody chose.

### G37b. ⚠ A zero-rated item must stay zero-rated

1. Add a **zero-rated** item (a comic — most of the shop). Adjust it to any price.

**✅ Expected:** **Sale Ex. Tax equals Sale Inc. Tax** — no VAT appears. ⚠ This is the case the old
two-box dialog got wrong most easily: type into the ex box and a zero-rated line acquires VAT.

### G37c. Backing out changes nothing

Adjust → press **✕**, then again with **Cancel**, then **outside**, then **Escape**.

**✅ Expected each time:** the box closes, **the price is unchanged**, and the till stays alive. ⚠ The
✕ crashed the till in 1.77.0 (`decimal.Parse("")`); that is fixed at the source, and all four exits
now behave identically.

### G37d. ⚠ Refusals

1. Adjust to **-5.00**. **✅ Expected: refused** — *"A price cannot be negative. Use Refund to send
   goods back."* ⚠ A negative price is money out of the drawer dressed as a sale line, with no reason
   recorded and no refund cap; returns are how goods go back.
2. Adjust to **0.00**. **✅ Expected: allowed** — a giveaway is legitimate, and ex must be £0.00 too.
3. Adjust the **same line twice** (£6.00, then £7.00). **✅ Expected:** ex tracks each time from the
   ITEM's proportion (£5.00 then £5.83) — never drifting a penny per edit, which is what deriving from
   the line's own current pair would do.

### G37e. The web till agrees, to the penny

Same item, same override, on `https://plutus.huggett.dscloud.me` — click the price in the row, type the
new one.

**✅ Expected: identical ex-VAT figure on both tills.** They now run the same rule
(`SharedKernel.PriceAdjust` / `till/priceAdjust.ts`). ⚠ A penny of difference here is a penny of
difference on every VAT return afterwards, which is exactly what the C2 register exists to prevent.

---

## G38. ⚠⚠ Adding a member — **till 1.82.0**. It has never once worked.

> ⚠⚠ **NEEDS 1.82.0, WHICH IS NOT BUILT.** The build on the box is **1.81.0**, and on that one the
> Loyalty tab has no Add member button — so §G38 will "fail" for the wrong reason. Ask for a build
> first, or skip this section.

> ⚠⚠ **Read this before running it.** Two separate faults meant adding a loyalty member on MAUI was
> impossible, not merely awkward:
>
> 1. **It answered 403 for every operator** whatever their role (1.81.0) — the till asked as the
>    *device*, and a device holds no permissions. So *"member added"* has never appeared on this till.
> 2. **Then the button wasn't there.** Add member / Set tier came off the till screen — correctly,
>    they do not belong on a sale screen — but the **Loyalty tab had neither**, so for a few hours
>    there was nowhere left to add a member from. Fixed in 1.82.0: both are on the Loyalty tab now.
>
> **So there is no "it used to work" to compare against.** Everything below is being seen for the
> first time.

### G38a. The buttons are there, and on the right screen

Sign in as **Manager**. Go to the **Loyalty** tab.

**✅ Expected:** the top row reads **[ search box ] [ Search ] [ Add member ] [ Set tier ]**, all four
on one line, none overlapping. ⚠ Overlapping controls are exactly how this was reported on the till
screen (*"Add m|ember"* printed over *"Set tier"*) — if they overlap here, say so.

Now go to the **Till** tab. **✅ Expected: NO Search, NO Add member, NO Set tier.** Only the attached-
customer row (who is on this sale, and Remove) when somebody is attached. ⚠ A member is put on a sale
by **scanning their card**, which is what the web till and NatApp both do.

### G38b. Add a member — the path that has never completed

Loyalty tab → **Add member**. **✅ Expected:** a box with **Name \***, *Email (optional)*, *Phone
(optional)*.

⚠ **Check the asterisk is on Name and nowhere else.** A required box that looks optional is a save
that fails for a reason nobody can see — that is the thing Matt reported.

Type a name only (leave email and phone empty) and confirm.

**✅ Expected:** *"<name> added. Membership number M-xxxx."* — an actual number, from the server. Then
the search box fills in with that number and the list shows **exactly that one member**.

⚠⚠ **If you get a permission error, stop and report it.** That is the 403 fault back again, and it
means the operator token is not reaching the API.

### G38c. ⚠ Backing out must add nobody

**Add member** again. Type a name, then close the box with the **✕**. Then again with **Cancel**. Then
again by tapping outside it.

**✅ Expected, all three times: no alert, no member added, no error.** Clear the search box and press
**Search** — the name you typed must not be in the list. ⚠ All three exits must behave identically;
the ✕ used to blank the fields to empty strings which the caller then read as real input, and that
crashed the till (1.78.0).

### G38d. Set a tier

With that member on screen, press **Set tier**.

**✅ Expected:** *"Whose tier?"* listing the members on screen as **name · membership number**, then
*"Tier for <name>?"* listing tiers as **name · discount** (e.g. *Gold · 10% off*).

Pick one. **✅ Expected:** the list refreshes and the **Tier** column shows it.

⚠ **The rate must not be typeable anywhere.** The till sends a tier id and nothing else — re-rating
Gold in the portal has to move every Gold member at once, not leave a snapshot on whichever till
assigned it.

### G38e. Set tier with nothing to set

Clear the search box, press **Search** so the list is empty, then press **Set tier**.

**✅ Expected:** *"Search for a member first…"* — an instruction, not an error, and **no crash**.

### G38f. ⚠ A cashier must not see Set tier

Sign out; sign in as a **Cashier**.

**✅ Expected: Add member is there, Set tier is NOT.** A cashier may sign somebody up (binding default
20) but a tier changes every future basket that customer puts through, so it is Supervisor and up.

⚠ It must be **absent, not disabled-and-refusing**. Then add a member as the cashier: the confirmation
must end *"A supervisor can set their tier."*

### G38g. Offline

Pull the network. **Add member**, then **Set tier**.

**✅ Expected:** both refuse, naming **online and signed in** — *not* "try again". ⚠ Permanently
online-only and correctly so: the membership number comes from a tenant-wide counter, so two offline
tills would mint the same one.
