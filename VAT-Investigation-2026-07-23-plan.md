# VAT Investigation — findings & remediation plan

**Date:** 2026-07-23
**Scope:** Is VAT calculated and recorded correctly in the NatApp till and the new webapp?
**Status:** Investigation complete (read-only — no code changed). Remediation is a plan awaiting approval.
**Data basis:** the restored Kapow live-till database (`plutus` schema on the Mac), period 01/01/2026–30/06/2026 unless stated.

---

## 1. Executive summary

1. **The two reports agree with each other and with the database.** H1-2026: **ex-tax £41,982.30, tax £2,078.23, total £44,060.53** — verified directly in SQL. (The NatApp print-out's "4406.53" total was a transcription slip; both systems hold 44,060.53.) The webapp's *Custom* report simply doesn't display the split yet — a display gap, not a data gap (§5.1).
2. **However, the VAT figure itself is only as good as the per-item price records — and those are provably wrong for at least 47 items.** Both tills compute sale VAT as Σ(line `Price` − line `ExPrice`), and `ExPrice` is a free-typed field that is not forced to match the item's tax band. Consequences found in the live data:
   - the **Exempt band shows −£56.72 of "VAT"** across 4,306 H1-2026 lines — impossible if recorded correctly;
   - **27 Exempt items** have `ExPrice ≠ Price` (worst: *ELUSIVE SAMURAI GN VOL 01*, price £7.99, ex-price **£799.00** — a decimal-entry slip; *FEARLESS TP* price £1.00 vs ex £14.50 — price reduced without touching ex);
   - **20 items in the 20% band** where `Price ≠ ExPrice × 1.2` (e.g. *Marvel's Rogue AF*: price 24.99 = ex 24.99 → 0% VAT charged on a standard-rated item);
3. **Discount records have lost their rate.** Every 2026 `Transaction_Discounts` row stores **DiscountRate = 0** (2019-era rows correctly stored 0.10). The sale headers still subtract the discount from `Total`/`TotalExTax`, so money is right — but per-line/per-band VAT reconstruction is impossible for discounted sales, and ~£1,253 of H1-2026 header-vs-lines gap (369 sales) cannot be attributed.
4. **Net effect:** headline VAT (£2,078.23) is *internally consistent* but **not fully trustworthy as a tax figure**, because it inherits every bad `ExPrice` on the ~47 broken items and the sign errors visible in the Exempt band. The error size is quantifiable (§5.2 step 1) and correctable both retrospectively (data repair) and prospectively (entry-validation fixes).

---

## 2. How VAT actually flows (both tills)

```
Item record: Price (inc VAT), ExPrice (ex VAT), TaxId  ← free-typed at item entry/edit
        │
Basket line: PriceExTax ← copied from item (or adjusted at till)
        │
Checkout: Sale.Total      = Σ line Price × qty (± returns, − discounts)
          Sale.TotalExTax = Σ line PriceExTax × qty (± returns, − discounts)   ← NatApp TillViewModel.cs:851
        │
Reports:  Tax = Total − TotalExTax        (both NatApp report and webapp headline)
```

- **NatApp**: `ExPrice` is stored per item and *nothing enforces* `Price = ExPrice × band rate` at entry, edit, or till-side price adjustment (the Adjust dialog sets Price and PriceExTax independently). `DiscountRate` is only ever *read* in `SalesReportsViewModel` — the checkout write path stores 0 (write-site confirmation is remediation step §5.3-2).
- **Webapp** (better, by construction): the item editor **derives** ex-price from the selected tax band (no free-typing); till price-adjust **scales ex-price proportionally** to keep the band ratio; discounts store the real rate in `Transaction_Discounts` and apportion the ex-tax reduction by the line's ex/inc ratio; returns refund at the recorded sold prices. Webapp-recorded sales therefore reconcile to the penny — but the webapp **inherits** bad `ExPrice` values on legacy items when they're scanned, so it is not immune until the catalogue is repaired.

---

## 3. Verified facts (all read-only SQL against the restored DB)

| Check | Result |
|---|---|
| H1-2026 headers | 1,501 orders; ex 41,982.30; tax 2,078.23; total 44,060.53 — matches NatApp report exactly |
| Band split from lines (H1-2026) | Exempt: net 31,017.93, VAT **−56.72** ⚠ · 20%: net 11,958.48, VAT 2,394.15 |
| Headers vs lines (H1-2026) | 369 sales differ; lines exceed headers by £1,253.31 (unrecorded discount value) |
| 2026 discount rows | 402 rows, **all DiscountRate = 0** ⚠ |
| Catalogue integrity | 27 Exempt items with `ExPrice ≠ Price`; 20 20%-band items with `Price ≠ ExPrice × 1.2` ⚠ |
| Refunds in period | 0 (not a factor here) |

Band-lines VAT (2,394.15 − 56.72 = 2,337.43) vs header VAT (2,078.23): the difference is the unrecorded discounts plus the bad-item noise — consistent with the above.

---

## 4. Findings register

| # | Finding | Where | Severity |
|---|---|---|---|
| F1 | `ExPrice` free-typed; no band-consistency enforcement at item entry/edit | NatApp item entry (also pre-fix MAUI) | **High** — corrupts every downstream VAT figure |
| F2 | 47+ catalogue items with band-inconsistent prices (incl. £7.99/£799.00 decimal slip) | Live data | **High** — direct VAT misstatement |
| F3 | `Transaction_Discounts.DiscountRate` written as 0 (current NatApp era) | NatApp checkout write path | **Medium** — headers right, attribution impossible |
| F4 | Till price-adjust can set Price/ExPrice independently | NatApp Adjust dialog | Medium — band ratio breakable per sale |
| F5 | Webapp Custom report shows no period ex/tax/total split | Webapp UI | Low — display only (Summary + VAT tabs have it) |
| F6 | Historic ETL: 30 skipped malformed-id items → lines missing under intact headers | Test DB only | Low — reconciliation noise, already documented |

---

## 5. Remediation plan (in order — nothing implemented yet)

### 5.1 Quick display fix (webapp, ~minutes)
Add an **Ex tax / Tax / Total totals strip** to Reporting → Custom (per-row split already exists), matching the NatApp report footer, so the same three figures are visible side-by-side in both systems.

### 5.2 Data repair (catalogue + quantification, ~an hour, reversible)
1. **Quantify the misstatement**: recompute H1-2026 VAT with band-derived ex-prices (`Price/rate`) and diff against 2,078.23 → gives the £ error to report to the bookkeeper.
2. **Repair the 47 items** (script): set `ExPrice = round(Price / band rate, 2)`; log every change; the obvious decimal slips (799.00) corrected against `Price`. Run against the *live NatApp SQLite* too, not just the test MySQL — that's where the shop trades.
3. Re-run the §3 integrity queries as acceptance (Exempt VAT must be exactly 0).

### 5.3 NatApp till fixes (small code changes, per BugFix-doc conventions)
1. **Item entry/edit derives ExPrice from the band** (display it, don't free-type it), with an "override" affordance only if the business genuinely needs off-band pricing.
2. **Find and fix the `Transaction_Discounts` write site** so `DiscountRate` records the real rate again (regression: 2019 data has rates, current data doesn't).
3. **Adjust dialog**: scale `PriceExTax` proportionally when Price changes (the webapp already does this) instead of independent fields.
4. Add the same derivation fix to the MAUI ClientUI's add/edit inventory screen.

### 5.4 Guard rails (webapp/backend, small)
1. Backend validation on Item create/update: reject/flag `|Price − ExPrice × rate| > 1p` unless an explicit override flag is set.
2. A **"VAT integrity" line on the Reporting → VAT tab**: run the Exempt-band-must-be-zero and off-band-items checks live and show a warning banner with counts, so this never regresses silently.
3. Discount allocation in the Summary endpoint (rate × line, per band) — meaningful once 5.3-2 restores real rates.

### 5.5 Acceptance criteria
- Exempt band VAT = £0.00 exactly for any period.
- For webapp-recorded sales: header VAT = Σ band VAT − allocated discounts, to the penny.
- Off-band item count = 0 (or every exception carries an explicit override flag).
- NatApp and webapp show identical ex/tax/total for any shared period (already true — must stay true after repairs).

---

## 6. What was *not* done
No code, schema, or data changes anywhere — investigation queries were read-only. The webapp/NatApp/MAUI all behave today exactly as before this document.
