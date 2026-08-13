# Test Maui — the MAUI till hand-test script

**For till 1.49.2.** Anyone can run this. You do not need to know the codebase, and you should not
need to ask anyone what a step means — if a step is unclear, that is a bug in this document, so please
say so.

**Time:** about 45 minutes for the whole thing. About 15 for §A alone, which is the part worth doing
if that is all the time you have.

---

## Before you start

| | |
|---|---|
| **Run** | `D:\tmp\plutus-till-1.49.2\Plutus.Frontend.AppClient.exe` — just double-click it. Nothing to install. ⚠ **Not** 1.48.0 (cannot take a sale — A0), 1.49.0 (crashes on a card overpay — A4) or 1.49.1 (a split payment tells you nothing — A5). |
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

The specific fixes in **1.42.0–1.49.2**, from the hand-runs of 2026-08-11 and 2026-08-13. Each one was
reported by a real person using the till. **If you only have 15 minutes, do this section.**

⚠ **A0, A4 and A5 are the newest and the least proven** — all three are checkout faults found on
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

## A8. Try to sell after closing the day

1. With the day **Z-closed** (from A7), go to the **Till** tab and try to ring up a sale.

**✅ Expected:** it refuses **before taking any money**, and your basket is left intact.
**❌ The bug:** the sale went through, and the platform accepted it against a day already counted and
banked — which makes the Z read, the banking and the platform's figures disagree for ever.

⚠ **Also try a refund** against that closed day. It should refuse too.

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
which should read **`MAUI till v1.49.2`**. (It is also on the sign-in screen.) ⚠ **If it says anything
else, stop and say so** — 1.48.0 cannot take a sale and 1.49.0 crashes on a card overpay, so a run on
either of those will just re-find faults that are already fixed. Two of the fourteen findings on
2026-08-11 took much longer to settle than they needed to, partly because nobody could be certain
which build had produced them.

⚠ **Cross-check it against the portal.** In **Locations & Tills**, your till's row should show the
**same** number next to Active. If the portal says something different — or says **v0.0.0** — that is
worth reporting on its own: the web tills read v0.0.0 there until 2026-08-11, because they were built
on a machine where the version file could not be found and the build fell back to zero in silence.

---

# §E — added late on 2026-08-11, never yet tested by anyone

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
