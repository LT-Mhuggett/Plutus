using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Plutus.Client.Storage;

/// <summary>
/// MAUI retrofit WP2: the till's local SQLite store, schema v2.
///
/// Deliberately a NEW context rather than an edit to the legacy <c>Plutus/Data/Database</c> one:
/// v2 has no legacy tables, integer-pence money and derived GUID ids, and trying to evolve the old
/// schema in place would leave both models half-true at once. The old file is ARCHIVED at cutover
/// (§9.3), never merged.
/// </summary>
public sealed class TillDbContext : DbContext
{
    /// <summary>⚠ Bump this AND add a matching step to <see cref="UpgradeAsync"/> in the same
    /// commit. A bump with no step silently stamps a store as current without changing it; a step
    /// with no bump never runs.</summary>
    public const int SchemaVersion = 7;

    public TillDbContext(DbContextOptions<TillDbContext> options) : base(options) { }

    public DbSet<MetaEntry> Meta => Set<MetaEntry>();
    public DbSet<CatalogueItem> CatalogueItems => Set<CatalogueItem>();
    public DbSet<PriceScheduleEntry> PriceSchedule => Set<PriceScheduleEntry>();
    public DbSet<LocalOperator> Operators => Set<LocalOperator>();
    public DbSet<LocalSale> LocalSales => Set<LocalSale>();
    public DbSet<LocalRefund> LocalRefunds => Set<LocalRefund>();
    public DbSet<SavedBasket> SavedBaskets => Set<SavedBasket>();
    public DbSet<LocalCashEvent> LocalCashEvents => Set<LocalCashEvent>();
    /// <summary>Additional barcodes that resolve to a catalogue item (multi-barcode, v7).</summary>
    public DbSet<LocalItemBarcode> ItemBarcodes => Set<LocalItemBarcode>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MetaEntry>(e => { e.ToTable("Meta"); e.HasKey(x => x.Key); });

        b.Entity<CatalogueItem>(e =>
        {
            e.ToTable("CatalogueItems");
            e.HasKey(x => x.Id);
            // Scanning is the till's hottest path: an index on the barcode, and uniqueness because
            // two items sharing a code makes a scan ambiguous.
            e.HasIndex(x => x.IdOne).IsUnique();
            e.Property(x => x.IdOne).IsRequired();
            e.Property(x => x.Name).IsRequired();
        });

        b.Entity<PriceScheduleEntry>(e =>
        {
            e.ToTable("PriceSchedule");
            e.HasKey(x => new { x.ItemId, x.EffectiveFromUtc });
        });

        // ⚠⚠ MULTI-BARCODE, v7 (2026-08-20). The old `Barcodes` table was deleted on 2026-08-09
        // because it was mapped, read, and never once written — no server-side entity existed to feed
        // it. All three prerequisites its own removal note named now exist (a server entity, a feed
        // field, a portal UI), so this one IS fed: `ApplyCatalogueAsync` replaces an item's rows on
        // every sync and `FindByBarcodeAsync` reads them. See LocalSchema for the full history.
        //
        // ⚠ Keyed on the CODE: one code cannot point at two items, the same guarantee
        // `CatalogueItems.IdOne` gets above and for the same reason — an ambiguous scan is
        // unresolvable at a counter.
        b.Entity<LocalItemBarcode>(e =>
        {
            e.ToTable("LocalItemBarcodes");
            e.HasKey(x => x.Code);
            e.Property(x => x.ItemIdOne).IsRequired();
            // The read `ApplyCatalogueAsync` makes to replace one item's set.
            e.HasIndex(x => x.ItemIdOne);
        });

        b.Entity<LocalOperator>(e => { e.ToTable("Operators"); e.HasKey(x => x.UserId); });

        b.Entity<LocalSale>(e =>
        {
            e.ToTable("LocalSales");
            e.HasKey(x => x.SaleId);
            // The pusher drains Pending in sequence order — index the pair it queries on.
            e.HasIndex(x => new { x.Status, x.DeviceSeq });
            // The server dedupes on it; a local duplicate would mean the counter went backwards.
            e.HasIndex(x => x.DeviceSeq).IsUnique();
            e.Property(x => x.PayloadJson).IsRequired();
        });

        b.Entity<LocalCashEvent>(e =>
        {
            e.ToTable("LocalCashEvents");
            e.HasKey(x => x.EventId);
            e.HasIndex(x => x.Status);
            // "Has this day already been Z-closed?" is asked before every cash action.
            e.HasIndex(x => x.BusinessDay);
        });

