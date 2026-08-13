# NatApp → Plutus Translation Agent — Plan

**Date:** 2026-08-05
**Status:** Plan only — no code changed.
**Companion docs:** `Build/Migration-2026-07-22-plan.md` (Workstream G), `Build/kapow-db-gap-analysis.md`, `HANDOVER.md` (Phase 3/6 notes), `tools/Plutus.TenantRestore/RUNBOOK.md` (the operational pattern this plan follows).

## 0. Terminology (confusingly, "Plutus" names two different things)

- **NatApp** — the old Xamarin.Forms till app (`NatApp.Plutus.*`), frozen per the modernization plan, still running live in Kapow Comics Ltd's shop. Its local database is a SQLite file on the till device, EF-Core-modelled (`Plutus.Database`), 7 migrations 2019–2020.
- **Kapow Comics Ltd** — the pilot retailer currently running NatApp. Not a piece of software — a tenant. The migration tooling is named after them because they were the first (only, so far) customer migrated.
- **The new platform** — the multi-tenant `Plutus.Entities`/MySQL backend + web POS + portal, which Kapow's webstore (WooCommerce connector) already trades against live. The physical till has **not** been cut over yet.
- **"Webtill"** — the browser-based POS (`Plutus.Frontend.WebApp`), already seeded once from a Kapow NatApp backup.

## 1. Current-state audit (what actually exists, verified 2026-08-05)

### 1.1 The original backup file

The seed data came from `Kapow Comics ltd - Database - 23_07_2026 15_57_23.db` — a SQLite dump of the till's local `Plutus.Database` schema (21,653 sales, 74,830 lines, 20,372 items, 1 store, 1 employee, 2019-01→2026-07). **This file is not retained anywhere in this environment** — `.gitignore:298` (`*Database*.db`) deliberately keeps real customer backups out of git, and no copy sits on disk here either. Anyone re-running this pipeline needs a **fresh export off the till** (NatApp already has a local DB-backup/restore menu per `Build/Migration-2026-07-22-plan.md` Workstream D — that's presumably how the original file was produced). First deliverable of any real run: confirm where the next backup is actually going to come from and how it gets off the till device.

### 1.2 Two migration tools already exist, and they do different, incomplete things

| | `Plutus.SeedMigrator` (top-level `Program.cs`) | `Plutus.Migration.Kapow` (+ `SeedMigrator sales-v2`) |
|---|---|---|
| Target shape | Old-shape tables lifted almost 1:1 (`Sale`, `Transaction`, `Item` keyed `(IdOne barcode, IdTwo tenant)`, `Stocks`, `Employees`, …) | New event-sourced shape (`SaleV2`, `SaleLine`, `SaleTender`, UUIDv7 keys, integer pence, per-line VAT) |
| Covers | Business, Store, Till, Taxes, Categories, Employees, AuthActions, PaymentMethods, Discounts, **Items, Stocks**, CheckoutItemChanges, Sales, Transactions, Notes, PaySales, TransactionDiscounts, Refunds | **Sales only** (header + lines + tenders). Nothing else. |
| Fixes gap-analysis findings F1–F5? | No — carries forward barcode-as-PK, decimal-text money, mutable stock, no per-line VAT | Yes — mints UUIDs, pence, VAT reconstruction, quarantines bad rows, keeps `LegacyRef` |
| Idempotent / re-runnable? | No — deterministic GUIDs (`DetGuid("business","kapow")` etc.) mean a second run collides on PK, and there's no LegacyRef-style skip | **No** — `tillId`/`deviceId` are `Guid.NewGuid()` **every invocation** (`Program.cs:94`), and there's no check for sales already recorded. Re-running duplicates. |
| Tenant-aware? | Hardcoded `businessId = DetGuid("business","kapow")` | Hardcoded `Plutus.Entities.Tenancy.KnownTenants.Kapow` |

Both were built, reasonably, as **one-shot** tools for a single known customer. Neither is the "parameterised... onboarding path for any future client" that `kapow-db-gap-analysis.md §5` already names as the end state. That gap is exactly what this plan closes.

### 1.3 Live database, queried directly (`plutus` schema, MySQL 9.6, 2026-08-05)

