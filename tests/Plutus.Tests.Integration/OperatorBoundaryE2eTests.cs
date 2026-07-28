using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// OP1: a pure operator (platform-admin, no tenant identity) must be 403'd from every client-data
/// API — reaching client data only via the audited impersonation flow (which carries a tid).
/// Operator surfaces (/platform/*, /tenants) still work; ordinary tenant tokens are unaffected.
/// </summary>
public class OperatorBoundaryE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public OperatorBoundaryE2eTests(PlutusAppFactory f) => _f = f;

    private async Task<HttpStatusCode> Get(string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new("Bearer", token);
        return (await _f.CreateClient().SendAsync(req)).StatusCode;
    }

    [Fact]
    public async Task Operator_is_blocked_from_client_data_but_not_operator_surfaces()
    {
        var op = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin); // platform-admin, no tid

        // client-data endpoints → 403 (the boundary)
        Assert.Equal(HttpStatusCode.Forbidden, await Get("/api/v1/customers", op));
        Assert.Equal(HttpStatusCode.Forbidden, await Get("/api/v1/loyalty", op));

        // operator surfaces → allowed through (platform-admin policy then serves them)
        Assert.Equal(HttpStatusCode.OK, await Get("/api/v1/platform/health", op));
        Assert.Equal(HttpStatusCode.OK, await Get("/api/v1/tenants", op));
    }

    [Fact]
    public async Task Ordinary_tenant_token_is_not_blocked_by_the_boundary()
    {
        // A plain staff token carries no platform-admin scope, so the boundary never applies:
        // /api/v1/customers is [Authorize] (any auth) and resolves to the tenant → 200.
        var staff = PlutusAppFactory.OperatorToken("pos.sell");
        Assert.Equal(HttpStatusCode.OK, await Get("/api/v1/customers", staff));
    }

    [Fact]
    public async Task Platform_admin_WITH_a_tenant_identity_passes_the_boundary()
    {
        // e.g. an impersonation token, or a tenant employee who also holds platform-admin — a tid
        // is present, so the boundary lets client-data requests through (RBAC then decides).
        var withTid = PlutusAppFactory.OperatorTokenFor(Guid.NewGuid(), PlutusPolicies.PlatformAdmin, Guid.NewGuid());
        Assert.NotEqual(HttpStatusCode.Forbidden, await Get("/api/v1/customers", withTid));
    }
}
