using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// A counted drawer that does not balance is SAID SO — on the response the till reads, and on the
/// portal pill a manager sees without going to look for it.
///
/// ⚠ Matt, 2026-08-11: *"If the Zclose is a different number than expected e.g. opened with £150,
/// spent £20 and close with £110. This should be flagged. There should also be a report and or
/// warning on the portal that shows that till closed with the incorrect amount of money."*
///
/// ⚠ THE ARITHMETIC WAS NEVER THE MISSING PART. The platform has computed expected-vs-counted since
/// WP9 and the banking report has shown it in red since §9.2. Two things were missing, and both are
/// about who gets TOLD:
///   • the till discarded the response body and kept only the status code, so the operator standing
///     at the drawer — the one person who could recount it there and then — learned nothing;
///   • the portal's figure lived on a tab, so a manager who never opened it never found out.
///
/// ⚠ THAT IS WHY THIS TEST ASSERTS THE RESPONSE BODY. `CashPushService` now depends on
/// `expectedPence` and `variancePence` arriving on the POST; nothing else pins that contract, and a
/// projection that quietly stopped sending them would put the till back where it started with every
/// suite still green.
/// </summary>
public class DrawerVarianceE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public DrawerVarianceE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class Credentials : IDeviceCredentialStore
    {
        public Guid? DeviceId { get; private set; }
        public string ClientSecret { get; private set; }
        public void Save(Guid deviceId, string clientSecret) { DeviceId = deviceId; ClientSecret = clientSecret; }
        public void Clear() { DeviceId = null; ClientSecret = null; }
    }

    private static async Task<JsonElement> PostAsync(HttpClient c, string url, string token, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var res = await c.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var res = await c.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<(PlutusApiClient Api, Guid DeviceId, Guid TenantId)> EnrolAsync(
        HttpClient http, string email)
    {
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        var tenant = await PostAsync(http, "/api/v1/tenants", admin,
            new { name = "Drawer " + email, plan = "standard", adminEmail = email, adminPassword = "S3cret!" });

        var tenantId = tenant.GetProperty("tenantId").GetGuid();
        var storeId = tenant.GetProperty("storeId").GetInt32();

        var portal = PlutusAppFactory.OperatorToken(PlutusPolicies.PortalTillsEnrol, tenantId);
        var till = await PostAsync(http, "/api/v1/tills", portal, new { storeId, name = "Drawer till" });

        var bootstrap = new PlutusApiClient(http);
        var enrolled = await bootstrap.EnrolAsync(till.GetProperty("enrolmentCode").GetString()!);
        var creds = new Credentials();
        creds.Save(enrolled.DeviceId, enrolled.ClientSecret);

        return (new PlutusApiClient(http, new DeviceTokenProvider(bootstrap, creds)), enrolled.DeviceId, tenantId);
    }

    /// <summary>
    /// A token that reads the dashboard AS A SHOPKEEPER — an Owner inside one tenant.
    ///
    /// ⚠⚠ THE OBVIOUS SHORTCUT IS WRONG, AND IT MADE TWO OF THESE TESTS FAIL BEFORE THEY WERE
    /// RIGHT. `OperatorToken(PlutusPolicies.PlatformAdmin, tenantId)` satisfies every `perm:*`
    /// policy, so it looks like the cheap way to reach any endpoint — but platform-admin resolves
    /// to a `Guid.Empty` tenant context, and the global filter reads
    /// `CurrentTenantId == Guid.Empty || TenantId == CurrentTenantId`. The `Guid.Empty` branch is a
    /// deliberate CROSS-TENANT BYPASS for operator tooling. Counting rows through it counts every
    /// tenant's rows — including those left behind by whichever tests ran first.
    ///
    /// ⚠ The existing `DashboardKpiE2eTests` uses that token and is not wrong to: it asserts the
    /// KPI *shape*, never a figure. The moment a test asserts a COUNT, the bypass stops being
    /// convenient and starts being the thing under test.
    /// </summary>
    private async Task<string> OwnerTokenAsync(Guid tenantId)
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();

        // ⚠ AN UNSCOPED CONTEXT, because the DI one falls back to Kapow and `StampAndGuardTenant`
        // then blocks writing a role for the tenant this test just created — "Cross-tenant write
        // blocked: RbacRole.TenantId … != context …". Runbook pitfall 3.
        var db = new Plutus.Entities.MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<Plutus.Entities.MySqlDbContext>>(),
            new Plutus.Entities.Tenancy.FixedTenantContext(Guid.Empty));

        db.CurrentUser = "drawer-variance-e2e-seed";
        await Plutus.Identity.RbacSeeder.EnsureBuiltInRolesAsync(db, tenantId);

        var owner = await db.RbacRoles.FirstAsync(r => r.Name == "Owner" && r.TenantId == tenantId);
        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = tenantId, UserId = userId, RoleId = owner.Id,
            ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return PlutusAppFactory.OperatorTokenFor(userId, "pos.sell", tenantId);
    }

    private static CashEventRequest Event(Guid deviceId, string type, DateOnly day,
        long amountPence = 0, long? countedPence = null, string reason = null) => new()
    {
        EventId = Uuid7.New(),
        DeviceId = deviceId,
        Type = type,
        BusinessDay = day,
        OccurredAtUtc = DateTime.UtcNow,
        AmountPence = amountPence,
        CountedPence = countedPence,
        Reason = reason,
    };

    /// <summary>
    /// Matt's example, end to end: open with £150, pay out £20, count £110 — £20 short.
    ///
    /// ⚠ THE FIGURES ARE ON THE RESPONSE, which is the whole point. This is the only moment the
    /// till can learn them: it has no way to compute the expected drawer itself (that needs the
    /// sales half, including sales another device on the same till posted) and the line may be down
    /// by the time anyone asks again.
    /// </summary>
    [Fact]
    public async Task A_drawer_counted_short_comes_back_saying_by_how_much()
    {
        var http = _f.CreateClient();
        var (api, deviceId, tenantId) = await EnrolAsync(http, $"drawer-short-{Guid.NewGuid():N}@kapow.test");
        var day = DateOnly.FromDateTime(DateTime.Now);

        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.OpenFloat, day, amountPence: 15000));
        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.PaidOut, day, amountPence: 2000, reason: "milk"));

        var (status, body) = await api.PostCashEventAsync(
            Event(deviceId, CashEventTypes.ZClose, day, countedPence: 11000));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.NotNull(body);

        // £150 float − £20 paid out + £0 cash takings = £130 expected. £110 counted is £20 short.
        Assert.Equal(13000, body.ExpectedPence);
        Assert.Equal(-2000, body.VariancePence);

        // ⚠ NEGATIVE IS SHORT. Inverting the sign would tell an operator their drawer is £20 OVER
        // while £20 is genuinely missing — the one reading that stops anybody looking for it.
        Assert.True(body.VariancePence < 0);

        // And the portal announces it, on the pill row a manager already looks at.
        var reports = await OwnerTokenAsync(tenantId);
        var kpis = await GetAsync(http, "/api/v1/reports/dashboard", reports);

        Assert.Equal(1, kpis.GetProperty("drawersOutOfBalance").GetInt32());
        Assert.Equal(2000, kpis.GetProperty("drawersShortPence").GetInt64());
        Assert.Equal(0, kpis.GetProperty("drawersOverPence").GetInt64());
    }

    /// <summary>
    /// ⚠ A DRAWER THAT BALANCES RAISES NOTHING. A warning that appears on a correct close is a
    /// warning that gets ignored on a wrong one — and this pill's whole value is that it is rare.
    ///
    /// ⚠ It still returns a variance of ZERO rather than null, because the till distinguishes the
    /// two: 0 shows "✅ balances", null shows nothing at all because nobody has judged it yet.
    /// </summary>
    [Fact]
    public async Task A_drawer_that_balances_raises_nothing_on_the_portal()
    {
        var http = _f.CreateClient();
        var (api, deviceId, tenantId) = await EnrolAsync(http, $"drawer-ok-{Guid.NewGuid():N}@kapow.test");
        var day = DateOnly.FromDateTime(DateTime.Now);

        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.OpenFloat, day, amountPence: 15000));

        var (_, body) = await api.PostCashEventAsync(
            Event(deviceId, CashEventTypes.ZClose, day, countedPence: 15000));

        Assert.Equal(0, body!.VariancePence);
        Assert.NotNull(body.VariancePence);

        var reports = await OwnerTokenAsync(tenantId);
        var kpis = await GetAsync(http, "/api/v1/reports/dashboard", reports);

        Assert.Equal(0, kpis.GetProperty("drawersOutOfBalance").GetInt32());
        Assert.Equal(0, kpis.GetProperty("drawersShortPence").GetInt64());
    }

    /// <summary>
    /// ⚠⚠ A SHORTAGE AND AN OVERAGE MUST NOT CANCEL, and this is the case worth having a test for.
    ///
    /// One till £20 short and another £20 over is not a quiet week — it is money in the wrong
    /// drawer, or a sale rung up on the wrong till, and it is exactly what a single netted figure
    /// would report as zero. Summing the two directions SEPARATELY is what stops the loudest signal
    /// on this pill row from being the one arrangement that silences it.
    /// </summary>
    [Fact]
    public async Task A_shortage_and_an_overage_do_not_cancel_each_other_out()
    {
        var http = _f.CreateClient();
        var (api, deviceId, tenantId) = await EnrolAsync(http, $"drawer-both-{Guid.NewGuid():N}@kapow.test");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var yesterday = today.AddDays(-1);

        // ⚠ Two DAYS on one till rather than two tills, because a business day takes exactly one Z.
        // Both fall inside the seven-day window the pill reports on.
        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.OpenFloat, yesterday, amountPence: 15000));
        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.ZClose, yesterday, countedPence: 13000));

        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.OpenFloat, today, amountPence: 15000));
        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.ZClose, today, countedPence: 17000));

        var reports = await OwnerTokenAsync(tenantId);
        var kpis = await GetAsync(http, "/api/v1/reports/dashboard", reports);

        Assert.Equal(2, kpis.GetProperty("drawersOutOfBalance").GetInt32());
        Assert.Equal(2000, kpis.GetProperty("drawersShortPence").GetInt64());
        Assert.Equal(2000, kpis.GetProperty("drawersOverPence").GetInt64());
    }

    /// <summary>
    /// ⚠ ANOTHER TENANT'S BAD DRAWER IS NOT THIS ONE'S PROBLEM. The pill reads through the global
    /// tenant filter like everything else, and a shop being told another shop's till is £20 down
    /// would be a leak as well as a nuisance.
    /// </summary>
    [Fact]
    public async Task One_tenants_short_drawer_is_invisible_to_another()
    {
        var http = _f.CreateClient();
        var (api, deviceId, _) = await EnrolAsync(http, $"drawer-a-{Guid.NewGuid():N}@kapow.test");
        var (_, _, otherTenantId) = await EnrolAsync(http, $"drawer-b-{Guid.NewGuid():N}@kapow.test");
        var day = DateOnly.FromDateTime(DateTime.Now);

        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.OpenFloat, day, amountPence: 15000));
        await api.PostCashEventAsync(Event(deviceId, CashEventTypes.ZClose, day, countedPence: 11000));

        var reports = await OwnerTokenAsync(otherTenantId);
        var kpis = await GetAsync(http, "/api/v1/reports/dashboard", reports);

        Assert.Equal(0, kpis.GetProperty("drawersOutOfBalance").GetInt32());
    }
}