| Table | Rows | Note |
|---|---|---|
| `SalesV2` | 21,660 | 21,646 carry `LegacyRef` (the historical ETL); **14 are genuinely new**, posted since cutover via the real ingest pipeline |
| `SaleLines` / `SaleTenders` | 82,945 / 21,795 | |
| `SaleQuarantine` | 16 | 8 distinct reasons, each **duplicated** — the `sales-v2` runner was invoked twice (once `--sqlite` validation, once `--mysql` live), and quarantine logging isn't deduped across runs either |
| `Sales` / `Trans` (old-shape) | **0 / 0** | The legacy-shape `Program.cs` path was *not* used for sales in the end — only for catalogue |
| `Items` | 20,342 | Still old-shape: PK is `(IdOne varchar(20) barcode, IdTwo char(36) tenant)` — **no `Barcodes`, `PriceList`, or UUID item-key table exists yet.** The gap-analysis's F4 fix (surrogate UUID PK) has not been built for catalogue, only for sales. |
| `Stocks` (legacy) / `StockLevels` (v2 ledger) | 7,841 / 3,200 | Both live in parallel — the legacy counters were never retired, per the dual-write bridge |
| `Till` | 3 rows | **None of them is `d4fa2572-691a-4891-b3ef-62bad7e99d21`** — the till ID that owns all 21,646 historical sales. It's an orphaned reference: the one-shot ETL stamped a throwaway `Guid.NewGuid()` onto every row and never created a matching `Till`/`Device` entity. |
| `Tenants` | 2 | `Kapow Comics Ltd` (`0192b8a0-…`), `Demo Store` |

**Confirmed defect to fix, not just a cleanliness nit:** any reporting/UI code that joins `SalesV2.TillId → Till` will silently drop or null-join all 21,646 historical rows. A translation agent must mint (or accept as input) a **real, persisted** `Till` + `Device` row before writing sales against it — and reuse the *same* one on every subsequent run, not a fresh random GUID.

### 1.4 The actual operational gap driving this request

Per `HANDOVER.md`: *"⚠ The ETL is ONE-SHOT... The PRODUCTION cutover (retiring the physical till) remains a separate future op: re-run the ETL from a FINAL till backup."* The last migrated sale is timestamped `2026-07-23 13:05:39`. Today is 2026-08-05. If the physical NatApp till is still ringing up sales in Kapow's shop (which the "cutover is a deliberate future op, not yet done" framing implies), **roughly two weeks of sales exist only on the till's local SQLite DB** and are invisible to the new platform's reporting, VAT rollups, and stock ledger. This is precisely the "take a newer backup and translate it" need — not a hypothetical future capability, but a live data gap right now.

---

## 2. What the translation agent needs to do

Two related but distinct use cases, both served by the same tool:

1. **Bridge runs (now → cutover day):** periodically (or on demand) take a fresh NatApp backup and bring the new platform's sales/stock up to date, **without duplicating** the 21,646 rows already migrated.
2. **Onboarding runs (future customers):** the same tool, pointed at a different tenant and a different NatApp/Kapow-schema backup, becomes the standard "customer arrives with an old till" import path — this was always the intended end state (`kapow-db-gap-analysis.md §5`, last line).

Both require the tool to stop being "run once, from empty, with random IDs" and become a proper incremental ETL.

---

## 3. Design

### 3.1 Idempotent identity model (fixes §1.3's orphaned-till defect)

