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
using Plutus.Identity;
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

    /// <summary>
    /// A token that passes the <c>perm:portal.prices.manage</c> gate.
    ///
    /// ⚠ Runbook pitfall #5: <c>perm:*</c> policies resolve from RBAC BY USER ID, not from token
    /// scopes — so a scope-only token 403s however it is spelled. Seed a role, assign it, then mint
    /// for that user.
    /// </summary>
    private async Task<string> PriceManagerTokenAsync(Till till)
    {
        using var scope = _f.Services.CreateScope();
        var db = Db(scope, till.TenantId);
        await RbacSeeder.EnsureBuiltInRolesAsync(db, till.TenantId);

        var owner = await db.RbacRoles.FirstAsync(r => r.Name == "Owner" && r.TenantId == till.TenantId);
        var userId = Uuid7.New();
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = till.TenantId, UserId = userId, RoleId = owner.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        // ⚠ The tenant MUST be on the token. Without it the principal falls back to the ambient
        // tenant and the assignment just made in THIS tenant is invisible — a 403 that looks like a
        // missing permission rather than a missing tenant.
        return PlutusAppFactory.OperatorTokenFor(userId, "pos.sell", till.TenantId);
    }

    // ── prices (WP5's outstanding DoD) ──

    [Fact]
    public async Task A_PRICE_change_reaches_the_till_even_though_prices_do_not_live_on_the_item()
    {
        // ⚠ THE DoD THAT WAS UNMET, and the reason it was: the feed pages by Item.ModifiedAt, and
        // prices are effective-dated rows in their OWN tables. Writing one changed nothing the feed
        // could see, so a till went on charging the baseline for ever with nothing reporting it.
        // PricingService.TouchItemForSyncAsync is what closes that, and this is what proves it.
        var till = await ProvisionAsync("cat-price@acme.test");
        await AddItemAsync(till.TenantId, till.BusinessId, "5012345679000", "Repriced", 10m);

        var cursor = (await ChangesAsync(till)).GetProperty("cursor").GetString();

        var portal = await PriceManagerTokenAsync(till);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/prices/5012345679000/central")
        { Content = JsonContent.Create(new { pricePence = 1699L, exPricePence = 1416L, effectiveFromUtc = (DateTime?)null }) };
        req.Headers.Authorization = new("Bearer", portal);
        Assert.Equal(HttpStatusCode.Created, (await till.Http.SendAsync(req)).StatusCode);

        var after = (await ChangesAsync(till, cursor)).GetProperty("items").EnumerateArray().ToList();

        var item = Assert.Single(after, i => i.GetProperty("idOne").GetString() == "5012345679000");
        var central = item.GetProperty("centralPrices").EnumerateArray().ToList();
        Assert.Single(central);
        Assert.Equal(1699, central[0].GetProperty("pricePence").GetInt64());
    }

    [Fact]
    public async Task A_FUTURE_dated_price_is_shipped_NOW_so_an_offline_till_can_apply_it_on_the_day()
    {
        // ⚠ The point of shipping the timeline rather than the current number. HQ schedules Sunday's
        // reprice on Thursday; the till syncs Thursday and may never be online again before it.
        var till = await ProvisionAsync("cat-future@acme.test");
        await AddItemAsync(till.TenantId, till.BusinessId, "5012345679001", "Future priced", 10m);

        var future = DateTime.UtcNow.AddDays(3);
        var portal = await PriceManagerTokenAsync(till);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/prices/5012345679001/central")
        { Content = JsonContent.Create(new { pricePence = 2500L, exPricePence = 2083L, effectiveFromUtc = future }) };
        req.Headers.Authorization = new("Bearer", portal);
        Assert.Equal(HttpStatusCode.Created, (await till.Http.SendAsync(req)).StatusCode);

        var item = Assert.Single(
            (await ChangesAsync(till)).GetProperty("items").EnumerateArray().ToList(),
            i => i.GetProperty("idOne").GetString() == "5012345679001");

        var point = Assert.Single(item.GetProperty("centralPrices").EnumerateArray().ToList());
        Assert.Equal(2500, point.GetProperty("pricePence").GetInt64());
        // Shipped, but dated ahead — the till holds it and applies it at the boundary.
        Assert.True(point.GetProperty("effectiveFromUtc").GetDateTime() > DateTime.UtcNow);
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

    /// <summary>
    /// ⚠ THE VERSION MUST SURVIVE THE REQUEST, and for two days it did not.
    ///
    /// `HeartbeatController` assigned `device.AppVersion` on the tracked entity and then called
    /// `SaveChangesAsync` ONLY inside its `if (syncNow)` branch — so on every ordinary beat the
    /// mutation was tracked and thrown away with the DbContext. Six real devices beat for two days
    /// and every one still read `AppVersion NULL`, while their SALES arrived perfectly. The till was
    /// never at fault; the write was missing.
    ///
    /// The old test asserted the RESPONSE and never looked at the database, which is exactly how a
    /// missing write hides: everything the caller can see is correct.
    /// </summary>
    [Fact]
    public async Task A_heartbeat_PERSISTS_the_reported_app_version()
    {
        var till = await ProvisionAsync("hb-version@acme.test");

        await BeatAsync(till, appVersion: "1.20.0+abc1234");

        using (var scope = _f.Services.CreateScope())
        {
            var device = await Db(scope, till.TenantId).Devices.FirstAsync(d => d.Id == till.DeviceId);
            Assert.Equal("1.20.0+abc1234", device.AppVersion);
            Assert.NotNull(device.AppVersionReportedAtUtc);
        }
    }

    /// <summary>
    /// ⚠ An UPGRADED till must be seen to upgrade — the fleet list's whole purpose is answering
    /// "which build is this exactly", and a version written once and never updated is worse than
    /// none, because it reads as current.
    /// </summary>
    [Fact]
    public async Task A_heartbeat_UPDATES_the_version_when_the_till_is_upgraded()
    {
        var till = await ProvisionAsync("hb-upgrade@acme.test");

        await BeatAsync(till, appVersion: "1.19.0+aaaaaaa");
        await BeatAsync(till, appVersion: "1.20.0+bbbbbbb");

        using (var scope = _f.Services.CreateScope())
        {
            var device = await Db(scope, till.TenantId).Devices.FirstAsync(d => d.Id == till.DeviceId);
            Assert.Equal("1.20.0+bbbbbbb", device.AppVersion);
        }
    }

    /// <summary>
    /// ⚠ A beat carrying NO version must not blank the one already recorded. The fleet list is asked
    /// about switched-off tills most of all, and an older or cut-down client that omits the field
    /// must not erase what a working one reported.
    /// </summary>
    [Fact]
    public async Task A_heartbeat_without_a_version_does_not_erase_the_one_on_record()
    {
        var till = await ProvisionAsync("hb-noversion@acme.test");

        await BeatAsync(till, appVersion: "1.20.0+ccccccc");
        await BeatAsync(till, appVersion: null);

        using (var scope = _f.Services.CreateScope())
        {
            var device = await Db(scope, till.TenantId).Devices.FirstAsync(d => d.Id == till.DeviceId);
            Assert.Equal("1.20.0+ccccccc", device.AppVersion);
        }
    }

    private async Task BeatAsync(Till till, string? appVersion)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                deviceId = till.DeviceId,
                appVersion,
                outboxDepth = 0,
                oldestUnsyncedAgeSeconds = (long?)null,
                deviceClockUtc = DateTime.UtcNow,
            }),
        };
        req.Headers.Authorization = new("Bearer", till.DeviceToken);

        var res = await till.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
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

    // ── the till version the portal shows (2026-08-09, Matt's request) ──────────────────────

    /// <summary>
    /// ⚠ THE VERSION HAS TO SURVIVE THE ROUND TRIP TO BE WORTH ANYTHING. Every till has sent its
    /// own build on the 60s heartbeat since WP5 and nothing ever surfaced it, so "is that till on
    /// the new build?" could only be answered by walking to it — the question asked after every
    /// single deploy.
    ///
    /// ⚠ Presence is IN-MEMORY by design (a write per till per minute for data that expires in
    /// five would be the busiest and least useful write path in the system), so this also pins
    /// that a till which has NOT beaten reads as unknown rather than as an old version.
    /// </summary>
    [Fact]
    public async Task The_tills_running_version_reaches_the_portal_after_a_heartbeat()
    {
        var till = await ProvisionAsync("tillversion@acme.test");

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, till.TenantId);

        // Before any heartbeat: known device, unknown version — never a stale one.
        var before = await TillsAsync(till, portal);
        Assert.True(before.TryGetProperty("appVersion", out var none));
        Assert.Equal(JsonValueKind.Null, none.ValueKind);
        Assert.Equal("Offline", before.GetProperty("presence").GetString());

        await BeatAsync(till);

        var after = await TillsAsync(till, portal);
        Assert.Equal("1.0.0", after.GetProperty("appVersion").GetString());
        Assert.Equal("Online", after.GetProperty("presence").GetString());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("lastSeenUtc").ValueKind);
    }

    /// <summary>The device row for this till, as the portal's fleet list renders it.</summary>
    private async Task<JsonElement> TillsAsync(Till till, string portalToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tills");
        req.Headers.Authorization = new("Bearer", portalToken);
        var res = await till.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var tills = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        foreach (var t in tills.EnumerateArray())
            foreach (var d in t.GetProperty("devices").EnumerateArray())
                if (d.GetProperty("id").GetGuid() == till.DeviceId)
                    return d;

        throw new Xunit.Sdk.XunitException("the enrolled device is not in the portal's till list");
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
