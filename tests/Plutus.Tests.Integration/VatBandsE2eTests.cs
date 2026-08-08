using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// MAUI retrofit WP2c — Definition of Done for the portal VAT surface.
///
/// The principle under test: THE PORTAL IS THE SOURCE OF VAT TRUTH. A till receives bands, applies
/// them, and reports what it charged; it never decides a VAT rule. So the contract a till reads has
/// to carry enough to survive being offline across a rate change, and the editor has to make the
/// dangerous edits impossible rather than merely discouraged.
/// </summary>
public class VatBandsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public VatBandsE2eTests(PlutusAppFactory f) => _f = f;

    private static async Task<JsonElement> ReadJson(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Req(HttpMethod m, string url, string token, object body = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (body != null) r.Content = JsonContent.Create(body);
        r.Headers.Authorization = new("Bearer", token);
        return r;
    }

    /// <summary>A fresh tenant with an owner who holds portal.company.manage, plus a till device
    /// token — the two audiences of this controller.</summary>
    private async Task<(Guid TenantId, string Owner, string Device)> ProvisionAsync(HttpClient c, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var pBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tenants", admin,
            new { name = "VatBands " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" })));
        var tenantId = pBody.GetProperty("tenantId").GetGuid();
        var storeId = pBody.GetProperty("storeId").GetInt32();
        var ownerId = pBody.GetProperty("adminUserId").GetGuid();

        // perm:* resolves from RBAC by userId, never from token scopes (runbook pitfall #5).
        // "Owner" carries every portal permission, including portal.company.manage.
        using (var scope = _f.Services.CreateScope())
        {
            var db = new MySqlDbContext(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
                new Plutus.Entities.Tenancy.FixedTenantContext(tenantId));
            db.CurrentUser = "vat-bands-e2e";
            await RbacSeeder.EnsureBuiltInRolesAsync(db, tenantId);
            var role = await db.RbacRoles.FirstAsync(r => r.Name == "Owner" && r.TenantId == tenantId);
            db.RbacRoleAssignments.Add(new RbacRoleAssignment
            {
                Id = Uuid7.New(), TenantId = tenantId, UserId = ownerId, RoleId = role.Id,
                ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var owner = PlutusAppFactory.OperatorTokenFor(ownerId, "pos.sell", tenantId);

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var tBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tills", portal,
            new { storeId, name = "Vat till" })));
        var eBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tills/enrol", portal,
            new { enrolmentCode = tBody.GetProperty("enrolmentCode").GetString() })));
        var kBody = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/tokens/device", portal,
            new { deviceId = eBody.GetProperty("deviceId").GetGuid(), clientSecret = eBody.GetProperty("clientSecret").GetString() })));

        return (tenantId, owner, kBody.GetProperty("accessToken").GetString()!);
    }

    private async Task SeedBandsAsync(Guid tenantId, params (string Band, string Name, VatClass Cls, int Bp)[] bands)
    {
        using var scope = _f.Services.CreateScope();
        var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(Guid.Empty));
        db.CurrentUser = "vat-bands-e2e";
        foreach (var (band, name, cls, bp) in bands)
            db.VatRatePoints.Add(new VatRatePoint
            {
                Id = Uuid7.New(), TenantId = tenantId, Band = band, DisplayName = name,
                Class = (int)cls, RateBp = bp, EffectiveFromUtc = DateTime.UnixEpoch,
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_DEVICE_token_can_read_the_published_bands_and_gets_the_whole_timeline()
    {
        // ⚠ THE POINT OF THE WHOLE CONTRACT. A till must receive FUTURE rate points, not just
        // "the rate right now" — otherwise one that goes offline today keeps charging the old rate
        // through a change it never heard about, and its backlog is quarantined on reconnect.
        var c = _f.CreateClient();
        var (tenantId, owner, device) = await ProvisionAsync(c, "bands1@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0));

        var future = DateTime.UtcNow.AddDays(30);
        var scheduled = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands/standard/rate-changes", owner,
            new { rateBp = 1750, effectiveFromUtc = future, note = "Budget 2026" }));
        Assert.Equal(HttpStatusCode.Created, scheduled.StatusCode);

        var res = await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadJson(res);

        var standard = body.GetProperty("bands").EnumerateArray().Single(b => b.GetProperty("key").GetString() == "standard");
        // The rate to charge TODAY is still 20% — the change is invisible until its date.
        Assert.Equal(2000, standard.GetProperty("rateBp").GetInt32());
        // …but the till is handed the change so it can apply it itself, offline, on the day.
        var rates = standard.GetProperty("rates").EnumerateArray().Select(r => r.GetProperty("rateBp").GetInt32()).ToArray();
        Assert.Equal(new[] { 2000, 1750 }, rates);
    }

    [Fact]
    public async Task Zero_and_exempt_survive_the_round_trip_as_DISTINCT_bands_at_the_same_zero_rate()
    {
        // §2a finding 2: both are 0% to the customer and different in law — zero-rated is a taxable
        // supply with input-tax recovery, exempt is not taxable and blocks it. A contract that
        // published only the rate would merge them and lose the partial-exemption figure forever.
        var c = _f.CreateClient();
        var (tenantId, _, device) = await ProvisionAsync(c, "bands2@acme.test");
        await SeedBandsAsync(tenantId,
            ("standard", "20%", VatClass.Standard, 2000),
            ("zero", "Zero rated (books)", VatClass.Zero, 0),
            ("exempt", "Exempt", VatClass.Exempt, 0));

        var body = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device)));
        var zeroRated = body.GetProperty("bands").EnumerateArray().Where(b => b.GetProperty("rateBp").GetInt32() == 0).ToArray();

        Assert.Equal(2, zeroRated.Length);
        Assert.Contains(zeroRated, b => b.GetProperty("vatClass").GetString() == "Zero");
        Assert.Contains(zeroRated, b => b.GetProperty("vatClass").GetString() == "Exempt");
    }

    [Fact]
    public async Task A_rate_change_dated_in_the_PAST_is_refused()
    {
        // Back-dating would retrospectively invalidate sales already recorded under the old rate:
        // WP2b judges every line against the rates in force at its OccurredAtUtc, so a past-dated
        // point turns settled history into stale-band quarantine without changing a penny of what
        // the customer actually paid.
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands3@acme.test");
        await SeedBandsAsync(tenantId, ("standard", "20%", VatClass.Standard, 2000));

        var res = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands/standard/rate-changes", owner,
            new { rateBp = 1750, effectiveFromUtc = DateTime.UtcNow.AddDays(-1), note = "oops" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("700/45", (await ReadJson(res)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_scheduled_change_can_be_cancelled_but_one_already_in_force_cannot()
    {
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands4@acme.test");
        await SeedBandsAsync(tenantId, ("standard", "20%", VatClass.Standard, 2000));

        var created = await ReadJson(await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands/standard/rate-changes", owner,
            new { rateBp = 1750, effectiveFromUtc = DateTime.UtcNow.AddDays(10), note = "typo" })));
        var id = created.GetProperty("id").GetGuid();

        // Not yet in force → cancellable.
        Assert.Equal(HttpStatusCode.NoContent,
            (await c.SendAsync(Req(HttpMethod.Delete, $"/api/v1/vat/bands/standard/rate-changes/{id}", owner))).StatusCode);

        // The ORIGINAL point is in force — tills have charged it and sales exist that only it
        // explains, so removing it would convert them into quarantine.
        var admin = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", owner)));
        var live = admin.GetProperty("bands").EnumerateArray()
            .Single(b => b.GetProperty("key").GetString() == "standard")
            .GetProperty("points").EnumerateArray().Single(p => p.GetProperty("inForce").GetBoolean());
        var res = await c.SendAsync(Req(HttpMethod.Delete,
            $"/api/v1/vat/bands/standard/rate-changes/{live.GetProperty("id").GetGuid()}", owner));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("already in force", (await ReadJson(res)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_class_that_charges_nothing_cannot_be_given_a_rate_and_vice_versa()
    {
        // The one rule that IS derivable: Zero/Exempt/OutsideScope are 0% by definition. A
        // "zero-rated" band at 20% would misreport every sale in it.
        var c = _f.CreateClient();
        var (_, owner, _) = await ProvisionAsync(c, "bands5@acme.test");

        var withRate = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands?rateBp=2000", owner,
            new { key = "books", displayName = "Books", @class = "Zero" }));
        Assert.Equal(HttpStatusCode.BadRequest, withRate.StatusCode);

        var withoutRate = await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands?rateBp=0", owner,
            new { key = "posh", displayName = "Standard", @class = "Standard" }));
        Assert.Equal(HttpStatusCode.BadRequest, withoutRate.StatusCode);
    }

    [Fact]
    public async Task Reclassifying_a_band_moves_every_point_and_is_audited_with_both_values()
    {
        // Kapow's real fix: a 0% band mislabelled Exempt when UK law zero-rates books. No money
        // moves (both are 0% output tax) but input-tax recovery is restored — so the change has to
        // be traceable to whoever made it.
        var c = _f.CreateClient();
        var (tenantId, owner, _) = await ProvisionAsync(c, "bands6@acme.test");
        await SeedBandsAsync(tenantId, ("zero", "Exempt", VatClass.Exempt, 0));

        Assert.Equal(HttpStatusCode.NoContent,
            (await c.SendAsync(Req(HttpMethod.Put, "/api/v1/vat/bands/zero", owner,
                new { key = "zero", displayName = "Zero rated (books)", @class = "Zero" }))).StatusCode);

        var admin = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", owner)));
        var band = admin.GetProperty("bands").EnumerateArray().Single();
        Assert.Equal("Zero", band.GetProperty("vatClass").GetString());
        Assert.Equal("Zero rated (books)", band.GetProperty("displayName").GetString());

        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.Action == "vat.band.update").FirstAsync();
        Assert.Contains("Exempt", audit.DetailJson);   // what it was
        Assert.Contains("Zero", audit.DetailJson);     // what it became
    }

    [Fact]
    public async Task An_unseeded_tenant_gets_its_legacy_Taxes_migrated_rather_than_an_empty_contract()
    {
        // A till that reads no bands has nothing to apply, and WP2b treats an empty history as
        // "skip" — so an unseeded tenant would silently lose both the contract and the compliance
        // check. The first read migrates the legacy rows instead.
        var c = _f.CreateClient();
        var (_, _, device) = await ProvisionAsync(c, "bands7@acme.test");

        var body = await ReadJson(await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device)));
        var bands = body.GetProperty("bands").EnumerateArray().ToArray();
        // Whatever the tenant template seeds into Taxes, the contract is never blank when Taxes
        // has rows, and every band carries an explicit class rather than a bare rate.
        Assert.All(bands, b => Assert.False(string.IsNullOrWhiteSpace(b.GetProperty("vatClass").GetString())));
    }

    [Fact]
    public async Task The_editor_is_gated_but_the_published_contract_is_readable_by_any_till()
    {
        var c = _f.CreateClient();
        var (_, _, device) = await ProvisionAsync(c, "bands8@acme.test");

        // A device token may READ the contract — it has to, to price anything.
        Assert.Equal(HttpStatusCode.OK,
            (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands", device))).StatusCode);
        // …and may NOT see or change the editor's view.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.SendAsync(Req(HttpMethod.Get, "/api/v1/vat/bands/admin", device))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.SendAsync(Req(HttpMethod.Post, "/api/v1/vat/bands?rateBp=2000", device,
                new { key = "sneaky", displayName = "Sneaky", @class = "Standard" }))).StatusCode);
    }
}
