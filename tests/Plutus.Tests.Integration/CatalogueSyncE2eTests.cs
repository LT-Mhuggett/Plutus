using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Client.Core;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP5 — the catalogue changes feed and the heartbeat, against the real controllers.
///
/// These two endpoints are what turn the MAUI till from "trades offline against whatever it was
/// installed with" into "trades offline against a catalogue that is current". Everything downstream
/// (prices, the Bin, VAT bands) arrives through this pipe, so its edges are worth pinning hard:
/// tombstones, paging under concurrent edits, and the tenant boundary.
/// </summary>
public class CatalogueSyncE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CatalogueSyncE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class InMemoryCredentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string? ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private sealed record Till(HttpClient Http, string DeviceToken, Guid DeviceId, Guid TillId, Guid TenantId, Guid BusinessId);

    /// <summary>Provision a tenant + till, enrol a device, and seed a business to hang items on.</summary>
    private async Task<Till> ProvisionAsync(string email)
    {
        var http = _f.CreateClient();

        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using var pReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenants")
        { Content = JsonContent.Create(new { name = "Cat " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" }) };
        pReq.Headers.Authorization = new("Bearer", admin);
        var pRes = await http.SendAsync(pReq);
        Assert.Equal(HttpStatusCode.Created, pRes.StatusCode);
        var pBody = JsonDocument.Parse(await pRes.Content.ReadAsStringAsync()).RootElement;
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        using var tReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tills")
        { Content = JsonContent.Create(new { storeId, name = "Counter 1" }) };
        tReq.Headers.Authorization = new("Bearer", portal);
        var tRes = await http.SendAsync(tReq);
        Assert.Equal(HttpStatusCode.Created, tRes.StatusCode);
        var code = JsonDocument.Parse(await tRes.Content.ReadAsStringAsync()).RootElement
            .GetProperty("enrolmentCode").GetString()!;

        var api = new PlutusApiClient(http);
        var enrolled = await api.EnrolAsync(code);
        var token = (await api.GetDeviceTokenAsync(enrolled.DeviceId, enrolled.ClientSecret)).AccessToken;

        var businessId = await SeedBusinessAsync(tenantId);
        return new Till(http, token, enrolled.DeviceId, enrolled.TillId, tenantId, businessId);
    }

    private async Task<Guid> SeedBusinessAsync(Guid tenantId)
    {
        using var scope = _f.Services.CreateScope();
        var db = Db(scope, tenantId);
        var bizId = Guid.NewGuid();
        db.Business.Add(new Business { Id = bizId, Name = "Cat E2E", NameAbbr = "CE2E", VatIN = "GB0" });
        db.Taxes.Add(new Tax { IdOne = 1, IdTwo = bizId, Name = "Standard", Rate = 1.2 });
        db.Category.Add(new Category { IdOne = Guid.NewGuid(), IdTwo = bizId, Name = "Cat", Description = "seed" });
        await db.SaveChangesAsync();
        return bizId;
    }

    private MySqlDbContext Db(IServiceScope scope, Guid tenantId)
    {
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(tenantId));
        db.CurrentUser = "catalogue-e2e";
        return db;
    }

    private async Task AddItemAsync(Guid tenantId, Guid bizId, string barcode, string name, decimal price)
    {
        using var scope = _f.Services.CreateScope();
        var db = Db(scope, tenantId);
        var catId = await db.Category.Where(c => c.IdTwo == bizId).Select(c => c.IdOne).FirstAsync();
        db.Items.Add(new Item
        {
            IdOne = barcode, IdTwo = bizId, Name = name, Brand = "-", Desc = "",
            Cost = 1m, ExPrice = price, Price = price, TaxId = 1, CatId = catId,
        });
        await db.SaveChangesAsync();
    }

    private async Task MutateItemAsync(Guid tenantId, string barcode, Action<Item> mutate)
    {
        using var scope = _f.Services.CreateScope();
        var db = Db(scope, tenantId);
        var item = await db.Items.FirstAsync(i => i.IdOne == barcode);
        mutate(item);
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage Get(string url, string token)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        r.Headers.Authorization = new("Bearer", token);
        return r;
    }

    private async Task<JsonElement> ChangesAsync(Till till, string? since = null, int? limit = null)
    {
        var url = "/api/v1/catalogue/changes"
            + (since is null ? "" : $"?since={Uri.EscapeDataString(since)}")
            + (limit is null ? "" : (since is null ? "?" : "&") + $"limit={limit}");
        var res = await till.Http.SendAsync(Get(url, till.DeviceToken));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    // ── the feed ──

    [Fact]
    public async Task A_device_token_reads_the_catalogue_and_ids_match_the_web_tills_derivation()
    {
        // ⚠ The id is DERIVED from the legacy BUSINESS id, not the tenant id. Getting that wrong
        // produces ids that look fine and are wrong everywhere — and nothing notices quickly,
        // because stock moves on the barcode.
        var till = await ProvisionAsync("cat-read@acme.test");
        await AddItemAsync(till.TenantId, till.BusinessId, "5012345678900", "Batman Year One", 14.99m);

        var body = await ChangesAsync(till);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        var one = Assert.Single(items, i => i.GetProperty("idOne").GetString() == "5012345678900");
        Assert.Equal(DeterministicGuid.ForItem(till.BusinessId, "5012345678900"), one.GetProperty("id").GetGuid());
        Assert.Equal("Batman Year One", one.GetProperty("name").GetString());
        Assert.Equal(1499, one.GetProperty("pricePence").GetInt64());
        Assert.False(one.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task A_binned_item_arrives_as_a_TOMBSTONE_not_as_an_absence()
    {
        // ⚠ THE POINT OF THE WHOLE FEED SHAPE. "Not in this page" and "withdrawn from sale" look
        // identical to a client that only ever receives upserts — so without an explicit removal
        // flag, a binned item stays sellable on every offline till indefinitely, and nothing
        // anywhere reports it.
        var till = await ProvisionAsync("cat-bin@acme.test");
        await AddItemAsync(till.TenantId, till.BusinessId, "5012345678901", "Withdrawn", 5m);

        var first = await ChangesAsync(till);
        var cursor = first.GetProperty("cursor").GetString();

        await MutateItemAsync(till.TenantId, "5012345678901", i => i.BinnedAtUtc = DateTime.UtcNow);

        var after = await ChangesAsync(till, cursor);
        var tombstone = Assert.Single(
            after.GetProperty("items").EnumerateArray().ToList(),
            i => i.GetProperty("idOne").GetString() == "5012345678901");
        Assert.True(tombstone.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task The_cursor_returns_only_what_changed_since()
    {
        var till = await ProvisionAsync("cat-cursor@acme.test");
        await AddItemAsync(till.TenantId, till.BusinessId, "5012345678910", "First", 1m);

        var first = await ChangesAsync(till);
        var cursor = first.GetProperty("cursor").GetString();
        Assert.NotNull(cursor);

        // Nothing has changed: the same cursor yields nothing, which is the common case and must
        // cost nothing.
        Assert.Empty((await ChangesAsync(till, cursor)).GetProperty("items").EnumerateArray());

        await AddItemAsync(till.TenantId, till.BusinessId, "5012345678911", "Second", 2m);

        var after = (await ChangesAsync(till, cursor)).GetProperty("items").EnumerateArray().ToList();
        Assert.Single(after);
        Assert.Equal("5012345678911", after[0].GetProperty("idOne").GetString());
    }

    [Fact]
    public async Task Paging_a_bulk_edit_loses_nothing_even_when_every_row_shares_a_timestamp()
    {
        // ⚠ THE REASON THE CURSOR IS (ModifiedAt, IdOne) AND NOT ModifiedAt ALONE. A bulk edit
        // stamps many rows in one SaveChanges, so they share a timestamp to the tick. Keyed on the
        // timestamp only, a page boundary landing inside that group either drops the remainder or
        // loops on it forever. Both are silent.
        var till = await ProvisionAsync("cat-page@acme.test");

        using (var scope = _f.Services.CreateScope())
        {
            var db = Db(scope, till.TenantId);
            var catId = await db.Category.Where(c => c.IdTwo == till.BusinessId).Select(c => c.IdOne).FirstAsync();
            for (var i = 0; i < 25; i++)
                db.Items.Add(new Item
                {
                    IdOne = $"600000000{i:D4}", IdTwo = till.BusinessId, Name = $"Bulk {i}", Brand = "-", Desc = "",
                    Cost = 1m, ExPrice = 1m, Price = 1m, TaxId = 1, CatId = catId,
                });
            await db.SaveChangesAsync(); // one stamp, 25 rows
        }

        // Page through in fours and prove every row arrives exactly once.
        var seen = new List<string>();
        string? cursor = null;
        for (var guard = 0; guard < 50; guard++)
        {
            var page = await ChangesAsync(till, cursor, limit: 4);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("idOne").GetString()!));
            cursor = page.GetProperty("cursor").GetString();
            if (!page.GetProperty("hasMore").GetBoolean()) break;
        }

        var bulk = seen.Where(s => s.StartsWith("600000000")).ToList();
        Assert.Equal(25, bulk.Count);
        Assert.Equal(25, bulk.Distinct().Count());
    }

    [Fact]
    public async Task One_tenants_till_never_sees_anothers_catalogue()
    {
        var a = await ProvisionAsync("cat-tenant-a@acme.test");
        var b = await ProvisionAsync("cat-tenant-b@acme.test");
        await AddItemAsync(a.TenantId, a.BusinessId, "7000000000001", "A only", 1m);
        await AddItemAsync(b.TenantId, b.BusinessId, "7000000000002", "B only", 1m);

        var seenByB = (await ChangesAsync(b)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("idOne").GetString()).ToList();

        Assert.Contains("7000000000002", seenByB);
        Assert.DoesNotContain("7000000000001", seenByB);
    }

    // ── the heartbeat ──

    [Fact]
    public async Task A_heartbeat_reports_presence_and_hands_back_the_catalogue_cursor()
    {
        var till = await ProvisionAsync("hb-basic@acme.test");
        await AddItemAsync(till.TenantId, till.BusinessId, "8000000000001", "Anything", 1m);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                deviceId = till.DeviceId,
                appVersion = "1.0.0",
                outboxDepth = 3,
                oldestUnsyncedAgeSeconds = 42L,
                deviceClockUtc = DateTime.UtcNow,
            }),
        };
        req.Headers.Authorization = new("Bearer", till.DeviceToken);

        var res = await till.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.False(body.GetProperty("syncNow").GetBoolean());
        Assert.False(body.GetProperty("locked").GetBoolean());
        // The cursor is what saves the common case a second call: same as ours, nothing to pull.
        Assert.False(string.IsNullOrEmpty(body.GetProperty("catalogueCursor").GetString()));
    }

    [Fact]
    public async Task SyncNow_is_delivered_exactly_once()
    {
        // ⚠ One operator click must not become a permanent load. If the flag were not cleared as it
        // is delivered, the till would resync on every beat — every 60 seconds, forever.
        var till = await ProvisionAsync("hb-syncnow@acme.test");

        using (var scope = _f.Services.CreateScope())
        {
            var db = Db(scope, till.TenantId);
            var device = await db.Devices.FirstAsync(d => d.Id == till.DeviceId);
            device.SyncNow = true;
            await db.SaveChangesAsync();
        }

        Assert.True((await BeatAsync(till)).GetProperty("syncNow").GetBoolean());
        Assert.False((await BeatAsync(till)).GetProperty("syncNow").GetBoolean());
    }

    [Fact]
    public async Task A_locked_device_says_so_and_says_why()
    {
        var till = await ProvisionAsync("hb-lock@acme.test");

        using (var scope = _f.Services.CreateScope())
        {
            var db = Db(scope, till.TenantId);
            var device = await db.Devices.FirstAsync(d => d.Id == till.DeviceId);
            device.Locked = true;
            device.LockReason = "Returned to head office";
            await db.SaveChangesAsync();
        }

        var body = await BeatAsync(till);
        Assert.True(body.GetProperty("locked").GetBoolean());
        // A locked till that cannot say why is a support call. Lock is not the same as broken.
        Assert.Equal("Returned to head office", body.GetProperty("lockReason").GetString());
    }

    [Fact]
    public async Task A_heartbeat_for_an_unknown_device_is_404_not_a_silent_success()
    {
        var till = await ProvisionAsync("hb-unknown@acme.test");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                deviceId = Guid.NewGuid(), outboxDepth = 0, deviceClockUtc = DateTime.UtcNow,
            }),
        };
        req.Headers.Authorization = new("Bearer", till.DeviceToken);

        Assert.Equal(HttpStatusCode.NotFound, (await till.Http.SendAsync(req)).StatusCode);
    }

    private async Task<JsonElement> BeatAsync(Till till)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                deviceId = till.DeviceId, appVersion = "1.0.0", outboxDepth = 0,
                oldestUnsyncedAgeSeconds = (long?)null, deviceClockUtc = DateTime.UtcNow,
            }),
        };
        req.Headers.Authorization = new("Bearer", till.DeviceToken);
        var res = await till.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }
}
