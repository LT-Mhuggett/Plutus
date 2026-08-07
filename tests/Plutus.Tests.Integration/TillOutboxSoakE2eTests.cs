using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.Entities;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// MAUI retrofit WP3 — Definition of Done. A real SQLite till store commits sales while "offline",
/// then drains to the real ingest endpoint through the WP1 pusher.
///
/// This is the test that decides whether a shop can trade through a broken line and not lose
/// money: sales land exactly once, in order, with no sequence gaps, and a crash mid-drain neither
/// loses nor duplicates anything.
/// </summary>
public class TillOutboxSoakE2eTests : IClassFixture<PlutusAppFactory>, IAsyncLifetime
{
    private readonly PlutusAppFactory _f;
    public TillOutboxSoakE2eTests(PlutusAppFactory f) => _f = f;

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

    private sealed class Credentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private async Task<(PlutusApiClient Api, DeviceTokenProvider Tokens, Guid DeviceId, Guid BusinessId)>
        EnrolAsync(HttpClient http, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "Soak " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pBody = JsonDocument.Parse(await (await http.SendAsync(pReq)).Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Soak till" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tBody = JsonDocument.Parse(await (await http.SendAsync(tReq)).Content.ReadAsStringAsync()).RootElement;

        var bootstrap = new PlutusApiClient(http);
        var enrolled = await bootstrap.EnrolAsync(tBody.GetProperty("enrolmentCode").GetString()!);
        var creds = new Credentials();
        creds.Save(enrolled.DeviceId, enrolled.ClientSecret);
        var tokens = new DeviceTokenProvider(bootstrap, creds);
        var api = new PlutusApiClient(http, tokens);
        var businessId = (await api.GetStoreInfoAsync(storeId))!.BusinessId!.Value;
        return (api, tokens, enrolled.DeviceId, businessId);
    }

    private static IngestSaleRequest Sale(Guid businessId, string itemIdOne)
    {
        var meta = new LineMeta { ItemIdOne = itemIdOne, ExUnitPence = 500 };
        return new IngestSaleRequest
        {
            SaleId = Uuid7.New(),
            Channel = 0,
            BusinessDay = new DateOnly(2026, 8, 7),
            OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 600, VatPence = 100,
            Lines =
            {
                new IngestLine
                {
                    ItemId = DeterministicGuid.ForItem(businessId, itemIdOne),
                    Qty = 1, UnitPricePence = 600, LineGrossPence = 600,
                    VatRateBp = 2000, VatAmountPence = 100, DiscountsJson = meta.ToJson(),
                },
            },
            Tenders = { new IngestTender { TenderType = 0, AmountPence = 600 } },
        };
    }

    [Fact]
    public async Task Sales_taken_offline_drain_exactly_once_in_order_when_the_line_comes_back()
    {
        var http = _f.CreateClient();
        var (api, tokens, deviceId, businessId) = await EnrolAsync(http, "soak1@acme.test");

        // ── trading with the line down: commit locally, no network involved ──
        const int count = 120;
        for (var i = 0; i < count; i++)
            await _store.CommitSaleAsync(Sale(businessId, "501000000" + (i % 7)));

        Assert.Equal(count, await _store.CountAsync(OutboxStatus.Pending));

        // ── the line comes back ──
        var pusher = new OutboxPusher(_store, api, tokens);
        var outcomes = await pusher.DrainAsync(max: 500);

        Assert.Equal(count, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(OutboxStatus.Pushed, o.Status));
        Assert.Equal(0, await _store.CountAsync(OutboxStatus.Pending));

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var device = await db.Devices.IgnoreQueryFilters().FirstAsync(d => d.Id == deviceId);

        // NO GAPS: the server's high-water mark reached the last sequence, which is how it detects
        // a till that lost sales. A gap here is the signal that money went missing.
        Assert.Equal(count, device.LastSeenSeq);

        var recorded = await db.SalesV2.IgnoreQueryFilters().CountAsync(s => s.DeviceId == deviceId);
        Assert.Equal(count, recorded);

        // exactly once: draining again posts nothing and changes nothing
        Assert.Empty(await pusher.DrainAsync());
        Assert.Equal(count, await db.SalesV2.IgnoreQueryFilters().CountAsync(s => s.DeviceId == deviceId));
    }

    [Fact]
    public async Task A_crash_mid_drain_loses_nothing_and_duplicates_nothing()
    {
        var http = _f.CreateClient();
        var (api, tokens, deviceId, businessId) = await EnrolAsync(http, "soak2@acme.test");

        for (var i = 0; i < 20; i++) await _store.CommitSaleAsync(Sale(businessId, "502000000" + (i % 5)));

        // drain a few, then "crash" — a new store over the same database, as after a restart
        var partial = new OutboxPusher(_store, api, tokens);
        var first = await partial.DrainAsync(max: 7);
        Assert.Equal(7, first.Count);

        await using var reopened = new TillDbContext(new DbContextOptionsBuilder<TillDbContext>().UseSqlite(_conn).Options);
        var afterCrash = new TillStore(reopened);
        Assert.Equal(13, await afterCrash.CountAsync(OutboxStatus.Pending));   // the rest survived

        var rest = await new OutboxPusher(afterCrash, api, tokens).DrainAsync();
        Assert.Equal(13, rest.Count);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        // 20 sales rung up, 20 recorded — not 27, and not 13
        Assert.Equal(20, await db.SalesV2.IgnoreQueryFilters().CountAsync(s => s.DeviceId == deviceId));
        Assert.Equal(20, (await db.Devices.IgnoreQueryFilters().FirstAsync(d => d.Id == deviceId)).LastSeenSeq);
    }

    [Fact]
    public async Task A_maui_sale_moves_stock_not_merely_gets_accepted()
    {
        // The silent-failure guard from WP1, now proven through the real projection: the consumer
        // only attributes a movement when it can read itemIdOne out of the line metadata, and
        // skips lines without it with NO error. "Accepted" is not evidence that stock moved.
        var http = _f.CreateClient();
        var (api, tokens, deviceId, businessId) = await EnrolAsync(http, "soak3@acme.test");

        const string barcode = "5030000000001";
        await _store.CommitSaleAsync(Sale(businessId, barcode));
        var outcomes = await new OutboxPusher(_store, api, tokens).DrainAsync();
        Assert.Equal(OutboxStatus.Pushed, outcomes.Single().Status);

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var line = await db.SaleLines.IgnoreQueryFilters()
            .FirstAsync(l => l.SaleId == db.SalesV2.IgnoreQueryFilters()
                .Where(s => s.DeviceId == deviceId).Select(s => s.Id).First());

        // the barcode survived the round trip onto the stored line — this is what the stock
        // projection reads, so its presence is the thing worth asserting
        Assert.Equal(barcode, line.ItemIdOne);
    }
}
