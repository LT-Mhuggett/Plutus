using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP1.1 tenancy enforcement (spec T0.4 rule 3 + T1.1). Two layers:
///  - model metadata: exactly the tenant-owned entities carry a TenantId + global filter;
///    shared/reference entities carry neither.
///  - behaviour: two contexts on different tenants cannot see each other's rows, and a
///    cross-tenant write is blocked.
/// The MySqlDbContext model is provider-agnostic here (SQLite for behaviour, a non-connecting
/// MySql options for metadata) — the tenancy config lives in OnModelCreating regardless.
/// </summary>
public class TenancyTests
{
    private static readonly Type[] ExpectedTenantOwned =
    {
        typeof(Business), typeof(Store), typeof(Till), typeof(Item), typeof(Category), typeof(Tax),
        typeof(Discount), typeof(Discount_Category), typeof(Discount_Item), typeof(Transaction_Discount),
        typeof(Sale), typeof(Transaction), typeof(PaymentMethod_Sale), typeof(Refund),
        typeof(Note), typeof(SavedTransaction), typeof(Stock), typeof(Person), typeof(CheckoutItemChange),
    };

    // Confirmed global/shared 2026-07-24: no TenantId, no filter.
    private static readonly Type[] ExpectedGlobal =
    {
        typeof(Role), typeof(PaymentMethod), typeof(AuthActions),
    };

    private static MySqlDbContext MetadataContext()
    {
        // A MySql options that is never connected — enough to build (and inspect) the model.
        var opts = new DbContextOptionsBuilder<MySqlDbContext>()
            .UseMySql("server=localhost;database=none;user=none;password=none",
                      new MySqlServerVersion(new Version(8, 0, 23)))
            .Options;
        return new MySqlDbContext(opts, new FixedTenantContext(KnownTenants.Kapow));
    }

    [Fact]
    public void Tenant_owned_entities_have_TenantId_and_a_global_query_filter()
    {
        using var ctx = MetadataContext();
        var model = ctx.Model;

        foreach (var clr in ExpectedTenantOwned)
        {
            var et = model.FindEntityType(clr);
            Assert.True(et != null, $"{clr.Name} is not mapped");
            Assert.True(et!.FindProperty("TenantId") != null, $"{clr.Name} is missing the TenantId shadow property");
            Assert.True(et.GetQueryFilter() != null, $"{clr.Name} has no global query filter");
        }
    }

    [Fact]
    public void Global_entities_are_not_tenant_scoped()
    {
        using var ctx = MetadataContext();
        var model = ctx.Model;

        foreach (var clr in ExpectedGlobal)
        {
            var et = model.FindEntityType(clr);
            Assert.True(et != null, $"{clr.Name} is not mapped");
            Assert.True(et!.FindProperty("TenantId") == null, $"{clr.Name} must NOT carry a TenantId");
            Assert.True(et.GetQueryFilter() == null, $"{clr.Name} must NOT have a tenant query filter");
        }
    }

    private static MySqlDbContext SqliteContext(SqliteConnection conn, Guid tenantId)
    {
        var opts = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        return new MySqlDbContext(opts, new FixedTenantContext(tenantId)) { CurrentUser = "tenancy-test" };
    }

    private static Business NewBusiness(string name) =>
        new Business { Id = Guid.NewGuid(), Name = name, NameAbbr = name[..Math.Min(3, name.Length)], VatIN = "GB000" };

    [Fact]
    public void Query_filter_isolates_tenants_and_blocks_cross_tenant_writes()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        // Build schema once, then seed one business per tenant (each stamped by SaveChanges).
        using (var ctx = SqliteContext(conn, tenantA))
        {
            ctx.Database.EnsureCreated();
            ctx.Business.Add(NewBusiness("Alpha"));
            ctx.SaveChanges();
        }
        using (var ctx = SqliteContext(conn, tenantB))
        {
            ctx.Business.Add(NewBusiness("Bravo"));
            ctx.SaveChanges();
        }

        // Each tenant sees ONLY its own row.
        using (var ctx = SqliteContext(conn, tenantA))
        {
            var names = ctx.Business.Select(b => b.Name).ToList();
            Assert.Equal(new[] { "Alpha" }, names);
        }
        using (var ctx = SqliteContext(conn, tenantB))
        {
            var names = ctx.Business.Select(b => b.Name).ToList();
            Assert.Equal(new[] { "Bravo" }, names);
        }

        // Unscoped (platform admin / Guid.Empty) sees both.
        using (var ctx = SqliteContext(conn, Guid.Empty))
        {
            Assert.Equal(2, ctx.Business.Count());
        }

        // Defence in depth: tenant A cannot write a row stamped for tenant B.
        using (var ctx = SqliteContext(conn, tenantA))
        {
            var smuggled = NewBusiness("Charlie");
            var entry = ctx.Business.Add(smuggled);
            entry.Property("TenantId").CurrentValue = tenantB;
            Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());
        }
    }
}
