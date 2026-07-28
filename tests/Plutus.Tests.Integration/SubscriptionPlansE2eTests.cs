using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// OP2 subscription plans: CRUD + duplicate-name/delete-in-use 409s; assigning a plan copies its
/// name + entitlement bundle onto the tenant (so the tenants list + entitlement reads reflect it);
/// platform-admin gated.
/// </summary>
public class SubscriptionPlansE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SubscriptionPlansE2eTests(PlutusAppFactory f) => _f = f;

    private HttpRequestMessage Admin(HttpMethod m, string url, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
        return req;
    }

    [Fact]
    public async Task Plan_crud_conflicts_and_assignment_reflects_on_the_tenant()
    {
        var client = _f.CreateClient();
        var planName = "Standard-" + Guid.NewGuid().ToString("N")[..6];

        // non-admin → 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/plans"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        // create £99/mo with an entitlement bundle
        Guid planId;
        using (var resp = await client.SendAsync(Admin(HttpMethod.Post, "/api/v1/platform/plans",
            new { name = planName, pricePenceMonthly = 9900, entitlements = new[] { "woo-connector" }, active = true })))
        {
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            planId = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        // duplicate name → 409
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Admin(HttpMethod.Post, "/api/v1/platform/plans",
            new { name = planName, pricePenceMonthly = 100, entitlements = Array.Empty<string>(), active = true }))).StatusCode);

        // seed a tenant, assign the plan
        var tenantId = Guid.NewGuid();
        using (var scope = _f.Services.CreateScope())
        {
            var db = new MySqlDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(), new FixedTenantContext(Guid.Empty)) { CurrentUser = "plans-e2e" };
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Plan Co", Status = 1, Plan = "old", Entitlements = "[]", ConnectionRef = "", IsSandbox = false, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Admin(HttpMethod.Put, $"/api/v1/tenants/{tenantId}/plan", new { planId }))).StatusCode);

        // the tenants list shows the plan + its price + the copied entitlements
        var tenantsJson = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/v1/tenants"))).Content.ReadAsStringAsync();
        Assert.Contains(planId.ToString(), tenantsJson);
        Assert.Contains("\"planPricePenceMonthly\":9900", tenantsJson);
        Assert.Contains("woo-connector", tenantsJson); // entitlement bundle copied onto the tenant

        // list shows tenantCount = 1 for the plan
        var plansJson = await (await client.SendAsync(Admin(HttpMethod.Get, "/api/v1/platform/plans"))).Content.ReadAsStringAsync();
        Assert.Contains("\"tenantCount\":1", plansJson);

        // delete while assigned → 409
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Admin(HttpMethod.Delete, $"/api/v1/platform/plans/{planId}"))).StatusCode);

        // unassign, then delete succeeds
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Admin(HttpMethod.Put, $"/api/v1/tenants/{tenantId}/plan", new { planId = (Guid?)null }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Admin(HttpMethod.Delete, $"/api/v1/platform/plans/{planId}"))).StatusCode);
    }
}
