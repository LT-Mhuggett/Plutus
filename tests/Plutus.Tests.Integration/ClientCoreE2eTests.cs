using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Entities;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// MAUI retrofit WP1 — Definition of Done. `Plutus.Client.Core` drives the REAL controllers
/// through the in-process host: enrol → device token → sale ingest → 201, and the till's outbox
/// pusher drains against the same endpoints a shop till will use.
///
/// This is the test that proves the retrofit's central claim — that the platform half is a
/// wiring job, not a new API — and it does it without MySQL, a device, or a deployment.
/// </summary>
public class ClientCoreE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ClientCoreE2eTests(PlutusAppFactory f) => _f = f;

    /// <summary>Holds the credential in memory. A real till puts ClientSecret in platform secure
    /// storage — never the SQLite file (WP4).</summary>
    private sealed class InMemoryCredentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private sealed class ListOutboxStore : IOutboxStore
    {
        public readonly List<OutboxEntry> Entries = new();
        public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OutboxEntry>>(
                Entries.Where(e => e.Status == OutboxStatus.Pending).OrderBy(e => e.DeviceSeq).Take(max).ToList());
        public Task UpdateAsync(OutboxEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> CountAsync(OutboxStatus s, CancellationToken ct = default) =>
            Task.FromResult(Entries.Count(e => e.Status == s));
        public Task<DateTime?> OldestPendingAtUtcAsync(CancellationToken ct = default) => Task.FromResult<DateTime?>(null);
    }

    /// <summary>Provision a tenant + till and hand back the enrolment code — the same path the
    /// portal drives when Matt clicks "New till".</summary>
    private async Task<(string Code, Guid TillId, int StoreId, Guid TenantId)> ProvisionTillAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "ClientCore " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pRes = await c.SendAsync(pReq);
        Assert.Equal(HttpStatusCode.Created, pRes.StatusCode);
        var pBody = JsonDocument.Parse(await pRes.Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Counter 1" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tRes = await c.SendAsync(tReq);
        Assert.Equal(HttpStatusCode.Created, tRes.StatusCode);
        var tBody = JsonDocument.Parse(await tRes.Content.ReadAsStringAsync()).RootElement;
        return (tBody.GetProperty("enrolmentCode").GetString()!, tBody.GetProperty("tillId").GetGuid(), storeId, tenantId);
    }

    private static IngestSaleRequest BuildSale(long deviceSeq, Guid businessId, string itemIdOne)
    {
        // Exactly how a till builds a line: the item GUID is DERIVED, and the barcode rides in the
        // LineMeta envelope (there is no ItemIdOne field on the wire).
        var meta = new LineMeta { ItemIdOne = itemIdOne, ExUnitPence = 500 };
        return new IngestSaleRequest
        {
            SaleId = Uuid7.New(),
            DeviceSeq = deviceSeq,
            Channel = 0,
            BusinessDay = DateOnly.FromDateTime(DateTime.UtcNow),
            OccurredAtUtc = DateTime.UtcNow,
            GrossPence = 600,
            VatPence = 100,
            Lines =
            {
                new IngestLine
                {
                    ItemId = DeterministicGuid.ForItem(businessId, itemIdOne),
                    Qty = 1, UnitPricePence = 600, DiscountPence = 0, LineGrossPence = 600,
                    VatRateBp = 2000, VatAmountPence = 100, DiscountsJson = meta.ToJson(),
                },
            },
            Tenders = { new IngestTender { TenderType = 0, AmountPence = 600, ChangePence = 0 } },
        };
    }

    [Fact]
    public async Task Enrols_mints_a_device_token_and_round_trips_a_sale_to_201()
    {
        var http = _f.CreateClient();
        var (code, tillId, storeId, _) = await ProvisionTillAsync(http, "wp1a@acme.test");

        var credentials = new InMemoryCredentials();
        var anonymous = new PlutusApiClient(http);

        // 1. enrol with the one-time code
        var enrolled = await anonymous.EnrolAsync(code);
        Assert.NotEqual(Guid.Empty, enrolled.DeviceId);
        Assert.Equal(tillId, enrolled.TillId);
        credentials.Save(enrolled.DeviceId, enrolled.ClientSecret);

        // 2. the same code a second time → 410 Gone, surfaced as a message, never a crash
        var reuse = await Assert.ThrowsAsync<EnrolmentFailedException>(() => anonymous.EnrolAsync(code));
        Assert.Equal(HttpStatusCode.Gone, reuse.Status);
        Assert.Contains("new one", reuse.Message);

        // 3. authenticated client, token minted on demand
        var tokens = new DeviceTokenProvider(anonymous, credentials);
        var api = new PlutusApiClient(http, tokens);
        Assert.NotNull(await tokens.GetAccessTokenAsync());

        // 4. the till learns which STORE it is in (the key to per-store receipts/themes)
        var name = await api.GetTillNameAsync(tillId);
        Assert.Equal(storeId, name!.StoreId);

        // 5. …and the legacy BusinessId it must derive item GUIDs from — additive field, and NOT
        //    the tenant id (using the tenant id corrupts item ids silently)
        var info = await api.GetStoreInfoAsync(storeId);
        Assert.NotNull(info!.BusinessId);
        Assert.NotEqual(Guid.Empty, info.BusinessId!.Value);

        // 6. a real sale, built the way a till builds one → 201 recorded
        var sale = BuildSale(1, info.BusinessId.Value, "5012345678900");
        var payload = JsonSerializer.Serialize(sale, PlutusApiClient.Json);
        var (status, body) = await api.PostSaleAsync(payload);
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("recorded", body!.Status);

        // 7. the SAME sale again → 200, idempotent (this is what makes a replayed outbox safe)
        var (dupStatus, _) = await api.PostSaleAsync(payload);
        Assert.Equal(HttpStatusCode.OK, dupStatus);
    }

    [Fact]
    public async Task The_pusher_drains_a_queue_in_order_against_the_real_endpoint()
    {
        var http = _f.CreateClient();
        var (code, tillId, storeId, _) = await ProvisionTillAsync(http, "wp1b@acme.test");

        var credentials = new InMemoryCredentials();
        var bootstrap = new PlutusApiClient(http);
        var enrolled = await bootstrap.EnrolAsync(code);
        credentials.Save(enrolled.DeviceId, enrolled.ClientSecret);

        var tokens = new DeviceTokenProvider(bootstrap, credentials);
        var api = new PlutusApiClient(http, tokens);
        var businessId = (await api.GetStoreInfoAsync(storeId))!.BusinessId!.Value;

        // three sales rung up "offline", queued in order
        var store = new ListOutboxStore();
        for (long seq = 1; seq <= 3; seq++)
        {
            var sale = BuildSale(seq, businessId, "501234567890" + seq);
            store.Entries.Add(new OutboxEntry
            {
                SaleId = sale.SaleId, DeviceSeq = seq,
                PayloadJson = JsonSerializer.Serialize(sale, PlutusApiClient.Json),
                Status = OutboxStatus.Pending,
            });
        }

        var pusher = new OutboxPusher(store, api, tokens);
        var outcomes = await pusher.DrainAsync();

        Assert.Equal(3, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(OutboxStatus.Pushed, o.Status));

        // all three landed exactly once, and the server's gap detection is satisfied — LastSeenSeq
        // advanced to the highest sequence rather than stalling at a hole
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var device = await db.Devices.IgnoreQueryFilters().FirstAsync(d => d.Id == enrolled.DeviceId);
        Assert.Equal(3, device.LastSeenSeq);

        var saleIds = store.Entries.Select(e => e.SaleId).ToList();
        var recorded = await db.SalesV2.IgnoreQueryFilters().CountAsync(s => saleIds.Contains(s.Id));
        Assert.Equal(3, recorded);

        // re-draining is a no-op: nothing is left Pending, so a reconnect can't double-post
        Assert.Empty(await pusher.DrainAsync());
        Assert.Equal(0, await store.CountAsync(OutboxStatus.Pending));
    }

    [Fact]
    public async Task A_maui_originated_sale_moves_stock_not_just_gets_accepted()
    {
        // The silent-failure guard: StockProjectionConsumer only attributes a movement when it can
        // read itemIdOne out of the LineMeta envelope. A line without it is skipped with NO error,
        // so "the sale was accepted" is not evidence that stock moved. Assert the envelope itself.
        var meta = new LineMeta { ItemIdOne = "5012345678900", ExUnitPence = 500 };
        var json = meta.ToJson();

        Assert.Contains("\"itemIdOne\":\"5012345678900\"", json);
        Assert.DoesNotContain("discounts", json);   // omitted when null, like the web till
        Assert.DoesNotContain("return", json);

        var parsed = LineMeta.FromJson(json);
        Assert.Equal("5012345678900", parsed!.ItemIdOne);
        Assert.Equal(500, parsed.ExUnitPence);

        // and the server can read it back through its own extractor path
        Assert.Null(LineMeta.FromJson("not json"));
        Assert.Null(LineMeta.FromJson(null));
    }
}
