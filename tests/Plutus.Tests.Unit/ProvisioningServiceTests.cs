using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP1.2 provisioning: one call creates a tenant + company + store + admin whose
/// credentials verify (so they can log in), and everything is stamped to the new tenant.</summary>
public class ProvisioningServiceTests
{
    private static MySqlDbContext Ctx(SqliteConnection conn, Guid tenant, string user = "test")
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(tenant)) { CurrentUser = user };

    [Fact]
    public async Task Provision_creates_full_tenant_graph_with_working_admin_login()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var ctx = Ctx(conn, Guid.Empty)) ctx.Database.EnsureCreated();

        ProvisionResult res;
        using (var ctx = Ctx(conn, Guid.Empty, "platform-admin")) // unscoped platform-admin
            res = await new ProvisioningService(ctx).ProvisionAsync(
                new ProvisionRequest("Acme Comics", "standard", "admin@acme.test", "S3cret!"), "platform-admin");

        Assert.NotEqual(Guid.Empty, res.TenantId);
        Assert.NotEqual(Guid.Empty, res.CompanyId);

        // Rows exist and the tenant-owned ones are stamped to the new tenant.
        using (var ctx = Ctx(conn, Guid.Empty))
        {
            Assert.True(await ctx.Tenants.AnyAsync(t => t.Id == res.TenantId && t.Name == "Acme Comics"));
            Assert.True(await ctx.Business.AnyAsync(b => b.Id == res.CompanyId));
            Assert.True(await ctx.Stores.AnyAsync(s => s.Id == res.StoreId));

            var bizTenant = (Guid)ctx.Entry(await ctx.Business.FirstAsync(b => b.Id == res.CompanyId))
                .Property("TenantId").CurrentValue!;
            Assert.Equal(res.TenantId, bizTenant);

            // Admin credentials verify with the supplied password (proves login will work).
            var cred = await ctx.WebCredentials.FirstAsync(w => w.Email == "admin@acme.test");
            Assert.Equal(res.AdminUserId, cred.EmployeeId);
            Assert.True(Pbkdf2.Verify("S3cret!",
                Convert.FromBase64String(cred.Salt), Convert.FromBase64String(cred.HashedPassword)));
            Assert.False(Pbkdf2.Verify("wrong-password",
                Convert.FromBase64String(cred.Salt), Convert.FromBase64String(cred.HashedPassword)));
        }

        // The new tenant's own scope sees its company; a different tenant sees nothing.
        using (var ctx = Ctx(conn, res.TenantId))
            Assert.Equal(1, await ctx.Business.CountAsync(b => b.Id == res.CompanyId));
        using (var ctx = Ctx(conn, Guid.NewGuid()))
            Assert.Equal(0, await ctx.Business.CountAsync(b => b.Id == res.CompanyId));

        conn.Dispose();
    }

    [Fact]
    public async Task Provision_rejects_missing_fields()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var ctx = Ctx(conn, Guid.Empty)) ctx.Database.EnsureCreated();

        using var c2 = Ctx(conn, Guid.Empty, "platform-admin");
        var ex = await Assert.ThrowsAsync<EnrolmentException>(() =>
            new ProvisioningService(c2).ProvisionAsync(new ProvisionRequest("", "standard", "a@b.c", "pw"), "platform-admin"));
        Assert.Equal(400, ex.StatusCode);
        conn.Dispose();
    }
}
