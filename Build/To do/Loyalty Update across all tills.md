# Loyalty Update across all tills — design document

**Product:** Loyalty section for store / club platform
**Author:** Matt Huggett (Leading Talent) with Claude
**Date:** 13 August 2026 · renamed from `updatedesign.md` 2026-08-17
**Status:** Draft for review — ⚠ **NOT STARTED. No part of this is implemented; see the box below.**

> ## ⚠⚠ NOTHING HERE IS BUILT — verified against the code 2026-08-17
>
> Grep for this design's own types and **every one returns zero files**: `LoyaltySettings`,
> `LoyaltyPointEntry`, `PencePerPoint`, `SpendPerPointPence`, `PencePerPointAtEarn`, `ExpiryChoice`,
> `NameSingular`/`NamePlural`, `LoyaltyCache`. There is no earning engine either.
>
> ⚠ **And §17's "first platform slice" has not been cut:** `IngestSaleRequest` still carries no
> `CustomerId`, so **every sale arrives anonymous**, and `LineMeta` has no `earnsCredits`. Without the
> member link on the sale header there is nothing to earn against — which is exactly why the design
> named it first.
>
> **What DOES exist is the foundation this extends, not this programme:** customers with member
> numbers, `LoyaltyTier` + `AutoDiscountRate` (manually assigned, discount applied on both tills),
> **store credit** (`CreditEntry`, append-only, a `CREDIT` tender — ⚠ that is *money* from refunds, not
> gems), `GiftCardSettings` as the idiom `LoyaltySettings` copies, and refund plumbing whose clawback
> hooks are unused.
>
> ⚠ **Retrofit step 27 is the PARITY slice, not this.** It closed "a Gold member is charged 10% more on
> MAUI" by bringing MAUI level with the web till on members, tiers, the tier discount, store credit and
> a Loyalty tab. This programme is a separate **~45–55 days**.
>
> ✅ **Nothing is half-built** — no orphan tables, no dead `LoyaltyPoint` types, no partially-wired
> endpoints. It is a design waiting on a start, which is the cleanest state for it to be in.
>
> ### ⚠ Before any of it starts — nine of twenty-one decisions are not yours yet
>
> **Open (4):** 3 tier qualification basis · 4 tier downgrade policy & grace period · 6 transfers /
> family pooling · 7 enrolment opt-in vs automatic. ⚠ Two of those four are about **tiers**, and §5 is
> eight lines long.
>
> **Proposed — my recommendation, never ratified (5):** 12 where earning is computed · 13 loyalty
> credits vs store credit · 14 earn rounding · 16 reward catalogue · ⚠⚠ **18 refund symmetry**.
>
> ⚠⚠ **Decision 18 is the one not to leave sitting.** A refund must claw back the earn **and** restore
> the burn: the earn alone is a gem printer — buy £1,000, refund it, keep 100 gems. ⚠ **Decision 14 has
> a deadline** — carrying remainders forward *later* means recomputing every sale ever made.
>
> ### ⚠ The depth is uneven, and §18 is 46% of the document
>
> Everything interrogated at the 2026-08-13 review is deep and buildable — the economics (§18, 361
> lines), redemption, earning, parity. What was not is still headline bullets: **§15 Core Entities is
> nine names and no schema**, **§13 Legal & Financial (UK) is eight lines** for a feature carrying a
> real accounting liability (breakage, outstanding liability), and §5 tiers, §10 member portal and §12
> enrolment are sketches. **Phase A is specified well enough to build; B–D are not.**

---

## 1. Overview

A configurable loyalty module that lets a store or club reward members with a branded credit currency (e.g. "gems") earned through purchases and other actions, redeemable against future purchases. The module supports loyalty tiers, a member-facing portal, an admin portal, and full integration at the point of sale — **on every till: the web till, the MAUI till, and any future till, plus the webstore channel** (Matt, 2026-08-13: *"I need this to cover MAUI/Portal/Web"*).

⚠ **Parity is binding here, not aspirational** (CLAUDE.md / till-design Part B): a loyalty capability is not done until its row is filled for every till, and no till ever holds a loyalty *rule* of its own — see §16 for where each rule lives.

Design principle throughout: **everything a merchant might want to change is configuration, not code** — the same module should serve a coffee shop and a members' club without modification.

---

## 2. Loyalty Currency ("Credits")

The credit currency is fully configurable per programme:

| Setting | Detail |
|---|---|
| Name | ✅ **CONFIRMED (Matt, 2026-08-13: *"the gem term needs to be configurable yes? I am just using it for ease of conversation"*)** — singular and plural both configurable ("1 gem" / "250 gems"), both stored because English plurals are not reliably "+s" and a tenant may pick a word that is not. See the naming rule below |
| Icon / visual | Configurable icon or uploaded image, with a fallback (emoji or default graphic) if none set. Must render well small (nav badge) and large (balance page) |
| Value semantics | ✅ **DECIDED (Matt, 2026-08-13): money-mapped. 1 gem = £0.10**, set in the portal. See §18 for the settings row and what follows from it |

⚠ **The ledger stores a COUNT of gems, never their pence value** — see §18. The rate is a
portal setting and a setting can change; a balance stored in pence would either silently
re-value every historical entry or silently fail to, depending on which figure was written.

#### ⚠ The naming rule: "gem" is Kapow's SETTING, never an identifier

**Code, tables, columns, API fields and DTOs use the neutral term `Point`** — `LoyaltyPointEntry`,
`PencePerPoint`, `PencePerPointAtEarn`, `SpendPerPointPence`. **"Gem" appears only in data**, as
`NameSingular`/`NamePlural` on `LoyaltySettings`, and only ever reaches a screen through them.

⚠ This is not tidiness. A column called `GemBalance` makes one tenant's brand term permanent
schema, and the next tenant's "stars" then either lives in a table that calls them gems or needs a
migration to rename it. *"Point"* is chosen over *"credit"* deliberately — `CreditEntry` already
exists and means **money** (§17), and reusing that word is the collision this whole design is
avoiding. ⚠ **This document deliberately says "gems" in prose**, because it is Kapow's configured
term and reads better than "points"; that is the display name in use, not a name in the code.

⚠ **The term must reach every surface from the setting** — both tills, receipts, the member portal,
the admin portal and any notification. A hardcoded "gems" on a receipt template or a till button is
a bug the moment a second tenant enrols, and it will be found by that tenant rather than by us. For
MAUI that means the two strings ride along with the synced `LoyaltyCache`, not a resource file.

⚠ **Each batch also carries the rate it was EARNED under** (`PencePerPointAtEarn`) — decision 19,
**§18.8**: gems are *grandfathered*, so a rate change touches future earns only and can never take
value from a member. A member's balance is therefore **Σ(batch count × that batch's rate)**, and
what they are shown is **the money** — *"you have £23.50 in gems"* — because once batches differ a
raw count has no single value. ⚠ Consequently **no till ever holds `PencePerPoint`**: it receives a
money balance and sends a money redemption, so the rate cannot drift across surfaces.

---

## 3. Earning Rules

Each earning source is individually configurable and toggleable:

