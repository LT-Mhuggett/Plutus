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
    public const int SchemaVersion = 4;

    public TillDbContext(DbContextOptions<TillDbContext> options) : base(options) { }

    public DbSet<MetaEntry> Meta => Set<MetaEntry>();
    public DbSet<CatalogueItem> CatalogueItems => Set<CatalogueItem>();
    public DbSet<PriceScheduleEntry> PriceSchedule => Set<PriceScheduleEntry>();
    public DbSet<LocalOperator> Operators => Set<LocalOperator>();
    public DbSet<LocalSale> LocalSales => Set<LocalSale>();
    public DbSet<LocalRefund> LocalRefunds => Set<LocalRefund>();
    public DbSet<SavedBasket> SavedBaskets => Set<SavedBasket>();
    public DbSet<LocalCashEvent> LocalCashEvents => Set<LocalCashEvent>();

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

        // ⚠ The `Barcodes` table is gone (2026-08-09) — see LocalSchema. It was mapped and read and
        // never once written, because no server-side barcode entity exists to feed it. An existing
        // till simply keeps an empty table it no longer opens; there is nothing to migrate, and
        // EnsureReadyAsync creates the rest unchanged.

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
                    "ServerResponseJson" TEXT NULL
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
    public const string LegacyArchivedAtUtc = "legacyArchivedAtUtc";

    /// <summary>The portal's published VAT bands, WHOLE effective-dated timeline, as the wire sent
    /// them. ⚠ Never "today's rate" — the timeline is what lets an offline till apply a
    /// future-dated rate change on the correct day.</summary>
    public const string VatBands = "vatBands";
}
