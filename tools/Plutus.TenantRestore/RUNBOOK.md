# Per-tenant restore runbook (WP15.3)

Reconstruct a single tenant's rows from a nightly dump (WP12.3) after accidental deletion or
corruption — **without** restoring the whole database and clobbering every other tenant.

This is tooling + procedure, **not** a new backup system. The backups already exist
(`~/PLUTUS/backups/nightly/plutus-YYYYMMDD.sql.gz`, taken 03:30 daily).

## What the tool does

`plutus-tenant-restore` compares one tenant's rows between two MySQL **schemas** on the same
server and, in apply mode, inserts the rows that are missing from the target:

- **Tenant-owned tables are discovered from `information_schema`** (any column named `TenantId`) —
  the same schema-as-source-of-truth the WP10.3 exporter uses. Never a hand-kept list, so the tool
  tracks the EF model automatically. Global tables (no `TenantId`) are ignored.
- **`--verify` (default, read-only):** per-table row counts (dump vs live) + `SalesV2` penny totals.
- **`--apply`:** `INSERT` rows present in the dump but missing (matched by primary key) in live.
  It **never updates an existing row** — this honours the immutability of event tables (decision
  D5): a restore can only re-add what was lost, never rewrite history. FK checks are relaxed for
  the session during the bulk insert (as `mysqldump` does around any restore), since the restored
  rows are a self-consistent set. Every write is scoped `WHERE TenantId = @tenant`, and the tool
  fingerprints every table's **other-tenant** rows (order-independent XOR of per-row `CRC32`) before
  and after, aborting if any fingerprint moves — so a bug can never silently touch another tenant.

## Prerequisites

- The nightly dump for the day you want to restore from.
- A scratch schema to load it into (`plutus_restore`). **Creating a schema needs `root`** — the
  `plutus` user only has rights on `plutus` and `plutus_t1`. In a real incident, ask Matt (or use
  the MySQL root credentials) for the two `CREATE DATABASE` / `GRANT` lines below.
- The tool binary on the Mac: `~/PLUTUS/tenant-restore/plutus-tenant-restore` (self-contained
  osx-arm64 publish of `tools/Plutus.TenantRestore`; re-publish with
  `dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true`).

## Canonical procedure (real incident)

```sh
set -a; . ~/PLUTUS/secrets/mysql.env; set +a
BIN=/opt/homebrew/bin; PW="$MYSQL_PLUTUS_PASSWORD"
TENANT=<tenant-guid>
DUMP=~/PLUTUS/backups/nightly/plutus-YYYYMMDD.sql.gz

# 1. Load last night's dump into a fresh scratch schema (root — one-time per restore).
#    (As root:) CREATE DATABASE plutus_restore; GRANT ALL ON plutus_restore.* TO 'plutus'@'localhost';
gunzip -c "$DUMP" | "$BIN/mysql" -uroot -p plutus_restore

# 2. VERIFY first (read-only): what is live missing vs the dump?
~/PLUTUS/tenant-restore/plutus-tenant-restore --tenant "$TENANT" \
    --source plutus_restore --target plutus --server 127.0.0.1 --user plutus --password "$PW"

# 3. APPLY when the verify output looks right. Re-runnable (insert-only-missing is idempotent).
~/PLUTUS/tenant-restore/plutus-tenant-restore --tenant "$TENANT" \
    --source plutus_restore --target plutus --server 127.0.0.1 --user plutus --password "$PW" --apply

# 4. Drop the scratch schema afterwards (root): DROP DATABASE plutus_restore;
```

> `--target` defaults to `plutus` (**live**). Always pass it explicitly and double-check it before
> `--apply`. Rehearse against a disposable schema (`plutus_t1`), never live.

## Rehearsal — 2026-07-28 (PASSED)

Rehearsed end-to-end on the test env against the demo/sandbox tenant
`de300000-0000-0000-0000-000000000001` ("Demo Store", WP14.3). Because the `plutus` user cannot
`CREATE DATABASE`, the rehearsal used the two schemas it does own: `plutus_t1` (disposable staging)
as the **target**, freshly re-cloned from a live `plutus` dump, and live `plutus` as the read-only
**source** (the tool never writes to source, so live was never mutated).

1. Re-cloned `plutus_t1` from a fresh `mysqldump` of live `plutus`
   (`--no-tablespaces --skip-lock-tables --routines --set-gtid-purged=OFF`; the `plutus` user lacks
   `RELOAD`/`SUPER`, so `--single-transaction` and GTID statements must be omitted). 76 tables each.
2. **Baseline verify:** MATCH — demo tenant = 211 rows across 7 populated tables (30 `SalesV2`, 30
   `SaleLines`, 30 `SaleTenders`, 30 `SalesRollups`, 30 `VatRollups`, 60 `TenantUsageRollups`, 1
   `AuditLog`), `SalesV2` gross = 15000p (£150.00).
3. Recorded an independent witness — Kapow `SalesV2` in `plutus_t1`: **21650 rows / 55693170p**.
4. Deleted the demo tenant's rows from all 63 tenant-owned tables in `plutus_t1` (FK checks off).
   Verify → live missing all 211 rows, gross delta 15000p.
5. **`--apply`** → inserted exactly 211 rows; other-tenant fingerprint check **OK (byte-untouched)**;
   follow-up verify → **MATCH**, gross back to 15000p.
6. Witness re-checked: Kapow `SalesV2` still **21650 rows / 55693170p** — byte-identical. ✅

DoD met: row counts + penny totals match pre-delete; other tenants provably untouched (tool
fingerprint + independent witness); runbook committed; rehearsal recorded.