- **Purchases** — ✅ **DECIDED: 1 gem per £10 spent**, portal-configurable (§18). Rounding is
  **floor, per sale, remainder discarded** — a £15 basket earns 1 gem, not 1.5, and the £5 is
  not carried forward. Whole gems only; this matches Clubcard/Boots and avoids a per-member
  remainder accumulator. ⚠ Decision 14 — cheap to revisit *before* launch, expensive after,
  because carrying the remainder forward retrospectively means recomputing every sale ever made.
- **Visits / check-ins**
- **Sign-up bonus**
- **Referrals** — with fraud protection
- **Birthdays**
- **Reviews**
- **Event attendance**

Additional rules:

- **Exclusions** — gift cards, already-discounted items, delivery fees; implemented via an "earns credits" flag at line-item level (§8)
- **Multipliers & promotions** — double-points weekends, tier-based multipliers, category boosts, each with start/end dates
- **Caps & anti-abuse** — daily and per-transaction earning limits
- ⚠ **THE EARN BASE, stated exactly** (decision 15 — every channel must use the same one or two
  tills disagree about what a customer earned): **the gross, inc-VAT amount the customer actually
  paid** — i.e. *after* the tier discount, *after* any gem redemption, *excluding* gift-card
  activation lines, *excluding* delivery. Consequences, all deliberate:
  - **After the tier discount:** a Gold member paying £90 on a £100 basket earns on £90. Earning
    on the pre-discount figure would pay the tier benefit twice.
  - **After gem redemption:** this *is* the "no earn on credit-paid amounts" rule, and it is what
    stops the compounding loop — gems earned on gem-funded spend is a slow, permanent leak.
  - **Inc-VAT:** it is the figure on the receipt, so it is the only one explainable at a counter.
  - ⚠ **Excluding gift-card activation:** selling a £50 gift card is a *liability*, not a supply —
    it posts zero VAT (see `GiftCardSettings`). Earning on it pays out twice: once on the card,
    once when the card is spent. This falls straight out of the gift-card VAT decision already
    taken and is easy to miss.
- Earning is calculated on sale completion and written to the ledger as an EARN entry referencing the sale (§9)

---

## 4. Redemption

✅ **DECIDED (Matt, 2026-08-13): a member may spend as many gems as they like on an order** — no
per-order cap, no minimum balance, no minimum spend. £0.10 a gem, straight off the basket.

⚠ **The redemption is entered as MONEY, not as a count of gems** (decision 19, §18.8). Because gems
are grandfathered at the rate they were earned under, *"spend 100 gems"* has no single value — so the
cashier asks **"how much would you like to take off?"** and the engine consumes whatever batches that
costs, **oldest first**. This also makes the oldest-first order value-neutral to the member: spending
a money amount means batch order changes which rows drain, never what the member gets.

⚠ **"As many as you like" is still bounded by the basket, and the bound is already enforced in
code.** `DiscountApportionment.Across` **throws** if the discount exceeds the lines it is spread
over, so a basket can reach £0.00 but never go below. That guard is the backstop, not the UX: the
till must **cap the offered redemption at the basket value** before it gets there, or a
too-large redemption becomes a *quarantined sale* (a 202 from `SalesIngestService`) rather than a
polite refusal at the counter. See §18 for the two edge cases this creates.

- **Reward catalogue** — each reward has a configurable credit cost. Reward types: percentage discounts, fixed-value vouchers, free products, experiences, tier-exclusive items. ⚠ **Not v1** — v1 is the flat 10p-a-gem rate above, which needs no catalogue at all
- **Mechanics to configure** — redeem directly at checkout vs converting to a voucher first; whether credits can part-pay or must cover the whole reward; minimum balance to redeem; minimum spend requirements. ⚠ Superseded for v1 by the decision above: redeem directly at checkout, part-pay always allowed, no minimums
- **Refund handling** — refunds reference the original sale: earned credits are clawed back and spent credits restored automatically (§9)

### ✅ SETTLED: a redemption is a DISCOUNT — Matt, 2026-08-14

*"Credits are only ever earned, never purchased, not transferable to cash → discount. This is the
model, and it's the standard retail approach (Tesco Clubcard, Boots points, Nectar all work this
way)."*

⚠ **The two clauses before the arrow are the whole argument, and they are what to cite — not the
brand names.** Clubcard is evidence that the treatment is orthodox; it is not the reason:

| Because a gem… | It cannot be a tender, because… |
|---|---|
| is **only ever earned, never purchased** | no money entered the business against it. A tender discharges a liability; nothing here was ever owed |
| is **not transferable to cash** | it is not a monetary instrument. A wallet you cannot withdraw from is not a wallet |
| **reduces what is owed on a supply** | that is a **price reduction** — and HMRC treats one as reducing the VAT-inclusive consideration, so §18.4's apportionment is the *correct treatment*, not a convenience |

⚠⚠ **This is why the gift-card contrast matters, and why the two must never be merged.** A gift card
**is** purchased: real money arrives, VAT is deferred to redemption, and it is a **liability** — hence
the zero-VAT activation. A gem is neither purchased nor a liability, so pointing the loyalty build at
the gift-card machinery because "they both take money off a basket" would put a genuine liability and
a mere price reduction through one code path, and the VAT return would be wrong in one of the two
cases for ever.

**§18.4 is the code-level half:** the apportionment and VAT-split machinery a discount needs
(`DiscountApportionment.Across`, `VatLineMath.ForLine`) **already exists, is deterministic to the
penny, and is already a C2 twin with the web till** — so a mixed-VAT basket apportions correctly for
free. A tender needs a new tender type and reports gem "takings" the bank never saw. ⚠ **The build
cost was never symmetric, and now it does not have to be argued** — it agrees with the accounting.

---

## 5. Tiers / Levels

- **Qualification basis (configurable):** lifetime spend, rolling 12-month spend, credits earned, or number of visits
- **Spent credits should still count toward status** — otherwise members hoard rather than redeem
- **Per-tier configuration:** name, icon/colour, threshold, benefits (earn multiplier, exclusive rewards, perks)
- **Downgrade rules:** whether members can drop a tier, and with what grace period. This is the most emotionally sensitive rule in the system — design and communicate it carefully

---

## 6. Credit Lifecycle Rules

- ✅ **Expiry — DECIDED (Matt, 2026-08-13): a settable expiry, or never — and it is the TILL OWNER's
  decision to make.** Portal-configured as `ExpiryChoice` (`NotChosen` / `Never` / `AfterMonths`)
  plus `ExpiryMonths`, rolling from the earn date. ⚠ **`NotChosen` is the gate** and the programme
  does not earn until the owner has picked, per `GiftCardSettings`; **"never" is chosen, never
  defaulted into**. ⚠ Two fields rather than a bare nullable so *"the owner chose never"* is
  distinguishable from *"nobody has decided"* — see **§18.9**, which also covers the sweeper,
  breakage and the accounting.
