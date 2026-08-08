using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The till's copy of the portal's VAT bands.
///
/// ⚠ The failure this exists to prevent is expensive and completely silent: a till that cached
/// "the standard rate is 20%" instead of the TIMELINE, went offline across a rate change, and kept
/// charging the old rate. WP2b's ingest check would then quarantine its entire backlog on
/// reconnect — a day's takings stuck, discovered late, on a shop's busiest week.
/// </summary>
public class VatBandCacheTests
{
    private sealed class FakeStore : IVatBandStore
    {
        public VatBandsResult? Bands;
        public int Saves;
        public Task<VatBandsResult?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Bands);
        public Task SaveAsync(VatBandsResult bands, CancellationToken ct = default) { Bands = bands; Saves++; return Task.CompletedTask; }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(Status != HttpStatusCode.OK
                ? new HttpResponseMessage(Status)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Body ?? "{}", System.Text.Encoding.UTF8, "application/json"),
                });
    }

    private static readonly DateTime Jan = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jul = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Standard at 20% from January, moving to 17.5% in July — a future-dated change,
    /// which is the case that matters.</summary>
    private static VatBandsResult Timeline() => new(Jan, "test", new[]
    {
        new VatBandDto("standard", "Standard", "Standard", 2000, Jan,
            new[] { new VatRatePointDto(2000, Jan), new VatRatePointDto(1750, Jul) }, new[] { 1 }),
        new VatBandDto("zero", "Zero rated", "Zero", 0, Jan,
            new[] { new VatRatePointDto(0, Jan) }, new[] { 2 }),
    });

    private static (VatBandCache Cache, FakeStore Store, Handler Handler) Build()
    {
        var handler = new Handler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://plutus.example") };
        var store = new FakeStore();
        return (new VatBandCache(new PlutusApiClient(http), store), store, handler);
    }

    // ── the timeline ──

    [Fact]
    public async Task A_FUTURE_dated_change_applies_on_the_day_to_a_till_that_never_reconnects()
    {
        // ⚠ THE WHOLE POINT. The till syncs in January and is never online again. It must charge
        // 20% in June and 17.5% in August, on its own.
        var (cache, store, _) = Build();
        store.Bands = Timeline();

        Assert.Equal(2000, await cache.RateBpAtAsync("standard", new DateTime(2026, 6, 30, 23, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(1750, await cache.RateBpAtAsync("standard", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task The_rate_is_taken_at_the_SALE_instant_not_at_sync_time()
    {
        // A sale being re-read, or an outbox entry draining days later, must price against when it
        // HAPPENED — not when the till last spoke to the server.
        var (cache, store, _) = Build();
        store.Bands = Timeline();

        Assert.Equal(2000, await cache.RateBpAtAsync("standard", Jan.AddDays(1)));
        Assert.Equal(1750, await cache.RateBpAtAsync("standard", Jul.AddDays(1)));
    }

    [Fact]
    public async Task Before_a_band_starts_there_is_no_rate_rather_than_a_guess()
    {
        var (cache, store, _) = Build();
        store.Bands = Timeline();

        Assert.Null(await cache.RateBpAtAsync("standard", Jan.AddDays(-1)));
    }

    [Fact]
    public async Task An_unknown_band_is_null_never_zero()
    {
        // ⚠ Returning 0 would silently zero-rate everything on that band — a VAT return that looks
        // plausible and is wrong in the expensive direction.
        var (cache, store, _) = Build();
        store.Bands = Timeline();

        Assert.Null(await cache.RateBpAtAsync("reduced", Jul));
    }

    // ── refresh ──

    [Fact]
    public async Task Refresh_stores_the_bands()
    {
        var (cache, store, handler) = Build();
        handler.Body =
            "{\"asOfUtc\":\"2026-01-01T00:00:00Z\",\"basis\":\"test\",\"bands\":[" +
            "{\"key\":\"standard\",\"displayName\":\"Standard\",\"vatClass\":\"Standard\",\"rateBp\":2000," +
            "\"effectiveFromUtc\":\"2026-01-01T00:00:00Z\"," +
            "\"rates\":[{\"rateBp\":2000,\"effectiveFromUtc\":\"2026-01-01T00:00:00Z\"}],\"legacyTaxIds\":[1]}]}";

        Assert.Equal(1, await cache.RefreshAsync());
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task A_failed_refresh_KEEPS_the_bands_the_till_already_had()
    {
        // ⚠ An empty band list is not a smaller problem than a stale one — it is a till that cannot
        // price anything. Dropping the cache because the wifi blinked would stop a shop trading.
        var (cache, store, handler) = Build();
        store.Bands = Timeline();
        handler.Status = HttpStatusCode.InternalServerError;

        Assert.Null(await cache.RefreshAsync());
        Assert.Equal(0, store.Saves);
        Assert.Equal(2000, await cache.RateBpAtAsync("standard", Jan.AddDays(1)));
    }

    [Fact]
    public async Task A_till_that_has_never_synced_says_so_rather_than_pricing_at_zero()
    {
        var (cache, _, _) = Build();

        Assert.False(await cache.HasBandsAsync());
        Assert.Null(await cache.RateBpAtAsync("standard", Jan));
    }

    // ── legacy tax row → band ──

    [Fact]
    public async Task A_legacy_tax_row_resolves_to_its_band()
    {
        var (cache, store, _) = Build();
        store.Bands = Timeline();

        Assert.Equal("standard", await cache.BandKeyForTaxIdAsync(1));
        Assert.Equal("zero", await cache.BandKeyForTaxIdAsync(2));
    }

    [Fact]
    public async Task An_UNMAPPED_tax_row_is_null_and_that_is_CORRECT()
    {
        // ⚠ Null means "nobody has said which band this is", and the server backfills what it can
        // and reports the rest as unclassified — which is VISIBLE. A guess here would be an invented
        // figure on a VAT return that nothing would ever flag.
        var (cache, store, _) = Build();
        store.Bands = Timeline();

        Assert.Null(await cache.BandKeyForTaxIdAsync(99));
    }

    [Fact]
    public async Task A_tax_row_claimed_by_TWO_bands_is_ambiguous_not_first_wins()
    {
        // ⚠ Zero-rated and exempt are both 0%, so "nearest rate, first wins" would silently
        // attribute takings to whichever sorted first — the exact bug VatAccounting.BandFor was
        // changed to stop committing. Ambiguity must surface as unclassified.
        //
        // ⚠ PARITY NOTE: the web till's api.ts returns the FIRST match here. This is stricter, and
        // the divergence is recorded in till-design C2 — the web till should follow, not this
        // relax.
        var (cache, store, _) = Build();
        store.Bands = new VatBandsResult(Jan, "test", new[]
        {
            new VatBandDto("zero", "Zero", "Zero", 0, Jan, new[] { new VatRatePointDto(0, Jan) }, new[] { 7 }),
            new VatBandDto("exempt", "Exempt", "Exempt", 0, Jan, new[] { new VatRatePointDto(0, Jan) }, new[] { 7 }),
        });

        Assert.Null(await cache.BandKeyForTaxIdAsync(7));
    }
}
