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
    public const int SchemaVersion = 2;

    public TillDbContext(DbContextOptions<TillDbContext> options) : base(options) { }

    public DbSet<MetaEntry> Meta => Set<MetaEntry>();
    public DbSet<CatalogueItem> CatalogueItems => Set<CatalogueItem>();
    public DbSet<PriceScheduleEntry> PriceSchedule => Set<PriceScheduleEntry>();
    public DbSet<BarcodeAlias> Barcodes => Set<BarcodeAlias>();
    public DbSet<LocalOperator> Operators => Set<LocalOperator>();
    public DbSet<LocalSale> LocalSales => Set<LocalSale>();
    public DbSet<SavedBasket> SavedBaskets => Set<SavedBasket>();

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

        b.Entity<BarcodeAlias>(e =>
        {
            e.ToTable("Barcodes");
            e.HasKey(x => x.Code);
            e.HasIndex(x => x.ItemId);
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

        b.Entity<SavedBasket>(e => { e.ToTable("SavedBaskets"); e.HasKey(x => x.Id); });
    }

    /// <summary>Create the store if absent and stamp its schema version.</summary>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await Database.EnsureCreatedAsync(ct);
        var stamped = await Meta.FindAsync(new object[] { MetaKeys.SchemaVersion }, ct);
        if (stamped == null)
        {
            Meta.Add(new MetaEntry { Key = MetaKeys.SchemaVersion, Value = SchemaVersion.ToString() });
            await SaveChangesAsync(ct);
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