- ⚠ **Expiry is a property of the EARN ENTRY, not of the member.** Gems earned in January expire
  before gems earned in June, so the balance is never one number with one date — it is a set of
  dated batches. Two consequences that must be built, not discovered:
  - **Redemption consumes oldest-expiring-first (FIFO).** Spending newest-first would silently
    burn gems the member was about to lose anyway and let the older ones lapse — the member is
    worse off for shopping. FIFO belongs in `Plutus.SharedKernel` because *both* tills must show
    the same "you have £23.50 in gems, £3.00 of it expiring on 14 Sep".
    ⚠ **The same ordering also drains the old-rate cohort** under grandfathering (§18.8), so one
    rule serves both purposes. ⚠ **Precision, or the two diverge:** oldest-*earned* and
    oldest-*expiring* coincide only while `ExpiryMonths` is unchanged — shorten it and a June gem
    can expire before a March one. **Soonest-expiring wins; oldest-earned is the tie-break.**
    Ordering by earn date instead would let the sooner-expiring batch lapse, destroying exactly the
    value FIFO exists to protect.
  - **Changing `ExpiryMonths` must not retro-expire.** Entries carry their own computed expiry, so
    shortening the window affects *future* earns only. Otherwise one portal edit can vaporise a
    balance a member is standing at the counter holding.
- **Manual adjustments:** staff can add/remove credits, with a mandatory reason logged to the audit trail
- **Transfers / pooling:** optional — gifting credits between members, or shared family/household balances
- **Negative balances:** define behaviour when a refund claws back credits that were already spent

---

## 7. The Loyalty Ledger (source of truth)

The member's balance is **never a mutable number**. It is derived from an append-only ledger of transactions:

| Entry type | Trigger |
|---|---|
| `EARN` | Sale completion, sign-up, referral, birthday, etc. |
| `BURN` | Redemption (confirmed) |
| `HOLD` / pending burn | Redemption applied but sale not yet completed (§10) |
| `RELEASE` | Sale abandoned or payment failed — hold returned |
| `ADJUST` | Manual staff adjustment (reason required) |
| `EXPIRE` | Lifecycle expiry |
| `REVERSE` | Refund clawback / restoration |

Every earn/burn entry references the `sale_id` that produced it. This makes disputes, refunds, statements, and reporting trivial, and guarantees the member and admin portals can never disagree.

---

## 8. Sales Capture

Every sale is captured as an immutable record in three layers:

**Sale header**
- id, timestamp, location, staff member
- `member_id` (nullable — anonymous sales still recorded)
- gross total, discount total, loyalty discount, net total, VAT

**Line items**
- product, quantity, unit price
- discount applied to the line
- `earns_credits` flag (drives exclusions from §3)

**Payments**
- split-tender support: cash, card, and the credit redemption represented per the §4 decision

On completion the system writes the EARN entry (and confirms any BURN) to the ledger against the `sale_id`. "Credits earned" is never stored as a column on the sale — the ledger is the source of truth.

**Refunds** reference the original sale so the earn is reversed and any burn restored automatically.

---

## 9. Applying Credits at Purchase (till & online checkout)

1. **Identify the member** — QR/card scan, phone number, or email lookup at the till; already logged in online
2. **Surface the balance automatically** — the till shows "Matt · 1,240 gems" and qualifying redemptions; staff must not have to remember to check
3. **Select the redemption** — one-tap options ("Use 500 gems → £5 off"), validated against balance, minimum spend, and reward rules
4. **Hold the credits** — a *pending* burn is written when applied, so the balance cannot be spent twice while the sale is in flight (simultaneous online + in-store use is a real case)
5. **Finalise on payment** — payment succeeds → burn confirmed against the `sale_id`; sale abandoned or payment fails → hold released, credits back instantly

The balance check and burn must be a **single atomic operation** on the ledger. The hold-then-confirm step is where double-spend bugs and support tickets come from if skipped.

---

## 10. Member Portal

A bank-statement view of the member's own ledger joined to their own sales:

- Purchase history: date, location, items, total
- Credits earned and spent per transaction
- Running balance statement (straight off the ledger)
- Tier progress ("120 gems to Gold")
- Notifications: earn, redeem, expiry warnings
- Digital card / QR code for in-person identification

## 11. Admin Portal

Built as the **same history service with a different authorization scope** — one service, two lenses, never two data stores:

- The identical per-member drilldown (essential for support: "where did my gems go?")
- Aggregate reporting:
  - discounted sales by period / location
  - credits issued vs redeemed
  - outstanding credit liability
  - breakage (credits expiring unredeemed)
  - engagement by tier
- Audit trail of every manual adjustment and configuration change
- Staff permissions controlling who can adjust balances and change programme config

---

## 12. Member Experience & Enrollment

- Enrollment: opt-in vs automatic (configurable)
- Clear balance display and progress indicators throughout
- Per-user preference support in member and staff UIs

### Accessibility (member portal & till surfaces)

Target **WCAG 2.2 AA**. Highlights relevant to loyalty screens: AA contrast with an optional high-contrast theme, rem-based scalable text that reflows at 200%, no colour-only meaning (pair icons/labels with tier colours and status), large touch targets at the till, ARIA live regions so balance/total changes are announced to screen readers, and full keyboard operability. (Full till accessibility spec held separately.)

---

## 13. Legal & Financial (UK)

- **Outstanding credits are an accounting liability** — finance needs the liability report (§11)
- Clear T&Cs on expiry, programme changes, and redemption rules
- **GDPR** — lawful basis and transparency around member purchase data; retention policy for history
- **VAT** treatment of rewards and redemptions (interacts with the discount-vs-tender decision, §4)

---

## 14. Key Decisions & Open Questions

