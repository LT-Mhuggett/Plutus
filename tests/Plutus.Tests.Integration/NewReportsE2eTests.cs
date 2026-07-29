using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP3.7/3.8/3.9: the new report endpoints (category-sales, best-sellers, negative stock levels).
/// 200 + gated proves the SQL group-by + barcode→category resolve + the negative filter translate
/// on the SQLite host and are authorised as reports.
/// </summary>
public class NewReportsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public NewReportsE2eTests(PlutusAppFactory f) => _f = f;
    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    [Theory]
    [InlineData("/api/v1/reports/category-sales?from=2020-01-01&to=2030-01-01")]
    [InlineData("/api/v1/reports/best-sellers?from=2020-01-01&to=2030-01-01&by=gross&take=10")]
    [InlineData("/api/v1/stock/levels?filter=negative&take=25")]
    public async Task New_reports_are_gated_and_return_200(string route)
    {
        var client = _f.CreateClient();

        using (var req = new HttpRequestMessage(HttpMethod.Get, route))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell", Kapow));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, route))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        }
    }
}
