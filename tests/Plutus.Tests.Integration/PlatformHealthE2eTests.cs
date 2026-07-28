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
/// WP13.2 gate: the operator health endpoints read across tenants and are platform-admin only.
/// A non-platform token is 403'd; a platform-admin token gets 200 and sees the seeded per-tenant
/// stats + quarantine depth.
/// </summary>
public class PlatformHealthE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public PlatformHealthE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    [Fact]
    public async Task Health_is_platform_admin_only_and_surfaces_stats_and_quarantine()
    {
        var minute = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day,
            DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0, DateTimeKind.Utc).AddMinutes(-5);
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "health-e2e-seed";
            db.TenantRequestStats.Add(new TenantRequestStats
            {
                TenantId = Kapow, MinuteUtc = minute, RouteGroup = "sales",
                Count = 50, Err4xx = 2, Err5xx = 7, P50Ms = 12, P95Ms = 240, MaxMs = 900,
            });
            db.SaleQuarantine.Add(new SaleQuarantine
            {
                Id = Uuid7.New(), TenantId = Kapow, SaleId = Uuid7.New(),
                PayloadJson = "{}", Reason = "test", ReceivedAtUtc = DateTime.UtcNow, ResolvedAtUtc = null,
            });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();

        // non-platform operator → 403 on both routes
        var operatorTok = PlutusAppFactory.OperatorToken("pos.sell");
        foreach (var route in new[] { "/api/v1/platform/health", $"/api/v1/platform/health/{Kapow}" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, route);
            req.Headers.Authorization = new("Bearer", operatorTok);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // platform-admin → 200 and sees the seeded stats + open quarantine
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/health"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"err5xx\":7", body);
            Assert.Contains("\"peakP95Ms\":240", body);
            Assert.Contains("\"quarantineOpen\":1", body);
            Assert.Contains("\"consumerLag\"", body);
        }
    }
}