| # | Decision | Recommendation | Status |
|---|---|---|---|
| 1 | Credits as discount or tender | **DISCOUNT.** Matt, 2026-08-14: *"Credits are only ever earned, never purchased, not transferable to cash → discount. This is the model, and it's the standard retail approach (Tesco Clubcard, Boots points, Nectar all work this way)."* ⚠ **The reasoning is the substance, not the citation:** a gem is never *bought*, so no money ever entered the business against it — there is no liability to discharge, and a tender exists to discharge one. It cannot leave as cash, so it is not a monetary instrument. What it does is reduce what is owed on a supply, which is a **price reduction**, and HMRC treats a price reduction as reducing the VAT-inclusive consideration — §18.4's apportionment is then not a convenience but the correct treatment. §18 adds the code-level half: the apportionment-and-VAT machinery *already exists and is already tested*; a tender needs a new tender type and puts "gem takings" in the Z-read that never reached the bank | ✅ **DECIDED — Matt, 2026-08-14.** ⚠ **Unblocks phase A** (§17) and closes the last gate on binding default 21 |
| 2 | Credit value semantics (abstract vs money-mapped) | ~~Abstract points~~ → **money-mapped, 1 gem = £0.10**, portal-set. ⚠ Ledger stores the **count**, not pence (§18) | ✅ **Decided — Matt, 2026-08-13** |
| 3 | Tier qualification basis | Rolling 12-month, spent credits count | Open — ⚠ **but tiers already exist and are assigned by hand**; this decision only bites when the engine replaces manual assignment (§17) |
| 4 | Tier downgrade policy & grace period | — | Open |
| 5 | Expiry policy | **Settable expiry, or never — and it is the TILL OWNER's decision.** `ExpiryChoice` (`NotChosen`/`Never`/`AfterMonths`) + `ExpiryMonths`; ⚠ **`NotChosen` gates earning** and "never" must be *chosen*, not defaulted into. ⚠ Per-entry, oldest-first redemption, **no retro-expiry ever** (§6, §18.9) | ✅ **Decided — Matt, 2026-08-13** |
| 6 | Transfers / family pooling in v1? | Defer to v2 | Open |
| 7 | Enrollment: opt-in or automatic | — | Open |
| 8 | Earn on VAT-inclusive or net amount | **Gross inc-VAT, on what was actually paid** — after tier discount, after gem redemption, excluding gift-card activation (§3) | ✅ **Decided — Matt, 2026-08-13** (implied by "£10 spent"; stated exactly so no two channels differ) |
| 9 | Where tiers are **configured** | Portal only | ✅ **Decided — Matt, 2026-08-13** |
| 10 | Assign / change a member's tier at a till | **Supervisor and up** (`customers.manage`, which Supervisor already holds — no permission work) | ✅ **Decided — Matt, 2026-08-13** |
| 11 | Add a new member at a till | **Till operator (Cashier and up)**, via a new `pos.customers.add` — create-only; editing and tiers stay `customers.manage` | ✅ **Decided — Matt, 2026-08-13** |
| 12 | Where earning is **computed** | At **ingest**, server-side, never on a till — §16 | Proposed (follows the platform's own precedent) |
| 13 | Loyalty credits vs the existing **store credit** | Two distinct things, named distinctly — §17 | Proposed |
| 14 | Earn rounding | **Floor per sale, remainder discarded** — £15 earns 1 gem (§3) | Proposed — ⚠ decide **before** launch; carrying remainders forward later means recomputing history |
| 15 | Per-order redemption cap | **None** — as many gems as the member likes, bounded only by the basket reaching £0.00 (§4) | ✅ **Decided — Matt, 2026-08-13** |
| 16 | Reward catalogue (tiered rewards, vouchers, free products) | **Not v1.** A flat 10p-a-gem rate needs no catalogue; adding one later is additive | Proposed |
| 17 | ⚠ ~~Changing `PencePerPoint` re-values every outstanding balance~~ → **it no longer does** (decision 19) | **Two-stage `Ask.tsx` confirm** — the measured impact (*"X members holding Y gems keep their earned rate, worth £A, unchanged"*), `typeToConfirm` on the new rate, and an `AuditLog` row carrying the figures shown. ⚠ Figures from the **same code as §11's liability report**, server-side at dialog-open, never a cached rollup. ⚠ Skip the dialog when X = 0. ⚠ The hard `danger` warning **moves to shortening `ExpiryMonths`**, the one edit that still destroys value — §18.7 + §18.8 | ✅ **Decided — Matt, 2026-08-13** |
| 18 | ⚠⚠ Refund symmetry (see §18) | Refund **claws back the earn** *and* **restores the burn**; balance may go negative and redemption is blocked while it is | Proposed — this is the one that is a **cash-out exploit** if got wrong |
| 19 | ⚠ Grandfather gems at the rate they were earned? | **YES — grandfathering, with oldest-first consumption**, so the old-rate cohort liquidates itself and a rate change can never take value from a member. ⚠ Requires two things: the member-facing figure becomes **money not a count** (a count has no single value once batches differ), and a redemption becomes **one ledger row per source batch** (so a refund restores the same batches at the same rates). ⚠ Converges for active members; for a hoarder whose owner chose `Never` it is **permanent, not transitional** — §18.8 | ✅ **Decided — Matt, 2026-08-13** |
| 20 | ⚠ How gems actually expire, and how lapsing is accounted for | **The unexpired balance is computed ON READ from the date** — the `GiftCardLedger.cs:57` precedent, where nothing sweeps at all — so the balance is right **even if no job has run**. A sweeper (`RetentionSweeper`/`JobRun` shape) writes idempotent `Expire` rows **only for the accounting record**, dated when they lapsed, so **breakage lands in a period**; never load-bearing for correctness. ⚠ Breakage and outstanding liability both valued at each batch's **own** rate, from the same code as §18.7's dialog — §18.9 | ✅ **Decided — Matt, 2026-08-13** |
| 21 | The currency term is configurable | **Yes** — `NameSingular`/`NamePlural` are the **only** place the brand word lives. ⚠ **Code, tables, columns and DTOs use the neutral `Point`** (`LoyaltyPointEntry`, `PencePerPoint`); a `GemBalance` column would make one tenant's brand term permanent schema. "Point" not "credit", because `CreditEntry` already means money. ⚠ Must reach **every** surface incl. receipts and MAUI — §2 | ✅ **Confirmed — Matt, 2026-08-13** |

### Decisions 10–11, mechanics (so nobody re-derives them)

*"Tiers need to be set on the portal, but you need to be able to assign and change a tier on the tills
IF you have the correct permissions. Supervisor to change tiers. Till operator to add new loyalty
members."* — Matt, 2026-08-13. Recorded as **binding default 20** in `MAUI-retrofit.md`.

- **Assigning a tier** is `POST /api/v1/customers/{id}/membership`, already gated `customers.manage`,
  which Supervisor already holds. Screen work only, on **both** tills.
- **Adding a member** needs the new `pos.customers.add` (Cashier and up), with
  `POST /api/v1/customers` accepting **either** it **or** `customers.manage` — the same `CheckAny`
  shape as `pos.stock.adjust`, so managers need nothing new. ⚠ **Create-only, deliberately**: a
  cashier may add but not alter — changing a member's email quietly redirects their account, and
  changing a tier changes what every future basket is discounted by.
- ⚠ **This changes the WEB till too**: its create dialog is currently gated on `customers.manage`
  alone, so today a web-till cashier cannot add a member either. Both tills gain the new gate in the
  same slice, or Part B gains a drift row.
- ⚠ Adding a member is **online-only on every till**: the membership number comes from a tenant-wide
  counter, and two offline tills would mint the same one.

---

## 15. Core Entities (summary)

`Member` · `Tier` · `Sale` · `LineItem` · `Payment` · `LedgerEntry` · `Reward` · `ProgrammeConfig`

Relationships: a `Sale` optionally belongs to a `Member`; `LedgerEntry` rows reference `Member` and (for earns/burns) `Sale`; `Member` balance and tier are derived from the ledger and sales history; `ProgrammeConfig` holds the currency name/icon, earning rules, tier definitions, and lifecycle rules.

---

## 16. Surfaces & parity *(added at review, 2026-08-13 — "cover MAUI/Portal/Web")*

| Surface | Role in loyalty | Notes |
|---|---|---|
| **Web till** | Full POS loop: identify → surface balance → one-tap redeem → hold → finalise | The reference implementation for POS interactions (binding default 10) |
| **MAUI till** | **The same loop, same slice** — a capability is not done until its Part B row is ✅ for both | Offline rules are already settled and carry over unchanged: `LoyaltyCache` is a **display hint only** (name/tier/discount), never an input to redemption maths; redeeming needs online (the §9 hold *is* a server operation); adding a member needs online (§14 decision 11) |
| **Webstore (Woo)** | **Earns automatically** — see the ingest rule below; a webstore sale linked to a member (FE5.3 email match) earns like any other | ⚠ **Redeeming inside the Woo checkout is out of scope for v1** — Woo owns that checkout. Deferred, recorded, not forgotten |
| **Admin portal** | Programme config (currency, earning rules, tiers, rewards, lifecycle) + §11 reporting + per-member drilldown | The only place tiers and config are *created* (§14 decision 9) |
| **Member portal** | §10 — the member's own statement | ⚠ **A NEW SURFACE with a new authentication story**: members are `Customer` rows, not operators or portal users — nothing that exists today can log one in. Phase it separately (§17) |

### ⚠ The load-bearing rule: earning is computed AT INGEST, never on a till

`SalesIngestService` is the single choke point every channel already passes through — web till, MAUI,
webstore. Computing EARN entries there (the same pattern as `VatBandStamp`) means:

- **every channel earns identically**, including the webstore, with zero till-side code;
- **an offline MAUI sale earns correctly when its outbox drains** — no special case at all;
- **no till ever holds an earning rule**, exactly as no till holds a VAT rate. A rule that lived on
  tills would be version-skewed across the estate within a week of its first change;
- refund clawback (§8) rides the same machinery `SaleAdjustments` already provides.

The till's job is **identification and redemption UX**; the platform's is every number.

---

## 17. What exists today, and what changes *(verified against the code, 2026-08-13)*

**Already built and live** — this design extends a working foundation, it does not start from zero:

| Exists | Detail |
|---|---|
| `Customer` + `MemberNo` | FE2 — `NNNNNNC` with a Crockford check character, printable card, scan-to-attach on the web till |
| `LoyaltyTier` | FE1 — `Name, AutoDiscountRate, DurationMonths, Active, SortOrder`. **Manually assigned**; portal + web till dropdowns |
| **Store credit** | Pence balance on the customer, **append-only `CreditLedgerService`**, `POST /customers/{id}/credit/redeem`, a `CREDIT` tender on the web till (online-only, live-balance-checked) |
| Refund plumbing | Refunds reference the origin sale (`SaleAdjustments`) — the §8 clawback has its hooks already |
| Member scan | `C…` barcode routes to customer attach, not item lookup (web till; MAUI gets it in retrofit step 27) |

**Changes to existing things:**

- **`LoyaltyTier` grows** — threshold, benefits (earn multiplier, exclusive rewards), icon/colour,
  and the §5 qualification engine. ⚠ Assignment becomes **derived with a manual override** — the
  Supervisor till assignment (§14 decision 10) is the override path, and the downgrade rules (§5)
  decide what happens when the engine and an override disagree.
- ⚠⚠ **STORE CREDIT IS NOT "CREDITS", and merging them would make points into cash.** Store credit
  is money (typically from a refund), a real liability in pence, spent as a **tender**. Loyalty
  credits are earned points, represented as a **discount** (§14 decision 1, settled 2026-08-14 — and
  the *reason* they differ is the same reason: store credit was **paid for**, a gem never was). **Both exist,
  named distinctly everywhere a member or operator sees them** — "store credit £4.40" vs "1,240
  gems". A single bucket would let an earning promotion mint refundable cash.
- ⚠ **The sale header has NO member link today.** Verified: `IngestSaleRequest` carries
  `OperatorUserId` and nothing about a customer — the web till attaches a member for the *discount*
  and the sale arrives anonymous. §8 requires `CustomerId?` on the header: an **additive** wire +
  schema change touching both tills, the webstore mapper and ingest. Without it there is no EARN, so
  it is the first platform slice.
- **`LineMeta` gains `earnsCredits`** (§3 exclusions) — additive and null-safe, like `vatBand`:
  null means "the catalogue decides", stated only where the till knows better (a gift-card line).

**New builds, phased** *(±25% at least). ✅ **Phase A is NO LONGER GATED** — §14 decision 1 was
settled on 2026-08-14: redemption is a **discount**, so `DiscountApportionment.Across` +
`VatLineMath.ForLine` carry it and no new tender type is built:*

| Phase | What | ~ |
|---|---|---|
| **A — the engine** (platform) | `LoyaltySettings` (§18.2) · the `LoyaltyPointEntry` ledger with `HOLD`/`RELEASE` (atomic balance-and-burn) and per-entry expiry · earn-at-ingest · redemption-as-discount per decision 1 · `CustomerId` on the sale header · **FIFO consumption + refund symmetry in SharedKernel** (§18.5) | **15–20d** |
| **B — POS surfaces** | Web till (~4–5d) and MAUI (~5–6d, extends retrofit step 27): identify, balance surfacing, one-tap redeem, holds | **~10d** |
| **C — admin portal** | Config screens, §11 reporting (liability, breakage, engagement), drilldown | **5–7d** |
| **D — member portal** | The new surface **and member authentication** (customers are not users), statement, tier progress, QR, notifications | **15–20d** — the strongest v2 candidate |

⚠ **The retrofit's step 27 is the parity slice, not this programme.** Step 27 brings MAUI level with
what the web till has *today* (attach, store-credit tender, member scan, tier assignment, member add
per decisions 10–11, then gift cards). This design is platform-first work that follows it — building
the engine before the parity slice exists would put the cart before a horse that cannot yet walk.

---

## 18. The configured economics *(Matt, 2026-08-13 — the numbers, and what follows from them)*

> *"We already have tier'd discount. The credits/Gems value need to be set in the portal. Suggested is
> 'Each credit/gem' is worth £0.10. Earn one credit/gem for every £10 spent. Use as many
> credits/gems as you want on an order. Need to be able to set an expiry date or never."*

Applied in place above — §2 (value), §3 (earn), §4 (redemption), §6 (expiry), §14 rows 2/5/8/15.
This section holds the parts that are consequences rather than choices, so they are decided once here
instead of three times in three surfaces.

### 18.1 The rate is a 1% reward — worth sanity-checking against margin

£10 spent → 1 gem → £0.10 back. **That is 1% of revenue given away**, and because it comes out of
margin rather than turnover the real cost is 1%/margin — on a 40%-margin line, **~2.5% of gross
margin**. That is a normal, conservative retail rate (Clubcard is ~1%, Nectar ~0.5%). Noted only so
the figure is deliberate: it is the single number that decides what the programme costs, and it is
one portal field away from being 10× that by accident. Hence §14 row 17 — that field warns.

### 18.2 `LoyaltySettings` — one row per tenant, following `GiftCardSettings`

The platform already has a settled idiom for "a per-tenant money decision": a settings table whose
**absence is the gate** (`GiftCardSettings` — *"until the owner has chosen a treatment,
generate/activate/redeem all refuse"*). Loyalty copies it rather than inventing a second idiom.

| Field | v1 value | Why it is a column and not a constant |
|---|---|---|
| `PencePerPoint` | `10` | Matt's rate, and the number §18.1 is about. ⚠ **Applies to FUTURE earns only** — existing batches keep `PencePerPointAtEarn` (§18.8), so this is the rate the *next* gem is minted at, not a global multiplier |
| `SpendPerPointPence` | `1000` | "every £10" — the earn divisor |
| `ExpiryChoice` | `enum { NotChosen, Never, AfterMonths }` | ⚠ **The OWNER's decision, made explicitly** — Matt, 2026-08-13: *"Never expires needs to be a setting decision for the till owner."* `NotChosen` is the gate (`GiftCardSettings`' own idiom): the programme does not start earning until the owner has picked. **"Never" must be chosen, not defaulted into** — see §18.9 |
| `ExpiryMonths` | `int?` — required when `AfterMonths`, ignored otherwise | Rolling from the earn date. ⚠ Two fields rather than "null means never" so the schema can tell *"the owner chose never"* from *"nobody has decided"*; `GiftCard.ExpiresAtUtc` can use bare null because a card is expiry-per-batch by hand |
| `ExpiryWarnDays` | `30` | §6 wants warning before gems lapse; the lead time is the owner's call too. 0 = no warning |
| `NameSingular` / `NamePlural` | `"gem"` / `"gems"` | §2. ⚠ **Never the word "credit"** in member-facing text — store credit already exists and means *money* (§17). ⚠ The only place the brand term lives |
| `MaxRedeemPerOrder` | `0` = unlimited | Matt's ruling is unlimited; the column costs nothing now and a migration later. Another tenant will want a cap |
| `Active` | — | Turn the programme off without deleting a ledger |
| `DecidedByUserId` / `DecidedAtUtc` | — | Straight from `GiftCardSettings`: a money rule records who set it |

⚠ **Do not put these on `LoyaltyTier`.** They are programme-wide; per-tier *multipliers* are a §5
concern and belong on the tier row.

### 18.3 The gem ledger is a second ledger, and it counts gems

`CreditEntry` already exists and is exactly the right *shape* — append-only, signed, `Issue`/`Redeem`/
`Expire`, anchored to `SaleId` and `ActorUserId`, balance = Σ entries (D15). **Reuse the pattern, not
the table**: `CreditEntry.AmountPence` is **money**, and gems are a **count** (§2, §17).

`LoyaltyPointEntry` therefore differs from `CreditEntry` in exactly three ways:

| | `CreditEntry` (exists) | `LoyaltyPointEntry` (new) |
|---|---|---|
| Amount | `AmountPence` — money | `Amount` — **whole gems**, signed |
| Types | `Issue` / `Redeem` / `Expire` | `Earn` / `Redeem` / `Expire` / `Adjust` (§6 manual, reason mandatory) |
| Expiry | on the gift card, not the entry | **`ExpiresOn DateOnly?` on the entry** — §6, because batches expire independently |
| Rate | n/a — pence *are* the value | **`PencePerPointAtEarn` on the entry** — §18.8 grandfathering. Set on `Earn`; on a `Redeem` row it records the rate the consumed batch was valued at |
| Redemption granularity | one row | ⚠ **one `Redeem` row PER SOURCE BATCH**, each with `SourceEntryId` — see below |

⚠⚠ **A redemption is several rows, not one, and this is load-bearing rather than bookkeeping.**
Consuming £10 of gems may draw 10 @15p from January and 40 @10p from March. §18.5 requires a refund
to **restore the burn**, and restoring it correctly means putting back *those* batches at *those*
rates — not 50 gems at today's rate. Without per-batch rows a refund must either invent a rate or
silently re-value the member's balance, which is the precise failure grandfathering exists to
prevent. It also makes §10's statement explainable line by line, and §11's liability the **true**
Σ(count × rate) instead of `total × current rate`.

### 18.4 Redemption as a discount: the machinery already exists

This is a second, code-level argument for §14 decision 1 that the design did not have:

- `DiscountApportionment.Across` already spreads a basket-wide discount across lines
  **deterministically, to the penny, ties breaking on the earlier line**.
- `VatLineMath.ForLine` already splits each line's discount across net and VAT — so a gem
  redemption on a **mixed-VAT basket apportions correctly for free**. This is the thing that would
  otherwise be got wrong quietly and show up as a wrong VAT return.
- Both are already a **C2 twin** with the web till (`till/basket.ts`), so parity is already pinned.

A gem redemption is therefore *"a basket-wide discount of `gems × PencePerPoint`"* and reuses tested
code. As a **tender** it would instead need a new tender type, and would report gem "takings" in the
Z-read that never reached the bank — a reconciliation gap to be journalled out for ever.

### 18.5 ⚠ Two edge cases "as many as you want" creates

**A £0.00 basket can now happen** — 10% tier discount plus enough gems, and there is nothing left to
pay. Neither till has been shown to handle it:

- Web till: `CheckoutDialog.tsx:161` gates completion on `parsed.valid && remaining === 0`. With a
  zero total and no tenders, `remaining` *is* 0 — but whether `parsed.valid` holds for an empty
  amounts box is **untested**, and `TenderLoop` explicitly refuses a zero payment (`TenderRefusal.Zero`
  — *"Zero settles nothing, so accepting it is an infinite loop with a friendly face"*).
- The right answer is to **skip tendering entirely when the basket settles to zero** and complete the
  sale with no payment rows — not to invent a £0 payment. ⚠ It needs a test on both tills and a row
  in `Test Maui.md`; a fully-redeemed sale is exactly the demo someone will try first.

**Refunds are where gems become a cash-out exploit.** Both halves are required, and each is useless
alone:

1. **A refund must claw back what the sale earned.** Otherwise buy £1,000, refund it, keep 100 gems —
   a gem printer. Balance may go **negative**; redemption is blocked while it is (honest, and the
   append-only ledger makes it explainable).
2. **A refund must give back the gems the sale consumed.** `LineDiscounts` already states the money
   half correctly — *"RETURNS TAKE NO DISCOUNT… refunding a discounted sale gives back what the
   customer actually paid"* — so a customer who paid £90 after £10 of gems gets **£90 back**. If the
   gems did not also return they would simply have lost them, which is both unfair and a support
   ticket. ⚠ And note the inverse is the exploit: refunding the **full** £100 *and* returning the
   gems pays the discount out in cash. Exactly one of those two may happen.

### 18.6 ~~⚠ Found while checking this: MAUI applies no tier discount at all~~ → ✅ CLOSED 2026-08-16

> ✅ **Fixed by retrofit step 27** (`1f0aa9af` the members' discount engine, `274c6d53` the attach screen).
> MAUI now applies the tier discount through `SharedKernel.MemberDiscount`, shared with the web till.
> ⚠ 🟡 not ✅ on Part B until a person has run it. **The gap below was real on 2026-08-13 and is not now** —
> kept because it is the clearest example in this document of why §16's parity rule exists.

Not a design question — a **live parity gap**, found in the code today. `TillPage.tsx:122–123`
applies the member discount on the web till:

```ts
if (m && !m.expired && m.autoDiscountRate > 0)
  dispatch({ type: "applyMemberDiscount", rate: m.autoDiscountRate, … });
```

`autoDiscountRate` appears nowhere in `Plutus.Frontend.AppClient` — MAUI has **no customer attach at
all**, so it cannot apply it. **A Gold member is charged 10% more on the MAUI till than on the web
till, for the same basket, today.** That is the exact failure `till-design.md` Part B exists to
catch, and "we already have tier'd discount" is true only of the browser. It is inside retrofit
**step 27** and is the reason step 27 comes first.

### 18.7 Changing the rate: the warning, and the figures behind it

> *"Yes needs a warning, also saying something like 'Do not change this when you have live users. The
> store will have reputation damage. Are you sure you want to do this?' Then 'It will effect X
> customers, Y credits with a value change of Z'."* — Matt, 2026-08-13. Closes decision 17.

⚠⚠ **READ §18.8 FIRST — it supersedes the SEVERITY of this section, not its machinery.** Decision 19
adopted grandfathering, so a rate change can no longer take value from a member and **Stage 1's
reputation warning is no longer the right copy for the ordinary case**. Every mechanism below still
stands (the preview endpoint, the shared figures, the audit row, type-to-confirm); the wording and
the `danger` flag move to the informational form in §18.8. Stage 1 as written is retained because it
is still exactly right for **shortening `ExpiryMonths`**, which does destroy value.

Two stages, using the portal's **existing** confirm idiom — `Ask.tsx`'s `ConfirmOptions` already has
`danger` and `typeToConfirm`, and `UsersPage.tsx` already does type-to-confirm for deleting a user.
Do not build a third dialog.

**Stage 1 — the warning** (Matt's copy, with *affect* for *effect*, since this is a live UI string):

> ⚠ **Do not change this when you have live members.** The store will suffer reputation damage.
> Are you sure you want to do this?

**Stage 2 — the measured impact**, before the confirm button unlocks:

> This will affect **X members** holding **Y gems**, changing what they are worth from **£A** to
> **£B** — a change of **Z**.

`danger: true`, and `typeToConfirm` set to the **new rate in pence** so the owner has to type the
number they are imposing. Then an `AuditLog` row: `Action = "loyalty.rate.change"`, `EntityType =
"LoyaltySettings"`, `DetailJson` carrying `{from, to, members, gems, valueFromPence, valueToPence}`.
⚠ **The figures go in the audit record, not just the dialog** — the point of auditing this is
evidence of *what the owner was shown when they agreed to it*.

#### What X, Y and Z actually are — where this can quietly lie

| | Definition | ⚠ The trap |
|---|---|---|
| **X** | Members with a **non-zero, unexpired** balance as at now | Counting expired gems inflates X and the dialog cries wolf. Counting *all* members ever enrolled inflates it enormously |
| **Y** | Total **unexpired** gems outstanding | Same. §6 expiry is per entry, so "unexpired" is a per-batch test, not one date |
| **Z** | `Y × (new − old)` pence, shown **signed and with both totals** (£A → £B) | A bare delta hides the scale: "−£300" reads differently next to "£600 → £300" |

⚠⚠ **X, Y and £A must come from the same code as §11's outstanding-liability report.** If the
dialog and the report disagree, the portal contradicts itself about the store's own liability, and
whichever number is worse is the one the owner will remember. One function, one source, called by
both — a C1 concern even though it never leaves C#.

⚠ **Compute it server-side from the ledger when the dialog opens, never from a cached rollup.** A
stale figure inside a confirmation is worse than no figure: it makes a wrong number authoritative at
the exact moment someone is deciding. This needs a **preview** call — the impact depends on the
*proposed* rate, so a plain `PUT` cannot warn: `GET /api/v1/loyalty/settings/rate-preview?pencePerGem=5`.

#### ⚠ Two things the copy should not say when they are not true

- **If X = 0, skip the scary dialog entirely** and say so plainly: *"No members are holding gems, so
  this change affects nobody."* A warning shown when it does not apply is how people learn to click
  through warnings — and then the one that matters gets clicked through too.
- **Direction is not symmetric.** A **decrease** is a takeaway, and Matt's reputation sentence is
  exactly right for it. An **increase** is a giveaway: nobody's reputation suffers, but the store's
  **liability rises** by Z. Show the figures for both, and the reputation line only on a decrease —
  an owner being generous should not be told they are damaging their reputation.

#### The alternative that removes the problem instead of warning about it → ✅ ADOPTED, §18.8

**Grandfathering:** store `PencePerPointAtEarn` on each `LoyaltyPointEntry`, so gems keep the rate they were
earned under and a rate change only ever affects **future** earns. No reputation risk, no warning
needed, and it is the same shape as §6's per-entry expiry and the platform's existing habit of
stamping a rule onto the row it applied to (`VatBandStamp`, `Membership`'s as-assigned snapshot).

It was raised here as a *deferred escape hatch* — build the warning, stamp the column, decide later.
**Matt took it further and settled it immediately** (*"grandfathering but oldest gems are used first
… slowly removing the problem"*), which also answers the objection this block raised: see **§18.8**,
where the cost turns out to fall on the gem *count*, not on the balance's value.

### 18.8 Grandfathering + oldest-first: decision 19 settled, and it is the better design

> *"Can we make it grandfathering but oldest gems are used first in any transaction, but slowly
> removing the problem?"* — Matt, 2026-08-13. **Yes. Adopted.** Decision 19 ✅.

Each `LoyaltyPointEntry` carries `PencePerPointAtEarn`; redemption consumes **oldest first**; so every
redemption and every expiry drains the old-rate cohort, which can only ever shrink. A rate change
therefore **cannot take value from anyone**, and the mixed-rate population liquidates itself without
anybody administering it.

It also needs no new consumption rule — **§6 already says oldest-first** (to protect gems from
lapsing). The same ordering that stops a member losing gems to expiry is the one that drains the
legacy rate. ⚠ **One precision, or the two rules diverge:** oldest-*earned* and oldest-*expiring* are
the same order only while `ExpiryMonths` is unchanged; shorten it and a June gem can expire before a
March one. **Soonest-expiring wins, oldest-earned is the tie-break** — consuming by earn date would
let the sooner-expiring batch lapse, destroying value FIFO exists to protect.

#### ⚠ What has to change for it to work at the counter: the member-facing number becomes MONEY

This is the whole hinge, and it removes the objection I raised against grandfathering in §18.7.

Once batches hold different rates, **"spend 100 gems" has no single meaning** — its value depends on
*which* gems. So redemption is denominated in **money**, not in a count: the cashier asks *"how much
would you like to take off?"*, the engine consumes whatever batches that costs, oldest first.

Three things fall out, all good:

- **The balance has one honest answer again** — *"you have £23.50 in gems"*, computed as
  Σ(batch count × batch rate). My §18.7 worry that grandfathering costs the balance its single
  answer was wrong: it costs the *count* its single meaning, and the count was never the number a
  member cares about.
- **FIFO becomes value-neutral to the member.** Spending a money amount means batch order cannot
  change what they get — only which rows drain. Had we let them spend a *count*, oldest-first would
  actively disadvantage them after a rate rise. The fairness objection disappears entirely.
- ⚠⚠ **No till ever holds `PencePerPoint`.** The till receives a money balance and sends a money
  redemption; the rate stays server-side. That is a **C2 twin that never gets created** — exactly the
  CLAUDE.md principle. It also makes MAUI's offline `LoyaltyCache` hint correct by construction,
  since a cached *value* needs no rate to interpret.

⚠ Display the **value** as primary. If a count is shown too it must sit beside the value, never
instead of it — a member reading "240 gems" who multiplies by today's 10p and gets £24 against a
stated £23.50 is a support ticket. §10's per-batch statement is what answers it.

#### The honest limit: it converges for active members, not for hoarders

"Slowly removing the problem" is right, with one caveat worth stating before someone relies on it:

| | Old-rate cohort |
|---|---|
| **Active member** (redeems periodically) | Drains in a few redemption cycles — typically months |
| **Hoarder, `ExpiryMonths` set** | Drains **no later than `ExpiryMonths` after the change** — a guaranteed end date |
| **Hoarder, `ExpiryChoice = Never`** | ⚠ **Never drains.** Grandfathering is permanent for them |

⚠ So the *guaranteed* convergence date exists only when expiry is set — and Matt has deliberately
allowed "never". With never-expire, grandfathering is not a transitional state but a standing
feature: correct, self-consistent, and permanently mixed for the long tail. That is acceptable —
it just must not be described internally as temporary.

#### The one place it costs more: a redemption becomes several ledger rows

Consuming across batches means a redemption is **one `Redeem` entry per source batch**, each naming
its `SourceEntryId` and the rate it was valued at — not one row for the whole redemption.

This is not optional bookkeeping. §18.5 requires a refund to **restore the burn**, and restoring it
correctly means putting back *the same batches at the same rates* — 10 gems @15p from January and
40 @10p from March, not 50 gems at today's rate. Without per-batch rows a refund either invents a
rate or silently re-values the member's balance, which is the exact failure grandfathering exists to
prevent. It also makes §10's statement explainable line by line, and §11's liability the **true**
Σ(count × rate) rather than `total × current rate`.

#### The payoff: the §18.7 dialog stops being frightening

With grandfathering, changing `PencePerPoint` **affects future earns only**. So the confirm downgrades
from `danger` to informational, and Matt's reputation warning is no longer the right copy for the
ordinary case:

> This changes what gems are worth **from now on**. Existing balances are unaffected — **X members
> holding Y gems keep their earned rate**, worth **£A**, unchanged.

⚠ **Keep §18.7's machinery, all of it.** The preview endpoint, the shared-with-the-liability-report
figures, the `AuditLog` row and the type-to-confirm all still apply — a rate change is still a
commercial decision worth recording, and the figures are still the evidence of what the owner was
shown. What changes is only the *severity*, and it changes because the risk genuinely went away.
⚠ Retain the hard `danger` warning for the one case that still destroys value: **shortening
`ExpiryMonths`**, which §6 already forbids from applying retroactively.

### 18.9 Expiry: the owner's choice, and accounting for gems that lapse

> *"Never expires needs to be a setting decision for the till owner. We need to account for gems
> expiring."* — Matt, 2026-08-13. Decision 20.

#### The owner chooses, and "never" is a choice — not a default

`ExpiryChoice` starts at **`NotChosen`**, and the programme **does not earn** until the owner has
picked. That is `GiftCardSettings`' idiom exactly — *"until the owner has chosen a treatment,
generate/activate/redeem all refuse"* — and it exists for the same reason: an unmade decision must
not resolve itself silently into whichever branch the code happens to take first.

⚠ **Why two fields instead of "null means never".** A single nullable `ExpiryMonths` cannot
distinguish *"the owner decided gems never expire"* from *"nobody has decided yet"*, and those must
never be confused: one is a deliberate commercial promise to members, the other is an unconfigured
programme. `GiftCard.ExpiresAtUtc` gets away with bare null because expiry there is set per print
batch by hand; a programme-wide policy needs the distinction.

⚠ **Turning expiry ON later must not expire anything retroactively** — §6's rule, restated because
this is where it will be tempted. Entries carry their own `ExpiresOn`, computed **at earn time** from
the policy then in force. A batch earned while the policy was "never" has `ExpiresOn = null` **for
ever**, even after the owner switches to 12 months. This is the same grandfathering principle as the
rate (§18.8), applied to expiry, and it is why the hard `danger` warning now lives on **shortening**
the window rather than on the rate.

#### ⚠⚠ The balance must be correct even if nothing has swept — the gift-card precedent

The platform has already solved this, and **not** with a sweeper. Gift cards test expiry **on read,
at the point of use** — `GiftCardLedger.cs:57` and `:95` both guard
`card.ExpiresAtUtc != null && card.ExpiresAtUtc <= DateTime.UtcNow`, and
`GiftCardsController.cs:162` derives the `"expired"` **status** on read. **Nothing sweeps gift-card
expiry at all.** Follow it:

- **The authority is the date filter, computed on read.** An unexpired balance is
  `Σ over batches where ExpiresOn is null or ExpiresOn >= today`. This is correct **whether or not
  any job has run**, which is the only property that matters: if expiry lived in swept `Expire` rows
  alone, a job that failed on Friday would let members spend lapsed gems all weekend, and the till
  would be right to allow it.
- **The sweeper writes the accounting record, and is never load-bearing for correctness.** It is
  therefore free to be late, to be re-run, and to be skipped in a test.

#### The sweeper, and why it still has to exist

`RetentionSweeper : BackgroundService` (with `Task.Delay(_opts.Interval)`) is the shape to copy, and
it should record each pass to **`JobRun`** so a sweeper that silently stopped is visible rather than
inferred from a wrong report months later.

It writes one `Expire` entry per lapsed batch. Two rules:

- **Idempotent** — an `Expire` row already present for a batch means skip. Since correctness does not
  depend on these rows, a double-run must be harmless, not merely unlikely.
- ⚠ **Valued at the batch's OWN rate** (`PencePerPointAtEarn`), not today's. Grandfathering applies
  to lapsing exactly as it applies to spending; valuing breakage at the current rate would misstate
  the liability released in precisely the periods where a rate had changed.

#### Accounting for it: breakage is income, and it belongs to a period

Unredeemed gems are a **liability** (§13). When they lapse, that liability is **released** — which is
income, and an accountant will want it in the period it happened. So:

- **`Expire` rows are dated when the gems lapsed**, which is what makes "breakage in period" a real
  query rather than a snapshot difference. This is the reason the sweeper must exist even though the
  balance does not need it: a compute-on-read balance shows *what is left*, never *what was released
  and when*.
- **§11's report gains breakage per period**, alongside outstanding liability — and both must come
  from the same Σ(count × own rate) code as §18.7's dialog, or the portal will state three different
  liabilities in three places.
- ⚠ **The liability report must value outstanding gems at each batch's own rate too.** After any rate
  change, `outstanding × current rate` is simply the wrong number, and it is wrong in a direction
  nobody notices — it moves the moment a setting changes, with no transaction behind it.

#### At the counter and on the member's side

- **Warn before it happens** — `ExpiryWarnDays` (default 30). The till shows it on attach
  (*"£23.50 in gems — £3.00 expires on 14 Sep"*) and §10's statement shows it per batch, which is
  also what answers *"where did my gems go?"* when a batch has already lapsed.
- ⚠ **The offline MAUI hint may overstate.** A cached balance can include gems that lapsed since
  `LoyaltyCache.RefreshedAtUtc`. That is tolerable **only because redemption is online-only** (the
  step 27 rule for store credit), so the server always revalidates — but the hint must be labelled
  *as at* its refresh time, and must never be the figure a redemption is computed from.
- ⚠ **If the owner sets expiry, the member has to have been told.** §13's T&Cs and §10's per-batch
  statement are the mechanism; expiring points a member was never warned about is the reputational
  damage §18.7's warning was originally worried about, arriving by a different route.
