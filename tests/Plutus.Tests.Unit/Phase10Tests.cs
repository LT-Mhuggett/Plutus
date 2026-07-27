using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Phase 10: entitlement resolution, the billing webhook seam (HMAC), and tenant lifecycle
/// (status + scheduled deletion). The hard-delete uses MySQL information_schema and is verified
/// live, not here. Runs the real MySqlDbContext model on SQLite.
/// </summary>
public class Phase10Tests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn, Guid tenant, string user = "test")
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(tenant)) { CurrentUser = user };

    private static SqliteConnection OpenWithTenant(byte status = 1, string entitlements = "[]")
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn, Tenant);
        ctx.Database.EnsureCreated();
        ctx.Tenants.Add(new Tenant
        {
            Id = Tenant, Name = "Acme", Status = status, Plan = "standard",
            Entitlements = entitlements, ConnectionRef = "", CreatedAtUtc = DateTime.UtcNow,
        });
        ctx.SaveChanges();
        return conn;
    }

    [Fact]
    public async Task Entitlement_service_reads_the_json_and_gates()
    {
        using var conn = OpenWithTenant(entitlements: "[\"woo-connector\"]");
        using var ctx = Ctx(conn, Tenant);
        var svc = new EntitlementService(ctx);

        Assert.True(await svc.IsEnabledAsync(Tenant, Entitlements.WooConnector));
        Assert.False(await svc.IsEnabledAsync(Tenant, Entitlements.MultiStore));
        Assert.False(await svc.IsEnabledAsync(Guid.NewGuid(), Entitlements.WooConnector)); // unknown tenant
        Assert.True(await svc.IsEnabledAsync(Guid.Empty, Entitlements.MultiStore));        // platform-admin
    }

    [Fact]
    public void Billing_webhook_verifies_hmac_and_maps_the_change()
    {
        const string secret = "billing-test-secret";
        var provider = new NullBillingProvider(secret);
        var payload = $"{{\"tenantId\":\"{Tenant}\",\"entitlements\":[\"woo-connector\"],\"plan\":\"pro\",\"status\":1}}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        Assert.True(provider.TryHandleWebhook(payload, sig, out var change));
        Assert.Equal(Tenant, change.TenantId);
        Assert.Contains("woo-connector", change.Entitlements);
        Assert.Equal("pro", change.Plan);
        Assert.Equal((byte)1, change.Status);

        // Tampered signature is rejected.
        Assert.False(provider.TryHandleWebhook(payload, sig[..^2] + "00", out _));
    }

    [Fact]
    public async Task Billing_change_applies_entitlements_and_status()
    {
        using var conn = OpenWithTenant();
        using var ctx = Ctx(conn, Tenant);
        await new TenantLifecycleService(ctx).ApplyBillingChangeAsync(
            new BillingChange(Tenant, new[] { "woo-connector", "advanced-reports" }, "pro", TenantStatus.Active), "billing:test");

        var t = await ctx.Tenants.AsNoTracking().FirstAsync(x => x.Id == Tenant);
        Assert.Contains("woo-connector", EntitlementService.Parse(t.Entitlements));
        Assert.Equal("pro", t.Plan);
    }

    [Fact]
    public async Task Suspended_tenant_blocks_portal_but_status_toggles()
    {
        using var conn = OpenWithTenant(status: TenantStatus.Active);
        using (var ctx = Ctx(conn, Tenant))
        {
            var svc = new TenantLifecycleService(ctx);
            Assert.True(await svc.PortalAccessAllowedAsync(Tenant));
            await svc.SetStatusAsync(Tenant, TenantStatus.Suspended, "admin");
        }
        using (var ctx = Ctx(conn, Tenant))
            Assert.False(await new TenantLifecycleService(ctx).PortalAccessAllowedAsync(Tenant));
    }

    [Fact]
    public async Task Deletion_request_closes_tenant_and_cannot_touch_kapow()
    {
        using var conn = OpenWithTenant();
        using (var ctx = Ctx(conn, Tenant))
        {
            var svc = new TenantLifecycleService(ctx);
            var schedule = await svc.RequestDeletionAsync(Tenant, 7, Guid.NewGuid());
            Assert.Equal(TenantLifecycleService.DeletionPending, schedule.Status);
            Assert.True(schedule.ExecuteAfterUtc > DateTime.UtcNow);
        }
        using (var ctx = Ctx(conn, Tenant))
            Assert.Equal(TenantStatus.Closed, (await ctx.Tenants.AsNoTracking().FirstAsync(t => t.Id == Tenant)).Status);

        // A second request while one is pending → 409.
        using (var ctx = Ctx(conn, Tenant))
        {
            var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
                new TenantLifecycleService(ctx).RequestDeletionAsync(Tenant, 7, Guid.NewGuid()));
            Assert.Equal(409, ex.StatusCode);
        }

        // The founding tenant is protected.
        using (var ctx = Ctx(conn, WellKnownTenants.Kapow))
        {
            var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
                new TenantLifecycleService(ctx).RequestDeletionAsync(WellKnownTenants.Kapow, 7, Guid.NewGuid()));
            Assert.Equal(400, ex.StatusCode);
        }
    }
}