        b.Entity<LocalRefund>(e =>
        {
            e.ToTable("LocalRefunds");
            // One row per (refund sale, origin) — a basket refunding two different sales writes two.
            e.HasKey(x => new { x.SaleId, x.OriginSaleId });
            // ⚠ The refund cap sums by ORIGIN, on every return, with a customer waiting.
            e.HasIndex(x => x.OriginSaleId);
        });

        b.Entity<SavedBasket>(e => { e.ToTable("SavedBaskets"); e.HasKey(x => x.Id); });
    }

    /// <summary>
    /// Create the store if absent, bring an older one up to date, and stamp its schema version.
    ///
    /// ⚠ `EnsureCreated` DOES NOTHING TO AN EXISTING DATABASE. It creates the schema only when the
    /// file is absent, so every table and column added after a till first ran would simply never
    /// exist there — and the failure is `SQLite Error 1: no such table`, at the counter, on the
    /// first sale that touches it. The version stamp was already being written and nothing ever
    /// read it; this is that missing half.
    ///
    /// ⚠ EF migrations are deliberately not used here. This store is created by `EnsureCreated` on
    /// devices we cannot reach, and retro-fitting a migrations history to those files is a bigger
    /// risk than a short ordered list of idempotent steps. Every step must therefore be safe to run
    /// twice (`IF NOT EXISTS`), because a crash between the DDL and the stamp leaves it half-done.
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        var fresh = await Database.EnsureCreatedAsync(ct);

        var stamped = await Meta.FindAsync(new object[] { MetaKeys.SchemaVersion }, ct);

        // A store EnsureCreated just built already has every table; anything else may be older.
        // ⚠ An unstamped existing store is treated as version 1, not as current — it predates the
        // stamp, so assuming it is up to date is exactly the wrong guess.
        var from = fresh ? SchemaVersion : int.TryParse(stamped?.Value, out var v) ? v : 1;

        if (from < SchemaVersion)
            await UpgradeAsync(from, ct);

        if (stamped == null) Meta.Add(new MetaEntry { Key = MetaKeys.SchemaVersion, Value = SchemaVersion.ToString() });
        else stamped.Value = SchemaVersion.ToString();

        await SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ordered, idempotent upgrade steps. ⚠ Raw SQL on purpose: the C# model describes the LATEST
    /// schema, so anything derived from it would describe where we are going, not the step in
    /// between.
    /// </summary>
    private async Task UpgradeAsync(int from, CancellationToken ct)
    {
        // v3 — LocalRefunds: what a sale gave back, per original sale. The origin id lives inside
        // PayloadJson, which cannot be indexed or summed, and the refund cap has to sum it on every
        // return. Without this a till cannot tell how much of a sale has already been refunded.
        if (from < 3)
        {
            await Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "LocalRefunds" (
                    "SaleId" TEXT NOT NULL,
                    "OriginSaleId" TEXT NOT NULL,
                    "RefundedPence" INTEGER NOT NULL,
                    CONSTRAINT "PK_LocalRefunds" PRIMARY KEY ("SaleId", "OriginSaleId")
                );
                """, ct);

            await Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LocalRefunds_OriginSaleId" ON "LocalRefunds" ("OriginSaleId");""", ct);
        }

        // v4 — LocalCashEvents (WP9). A shop opens before its broadband does: the opening float, a
        // paid-out for a supplier and the Z-close all have to be recordable with the line down,
        // because the money moves whether or not the platform hears about it. Queued and drained
        // like a sale rather than posted inline.
        if (from < 4)
        {
            await Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "LocalCashEvents" (
                    "EventId" TEXT NOT NULL CONSTRAINT "PK_LocalCashEvents" PRIMARY KEY,
                    "Type" TEXT NOT NULL,
                    "BusinessDay" TEXT NOT NULL,
                    "OccurredAtUtc" TEXT NOT NULL,
                    "AmountPence" INTEGER NOT NULL,
                    "CountedPence" INTEGER NULL,
                    "Reason" TEXT NULL,
                    "OperatorUserId" TEXT NULL,
                    "Status" INTEGER NOT NULL,
                    "PushedAtUtc" TEXT NULL,
                    "Attempts" INTEGER NOT NULL DEFAULT 0,
                    "ServerResponseJson" TEXT NULL,
                    "ExpectedPence" INTEGER NULL,
                    "VariancePence" INTEGER NULL
                );
                """, ct);

            // ⚠ The drain scans by status, and "has this day been Z-closed?" is asked before every
            // cash action — both on a table that grows by a handful of rows a day but is read on a
            // counter with a customer waiting.
            await Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LocalCashEvents_Status" ON "LocalCashEvents" ("Status");""", ct);
            await Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LocalCashEvents_BusinessDay" ON "LocalCashEvents" ("BusinessDay");""", ct);
        }

        // v5 — Brand, Desc and Cost on the catalogue row (WP10 / cutover step 25).
        //
        // ⚠ BRAND IS A SEARCHED FIELD and its absence was a live parity gap, not a missing nicety.
        // `SharedKernel.ItemSearch` matches on name, barcode AND brand — but the till's row had no
        // brand column, so `TillStore.SearchAsync` passed null and the scan box matched TWO fields
        // where the server and the web till matched three. Searching "Marvel" found nothing on a
        // MAUI till and everything on the web one: same query, same shop, two answers.
        //
        // ⚠ Cost is PENCE though the source column is decimal — money is integer pence everywhere
        // and the conversion happens once, at the server's projection.
        if (from < 5)
        {
            // ⚠ GUARDED, BECAUSE `ADD COLUMN` HAS NO `IF NOT EXISTS` IN SQLITE — unlike the
            // `CREATE TABLE IF NOT EXISTS` steps above, which is why this is the first step in the
            // file that needs a helper at all. An unguarded ADD COLUMN throws "duplicate column
            // name" on any store whose tables were built from the CURRENT model (`EnsureCreated`
            // makes the latest schema, which already has these columns) and on any store where the
            // upgrade is replayed. Either way it throws at START-UP, which is a till that will not
            // open. Caught by `Running_the_upgrade_twice_is_harmless`.
            await AddColumnIfMissingAsync("CatalogueItems", "Brand", "TEXT NULL", ct);
            await AddColumnIfMissingAsync("CatalogueItems", "Desc", "TEXT NULL", ct);
            await AddColumnIfMissingAsync("CatalogueItems", "CostPence", "INTEGER NOT NULL DEFAULT 0", ct);

            // ⚠⚠ THE CURSOR IS RESET, AND WITHOUT THIS THE WHOLE MIGRATION DOES NOTHING VISIBLE.
            //
            // The changes feed is keyset pagination over (ModifiedAt, IdOne): a till asks for what
            // has changed SINCE its cursor. Adding a field to the wire does not change any item's
            // ModifiedAt, so an existing till would receive the new columns only for items somebody
            // happens to edit afterwards — and would sit for months with brand populated on the
            // three items that were repriced and null on the other twenty thousand.
            //
            // That failure is invisible: search would work for some items and not others, with no
            // error, no pattern an operator could describe, and nothing in the logs. Clearing the
            // cursor forces one full re-pull, which is a few hundred KB once, on a schema upgrade
            // that already happens at start-up.
            //
            // ⚠ SAFE TO REPLAY. Every catalogue row is an upsert keyed on the item id, so a
            // re-pull rewrites what is already there rather than duplicating it.
            await Database.ExecuteSqlRawAsync(
                $"""DELETE FROM "Meta" WHERE "Key" = '{MetaKeys.CatalogueVersion}';""", ct);
        }

        // v6 — the platform's verdict on a counted drawer, recorded where the operator will see it.
        //
        // ⚠ The server has always answered a Z close with `expectedPence` and `variancePence`, and
        // `CashPushService` threw the body away and kept the status code. A drawer closed £20 short
        // was therefore accepted in total silence: the platform knew, the banking report showed it
        // in red, and the person who counted it was told nothing. Matt found it on 2026-08-11.
        //
        // ⚠ NULLABLE AND BACKFILLED BY NOBODY, deliberately. These hold what the platform SAID at
        // the moment the event was accepted; for events already sent under v5 that answer is gone,
        // and inventing one here — by re-asking the server, or worse by computing it locally —
        // would put a figure against a past Z that nobody actually saw on the night. A blank is
        // the truthful record of "we did not keep it".
        if (from < 6)
        {
            await AddColumnIfMissingAsync("LocalCashEvents", "ExpectedPence", "INTEGER NULL", ct);
            await AddColumnIfMissingAsync("LocalCashEvents", "VariancePence", "INTEGER NULL", ct);
        }

        // v7 — MULTI-BARCODE: an item may be scanned under more than one code
        // (`Build/archive/Multi-barcode plan.md`, MB3). Matt, 2026-08-20.
        //
        // ⚠ A TABLE, not columns — so `CREATE TABLE IF NOT EXISTS` like the v3/v4 steps above, NOT
        // `AddColumnIfMissingAsync` (which only accepts three column DDL fragments by design).
        //
        // ⚠⚠ AND THE CURSOR IS RESET AGAIN, for exactly the reason v5's note gives at length: the
        // feed pages over (ModifiedAt, IdOne), and adding a wire field changes no item's ModifiedAt.
        // Without this an existing till would receive aliases only for items somebody edits
        // afterwards — so a shop would set up a barcode, watch it work on a new till and not on an
        // old one, with no error and no pattern anybody could describe. One full re-pull at start-up
        // is a few hundred KB, once.
        //
        // ⚠ Safe to replay: catalogue rows are upserts keyed on the item id, and this table is
        // replaced per item on every sync.
        if (from < 7)
        {
            await Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "LocalItemBarcodes" (
                    "Code" TEXT NOT NULL CONSTRAINT "PK_LocalItemBarcodes" PRIMARY KEY,
                    "ItemIdOne" TEXT NOT NULL
                );
                """, ct);

            await Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_LocalItemBarcodes_ItemIdOne"
                    ON "LocalItemBarcodes" ("ItemIdOne");
                """, ct);

            await Database.ExecuteSqlRawAsync(
                $"""DELETE FROM "Meta" WHERE "Key" = '{MetaKeys.CatalogueVersion}';""", ct);
        }
    }

    /// <summary>
    /// `ALTER TABLE … ADD COLUMN`, but only if the column is not already there.
    ///
    /// ⚠ SQLITE HAS NO `ADD COLUMN IF NOT EXISTS`, and every upgrade step in this file must be
    /// idempotent — it runs at start-up, it can be replayed, and a store built by
    /// `EnsureCreatedAsync` already carries the LATEST schema, so the columns a step is trying to
    /// add can be there before the step ever runs. An unguarded ADD COLUMN then throws
    /// "duplicate column name" and the till does not open.
    ///
    /// ⚠ THE IDENTIFIERS GO INTO SQL AS TEXT, because SQL has no way to parameterise a table or
    /// column name in an `ALTER` (the `pragma_table_info` probe below CAN parameterise, and does).
    /// Every caller is in this file and passes a literal — but "every caller today" is not a
    /// guarantee, so the identifiers are VALIDATED rather than trusted. A private method one edit
    /// away from being handed a variable is exactly where injection gets in.
    /// </summary>
    private async Task AddColumnIfMissingAsync(string table, string column, string ddl, CancellationToken ct)
    {
        // ⚠ Letters, digits and underscores only — an allow-list, not an escape. Anything else is a
        // programming error here and there is no legitimate value it could refuse.
        static bool SafeIdentifier(string s) =>
            s.Length is > 0 and <= 64 && s.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

        if (!SafeIdentifier(table) || !SafeIdentifier(column))
            throw new ArgumentException($"Unsafe schema identifier: {table}.{column}");

        // The DDL fragment is a fixed vocabulary, matched exactly rather than pattern-checked.
        if (ddl is not ("TEXT NULL" or "INTEGER NOT NULL DEFAULT 0" or "INTEGER NULL"))
            throw new ArgumentException($"Unsupported column definition: {ddl}");

        var connection = Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column;";

        var t = probe.CreateParameter(); t.ParameterName = "$table"; t.Value = table; probe.Parameters.Add(t);
        var c = probe.CreateParameter(); c.ParameterName = "$column"; c.Value = column; probe.Parameters.Add(c);

        var present = Convert.ToInt32(await probe.ExecuteScalarAsync(ct)) > 0;
        if (present) return;

        // ⚠ Built as a variable, not an inline interpolation, so the EF analyser's "interpolated
        // string straight into raw SQL" rule (EF1002) is answered honestly: the identifiers are
        // validated above, and this line has nothing left to check.
        var sql = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + ddl + ";";
        await Database.ExecuteSqlRawAsync(sql, ct);
    }
}

