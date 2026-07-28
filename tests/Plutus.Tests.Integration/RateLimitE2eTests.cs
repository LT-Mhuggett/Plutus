using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP13.5 per-tenant rate limiting: a flood against tenant A is throttled (429) for A ONLY —
/// tenant B is unaffected (noisy-neighbour isolation) — and platform-admin is exempt. The default
/// rps is 30 (PlutusAppFactory); requests hit a non-routed path so the limiter is exercised
/// without any DB work (throttled requests never reach an endpoint).
/// </summary>
public class RateLimitE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public RateLimitE2eTests(PlutusAppFactory f) => _f = f;

    private async Task<int[]> FloodAsync(string token, int n)
    {
        var client = _f.CreateClient();
        var tasks = Enumerable.Range(0, n).Select(async _ =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/__ratelimit_probe");
            req.Headers.Authorization = new("Bearer", token);
            return (int)(await client.SendAsync(req)).StatusCode;
        });
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Flood_throttles_one_tenant_only_and_exempts_platform_admin()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Tenant A: 120 concurrent (≫ 30 rps) → guaranteed 429s even if the batch straddles a
        // one-second window boundary; the ~30 under the limit fall through to a (non-existent) route.
        var a = await FloodAsync(PlutusAppFactory.OperatorToken("pos.sell", tenantA), 120);
        Assert.Contains(429, a);
        Assert.Contains(404, a);

        // Tenant B: 3 concurrent → never throttled (isolated partition).
        var b = await FloodAsync(PlutusAppFactory.OperatorToken("pos.sell", tenantB), 3);
        Assert.DoesNotContain(429, b);

        // Platform-admin: 120 concurrent → exempt, never throttled.
        var admin = await FloodAsync(PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin), 120);
        Assert.DoesNotContain(429, admin);
    }
}
