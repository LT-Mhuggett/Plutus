> **📦 SUPERSEDED — the MAUI column moved to [`Build/To do/MAUI-Retrofit-Plan-2026-08-07.md`](../To%20do/MAUI-Retrofit-Plan-2026-08-07.md).**
> The web-POS column was closed on 2026-07-25 and stays closed. The four MAUI to-dos (effective
> pricing, customer attach, store credit, CustomerId on the sale) are now WP10 and WP12 there.
> Row 4 (card capture) is greenfield for both tills — there is no integration code anywhere in the
> repo — and is tracked as a risk, not a work package.

# Till retrofit — closing the frontend gap to the platform backend

**Date:** 2026-07-25 · **Applies to:** the web POS (`Plutus.Frontend.WebApp`) and the MAUI
till (`Plutus.Frontend.ClientUI`, upstream, Phase 4 paused).

Phases 2–8 added backend capability faster than the tills consumed it. This doc is the
authoritative list of what each till must retrofit to be feature-complete against the
platform. **The web POS items are IMPLEMENTED (2026-07-25); the MAUI items are documented
here as the to-do for when the upstream MAUI code is re-baselined.**

Money is always integer pence. Every new call is same-origin `/api/v1/*` with the existing
bearer (operator session token for reads/redeem; device token for the sale itself).

> **Status (re-verified 2026-08-07): KEPT OPEN — the MAUI column is still the outstanding work.**
> The web-POS column is closed. `MAUI-Backend-Sync-Plan-2026-08-01.md` §Feature-parity retrofit
> adopts this gap list wholesale, so this table remains the authoritative to-do; don't duplicate it
> there. Row 4 (card capture) is blocked on a payment-provider decision for **both** tills, and
> row 5 needs a small backend contract addition that nobody has scheduled.

---

## The gap list

| # | Feature | Backend (since) | Web POS | MAUI |
|---|---------|-----------------|---------|------|
| 1 | **Effective pricing** — resolve sell price from the pricing engine, not the legacy `Item.price` | WP5.4 `GET /api/v1/prices/effective` | ✅ implemented | ⬜ to-do |
| 2 | **Customer attach + members' auto-discount** | Phase 8 `/api/v1/customers/*` | ✅ implemented | ⬜ to-do |
| 3 | **Store credit as a tender** at checkout | Phase 8 `/api/v1/customers/{id}/credit/redeem` | ✅ implemented | ⬜ to-do |
| 4 | **Card capture events** for settlement reconciliation | WP7.1 `POST /api/v1/payments/events` | ⏸ blocked | ⏸ blocked |
| 5 | **CustomerId on the sale record** (nice-to-have) | needs a small backend contract add | ⬜ backend follow-up | ⬜ backend follow-up |

---

## 1. Effective pricing (WP5.4)

**Why:** the till reads `Item.price`/`Item.exPrice` from the legacy catalogue, so portal
price changes (central price list, store overrides, scheduled Sunday-night repricing) never
reach the till. The pricing engine already resolves *store override → central price list →
legacy baseline* per item.

**Contract:** `GET /api/v1/prices/effective?items={idOne[,idOne…]}&storeId={n}[&at=ISO]`
→ `[{ itemIdOne, pricePence, exPricePence, source, policy }]` (any authenticated principal).

**Behaviour to implement:**
- When an item is **added to the basket**, resolve its effective price and use that for the
  line's unit price (inc + ex VAT), not the catalogue price.
- **Offline fallback:** on any failure, use the cached legacy `Item.price`/`exPrice`. (The
  till still sells offline; prices catch up when connectivity returns.)
- A price *override at the till* (manual price adjust) still wins over the resolved price —
  it sets `overriddenFromPence` on the ingest line as today.
- Catalogue *search previews* may keep showing the list price; the authoritative price is
  resolved at add-time. (Web POS does this; note it in the MAUI UI too.)

**Web POS:** `effectivePriceFor(item)` in `api.ts`; `TillPage.addItem` awaits it and adds a
price-adjusted item copy.

**MAUI:** resolve in the add-to-basket command (the C# generated client already exposes the
endpoint); fall back to the SQLite-cached item price when offline.

## 2. Customer attach + members' auto-discount (Phase 8)

**Why:** no way to attach a customer to a sale, so store credit and loyalty can't be used at
the till.

