# WooCommerce fixtures — Phase 6 connector (WP6.0)

Real REST-shaped JSON captured from the **live** Kapow Comics store (kapow-comics.co.uk,
WooCommerce 10.9.4) on 2026-07-26, then **PII-scrubbed**. These let the connector (WP6.1–6.5)
be built and unit/integration-tested WITHOUT touching the live site — the live store is for
read-only smoke checks and final DoD proof only (plan rule 4).

## PII scrubbing

Every order fixture has been run through a scrubber. Guaranteed removed / replaced:
- `billing` / `shipping` → synthetic "Test Customer, 1 Example Street, Testville TE5 7ND"
- `email` → `customer@example.test`; `phone` → `01234 567890`
- `customer_ip_address` → `127.0.0.1`; `customer_user_agent` → scrubbed
- `transaction_id`, `order_key`, `cart_hash`, `payment_url`, `customer_note` → placeholders
- `meta_data` → stripped to a safe allowlist (`is_vat_exempt` only) — removes order-attribution,
  referrer, PayPal, and customer-source tracking keys
Products carry no customer PII and are kept verbatim (public catalogue data).
**If you re-capture, re-run the scrubber (`scratchpad/scrub.ps1`) before committing.**

## What each fixture demonstrates

| File | Why it's here |
|------|---------------|
| `order-7127.json` | 4-line completed order, account customer, PayPal, 20% VAT lines |
| `order-7137.json` | 3-line completed order |
| `order-8502.json` | Larger completed order (£115.49) |
| `order-guest-8505.json` | **Guest** checkout (`customer_id: 0`) — the no-account path |
| `order-8347.json` / `order-7878.json` | **Refunded** orders (pair with the refund files) |
| `refunds-8347.json` / `refunds-7878.json` | Refund records: whole-order `amount`, empty `line_items`, `refunded_payment:false` |
| `product-5106.json` | Simple product, **out of stock** (`stock_quantity:0`, `in_stock:false`), on sale (regular vs sale price), images/categories/tags (web-only fields) |
| `product-4936.json` | Second simple product |

## Structural facts the mapper (WP6.2) must honour

- **`prices_include_tax: true`** on the store, BUT line `total`/`subtotal` are **net** (ex-VAT);
  `total_tax` is the VAT; the order `total` is gross. (7127: 85.98 net + 17.18 VAT = 103.16 gross.)
  Trust these fields — do not recompute; Woo's per-line `taxes[]` can differ by a rounding penny.
- **SKU = the item barcode** (EAN-13, e.g. `5011921156993`) → joins to Plutus `Items.IdOne`.
- **`tax_lines[].rate_percent: 20`**, label "20% Vat" → per-line `VatRate`.
- `customer_id: 0` = guest; `payment_method` (`paypal`) drives tender mapping.
- **HPOS is OFF** on this store (`custom_orders_table_enabled = no`) — orders live in `wp_posts`;
  the `wc_orders` table is an inert leftover. The REST API abstracts this, so the connector is
  unaffected, but don't query `wc_orders` directly.
- `date_modified` is the incremental-sweep cursor field (products and orders).

See `Build/woo-sku-audit-2026-07-26.md` for the catalogue/SKU match analysis.