- **Till/Device identity becomes a stable input, not a per-run random value.** First run for a tenant mints a real `Till` + `Device` row (persisted, not just stamped onto sales) and records the minted IDs (e.g. in a small `TranslationRuns` audit table, or reuses the deterministic-GUID convention `Program.cs` already uses for other entities). Every subsequent run for the same tenant reads those IDs back rather than calling `Guid.NewGuid()`.
- **One-off backfill:** create the missing `Till`/`Device` rows for `d4fa2572-691a-4891-b3ef-62bad7e99d21` (or repoint the 21,646 existing `SalesV2` rows to a newly-created, correctly-registered till — cheaper and doesn't touch 21,646 rows) before the next bridge run, so the FK gap doesn't get worse.

### 3.2 Delta detection (fixes non-idempotency)

- **Sales:** `LegacyRef` is already the natural key (`KapowSaleMapper` already sets it). Before mapping, load the set of `LegacyRef` values already present in `SalesV2` for the tenant and skip those in `KapowSalesReader.Read(...)` (or filter post-read, pre-`KapowMigrator.Migrate`). This turns "re-run from a newer backup" into "only the sales added since the last backup get inserted" — no drop-and-reload, no manual `plutus_t1` rehearsal gymnastics for the routine case.
- **Catalogue side (items/stock/employees/categories):** needs the equivalent — upsert-by-old-id (`Items.IdOne`, `Employees.Id`, etc.) rather than insert-only. Today's `Program.cs` path has none of this; it assumes an empty target.
- **Quarantine log:** dedupe by `(TenantId, LegacyId)` before adding a new `SaleQuarantine` row, so re-running against an overlapping backup doesn't re-log the same 8 failures every time (currently at 2x from just two historical invocations).

### 3.3 Catalogue-side target-schema decision (currently unresolved, blocks nothing today but will bite later)

`Items` still uses barcode-as-PK (F4 from the gap analysis), with no `Barcodes`/`PriceList` table built. Two honest options, not a silent default:
- **(a) Migrate catalogue to the target UUID+Barcodes+PriceList shape now**, alongside this work — the bigger lift, but stops building more callers against a PK shape the architecture doc already says is wrong.
- **(b) Explicitly defer**, same as `Migration-2026-07-22-plan.md` Workstream G already defers `CoppperToCSV` reinvestigation — keep upserting into the current old-shape `Items`/`Stocks` for now, note it as tracked debt.
Recommend **(b)** for the bridge-runs use case (nothing forces this now, and `StockLevels`/`StockMovements` already give the ledger a UUID-keyed path independent of `Items.IdOne`), but **(a)** must happen before this tool is reused for a *second* onboarding customer — a new tenant's barcodes may collide with Kapow's under the shared `(IdOne, IdTwo)` PK if `IdTwo` (tenant) handling isn't airtight, and a fresh customer is exactly when doing it right costs least.

> ### ⚠ Added 2026-08-08 — how §3.3 binds the MAUI retrofit
>
> Matt confirmed this document is the mechanism that translates a legacy till database into the
> current schema, which makes it the input to the MAUI retrofit's cutover
> ([`MAUI-retrofit.md`](MAUI-retrofit.md) §16, and the archived [`MAUI-Retrofit-Plan-2026-08-07.md`](../archive/MAUI-Retrofit-Plan-2026-08-07.md) §10 it came from).
>
> **If option (a) is ever taken and `Items` gains real UUID PKs, those UUIDs MUST be
> `Plutus.SharedKernel.DeterministicGuid.ForItem(businessId, itemIdOne)`** — the same function the
> web till (`pipeline.ts itemGuid`) and the MAUI till both use — and **never** freshly minted.
> Mint them and the catalogue disagrees with both tills from day one, silently, because stock is
> attributed by barcode while the GUID rides along unnoticed.
>
> Option (b) — deferring — stays safe for the retrofit: with no catalogue UUIDs, both tills simply
> derive their own and agree with each other. The retrofit's cutover hard stop is therefore passed
> `null` today rather than comparing against a central id that does not exist.
>
> **Related live condition, relevant to §3.4 verification:** `Migration.Kapow`'s `IdRemap` minted
> random `ItemId`s for historic sale lines, so the same barcode already carries two distinct GUIDs
> in production (e.g. `761941391632`, 161 lines). Reports key on `ItemIdOne`, so this is tracked
> debt rather than damage — but **8,120 of 82,965 sale lines carry no barcode at all**, and those
> can never be item-attributed. A verification step that reconciles item-level figures needs to
> exclude them explicitly rather than silently under-count.

### 3.4 Verification, not just insertion

Extend the existing `KapowReconciliation` (already reports sales-read/recorded/quarantined + gross/VAT pence) with a **before/after snapshot** in the same style `Plutus.TenantRestore` uses: per-table row counts and `SalesV2` penny totals, source vs target, plus a fingerprint check that **no other tenant's rows moved** (the bridge tool touches a shared multi-tenant MySQL schema — the same blast-radius risk `TenantRestore` was built to contain). Default to `--verify` (read-only, prints the delta the next apply would make); require an explicit `--apply`.

### 3.5 Safety rails (reuse the pattern already proven in this codebase)

- **Rehearse on `plutus_t1` before touching live `plutus`** — exactly the discipline `TenantRestore`'s rehearsal log already demonstrates and that the WP3.4 incident (an ETL-vs-period-close ordering bug that mis-posted £44k) shows is not optional.
- **Sequencing guardrail, enforced by the tool, not just documented:** refuse to run if a `FinancialPeriod` covering the backup's date range is already closed for that tenant (this is exactly what caused the WP3.4 incident) — either block with a clear error, or require an explicit `--i-will-reopen-the-period` flag.
- **Order of operations, made explicit and scriptable:** sales ETL (delta only) → `rollups-rebuild` → `stock-open`/stock reconciliation. Today this is tribal knowledge in `HANDOVER.md`; it should be one command or one documented script, not three manually-sequenced ones.
- **Dry-run by default**, `--mysql` write path requires the explicit flag it already has, keep it.

---

## 4. Build plan

| Step | Work | Builds on |
|---|---|---|
| 1 | Backfill/repoint the orphaned `d4fa2572-…` till (§3.1) — one-time fix, do first so it doesn't compound | — |
| 2 | Add `LegacyRef`-based delta filtering to `KapowSalesReader`/`KapowMigrator` (§3.2) | `Plutus.Migration.Kapow` (reuse as-is otherwise — the mapper/transform logic is sound and already unit-tested, `KapowMigrationTests.cs`) |
| 3 | Dedupe `SaleQuarantine` by `(TenantId, LegacyId)` | same |
| 4 | Add upsert-by-old-id to the catalogue-side path (items/stock/employees/categories) in `SeedMigrator`'s legacy-shape loader | `Program.cs` |
| 5 | Add the period-close guardrail + before/after reconciliation snapshot (§3.4) | `KapowReconciliation`, `FinancialPeriods` table |
| 6 | Wire the 3-step sequencing (sales delta → rollups-rebuild → stock reconciliation) into one runner command | existing `SeedMigrator` subcommands (`sales-v2`, `rollups-rebuild`, `stock-open`) |
| 7 | Rehearse end-to-end on `plutus_t1` with a synthetic "newer" backup (take the existing backup, add a handful of synthetic post-2026-07-23 rows) | `plutus_t1` (already the standing rehearsal schema) |
| 8 | Run for real against Kapow's next backup; verify → apply; this closes the "bridge" use case | — |
| 9 | Generalize: replace `KnownTenants.Kapow`/hardcoded `businessId` with a required `--tenant` parameter + a tenant-provisioning step (create Tenant/Business/Store if not already present) for the *next* customer onboarding | §3.3(a) should land before this step is exercised for real |

Steps 1–3 are the minimum to make "take a newer backup" safe at all; 4–6 make it a proper repeatable tool rather than a careful manual procedure; 9 is the generalization the gap-analysis always intended.

---

## 5. Operational runbook (once built) — mirrors `tools/Plutus.TenantRestore/RUNBOOK.md`

```sh
# 1. Get the fresh backup off the till (mechanism TBD — §1.1).
# 2. Verify (read-only, default): what would the next apply add?
plutus-natapp-translate --tenant kapow --backup "<newer-backup>.db" \
    --target plutus_t1 --verify

# 3. Rehearse the apply against plutus_t1 first.
plutus-natapp-translate --tenant kapow --backup "<newer-backup>.db" \
    --target plutus_t1 --apply
#    → then: rollups-rebuild, stock reconciliation, spot-check portal figures.

# 4. Only once t1 looks right, repeat against live.
plutus-natapp-translate --tenant kapow --backup "<newer-backup>.db" \
    --target plutus --apply
```

Guardrails: refuses to run if the tenant's current `FinancialPeriod` is closed over the backup's date range; refuses `--apply` against `plutus` (live) without `--target` explicitly named (same convention `TenantRestore` uses); every write scoped and fingerprint-checked so no other tenant's rows can move.

---

## 6. Open questions for Matt

1. **How does a "newer" backup actually reach this tooling?** Manual USB/network pull off the till via NatApp's existing backup menu, or is a scheduled export worth building? Affects whether "bridge runs" can be routine or stay manual-per-incident.
2. **Cadence before cutover:** run this on a schedule (e.g. weekly) to keep reporting current, or only once, immediately before the final cutover? Changes whether idempotency (§3.2) is "nice to have" or load-bearing.
3. **Catalogue-side target schema (§3.3):** commit to option (a) or (b) now, so it isn't quietly decided by default.
4. **Cutover date/trigger:** what actually gates "retire the physical till" — is it this tool reaching parity, or an unrelated business decision?
5. **Multi-tenant generalization (§4 step 9):** is there a concrete next NatApp-based customer to design against, or should the parameterization stay speculative until one exists?
