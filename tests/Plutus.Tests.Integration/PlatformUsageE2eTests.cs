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
/// WP13.1 gate: the operator usage endpoints read ACROSS tenants and are platform-admin only.
/// A non-platform token is 403'd; a platform-admin token gets 200 and sees the seeded cell. This
/// is the isolation-suite exemption done by policy check (never by skipping the endpoint).
/// </summary>
public class PlatformUsageE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public PlatformUsageE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    [Fact]
    public async Task Usage_is_platform_admin_only_and_returns_seeded_cells()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "usage-e2e-seed";
            db.TenantUsageRollups.Add(new TenantUsageRollup
            { TenantId = Kapow, BusinessDay = day, Metric = UsageMetrics.SalesCount, Value = 7 });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();

        // an authenticated non-platform operator → 403 on both usage routes
        var operatorTok = PlutusAppFactory.OperatorToken("pos.sell");
        foreach (var route in new[] { "/api/v1/platform/usage", "/api/v1/platform/usage/summary" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, route);
            req.Headers.Authorization = new("Bearer", operatorTok);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // platform-admin → 200 and sees the seeded cell (cross-tenant read)
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/platform/usage?metric={UsageMetrics.SalesCount}"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("\"value\":7", await resp.Content.ReadAsStringAsync());
        }
    }
}
