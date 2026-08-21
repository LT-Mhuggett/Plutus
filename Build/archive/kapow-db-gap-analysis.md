# Current Database — Gap Analysis

> ## 📦 ARCHIVED 2026-08-20 — its job is done. ⚠ Still the only map of the SOURCE schema, so read §4 and ignore §5
>
> **This did what it was written to do: every finding it raised has been answered.** Checked against the
> code, not against its own headers:
>
> | Finding | Now |
> |---|---|
> | **F1** Sale IDs are timestamp strings and will collide across tills | ✅ Fixed — `Uuid7`, time-sortable and per-till safe |
> | **F2** VAT is not stored on the sale | ✅ Fixed — `VatBandStamp` + the VAT columns on `SaleLine`; the declared rate is derived from the inc/ex pair |
> | **F3** Money is decimal TEXT everywhere | ✅ Fixed — **integer pence throughout**, and mechanically enforced by `No_module_declares_decimal_or_double_money_members` in the architecture suite |
> | **F5** Stock is a mutable counter, and it's deeply negative | ✅ Fixed — `StockLevel` + a stock ledger with reasons |
> | **F4** ⚠⚠ **The item primary key is the barcode** | ⛔ **DELIBERATELY NOT FIXED, and it never will be.** `Item.IdOne` remains the barcode *and* the identity: it seeds `DeterministicGuid.ForItem` (a frozen golden vector with a TypeScript twin), it is half a composite PK with five FK families on it, and it is on all 74,830 historical sale lines. Re-keying was never a migration — it was a rewrite of the sale history. ✅ **The BEHAVIOUR F4 wanted arrived anyway**, without the re-key: `ItemBarcode` (2026-08-20) gives one item many codes as *additive alias rows*. See [`Multi-barcode plan.md`](Multi-barcode%20plan.md) and [`plutus-catalogue-sync-design.md`](plutus-catalogue-sync-design.md), whose banner makes the same point at length |
>
> ⚠ **§5's migration order is SUPERSEDED — do not follow it.** It says *"extends `Plutus.SeedMigrator`"*.
> The migration mechanism is now
> [`To do/NatApp data translation agent and scripts.md`](../To%20do/NatApp%20data%20translation%20agent%20and%20scripts.md),
> which has been **executed twice** (a bridge top-up on 2026-08-17, a full replace from the 19_08 backup
> on 2026-08-20) and carries the runnable rerun script. That document wins on anything to do with *how*
> data moves.
>
> ⚠ **AND IT ANALYSES A FILE THAT IS TWO BACKUPS OLD** — the `23_07_2026` snapshot, while `Build/seed-data/`
> now holds `15_08` and `19_08` as well. The *shape* it describes is still right (NatApp is frozen: seven
> migrations, 2019-06 → 2020-04, and no eighth is coming), but **every figure in it is a July count.**
>
> ✅ **What is still worth reading, and why it was not deleted:** **§4, the table-by-table disposition**, is
> the only map of the legacy schema anywhere in this repo, and the cutover run has not happened yet. When
> somebody has to answer *"what was this NatApp column for?"*, this is where the answer is.

**Source:** `Kapow Comics ltd - Database - 23_07_2026 15_57_23.db` (SQLite, NatApp EF Core model, 7 migrations 2019-06 → 2020-04)
**Companion to:** `plutus-platform-architecture.md` §14
**Data shape:** 21,653 sales · 74,830 lines · 21,784 tenders · 20,372 items · 1 store · 1 employee · 2019-01 → today

Overall verdict: **the core is sound and already more event-shaped than expected** — sales look write-once, refunds are separate records referencing the original sale, till price-overrides are audited (`CheckoutItemChangeModel`), and a permission model with amount thresholds already exists (`AuthActions`). The migration is mostly *addition + retyping*, not restructuring. But there are five findings that must be fixed in the target schema, one data-protection issue, and one data-repair job.

---

## 1. Headline findings (things the design must correct)

### F1 — Sale IDs are timestamp strings and will collide across tills ⚠ critical
`Sales.Id` = e.g. `202672314534618`, `2026723135329441` — a concatenated local datetime, **not zero-padded** (variable length, so not even sortable as text). Unique on one till by luck of the clock; **two tills will collide**, and a clock reset could collide with the past. Confirms the design decision exactly: target `SaleId` = UUIDv7 minted at the POS (D4), with the timestamp string kept only as a legacy receipt reference. Migration: mint a UUID per existing sale, keep old ID in `LegacyRef`.

