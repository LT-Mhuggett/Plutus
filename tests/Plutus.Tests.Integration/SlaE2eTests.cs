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
/// WP15.2 advisory SLA: platform-admin only; monthly availability = minutes whose 5xx-rate is
/// under 1% ÷ minutes with traffic, from TenantRequestStats. Seeded 9 clean minutes + 1 bad one
/// (5% 5xx) in a fixed month → 90.000% availability.
/// </summary>
public class SlaE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SlaE2eTests(PlutusAppFactory f) => _f = f;

    [Fact]
    public async Task Sla_is_platform_admin_only_and_computes_monthly_availability()
    {
        // Seed against Kapow (the context's fallback tenant) so StampAndGuardTenant permits the
        // write; the fixed June-2026 window isolates these rows from other tests' "now" seeds.
        var tenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;
        var month = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            db.CurrentUser = "sla-e2e-seed";
            for (var i = 0; i < 9; i++)
                db.TenantRequestStats.Add(new TenantRequestStats
                {
                    TenantId = tenant, MinuteUtc = month.AddMinutes(i), RouteGroup = "sales",
                    Count = 100, Err4xx = 0, Err5xx = 0, P50Ms = 10, P95Ms = 40, MaxMs = 90,
                });
            // one bad minute: 5% 5xx (>= 1% threshold) → counts against availability
            db.TenantRequestStats.Add(new TenantRequestStats
            {
                TenantId = tenant, MinuteUtc = month.AddMinutes(9), RouteGroup = "sales",
                Count = 100, Err4xx = 0, Err5xx = 5, P50Ms = 10, P95Ms = 40, MaxMs = 90,
            });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();
        var route = $"/api/v1/platform/sla?tenantId={tenant}&month=2026-06";

        // non-platform operator → 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, route))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // platform-admin → 200, 9/10 good minutes = 90%
        using (var req = new HttpRequestMessage(HttpMethod.Get, route))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"minutesWithTraffic\":10", body);
            Assert.Contains("\"goodMinutes\":9", body);
            Assert.Contains("\"availabilityPct\":90", body);
        }

        // a bad month string → 400
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/platform/sla?tenantId={tenant}&month=2026-13"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
        }
    }
}
