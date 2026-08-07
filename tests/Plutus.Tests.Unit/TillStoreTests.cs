using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// MAUI retrofit WP2 — the till's local store v2, against a real SQLite database (in-memory, but
/// the actual provider, so schema constraints and transactions are genuinely exercised).
///
/// These cover the parts that quietly ruin a POS: the sequence counter going backwards, a
/// scheduled price not landing on an offline till, a binned item still being sellable, and the
/// commit path losing a sale.
/// </summary>
public class TillStoreTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private TillDbContext _db = null!;
    private TillStore _store = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        _db = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        await _db.EnsureReadyAsync();
        _store = new TillStore(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private static IngestSaleRequest Sale() => new()
    {
        SaleId = Uuid7.New(),
        BusinessDay = new DateOnly(2026, 8, 7),
        OccurredAtUtc = DateTime.UtcNow,
        GrossPence = 600,
        VatPence = 100,
        Lines = { new IngestLine { Qty = 1, UnitPricePence = 600, LineGrossPence = 600, VatRateBp = 2000, VatAmountPence = 100 } },
        Tenders = { new IngestTender { TenderType = 0, AmountPence = 600 } },
    };

    [Fact]
    public async Task Schema_v2_is_created_and_stamped()
    {
        Assert.Equal("2", await _store.GetMetaAsync(MetaKeys.SchemaVersion));
    }

    [Fact]
    public async Task DeviceSeq_is_strictly_monotonic_and_survives_a_restart()
    {
        var a = await _store.CommitSaleAsync(Sale());
        var b = await _store.CommitSaleAsync(Sale());
        Assert.Equal(1, a.DeviceSeq);
        Assert.Equal(2, b.DeviceSeq);

        // "restart": a brand-new context over the SAME database file must not reset the counter —
        // a repeated sequence looks to the server like a replay and the sale is silently dropped.
        await using var reopened = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        var afterRestart = new TillStore(reopened);
        var c = await afterRestart.CommitSaleAsync(Sale());
        Assert.Equal(3, c.DeviceSeq);
    }

    [Fact]
    public async Task A_committed_sale_is_immediately_drainable_as_one_row()
    {
        // The sale and its outbox entry ARE the same row — they can never disagree.
        var sale = Sale();
        var row = await _store.CommitSaleAsync(sale);

        var pending = await _store.GetPendingAsync(10);
        Assert.Single(pending);
        Assert.Equal(row.SaleId, pending[0].SaleId);
        Assert.Equal(1, await _store.CountAsync(OutboxStatus.Pending));

        // and the payload is the exact contract, not a re-serialisation of entities
        var parsed = System.Text.Json.JsonSerializer.Deserialize<IngestSaleRequest>(
            pending[0].PayloadJson, PlutusApiClient.Json);
        Assert.Equal(sale.SaleId, parsed!.SaleId);
        Assert.Equal(600, parsed.GrossPence);
        Assert.Equal(1, parsed.DeviceSeq);      // the allocated sequence is IN the payload
    }

    [Fact]
    public async Task Marking_pushed_removes_it_from_the_queue_and_pruning_spares_what_is_still_owed()
    {
        var pushed = await _store.CommitSaleAsync(Sale());
        var failed = await _store.CommitSaleAsync(Sale());
        var stillPending = await _store.CommitSaleAsync(Sale());

        await _store.UpdateAsync(new OutboxEntry
        {
            SaleId = pushed.SaleId, Status = OutboxStatus.Pushed,
            PushedAtUtc = DateTime.UtcNow.AddDays(-30),
        });
        await _store.UpdateAsync(new OutboxEntry { SaleId = failed.SaleId, Status = OutboxStatus.Failed });

        Assert.Equal(1, await _store.CountAsync(OutboxStatus.Pending));

        var pruned = await _store.PrunePushedAsync(TimeSpan.FromDays(14));
        Assert.Equal(1, pruned);                                        // only the delivered one
        Assert.Equal(1, await _store.CountAsync(OutboxStatus.Failed));   // money a human must see
        Assert.Equal(1, await _store.CountAsync(OutboxStatus.Pending));  // money still owed
        Assert.Contains(await _db.LocalSales.ToListAsync(), s => s.SaleId == stillPending.SaleId);
    }

    [Fact]
    public async Task A_scheduled_price_activates_at_its_moment_even_with_no_sync()
    {
        var id = Uuid7.New();
        var at2am = new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc);
        _db.CatalogueItems.Add(new CatalogueItem { Id = id, IdOne = "5010", Name = "Mug", PricePence = 500, VatRateBp = 2000 });
        _db.PriceSchedule.Add(new PriceScheduleEntry { ItemId = id, EffectiveFromUtc = at2am, PricePence = 650 });
        await _db.SaveChangesAsync();

        // Evaluated at LOOKUP time, so the till needs no contact for a planned change to land.
        Assert.Equal(500, await _store.EffectivePricePenceAsync(id, at2am.AddSeconds(-1)));
        Assert.Equal(650, await _store.EffectivePricePenceAsync(id, at2am));
        Assert.Equal(650, await _store.EffectivePricePenceAsync(id, at2am.AddDays(7)));
    }

    [Fact]
    public async Task Barcode_lookup_resolves_aliases_and_refuses_binned_items()
    {
        var id = Uuid7.New();
        _db.CatalogueItems.Add(new CatalogueItem { Id = id, IdOne = "5010", Name = "Mug", PricePence = 500 });
        _db.Barcodes.Add(new BarcodeAlias { Code = "ALIAS-1", ItemId = id });

        var binnedId = Uuid7.New();
        _db.CatalogueItems.Add(new CatalogueItem { Id = binnedId, IdOne = "9999", Name = "Withdrawn", PricePence = 100, Removed = true });
        await _db.SaveChangesAsync();

        Assert.Equal(id, (await _store.FindByBarcodeAsync("5010"))!.Id);
        Assert.Equal(id, (await _store.FindByBarcodeAsync("ALIAS-1"))!.Id);
        Assert.Null(await _store.FindByBarcodeAsync("nope"));
        // FE5.4: a binned item must stop selling on an OFFLINE till too, which is why the feed
        // carries tombstones rather than just upserts.
        Assert.Null(await _store.FindByBarcodeAsync("9999"));
    }

    [Fact]
    public async Task A_tombstone_in_the_changes_feed_withdraws_an_item_already_on_the_till()
    {
        var id = Uuid7.New();
        await _store.ApplyCatalogueChangesAsync(
            new[] { new CatalogueItem { Id = id, IdOne = "5010", Name = "Mug", PricePence = 500 } }, 1);
        Assert.NotNull(await _store.FindByBarcodeAsync("5010"));

        await _store.ApplyCatalogueChangesAsync(
            new[] { new CatalogueItem { Id = id, IdOne = "5010", Name = "Mug", PricePence = 500, Removed = true } }, 2);

        Assert.Null(await _store.FindByBarcodeAsync("5010"));
        Assert.Equal("2", await _store.GetMetaAsync(MetaKeys.CatalogueVersion));
    }
}

