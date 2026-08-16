# Test Maui — the MAUI till hand-test script

**For till 1.59.0.** Anyone can run this. You do not need to know the codebase, and you should not
need to ask anyone what a step means — if a step is unclear, that is a bug in this document, so please
say so.

**Time:** about 45 minutes for the whole thing. About 15 for §A alone, which is the part worth doing
if that is all the time you have.

---

## Before you start

| | |
|---|---|
| **Run** | `D:\tmp\plutus-till-1.59.0\Plutus.Frontend.AppClient.exe` — just double-click it. Nothing to install. ⚠ **Nothing older.** Each of the builds before it fails a step in this document: **1.48.0** cannot take a sale (A0) · **1.49.0** crashes on a card overpay (A4) · **1.49.1** says nothing during a split payment (A5) · **1.49.2** lets a closed day take items from the item list (A8) · **1.49.3/1.50.0** let a split-paid refund go on one card (A4b) · **1.53.0 and earlier** let a discount be given with no reason recorded (§F). |
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

| Not in MAUI yet | Where it is |
|---|---|
| Loyalty, members, tiers | step 27 |
| Gift cards — sell and redeem | step 27 |
| Store credit as a payment method | step 27 |
| Attaching a customer to a sale | step 27 |
| Users — add an employee, set a password | step 24 |
| Colour themes pushed from the portal | step 22 |
| Fuller reports (items sold, VAT, best sellers) | step 26 |
| Refunding a sale rung up on **another** till | step 26 — you must type the sale id |
| Restoring an item from the Bin **on the till** | portal-side, by design |
| Reopening a closed day **on the WEB till** | MAUI only for now — next on the list |
| **Adding** a new item as one screen | still the old question-then-form flow; the EDIT screen is the new one |
| Announcements, help tickets, update prompts | platform notices |

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