/// <summary>The Meta keys, named once so a typo can't silently create a second setting.</summary>
public static class MetaKeys
{
    public const string SchemaVersion = "schemaVersion";
    public const string ServerUrl = "serverUrl";
    public const string DeviceId = "deviceId";
    public const string TenantId = "tenantId";
    /// <summary>⚠ The LEGACY Business id — seeds DeterministicGuid.ForItem. NOT the tenant id.</summary>
    public const string BusinessId = "businessId";
    public const string TillId = "tillId";
    public const string StoreId = "storeId";
    public const string DeviceSeq = "deviceSeq";
    public const string CatalogueVersion = "catalogueVersion";
    /// <summary>Set once the legacy database has been archived — enrolment refuses until then (§9.3).</summary>

    /// <summary>The portal's published VAT bands, WHOLE effective-dated timeline, as the wire sent
    /// them. ⚠ Never "today's rate" — the timeline is what lets an offline till apply a
    /// future-dated rate change on the correct day.</summary>
    public const string VatBands = "vatBands";

    /// <summary>The shop's scheduled discount rules, as the wire sent them. ⚠ Whole rules, schedule
    /// included — never "the discounts that apply today". The till judges the day itself, which is
    /// what makes a Wednesday rule work on a till that has been offline since Monday.</summary>
    public const string DiscountRules = "discountRules";

