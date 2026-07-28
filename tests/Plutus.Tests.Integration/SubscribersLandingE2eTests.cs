using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// OP3 subscribers landing: the bulk-contracts + per-tenant-users read endpoints are platform-admin
/// gated; the users endpoint reports the tenant's last portal-login day from WP13.1 metering.
/// </summary>
public class SubscribersLandingE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SubscribersLandingE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private async Task<HttpResponseMessage> Get(string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        return await _f.CreateClient().SendAsync(req);
    }

    [Fact]
    public async Task Contracts_and_users_are_platform_admin_gated()
    {
        var staff = PlutusAppFactory.OperatorToken("pos.sell");
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/v1/platform/contracts", staff)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get($"/api/v1/platform/tenants/{Kapow}/users", staff)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/v1/platform/contracts", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get($"/api/v1/platform/tenants/{Kapow}/users", admin)).StatusCode);
    }

    [Fact]
    public async Task Users_endpoint_reports_last_portal_login_day()
    {
        var day = new DateOnly(2026, 6, 15);
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>(); // Kapow-scoped
            db.CurrentUser = "op3-seed";
            db.TenantUsageRollups.Add(new TenantUsageRollup
            {
                TenantId = Kapow, BusinessDay = day, Metric = UsageMetrics.LoginsPortal, Value = 3,
            });
            await db.SaveChangesAsync();
        }

        var resp = await Get($"/api/v1/platform/tenants/{Kapow}/users", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("2026-06-15", await resp.Content.ReadAsStringAsync()); // lastPortalActivityDay
    }
}
