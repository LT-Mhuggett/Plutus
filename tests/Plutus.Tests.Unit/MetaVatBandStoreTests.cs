using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using System.Net.Http;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Cutover step 7 — the till's VAT band cache, against a real SQLite store.
///
/// ⚠ WHY THIS EXISTS AT ALL. `IVatBandStore` had NO production implementation anywhere in the
/// repo: the only one was a fake inside `VatBandCacheTests`. So `VatBandCache` — the class that
/// decides which VAT rate a line is charged at — was fully written, fully tested, and could not be
/// constructed by any till. Same shape as WP5's sync spine and `RefundRules`: tested components
/// are not a working feature until something can use them.
/// </summary>
public class MetaVatBandStoreTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private TillDbContext _db = null!;
    private TillStore _store = null!;
    private MetaVatBandStore _bands = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        _db = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        await _db.EnsureReadyAsync();
        _store = new TillStore(_db);
        _bands = new MetaVatBandStore(_store);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    /// <summary>The cache also fetches when told to; these tests only exercise the CACHED path, so
    /// the client points nowhere and is never used.</summary>
    private static VatBandCache Cache(IVatBandStore store) =>
        new(new PlutusApiClient(new HttpClient { BaseAddress = new Uri("https://till.example") }), store);

    private static readonly DateTime RateChange = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Standard at 20%, rising to 22% on 1 October — a FUTURE point at save time.</summary>
    private static VatBandsResult Published() => new(
        AsOfUtc: new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc),
        Basis: "accrual",
        Bands: new[]
        {
            new VatBandDto("standard", "Standard", "Standard", 2000, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new[]
                {
                    new VatRatePointDto(2000, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    new VatRatePointDto(2200, RateChange),
                },
                new[] { 1 }),
            // ⚠ Zero and Exempt are BOTH 0% and different in law — the pair that proves the class
            // survives the round trip, because no rate can carry the distinction.
            new VatBandDto("zero", "Zero rated", "Zero", 0, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new[] { new VatRatePointDto(0, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)) }, new[] { 2 }),
            new VatBandDto("exempt", "Exempt", "Exempt", 0, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new[] { new VatRatePointDto(0, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)) }, new[] { 3 }),
        });

    /// <summary>
    /// ⚠ THE ONE THAT MATTERS. The WHOLE timeline is cached, not today's rate — so a till that
    /// syncs today and then loses its connection still starts charging the new rate on the day it
    /// takes effect. Caching "the standard rate is 20%" would have it charging 20% into November,
    /// and WP2b's ingest check would quarantine its entire backlog on reconnect.
    /// </summary>
    [Fact]
    public async Task A_future_rate_change_applies_on_the_day_with_the_network_down()
    {
        await _bands.SaveAsync(Published());

        var cache = Cache(_bands);

        Assert.Equal(2000, await cache.RateBpAtAsync("standard", RateChange.AddSeconds(-1)));
        Assert.Equal(2200, await cache.RateBpAtAsync("standard", RateChange));
        Assert.Equal(2200, await cache.RateBpAtAsync("standard", RateChange.AddDays(60)));
    }

    /// <summary>Survives a restart: the bands live in the database, not in memory.</summary>
    [Fact]
    public async Task The_timeline_survives_a_restart()
    {
        await _bands.SaveAsync(Published());

        await using var reopened = new TillDbContext(
            new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        var afterRestart = Cache(new MetaVatBandStore(new TillStore(reopened)));

        Assert.Equal(2200, await afterRestart.RateBpAtAsync("standard", RateChange));
        Assert.True(await afterRestart.HasBandsAsync());
    }

    /// <summary>
    /// ⚠ NULL IS NOT "NO VAT". A till that has never synced is UNINFORMED, and a caller that read
    /// that as zero-rated would put 0% on every line of a real sale. `HasBandsAsync` is what lets
    /// the two be told apart.
    /// </summary>
    [Fact]
    public async Task A_till_that_has_never_synced_is_uninformed_not_zero_rated()
    {
        Assert.Null(await _bands.LoadAsync());

        var cache = Cache(_bands);
        Assert.False(await cache.HasBandsAsync());
        Assert.Null(await cache.RateBpAtAsync("standard", DateTime.UtcNow));
    }

    /// <summary>
    /// The zero-vs-exempt distinction survives the round trip, and an AMBIGUOUS tax row still
    /// resolves to null rather than a coin toss — the rule `VatBandCache` already owned, proven
    /// against a real stored payload rather than a fake.
    /// </summary>
    [Fact]
    public async Task Zero_and_exempt_round_trip_and_an_ambiguous_row_stays_unresolved()
    {
        await _bands.SaveAsync(Published());
        var cache = Cache(_bands);

        Assert.Equal("zero", await cache.BandKeyForTaxIdAsync(2));
        Assert.Equal("exempt", await cache.BandKeyForTaxIdAsync(3));

        var loaded = await _bands.LoadAsync();
        Assert.Equal("Zero", Array.Find(loaded!.Bands, b => b.Key == "zero")!.VatClass);
        Assert.Equal("Exempt", Array.Find(loaded.Bands, b => b.Key == "exempt")!.VatClass);

        // a tax row claimed by two bands is a wrong mapping, not a tie to break
        var ambiguous = Published() with
        {
            Bands = new[]
            {
                new VatBandDto("zero", "Zero rated", "Zero", 0, DateTime.UnixEpoch,
                    new[] { new VatRatePointDto(0, DateTime.UnixEpoch) }, new[] { 9 }),
                new VatBandDto("exempt", "Exempt", "Exempt", 0, DateTime.UnixEpoch,
                    new[] { new VatRatePointDto(0, DateTime.UnixEpoch) }, new[] { 9 }),
            },
        };
        await _bands.SaveAsync(ambiguous);
        Assert.Null(await Cache(_bands).BandKeyForTaxIdAsync(9));
    }

    /// <summary>
    /// ⚠ A corrupt or older-shaped blob reads as "not told yet", never as an exception thrown out
    /// of a rate lookup on the selling path. The next sync overwrites it.
    /// </summary>
    [Fact]
    public async Task A_corrupt_cached_payload_reads_as_never_told_rather_than_throwing()
    {
        await _store.SetMetaAsync(MetaKeys.VatBands, "{ this is not json");

        Assert.Null(await _bands.LoadAsync());
        Assert.False(await Cache(_bands).HasBandsAsync());
    }
}
