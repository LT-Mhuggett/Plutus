using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>A factory with WP18.1 operator-SSO enforcement turned on (in-memory config, so it's
/// isolated from other tests — no process-wide env var).</summary>
public sealed class SsoEnforcedFactory : PlutusAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OPERATOR_SSO_ENFORCED"] = "true",
        }));
    }
}

/// <summary>
/// WP18.1: with operator SSO enforced, an HMAC ("test") login authenticates but must NOT carry
/// platform-admin — so the Platform APIs 403 for it (platform-admin may only arrive via the
/// Keycloak/OIDC path). Its other scopes are unaffected. Default-off behaviour (HMAC platform-admin
/// works) is already exercised by every other platform test in this suite.
/// </summary>
public class OperatorSsoE2eTests : IClassFixture<SsoEnforcedFactory>
{
    private readonly SsoEnforcedFactory _f;
    public OperatorSsoE2eTests(SsoEnforcedFactory f) => _f = f;

    [Fact]
    public async Task Hmac_platform_admin_is_stripped_when_sso_enforced()
    {
        var client = _f.CreateClient();

        // An HMAC token minted WITH platform-admin scope no longer satisfies the platform policy.
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/health"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // But the same HMAC path still authenticates for ordinary scopes (auth works; only the
        // platform-admin scope is withheld). A non-platform authenticated call is not 401.
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/webstores/connector-health"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            var resp = await client.SendAsync(req);
            Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
    }
}