    /// <summary>
    /// The till's operator roster — the whole <c>TillOperatorsResult</c> as the wire sent it
    /// (step 24). This is what makes OFFLINE SIGN-IN work.
    ///
    /// ⚠⚠ THE WHOLE ENVELOPE, NOT ROWS IN <c>Operators</c>, and that is deliberate. The mapped
    /// `LocalOperator` entity cannot hold a roster without losing two things: it has **no Email**
    /// column — and email is login's first match clause, so relational storage would break the normal
    /// way staff sign in — and there is nowhere for the roster-level <c>AsOfUtc</c>, which
    /// `OfflineCredentials.Assess` measures its staleness horizons from. ⚠ `AsOfUtc` is the
    /// **SERVER's** clock by contract, precisely so a till with a wrong clock cannot decide its own
    /// credentials are fresh for ever; substituting a locally-written `UpdatedAtUtc` would quietly
    /// convert a server-anchored horizon into a client-anchored one, which nothing downstream could
    /// detect.
    ///
    /// So the roster is stored verbatim, exactly as `VatBands` and `ReceiptTemplate` are, and nothing
    /// is dropped. A relational move remains possible later — it needs `Email` on the entity and a
    /// home for the envelope first.
    /// </summary>
    public const string OperatorRoster = "operatorRoster";

