using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// 16.4 billing config (operator, platform-wide: stripe/paddle/chargebee/manual, secrets
/// write-only) + 17.2 per-tenant payment gateway (client-managed, standalone default, till reads
/// /active only, cross-tenant isolated).
/// </summary>
public class CommerceConfigE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CommerceConfigE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private HttpRequestMessage Req(HttpMethod m, string url, string token, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", token);
        return req;
    }

    // ── 16.4 billing ──

    [Fact]
    public async Task Billing_defaults_to_manual_and_secret_is_write_only()
    {
        var client = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        // non-admin → 403
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/config", PlutusAppFactory.OperatorToken("pos.sell")))).StatusCode);

        // default = manual
        var def = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/config", admin))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"manual\"", def);

        // catalogue carries all four options
        var cat = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/catalogue", admin))).Content.ReadAsStringAsync();
        foreach (var k in new[] { "manual", "stripe", "paddle", "chargebee" }) Assert.Contains(k, cat);

        // configure stripe with a secret → redacted on read; unknown provider → 400
        var put = new { provider = "stripe", enabled = true, config = new Dictionary<string, string> { ["secretKey"] = "sk_test_abc", ["webhookSecret"] = "whsec_xyz" } };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/platform/billing/config", admin, put))).StatusCode);
        var got = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/platform/billing/config", admin))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk_test_abc", got);
        Assert.Contains("__set__", got);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/platform/billing/config", admin, new { provider = "bitcoin", enabled = true, config = new Dictionary<string, string>() }))).StatusCode);
    }

    // ── 17.2 payment gateway ──

    private async Task<Guid> SeedOwnerAsync()
    {
        var userId = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "commerce-e2e-seed";
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        var owner = await db.RbacRoles.FirstAsync(r => r.Name == "Owner");
        db.RbacRoleAssignments.Add(new Plutus.Entities.Models.RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Kapow, UserId = userId, RoleId = owner.Id,
            ScopeType = Plutus.Entities.Models.RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Gateway_defaults_to_standalone_is_perm_gated_and_tenant_isolated()
    {
        var client = _f.CreateClient();

        // /active for ANY authenticated user defaults to standalone, not integrated
        var till = PlutusAppFactory.OperatorToken("pos.sell");
        var active = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", till))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"standalone\"", active);
        Assert.Contains("\"integrated\":false", active);

        // a pos.sell user without company.manage cannot write
        var put = new { provider = "sumup", config = new Dictionary<string, string> { ["apiKey"] = "sup_secret", ["merchantCode"] = "M123" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", till, put))).StatusCode);

        // an Owner (portal.company.manage) can select SumUp; secret is write-only on read-back
        var ownerId = await SeedOwnerAsync();
        var owner = PlutusAppFactory.OperatorTokenFor(ownerId, "pos.sell");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, "/api/v1/payments/gateway", owner, put))).StatusCode);
        var got = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway", owner))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"sumup\"", got);
        Assert.DoesNotContain("sup_secret", got);
        Assert.Contains("M123", got); // non-secret field visible

        // the till now sees sumup for Kapow — still not integrated (standalone flow continues)
        var activeNow = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", till))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"sumup\"", activeNow);
        Assert.Contains("\"integrated\":false", activeNow);

        // ISOLATION: a different tenant's till still sees standalone
        var otherTill = PlutusAppFactory.OperatorToken("pos.sell", Guid.NewGuid());
        var otherActive = await (await client.SendAsync(Req(HttpMethod.Get, "/api/v1/payments/gateway/active", otherTill))).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"standalone\"", otherActive);
    }
}
