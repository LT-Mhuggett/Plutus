using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP2.1: the portal dashboard KPI endpoint. Tenant-scoped, gated on portal.reports.view; returns
/// today/this-week sales + the active counts (users/tills/stores/warehouses/webstores). The 200
/// case also proves every underlying LINQ query (incl. DateOnly week window + CountAsync predicates)
/// translates on the SQLite host.
/// </summary>
public class DashboardKpiE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public DashboardKpiE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    [Fact]
    public async Task Dashboard_requires_reports_view_and_returns_the_kpi_shape()
    {
        var client = _f.CreateClient();

        // no reports permission → 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/dashboard"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell", Kapow));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // authorised (platform-admin always satisfies perm:* — same pattern as the usage tests),
        // scoped to Kapow → 200 with the full KPI shape (proves every LINQ query translates)
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reports/dashboard"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            foreach (var key in new[] { "salesTodayPence", "salesWeekPence", "weekStart", "activeUsers", "activeTills", "activeStores", "activeWarehouses", "activeWebstores" })
                Assert.Contains(key, body);
        }
    }
}
