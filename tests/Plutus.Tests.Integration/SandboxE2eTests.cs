using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP14.3 sandbox tenants: reset is refused on a non-sandbox tenant, restores a sandbox to an
/// identical deterministic demo state (row counts + penny totals invariant across resets), and
/// sandbox tenants are excluded from the commercial usage summary unless explicitly included.
/// </summary>
public class SandboxE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public SandboxE2eTests(PlutusAppFactory f) => _f = f;

    private async Task<Guid> SeedTenantAsync()
    {
        var id = Guid.NewGuid();
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "sandbox-seed";
        db.Tenants.Add(new Tenant { Id = id, Name = "Demo", Status = 1, Plan = "std", Entitlements = "[]", ConnectionRef = "", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(int sales, long gross)> CountAsync(Guid tenant)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var sales = await db.SalesV2.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenant);
        var gross = sales == 0 ? 0 : await db.SalesV2.IgnoreQueryFilters().Where(s => s.TenantId == tenant).SumAsync(s => s.GrossPence);
        return (sales, gross);
    }

    [Fact]
    public async Task Reset_is_sandbox_only_and_deterministic()
    {
        var id = await SeedTenantAsync();
        var client = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);

        HttpRequestMessage Req(HttpMethod m, string url, object body = null)
        {
            var r = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
            r.Headers.Authorization = new("Bearer", admin);
            return r;
        }

        // reset on a non-sandbox tenant → 409 (hard guard)
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/platform/tenants/{id}/reset"))).StatusCode);

        // mark sandbox, then reset → 30 sales / £150.00
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Req(HttpMethod.Put, $"/api/v1/platform/tenants/{id}/sandbox", new { isSandbox = true }))).StatusCode);
        var resetResp = await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/platform/tenants/{id}/reset"));
        Assert.True(resetResp.StatusCode == HttpStatusCode.OK, $"reset -> {resetResp.StatusCode}: {await resetResp.Content.ReadAsStringAsync()}");

        var first = await CountAsync(id);
        Assert.Equal(30, first.sales);
        Assert.Equal(15000, first.gross);

        // reset again → identical (not doubled)
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/platform/tenants/{id}/reset"))).StatusCode);
        var second = await CountAsync(id);
        Assert.Equal(first, second);

        // rollups populated for the sandbox (projections rebuilt honestly)
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            Assert.True(await db.SalesRollups.IgnoreQueryFilters().AnyAsync(r => r.TenantId == id));
        }

        // sandbox is excluded from the commercial usage summary by default, included on request
        using (var req = Req(HttpMethod.Get, "/api/v1/platform/usage/summary"))
        {
            var body = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(id.ToString(), body);
        }
        using (var req = Req(HttpMethod.Get, "/api/v1/platform/usage/summary?includeSandbox=true"))
        {
            var body = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
            Assert.Contains(id.ToString(), body);
        }
    }
}