/// <summary>
/// WP2 cutover: moving a till off the legacy Kapow-schema file. The hard stop here is the seam
/// with the translation agent — if a till's item ids disagree with the server's, the same barcode
/// means different things at each end and every sale that till pushes misattributes stock and
/// revenue, silently.
/// </summary>
public class CutoverTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private TillDbContext _db = null!;
    private static readonly Guid BusinessId = Guid.Parse("d5a31aac-159e-9a30-706b-02f9eb935600"); // Kapow

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        _db = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        await _db.EnsureReadyAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private static readonly LegacyItem[] Legacy =
    {
        new("5012345678900", "Batman: Year One", 14.99m, 12.49m, null),
        new("5012345678917", "Mug", 6.00m, 5.00m, null),
        new("5012345678924", "Zero-rated book", 8.00m, 8.00m, null),
    };

    [Fact]
    public void Item_ids_match_the_platform_derivation_exactly()
    {
        // The mapping is DeterministicGuid.ForItem keyed on the LEGACY BUSINESS id — the same
        // function the server and the web till use, so all three derive identical ids with no
        // mapping table anywhere.
        foreach (var l in Legacy)
            Assert.Equal(DeterministicGuid.ForItem(BusinessId, l.IdOne), Cutover.ItemIdFor(BusinessId, l.IdOne));

        // …and it is genuinely deterministic across runs, which is the property the whole cutover
        // rests on (unlike Migration.Kapow's IdRemap, which mints random ids per run).
        Assert.Equal(Cutover.ItemIdFor(BusinessId, "5012345678900"), Cutover.ItemIdFor(BusinessId, "5012345678900"));
        // a DIFFERENT business must not collide
        Assert.NotEqual(Cutover.ItemIdFor(BusinessId, "5012345678900"), Cutover.ItemIdFor(Guid.NewGuid(), "5012345678900"));
    }

    [Fact]
    public async Task Seeding_converts_money_to_pence_and_recovers_the_vat_band()
    {
        var result = await Cutover.SeedCatalogueAsync(_db, BusinessId, Legacy, "archive.db");
        Assert.Equal(3, result.ItemsSeeded);

        var batman = await _db.CatalogueItems.FirstAsync(i => i.IdOne == "5012345678900");
        Assert.Equal(1499, batman.PricePence);      // £14.99 → integer pence, no decimals survive
        // 14.99 / 12.49 derives 2002bp — real penny-rounding, snapped to the 20% band. Without the
        // snap, WP2b's ingest check would quarantine every sale of this item.
        Assert.Equal(2000, batman.VatRateBp);

        var book = await _db.CatalogueItems.FirstAsync(i => i.IdOne == "5012345678924");
        Assert.Equal(0, book.VatRateBp);            // price == exPrice → zero-rated

        // the businessId used for derivation is recorded, so nothing later has to guess it
        Assert.Equal(BusinessId.ToString("D"), (await _db.Meta.FirstAsync(m => m.Key == MetaKeys.BusinessId)).Value);
    }

    [Theory]
    // Penny-rounded real prices that derive slightly-off rates — all must snap to their band,
    // because a 2002bp item would be quarantined by WP2b's rate check on every sale.
    [InlineData(14.99, 12.49, 2000)]   // derives 2002
    [InlineData(9.99, 8.33, 2000)]     // derives 1993
    [InlineData(5.25, 5.00, 500)]      // exact 5%
    [InlineData(10.50, 10.00, 500)]    // exact 5%
    [InlineData(8.00, 8.00, 0)]        // zero-rated
    [InlineData(11.75, 10.00, 1750)]   // the historic 17.5% band
    public void Derived_vat_rates_snap_to_a_known_band(double price, double exPrice, int expectedBp) =>
        Assert.Equal(expectedBp, Cutover.VatRateBpFrom((decimal)price, (decimal)exPrice));

    [Fact]
    public void A_rate_far_from_any_band_is_preserved_not_flattened()
    {
        // A genuine oddity (bad legacy data, or a rate we don't know about) must survive for a
        // human to look at — silently calling it 20% would invent tax that was never charged.
        Assert.Equal(1000, Cutover.VatRateBpFrom(11.00m, 10.00m));
    }

    [Fact]
    public async Task Refuses_to_derive_ids_from_an_empty_business_id()
    {
        // Guards the silent-corruption trap: passing the tenant id (or nothing) yields ids that
        // look fine and are wrong everywhere.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Cutover.SeedCatalogueAsync(_db, Guid.Empty, Legacy, "archive.db"));
        Assert.Contains("not the tenant id", ex.Message);
    }

    [Fact]
    public async Task STOPS_when_the_server_disagrees_about_an_item_id()
    {
        // The §10 hard stop. If the server holds a different id for the same barcode, enrolling
        // this till would misattribute every sale it ever pushes.
        var wrong = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Cutover.SeedCatalogueAsync(_db, BusinessId, Legacy, "archive.db",
                centralIdLookup: _ => Task.FromResult<Guid?>(wrong)));

        Assert.Contains("STOP", ex.Message);
        Assert.Contains("do not enrol", ex.Message);
    }

    [Fact]
    public async Task Agrees_with_a_server_that_derives_ids_the_same_way()
    {
        var result = await Cutover.SeedCatalogueAsync(_db, BusinessId, Legacy, "archive.db",
            centralIdLookup: code => Task.FromResult<Guid?>(DeterministicGuid.ForItem(BusinessId, code)));
        Assert.Equal(3, result.SpotChecked);
    }

    [Fact]
    public async Task Duplicate_barcodes_in_the_legacy_file_do_not_fail_the_whole_cutover()
    {
        // Real legacy data has these; a unique index would otherwise abort on the last row and
        // leave a shop with no catalogue at all.
        var withDupes = Legacy.Append(new LegacyItem("5012345678900", "Batman (duplicate row)", 14.99m, 12.49m, null)).ToArray();
        var result = await Cutover.SeedCatalogueAsync(_db, BusinessId, withDupes, "archive.db");
        Assert.Equal(3, result.ItemsSeeded);
    }

    [Fact]
    public void Archiving_copies_the_legacy_file_and_refuses_to_overwrite_an_archive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plutus-cutover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var legacy = Path.Combine(dir, "Database.db");
            File.WriteAllText(legacy, "legacy bytes");
            var archiveDir = Path.Combine(dir, "archive");
            var at = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

            var archived = Cutover.ArchiveLegacyDatabase(legacy, archiveDir, at);

            // COPY, not move: §9.3 is archive-never-delete, and the original must survive a
            // failure later in the cutover.
            Assert.True(File.Exists(legacy));
            Assert.True(File.Exists(archived));
            Assert.Equal("legacy bytes", File.ReadAllText(archived));

            // a second cutover at the same instant must not silently replace the first archive —
            // that would destroy the only copy of a till's history
            Assert.Throws<IOException>(() => Cutover.ArchiveLegacyDatabase(legacy, archiveDir, at));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_missing_legacy_file_is_a_clear_error_not_a_null()
    {
        Assert.Throws<FileNotFoundException>(() =>
            Cutover.ArchiveLegacyDatabase(Path.Combine(Path.GetTempPath(), "does-not-exist.db"),
                Path.GetTempPath(), DateTime.UtcNow));
    }
}