    /// <summary>
    /// Step 28 — the device-local password verifiers minted by an ONLINE sign-in.
    ///
    /// ⚠⚠ SEPARATE FROM <see cref="OperatorRoster"/> ON PURPOSE. The roster is a cache replaced
    /// wholesale on every sync; a verifier is EARNED by an online sign-in and must outlive every
    /// roster pull. Storing them together would have a routine refresh silently re-impose
    /// "connect once" on everybody, mid-shift, with no way to tell why.
    /// </summary>
    public const string DeviceVerifiers = "deviceVerifiers";

    /// <summary>
    /// The store's receipt layout as the portal set it — the raw `receiptTemplateJson` blob.
    ///
    /// ⚠ CACHED SO A RECEIPT PRINTS THE SAME WITH THE LINE DOWN. The template decides what a
    /// customer's paper says about the trader; falling back to a hardcoded layout during an outage
    /// would print a different receipt for the same shop, and the customer's copy is the only
    /// evidence they have.
    ///
    /// ⚠ The RAW blob, not a parsed one — the same reasoning as `VatBands`. A field this build does
    /// not understand survives the round trip and reaches a till that does.
    /// </summary>
    public const string ReceiptTemplate = "receiptTemplate";

    /// <summary>
    /// The portal's effective theme for this till, as the wire sent it (FE10 / step 22).
    ///
    /// ⚠ CACHED SO COLOURS SURVIVE AN OFFLINE RESTART. A till assigned a scheme must wear it after a
    /// reboot with the line down — otherwise the shop opens in the stock palette and somebody
    /// reasonably reports the assignment as broken. The web till caches the same blob in
    /// `localStorage` for the same reason, and applies it before first paint.
    ///
    /// ⚠ Stored VERBATIM, including `colorsJson`, which is an **opaque blob owned by the frontends** —
    /// the server does not validate it and neither does this. Reading it is
    /// `Client.Core.ThemeSlots`' job, and that is the only place a bad value is caught.
    /// </summary>
    public const string EffectiveTheme = "effectiveTheme";
}