### F2 — VAT is not stored on the sale ⚠ critical for the financials requirement
Sale lines (`Trans`) carry unit prices but **no VAT rate or amount**; VAT is derived by live join `Items.VatId → Vats.Rate`. `Vats.Rate` is a mutable multiplier (`1.2`, `1.05`, `1.0`). Consequences today: if a VAT rate or an item's VAT band is ever edited, **historical VAT silently changes** — this is precisely what the yearly-statement/period-close requirement cannot tolerate. Also `Sales.TotalExTax` was added by migration with default `'0.0'`, so pre-2020 sales likely have no ex-tax total at all.
Target: denormalise `VatRate`, `VatAmountPence` onto every sale line at time of sale (§14.7 of the design); backfill historical lines from the item→rate join *once, explicitly, flagged as reconstructed*.
Also: the `Exempt` rate (×1.0) conflates **zero-rated and exempt** — different boxes on a UK VAT return. Target rates table needs a type: standard/reduced/zero/exempt.

### F3 — Money is decimal TEXT everywhere
`'3.49'`, `'1.5'`, `'0.0'` — string decimals, unnormalised (`1.5` vs `1.50`). Every aggregation today happens after parsing floats. Confirms integer-pence `BIGINT` end-to-end (already the webapp's convention). Migration is a parse-and-multiply pass; watch for the handful of odd values a 7-year-old dataset will contain (validate `SUM(lines) = Sales.Total` per sale during conversion and quarantine mismatches).

### F4 — The item primary key is the barcode
`Items.Id` = EAN/ISBN text (`'9781401238384'`)… and also pseudo-keys like `'Club'` and `'back issues'`. Barcode-as-PK breaks on: barcode reuse by suppliers, one product with multiple barcodes, items with none, and cross-tenant catalogue sync. Target: surrogate UUID PK; barcode becomes an attribute (`Barcodes` table, many-per-item, unique per tenant); pseudo-items become explicit **department/open-price keys** rather than fake items. Every FK that points at `Items.Id` (`Trans`, `Stocks`, `Refunds`, `DiscountItems`, `CheckoutItemChangeModel`) is remapped during migration.

### F5 — Stock is a mutable counter, and it's deeply negative
`Stocks` = `(ItemId, StoreId, Quantity)`, current range **−28,508 → 100** (e.g. `back issues: −28508`). Sales have decremented stock for seven years without goods-in ever being recorded. This confirms the movement-ledger design (D5-adjacent) and dictates the migration stance: **do not migrate these quantities as truth.** Opening balances should be established by stock take at cutover; the ledger starts from that adjustment event. (The negative counters are still useful — they're a sales-velocity record — but they are not on-hand stock.)

---

## 2. Findings that map cleanly (pleasant surprises)

| Current | Maps to (design §) | Notes |
|---|---|---|
| `Stores` (1 row: name, address, VAT number) | `Store` under Company (D10) | The hierarchy concept already exists; becomes one Store under one Company under tenant "Kapow" |
| `Employees.StoreId`, PBKDF2 `HashedPassword`+`Salt` | `User` + store-scoped assignment (§13.2) | Credential scheme already matches `TestTokenAuth`; carries over |
| `AuthActions` / `EmpAuthActions` (Till, Refund20, Refund100, Refund Unlimited, Staff, Item, Report, Admin, Management + `Amount` threshold) | The POS permission catalogue (§13.2) | This *is* a permission model with amount ceilings — `Refund20` → `pos.refund.max:2000`. Migrate as seed data for the built-in roles |
| `Refunds` (separate row, `SaleIdReturned` → original, `AuthoriserId`) | `SaleAdjustment` (D5) | Already event-shaped, already records the authoriser — exactly the audit trail §13.1 needs |
| `CheckoutItemChangeModel` (price + original price, linked from 5,705 lines) | Transaction-time price-override audit (§13.4 note) | Keep the concept; fold into the sale-line event as `overriddenFromPence` |
| `PaySales` (multiple tenders per sale, amount + change) | `SaleTender` | Split-tender already supported; 21,784 tenders vs 21,653 sales confirms real usage |
| `PayMethods` (Card/Cash/Online/Credit, surcharge, changeable, cashback flags) | Tender-type config, per tenant | Direct lift |
| `Transaction_Discounts` (+`DiscountRate` recorded at sale) | Per-line discount on the sale event | Rate captured at sale time — good precedent (unlike VAT) |
| `Notes`/`NotesSales` | Free-text note on sale event | Direct lift |
| Sales write-once (`Modified` is the `0001-01-01` sentinel throughout) | Append-only sales (D5) | The till never edits a completed sale today — the append-only rule formalises existing behaviour |

---

## 3. Other findings

- **PII columns in the till DB — confirmed never used, drop them:** `Employees` carries `Wage`, `ContractedHours`, `NIN`, home address and mobile, but inspection shows they were **never populated** (Wage `0.0`, NIN/address/mobile all NULL). Decision: these fields are **removed** — not carried into the MAUI port's local schema nor the central schema, nothing to migrate. Central `User` holds identity, email, credential, and access only. This also eliminates the would-be GDPR exposure of payroll data in shop-floor backups.
- **Timestamps are local, zoneless text** (`2026-07-23 14:05:34.6186178`), and `DateOfSale` ≠ `Created` (basket-start vs completion — worth preserving as two fields). Target: UTC `datetime(6)` + the device-local `businessDay` field from the sale contract (§4.1).
- **`SavedTransactions` (parked baskets)** serialises with .NET `$type: "NatApp.Plutus.Models.BasketItem, NatApp.Plutus"` — these type names break on the MAUI namespace change. They're device-local state; don't centralise, and switch to plain contract JSON in the MAUI port.
- **`Vats.Rate` as multiplier (×1.2)** — target stores the percentage (or basis points, `2000` = 20%) to avoid float representation issues; `REAL` today.
- **No secondary indexes** beyond PKs — fine for SQLite-on-till, but the central MySQL schema needs the `(TenantId, …)` composites from day one (§14.1).
- **`Items.Image BLOB`** — unused (0 rows populated). Target: images go to object storage keyed by item, never DB blobs.
- **Volumes are small** (23 MB, ~75k lines over 7 years) — the pooled multi-tenant MySQL model absorbs hundreds of clients this size without design strain; no partitioning needed for years.

---

## 4. Table-by-table disposition

| Current table | Rows | Disposition in central schema |
|---|---|---|
| `Sales` | 21,653 | → `Sales` — new UUID PK (F1), `LegacyRef`, `TenantId`, `TillId`, `DeviceSeq`, `BusinessDay`, `ReceivedAt`, pence totals (F3), UTC times |
| `Trans` | 74,830 | → `SaleLines` — pence prices, **+ `VatRate`/`VatAmountPence` backfilled** (F2), qty (`Amount`→`Qty`), remapped item FK (F4), discount + override folded in |
| `PaySales` | 21,784 | → `SaleTenders` — pence, tender-type FK |
| `Refunds` | 56 | → `SaleAdjustments` — typed (refund), links original sale + authoriser |
| `Transaction_Discounts` | 15,414 | → columns/child of `SaleLines` (rate already captured — keep) |
| `CheckoutItemChangeModel` | 5,723 | → `overriddenFromPence` on `SaleLines`; table retired |
| `Notes`, `NotesSales` | 4,672 | → `Sales.Note` (1 note per sale in practice) |
| `Items` | 20,372 | → `Items` (UUID PK) + `Barcodes` (F4) + department keys for pseudo-items; price → `PriceList` entries (§13.4) |
| `Category` | 9 | → `Categories` (+`TenantId`) |
| `Vats` | 3 | → `VatRates` — percentage not multiplier, type incl. zero-rated vs exempt (F2) |
| `Discounts`, `DiscountCats`, `DiscountItems` | 6/4/0 | → promotion tables, per tenant; same shape, pence/basis-point amounts |
| `Stocks` | 7,868 | **Not migrated as truth** (F5) → `StockLevels` seeded by cutover stock take; old counters archived for reference |
| `Stores` | 1 | → `Stores` under default Company (D10) |
| `Employees` | 1 | → `Users` (identity + credential only); payroll/address fields confirmed unused → dropped (§3) |
| `AuthActions`, `EmpAuthActions` | 11/11 | → seed for `Permissions`/`Roles`/`RoleAssignments` (§13.2), amount thresholds → `pos.refund.max:{n}` etc. |
| `PayMethods` | 4 | → `TenderTypes` per tenant |
| `SavedTransactions` | 1 | Stays device-local; format change in MAUI port (§3) |
| `__EFMigrationsHistory` | 7 | Superseded by the central schema's own migration history |

---

## 5. Migration order (extends `Plutus.SeedMigrator`)

1. Create tenant "Kapow" + default Company; adopt existing `Stores` row; create Till record for the existing device.
2. Items: mint UUIDs, split barcodes out, create department keys for pseudo-items, build ID remap table.
3. Prices: current `Items.Price` → company `PriceList` (policy `CENTRAL` initially — Kapow is single-store, so policy nuance is moot until a second store exists).
4. Sales/lines/tenders/refunds: mint sale UUIDs, convert money to pence with per-sale `SUM(lines)=Total` validation → quarantine mismatches, **backfill line VAT from item→rate join, flagged `VatReconstructed=1`**, convert timestamps to UTC + `BusinessDay`.
5. Users/permissions: migrate employee (minus payroll fields), seed roles from `AuthActions`.
6. Stock: archive old counters; opening balances via stock-take adjustment events at cutover.
7. Reconciliation report: sale count, gross total, VAT total per year — old DB vs new, before the old till is retired.

The same tool, parameterised, becomes the onboarding path for any future client arriving with a NatApp/Kapow database.
