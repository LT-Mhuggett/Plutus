> **📦 ARCHIVED — point-in-time audit (2026-07-26).** Fed WP6.0; the Woo connector shipped and the
> inbound path is LIVE. The figures below are a snapshot of the live Kapow store on that date and
> will have drifted — re-run the method in §Method rather than trusting the counts.

# Woo ↔ Plutus SKU match audit (WP6.0)

**Date:** 2026-07-26 · **Store:** kapow-comics.co.uk (LIVE, WooCommerce 10.9.4) ·
**Plutus DB:** `plutus` on the Mac test env (10.1.1.40)

## Method

- Woo: distinct `_sku` values of **published** products (`post_type=product`, `post_status=publish`),
  read directly from `postmeta` as a single column (a multi-column TSV export was discarded —
  comic titles contain tab characters that misaligned it).
- Plutus: `SELECT IdOne FROM Items` (the barcode column) — 20,341 rows, all distinct, none null.
- Match = exact string equality of Woo SKU to a Plutus `IdOne`.

## Results

| Metric | Count |
|---|---:|
| Plutus items (all with a distinct barcode) | 20,341 |
| Woo published products | 739 |
| — with a SKU | 648 |
| — **without** a SKU | 91 |
| Distinct published SKUs | 648 |
| **Matched** to a Plutus barcode | **596 (92.0%)** |
| **Unmatched** (SKU not in Plutus) | **52** |
| Woo product variations | 0 (no variable products) |

## The unmatched 52 — shape

| SKU length | Count | Likely nature |
|---|---:|---|
| 26 digits | 17 | Composite/internal codes (bundles, store-specific) — not EANs |
| 13 digits | 13 | EAN-13 / ISBN-13 (books & graphic novels — `978…`/`979…`) genuinely absent from Plutus |
| 9–12 digits | 21 | Shorter product codes not in the till catalogue |
| (empty) | 1 | One product with a whitespace-only SKU — data-quality fix on the site |

These 52 + the **91 SKU-less published products** = **143 products needing attention**, and they
are the seed for **WP6.2's `WebstoreSkuMap` review queue** (bind SKU→item, ignore, or — per
WP6.5 — create as a new Plutus item). The 26-digit composites and whitespace SKU are also a
data-quality nudge for the shopkeeper. Full unmatched list in the appendix.

## Live-site cost measurements (REST, read key)

Measured against the live VPS on 2026-07-26 — these calibrate the sweep cadence (ground rule 3):

| Call | Result | Implication |
|---|---|---|
| `GET orders?per_page=5` | 200, **2.1 s**, 28.5 KB | Poll is the backup net, small pages |
| `GET products?per_page=10` (page 1 of 75) | 200, **1.6 s**, `X-WP-Total: 745` | Full sweep ≈ 75 × 1.6 s ≈ **2 min** → nightly, off-peak |
| `GET products?modified_after=<yesterday>` | 200, **1.6 s**, `X-WP-Total: 7`, 1 page | Incremental sweep is ~1 page — **near-free**, safe every 15–30 min |

The VPS is visibly slow (~1.6–2 s/request) — confirms the small-page / generous-interval design;
webhooks (push) remain the primary transport, polling the reconciliation backstop.

## Fixtures captured

10 PII-scrubbed fixtures under `tests/Fixtures/Woo/` (6 orders incl. guest + 2 refunded, 2 refund
records, 2 products). See that folder's README for the structural facts (net line totals +
separate VAT, SKU=barcode, HPOS off, etc.).

## Read-only key

Minted via wp-cli, `read` permission only, recorded in `Build/secrets.local.md` (gitignored).
A `write` key is deferred to WP6.3 approval (plan rule 5).

---

## Appendix — the 52 unmatched published SKUs

```
00001641650000114593746001
00005632365000094593747001
00019712375000084593747004
00075022375000094593747005
00346992370000084593747006
00524791660000084593747003
00720112390000084257885005
00768281620000114593746002
00835802350000084593747007
02045932370000084593747008
02629212396000084257885009
02719072398000084257885008
02976072398000104518249068
03064872398000084257885002
03078762394000084593747010
03078822396000084257885003
03093962396000084257885006
085930100853
1561631094
1882931459
1901618013
1901618021
552079243
709853035596
725130314420
759606203741
761568003154
761568010510
761941200491
761941208398
761941210995
761941211138
761941237190
761941366623
827714121117
850026217049
850055994317
858992003017
9780486808727
9780874160864
9781534313583
9781561630844
9781561631698
9781561632039
9781561632244
9781606995242
9781882931293
9781882931699
9781901618006
9781975335922
9798886561203
99440199160
```
