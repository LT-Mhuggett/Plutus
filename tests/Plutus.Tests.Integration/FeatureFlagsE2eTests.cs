using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP14.2 feature flags + entitlement overrides: an operator override flips a tenant's feature on
/// (beta) with no restart; a global kill switch beats it for everyone; the endpoints are
/// platform-admin only and audited.
/// </summary>
public class FeatureFlagsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public FeatureFlagsE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;
    private const string Feature = "advanced-reports";

    private Task<bool> IsEnabled()
    {
        using var scope = _f.Services.CreateScope();
        var ent = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        return ent.IsEnabledAsync(Kapow, Feature);
    }

    [Fact]
    public async Task Override_grants_a_feature_and_a_kill_switch_beats_it_globally()
    {
        var client = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        Assert.False(await IsEnabled()); // not in Kapow's plan

        // non-admin can't set overrides
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/tenants/{Kapow}/overrides"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            req.Content = JsonContent.Create(new { overrides = new[] { new { entitlement = Feature, deny = false, reason = "beta" } } });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // admin grants it (beta) → enabled, no restart
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/tenants/{Kapow}/overrides"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { overrides = new[] { new { entitlement = Feature, deny = false, reason = "beta" } } });
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(req)).StatusCode);
        }
        Assert.True(await IsEnabled());

        // global kill switch beats the grant for every tenant
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/flags/{Feature}"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { enabled = false, reason = "incident" });
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(req)).StatusCode);
        }
        Assert.False(await IsEnabled());

        // re-enabling the switch restores the grant
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/flags/{Feature}"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { enabled = true, reason = "resolved" });
            await client.SendAsync(req);
        }
        Assert.True(await IsEnabled());

        // both changes were audited
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "entitlement.overrides.set"));
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "platform.flag.set"));
        }
    }
}