**Contracts:**
- `GET /api/v1/customers?search=&take=` → list (id, name, email, phone).
- `GET /api/v1/customers/{id}` → detail incl. `creditBalancePence` and `membership`
  `{ tier, autoDiscountRate, renewalDay, expired }`.

**Behaviour to implement:**
- A **customer control** on the till: search → attach; show name + credit balance + active
  membership; a way to clear it. Cleared automatically when a sale completes.
- When a customer with an **active, non-expired membership** is attached, auto-apply the
  members' discount (`autoDiscountRate`, a fraction) as a **line discount** to eligible
  lines — non-return lines that don't already carry a discount (no stacking; a manual
  discount wins). New lines added while the customer is attached get it too.
- **Bridge caveat (important):** the members' discount must NOT be emitted into the ingest
  line's `discountsJson.discounts[]` (that array maps to legacy `Transaction_Discount` rows
  by real `DiscountId`; a synthetic id would FK-fail the legacy bridge). Instead it flows
  only through the line's `discountPence` (correct money everywhere). Web POS marks the
  member discount with sentinel `discountId: 0` and filters `id !== 0` out of the projection
  metadata. **MAUI must do the same.**

**Web POS:** `searchCustomers`/`getCustomer` in `api.ts`; a customer chip + search in
`TillPage`; `applyMemberDiscount`/`clearMemberDiscount` basket actions.

**MAUI:** a customer picker bound to the basket VM; apply the discount in the basket engine
with the same sentinel-and-filter rule.

## 3. Store credit as a tender (Phase 8)

**Why:** a customer's store-credit balance can't be spent at the till.

**Contract:** `POST /api/v1/customers/{id}/credit/redeem`
`{ amountPence, saleId, entryId, reason }` — idempotent by `entryId`; `400` if it would
overdraw the live balance.

**Behaviour to implement:**
- At checkout, when a customer with `creditBalancePence > 0` is attached **and the till is
  online**, offer **"Store credit"** as a payment method, capped at `min(balance, amount
  due)`. Store credit is **online-only** (it needs a live balance check) — hide/disable it
  offline.
- The credit portion becomes a **`Credit` tender (tenderType 3)** on the sale (net tender
  must still equal gross — credit gives no change).
- On completion: **redeem first** (with the sale's `saleId` and a freshly-minted `entryId`),
  then record the sale. Redeem is idempotent, so a retry/redelivery is safe; the sale
  carries the credit tender. If redeem fails (overdraw/again) the sale is not recorded and
  the operator sees the error.

**Web POS:** synthetic method `id:-1` in `CheckoutDialog` (gated on `navigator.onLine` +
attached customer + balance); `checkout(..., { customerId, creditRedeemPence })` redeems
then records; `redeemCredit` in `api.ts`.

**MAUI:** add a "Store credit" tender in the payment VM under the same online + attached +
balance conditions; call redeem-then-record with the same idempotency discipline.

## 4. Card capture events (WP7.1) — ⏸ BLOCKED (both tills)

When a real card provider adapter exists (Dojo / Stripe Terminal / SumUp / Adyen / Zettle —
**awaiting Matt's commercial choice**), each card capture must `POST /api/v1/payments/events`
`{ eventId, providerRef, amountPence, capturedAtUtc }` with the **terminal reference**, and
that same `providerRef` must be written onto the card `SaleTender` — that linkage is what the
reconciliation queue matches (D13, "money taken but sale not recorded"). Until the adapter
lands there is nothing to send, so this is documented but not built in either till. Both
tills get it at the same time as the adapter.

## 5. CustomerId on the sale record — backend follow-up

Attaching a customer currently drives pricing + credit but the **sale row itself doesn't
store the customer** (`IngestSaleRequest`/`SaleV2` have no `CustomerId`). Credit entries carry
the `saleId`, so the credit linkage exists, but "which customer bought this" isn't on the
sale for reporting. Small additive backend change (nullable `CustomerId` on the ingest
contract + `SaleV2` + a rollup/report surface) — deferred; not required for the flows above.

---

## Verification (both tills)

- A portal central-price change is reflected in the next basket add (online).
- Attaching a member auto-discounts the basket; the sale's line `discountPence` includes it;
  the legacy bridge does **not** dead-letter (no bogus `Transaction_Discount`).
- Paying partly with store credit: the balance drops by exactly the redeemed amount, the
  sale records a `Credit` tender, net tender == gross, and a redelivery doesn't double-deduct.
- Offline: sales still queue and drain; store credit is unavailable; prices use the cache.
