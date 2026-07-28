using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP16.1–16.5 commercial ops: the churn sweep raises exactly one usage-declining signal that
/// clears on recovery; contract CRUD is platform-admin + audited; analytics is anonymised
/// (k-anonymity floor + no TenantId in any response field). All platform-admin gated.
/// </summary>
public class CommercialOpsE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CommercialOpsE2eTests(PlutusAppFactory f) => _f = f;

    private sealed class FakeAlerter : IOperatorAlerter
    {
        public readonly HashSet<string> Open = new();
        public Task RaiseAsync(string k, string j, Guid? t, string kind, string m, CancellationToken ct = default) { Open.Add(k); return Task.CompletedTask; }
        public Task ClearAsync(string k, CancellationToken ct = default) { Open.Remove(k); return Task.CompletedTask; }
    }

    private MySqlDbContext Unscoped(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(), new FixedTenantContext(Guid.Empty));

    [Fact]
    public async Task Churn_sweep_raises_declining_once_and_clears_on_recovery()
    {
        var tenant = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var alerter = new FakeAlerter();

        using var scope = _f.Services.CreateScope();
        await using var db = Unscoped(scope);
        db.CurrentUser = "churn-e2e";
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Declining Co", Status = 1, Plan = "std", Entitlements = "[]", ConnectionRef = "", IsSandbox = false, CreatedAtUtc = DateTime.UtcNow.AddMonths(-6) });
        // prior 28-day window busy (100), current window quiet (10) → down 90%
        db.TenantUsageRollups.Add(new TenantUsageRollup { TenantId = tenant, BusinessDay = today.AddDays(-40), Metric = UsageMetrics.SalesCount, Value = 100 });
        db.TenantUsageRollups.Add(new TenantUsageRollup { TenantId = tenant, BusinessDay = today.AddDays(-5), Metric = UsageMetrics.SalesCount, Value = 10 });
        await db.SaveChangesAsync();

        await ChurnSweep.EvaluateAsync(db, alerter, DateTime.UtcNow);
        var open = await db.TenantSignals.Where(s => s.TenantId == tenant && s.ClearedAtUtc == null).Select(s => s.Signal).ToListAsync();
        Assert.Contains(TenantSignals.UsageDeclining, open);
        Assert.Contains(ChurnSweep.AlertKey(tenant, TenantSignals.UsageDeclining), alerter.Open);

        // recover: current window now matches prior → no longer declining
        db.TenantUsageRollups.Add(new TenantUsageRollup { TenantId = tenant, BusinessDay = today.AddDays(-3), Metric = UsageMetrics.SalesCount, Value = 100 });
        await db.SaveChangesAsync();
        await ChurnSweep.EvaluateAsync(db, alerter, DateTime.UtcNow);
        var stillOpen = await db.TenantSignals.Where(s => s.TenantId == tenant && s.ClearedAtUtc == null).Select(s => s.Signal).ToListAsync();
        Assert.DoesNotContain(TenantSignals.UsageDeclining, stillOpen);
        Assert.DoesNotContain(ChurnSweep.AlertKey(tenant, TenantSignals.UsageDeclining), alerter.Open);
    }

    [Fact]
    public async Task Contract_crud_is_platform_admin_and_audited()
    {
        var tenant = Guid.NewGuid();
        var client = _f.CreateClient();
        var body = new { renewalAtUtc = DateTime.UtcNow.AddDays(20), termMonths = 12, pricePenceMonthly = 9900, notes = "annual" };

        // non-admin PUT → 403
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/tenants/{tenant}/contract") { Content = JsonContent.Create(body) })
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
        // admin PUT → 204, then GET returns it
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin);
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/platform/tenants/{tenant}/contract") { Content = JsonContent.Create(body) })
        {
            req.Headers.Authorization = new("Bearer", admin);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(req)).StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/platform/tenants/{tenant}/contract"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("9900", await resp.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Analytics_is_k_anonymised_and_never_leaks_a_tenant_id()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var three = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var solo = Guid.NewGuid();

        using (var scope = _f.Services.CreateScope())
        {
            await using var db = Unscoped(scope);
            db.CurrentUser = "analytics-e2e";
            foreach (var t in three)
                db.TenantUsageRollups.Add(new TenantUsageRollup { TenantId = t, BusinessDay = today, Metric = UsageMetrics.SalesCount, Value = 5 });
            // a made-up metric contributed by ONE tenant only → must be suppressed by the k floor
            db.TenantUsageRollups.Add(new TenantUsageRollup { TenantId = solo, BusinessDay = today, Metric = "test.solo", Value = 5 });
            await db.SaveChangesAsync();
        }

        var client = _f.CreateClient();

        // non-admin → 403
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/analytics"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }

        using var areq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/analytics");
        areq.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
        var aresp = await client.SendAsync(areq);
        Assert.Equal(HttpStatusCode.OK, aresp.StatusCode);
        var json = await aresp.Content.ReadAsStringAsync();

        Assert.Contains("sales.count", json);       // ≥3 tenants → shown
        Assert.DoesNotContain("test.solo", json);    // 1 tenant → suppressed (k-anonymity)
        Assert.DoesNotContain("tenantId", json);     // anonymity: no per-tenant field
        Assert.DoesNotContain(solo.ToString(), json);
        foreach (var t in three) Assert.DoesNotContain(t.ToString(), json);
    }

    [Fact]
    public async Task Margin_returns_clean_empty_state_when_unconfigured()
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/margin");
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin));
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // not a 500
        Assert.Contains("\"configured\":false", await resp.Content.ReadAsStringAsync());
    }
}
