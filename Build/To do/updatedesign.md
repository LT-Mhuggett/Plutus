# Loyalty Offering — Design Document

**Product:** Loyalty section for store / club platform
**Author:** Matt Huggett (Leading Talent) with Claude
**Date:** 13 August 2026
**Status:** Draft for review

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
| Name | Singular and plural forms, both configurable ("1 gem" / "250 gems") — appears throughout the UI |
| Icon / visual | Configurable icon or uploaded image, with a fallback (emoji or default graphic) if none set. Must render well small (nav badge) and large (balance page) |
| Value semantics | Abstract points vs money-mapped (e.g. 1 credit = £0.01). This decision affects redemption mechanics and accounting — see §7 and §13 |

---

## 3. Earning Rules

Each earning source is individually configurable and toggleable:

- **Purchases** — credits per £ spent, with configurable rounding (up / down / nearest)
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
- **No earn on credit-paid amounts** — members do not earn credits on the portion of a sale paid with credits, preventing a compounding loop
- Earning is calculated on sale completion and written to the ledger as an EARN entry referencing the sale (§9)

---

## 4. Redemption

- **Reward catalogue** — each reward has a configurable credit cost. Reward types: percentage discounts, fixed-value vouchers, free products, experiences, tier-exclusive items
- **Mechanics to configure** — redeem directly at checkout vs converting to a voucher first; whether credits can part-pay or must cover the whole reward; minimum balance to redeem; minimum spend requirements
- **Refund handling** — refunds reference the original sale: earned credits are clawed back and spent credits restored automatically (§9)

### Key decision: discount vs tender

How a redemption is represented on the sale:

- **As a discount** ("500 gems = £5 off") — simpler to build, sits naturally in discount reporting, standard retail treatment; VAT calculated on the reduced amount. **Recommended default.**
- **As a tender** (credits are a wallet that part-pays) — more flexible, but credits then behave like money, with heavier accounting and VAT implications.

⚠️ Confirm with the accountant before committing — this is expensive to change later.

---

## 5. Tiers / Levels

- **Qualification basis (configurable):** lifetime spend, rolling 12-month spend, credits earned, or number of visits
- **Spent credits should still count toward status** — otherwise members hoard rather than redeem
- **Per-tier configuration:** name, icon/colour, threshold, benefits (earn multiplier, exclusive rewards, perks)
- **Downgrade rules:** whether members can drop a tier, and with what grace period. This is the most emotionally sensitive rule in the system — design and communicate it carefully

---

## 6. Credit Lifecycle Rules

- **Expiry:** fixed-date, rolling from earn date, or inactivity-based — with warning notifications before credits lapse
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
| 1 | Credits as discount or tender | Discount | ⚠️ Confirm with accountant |
| 2 | Credit value semantics (abstract vs money-mapped) | Abstract points | Open |
| 3 | Tier qualification basis | Rolling 12-month, spent credits count | Open |
| 4 | Tier downgrade policy & grace period | — | Open |
| 5 | Expiry policy | Inactivity-based with warnings | Open |
| 6 | Transfers / family pooling in v1? | Defer to v2 | Open |
| 7 | Enrollment: opt-in or automatic | — | Open |
| 8 | Earn on VAT-inclusive or net amount | — | Open |
| 9 | Where tiers are **configured** | Portal only | ✅ **Decided — Matt, 2026-08-13** |
| 10 | Assign / change a member's tier at a till | **Supervisor and up** (`customers.manage`, which Supervisor already holds — no permission work) | ✅ **Decided — Matt, 2026-08-13** |
| 11 | Add a new member at a till | **Till operator (Cashier and up)**, via a new `pos.customers.add` — create-only; editing and tiers stay `customers.manage` | ✅ **Decided — Matt, 2026-08-13** |
| 12 | Where earning is **computed** | At **ingest**, server-side, never on a till — §16 | Proposed (follows the platform's own precedent) |
| 13 | Loyalty credits vs the existing **store credit** | Two distinct things, named distinctly — §17 | Proposed |

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
  credits are earned points, represented per §4 decision 1 (recommended: discount). **Both exist,
  named distinctly everywhere a member or operator sees them** — "store credit £4.40" vs "1,240
  gems". A single bucket would let an earning promotion mint refundable cash.
- ⚠ **The sale header has NO member link today.** Verified: `IngestSaleRequest` carries
  `OperatorUserId` and nothing about a customer — the web till attaches a member for the *discount*
  and the sale arrives anonymous. §8 requires `CustomerId?` on the header: an **additive** wire +
  schema change touching both tills, the webstore mapper and ingest. Without it there is no EARN, so
  it is the first platform slice.
- **`LineMeta` gains `earnsCredits`** (§3 exclusions) — additive and null-safe, like `vatBand`:
  null means "the catalogue decides", stated only where the till knows better (a gift-card line).

**New builds, phased** *(±25% at least, and phase A is gated on §14 decision 1 — the accountant)*:

| Phase | What | ~ |
|---|---|---|
| **A — the engine** (platform) | `ProgrammeConfig` · loyalty ledger with `HOLD`/`RELEASE` (atomic balance-and-burn) · earn-at-ingest · redemption per decision 1 · `CustomerId` on the sale header | **15–20d** |
| **B — POS surfaces** | Web till (~4–5d) and MAUI (~5–6d, extends retrofit step 27): identify, balance surfacing, one-tap redeem, holds | **~10d** |
| **C — admin portal** | Config screens, §11 reporting (liability, breakage, engagement), drilldown | **5–7d** |
| **D — member portal** | The new surface **and member authentication** (customers are not users), statement, tier progress, QR, notifications | **15–20d** — the strongest v2 candidate |

⚠ **The retrofit's step 27 is the parity slice, not this programme.** Step 27 brings MAUI level with
what the web till has *today* (attach, store-credit tender, member scan, tier assignment, member add
per decisions 10–11, then gift cards). This design is platform-first work that follows it — building
the engine before the parity slice exists would put the cart before a horse that cannot yet walk.
