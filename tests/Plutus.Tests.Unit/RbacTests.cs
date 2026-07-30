using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP3.1 matrix (DoD): scope inheritance down the spine, a till-scoped cashier can't see
/// store financials, time-window expiry at token issue, ceiling union semantics, and the
/// Kapow AuthActions seed mapping (refund pence ceilings from AuthActions.Amount).
/// </summary>
public class RbacTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private const int StoreA = 1;
    private static readonly Guid TillA1 = Guid.NewGuid(); // store A
    private static readonly Guid TillA2 = Guid.NewGuid(); // store A
    private static readonly Guid UserId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "rbac-test" };

    private static SqliteConnection OpenSeeded()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
        ctx.Stores.Add(new Store
        {
            Id = StoreA, BusinessId = BusinessId, ContactNumber = "-",
            AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });
        ctx.Till.Add(new Till { Id = TillA1, StoreId = StoreA, LastOnline = DateTime.UtcNow });
        ctx.Till.Add(new Till { Id = TillA2, StoreId = StoreA, LastOnline = DateTime.UtcNow });
        ctx.SaveChanges();
        return conn;
    }

    private static RbacRole Role(string name, params (string Code, long? Max)[] grants) => new()
    {
        Id = Uuid7.New(), TenantId = Tenant, Name = name, IsBuiltIn = false, CreatedAtUtc = DateTime.UtcNow,
        Grants = grants.Select(g => new RbacRoleGrant
        {
            Id = Uuid7.New(), TenantId = Tenant, PermissionCode = g.Code, MaxPence = g.Max,
        }).ToList(),
    };

    private static RbacRoleAssignment Assign(Guid userId, RbacRole role, ScopeNode scope) => new()
    {
        Id = Uuid7.New(), TenantId = Tenant, UserId = userId, RoleId = role.Id,
        ScopeType = scope.Type, ScopeId = scope.Id, CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task Company_scoped_assignment_grants_at_every_till_below_it()
    {
        using var conn = OpenSeeded();
        var admin = Role("Admin-ish", (PermissionCatalogue.PortalFinancialsView, null), (PermissionCatalogue.PosSell, null));
        using (var ctx = Ctx(conn))
        {
            ctx.RbacRoles.Add(admin);
            ctx.RbacRoleAssignments.Add(Assign(UserId, admin, ScopeNode.Company(BusinessId)));
            ctx.SaveChanges();
        }

        using var db = Ctx(conn);
        var svc = new EffectivePermissionsService(db);

        var atTill = await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), DateTime.Now);
        Assert.Contains(atTill, p => p.Code == PermissionCatalogue.PosSell);
        Assert.Contains(atTill, p => p.Code == PermissionCatalogue.PortalFinancialsView);

        var atStore = await svc.ResolveAsync(UserId, ScopeNode.Store(StoreA), DateTime.Now);
        Assert.Contains(atStore, p => p.Code == PermissionCatalogue.PortalFinancialsView);

        // …but NOT at bare tenant scope (company sits BELOW tenant on the spine).
        var atTenant = await svc.ResolveAsync(UserId, ScopeNode.Tenant, DateTime.Now);
        Assert.Empty(atTenant);
    }

    [Fact]
    public async Task Till_scoped_cashier_sells_at_that_till_only_and_sees_no_store_financials()
    {
        using var conn = OpenSeeded();
        var cashier = Role("Cashier", (PermissionCatalogue.PosSell, null));
        using (var ctx = Ctx(conn))
        {
            ctx.RbacRoles.Add(cashier);
            ctx.RbacRoleAssignments.Add(Assign(UserId, cashier, ScopeNode.Till(TillA1)));
            ctx.SaveChanges();
        }

        using var db = Ctx(conn);
        var svc = new EffectivePermissionsService(db);

        var atOwnTill = await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), DateTime.Now);
        Assert.Contains(atOwnTill, p => p.Code == PermissionCatalogue.PosSell);

        // no assignment covering the sibling till → no access there (architecture §7.2:
        // "which POS a user can access falls out of scoping")
        Assert.Empty(await svc.ResolveAsync(UserId, ScopeNode.Till(TillA2), DateTime.Now));

        // and a till assignment grants NOTHING at the store above it — store financials stay dark
        var atStore = await svc.ResolveAsync(UserId, ScopeNode.Store(StoreA), DateTime.Now);
        Assert.DoesNotContain(atStore, p => p.Code == PermissionCatalogue.PortalFinancialsView);
        Assert.Empty(atStore);
    }

    [Fact]
    public async Task Time_windows_gate_at_token_issue()
    {
        using var conn = OpenSeeded();
        var satStaff = Role("Sat staff", (PermissionCatalogue.PosSell, null));
        using (var ctx = Ctx(conn))
        {
            ctx.RbacRoles.Add(satStaff);
            var a = Assign(UserId, satStaff, ScopeNode.Company(BusinessId));
            a.DaysOfWeekMask = (byte)(1 << (int)DayOfWeek.Saturday);
            a.WindowStartLocal = new TimeOnly(9, 0);
            a.WindowEndLocal = new TimeOnly(17, 30);
            ctx.RbacRoleAssignments.Add(a);

            var expired = Role("Expired contract", (PermissionCatalogue.PosVoid, null));
            ctx.RbacRoles.Add(expired);
            var e = Assign(UserId, expired, ScopeNode.Company(BusinessId));
            e.ValidToUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            ctx.RbacRoleAssignments.Add(e);
            ctx.SaveChanges();
        }

        using var db = Ctx(conn);
        var svc = new EffectivePermissionsService(db);

        var saturdayNoon = new DateTime(2026, 7, 25, 12, 0, 0, DateTimeKind.Local); // a Saturday
        var saturdayLate = new DateTime(2026, 7, 25, 18, 0, 0, DateTimeKind.Local);
        var sundayNoon = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Local);

        Assert.Contains(await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), saturdayNoon),
            p => p.Code == PermissionCatalogue.PosSell);
        Assert.DoesNotContain(await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), saturdayLate),
            p => p.Code == PermissionCatalogue.PosSell);
        Assert.DoesNotContain(await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), sundayNoon),
            p => p.Code == PermissionCatalogue.PosSell);

        // the date-bounded assignment expired on 2026-01-01 → pos.void never appears
        Assert.DoesNotContain(await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), saturdayNoon),
            p => p.Code == PermissionCatalogue.PosVoid);
    }

    [Fact]
    public async Task Ceilings_union_highest_wins_and_unlimited_beats_all()
    {
        using var conn = OpenSeeded();
        using (var ctx = Ctx(conn))
        {
            var r20 = Role("Refunds to £20", (PermissionCatalogue.PosRefund, 2000L));
            var r100 = Role("Refunds to £100", (PermissionCatalogue.PosRefund, 10000L));
            ctx.RbacRoles.AddRange(r20, r100);
            ctx.RbacRoleAssignments.Add(Assign(UserId, r20, ScopeNode.Company(BusinessId)));
            ctx.RbacRoleAssignments.Add(Assign(UserId, r100, ScopeNode.Till(TillA1)));
            ctx.SaveChanges();
        }

        using (var db = Ctx(conn))
        {
            var svc = new EffectivePermissionsService(db);
            var atTill = await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), DateTime.Now);
            var refund = Assert.Single(atTill, p => p.Code == PermissionCatalogue.PosRefund);
            Assert.Equal(10000L, refund.MaxPence);           // highest ceiling wins
            Assert.Equal("pos.refund.max:10000", refund.ToString());

            // at the sibling till only the company-wide £20 applies
            var atOther = await svc.ResolveAsync(UserId, ScopeNode.Till(TillA2), DateTime.Now);
            Assert.Equal(2000L, Assert.Single(atOther, p => p.Code == PermissionCatalogue.PosRefund).MaxPence);
        }

        // add an unlimited grant → ceiling disappears
        using (var ctx = Ctx(conn))
        {
            var unlimited = Role("Refunds (unlimited)", (PermissionCatalogue.PosRefund, null));
            ctx.RbacRoles.Add(unlimited);
            ctx.RbacRoleAssignments.Add(Assign(UserId, unlimited, ScopeNode.Company(BusinessId)));
            ctx.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            var svc = new EffectivePermissionsService(db);
            var atTill = await svc.ResolveAsync(UserId, ScopeNode.Till(TillA1), DateTime.Now);
            Assert.Null(Assert.Single(atTill, p => p.Code == PermissionCatalogue.PosRefund).MaxPence);
        }
    }

    [Fact]
    public async Task Kapow_authactions_seed_maps_roles_and_refund_ceilings()
    {
        using var conn = OpenSeeded();
        var emp = Guid.NewGuid();
        using (var ctx = Ctx(conn))
        {
            ctx.AuthActions.AddRange(
                new AuthActions { Id = 1, Name = "Till", Module = "-", Amount = 0 },
                new AuthActions { Id = 2, Name = "Refund20", Module = "-", Amount = 20 },
                new AuthActions { Id = 8, Name = "Refund Unlimited", Module = "-", Amount = 0 },
                new AuthActions { Id = 10, Name = "Admin", Module = "-", Amount = 0 },
                new AuthActions { Id = 6, Name = "Force Loggout All Users", Module = "-", Amount = 0 });
            ctx.EmpAuthActions.AddRange(
                new Emp_AuthActions { EmpId = emp, AuthAId = 1 },
                new Emp_AuthActions { EmpId = emp, AuthAId = 2 },
                new Emp_AuthActions { EmpId = emp, AuthAId = 10 },
                new Emp_AuthActions { EmpId = emp, AuthAId = 6 }); // unmapped → skipped
            ctx.SaveChanges();
        }

        using (var db = Ctx(conn))
        {
            var (roles, assignments) = await RbacSeeder.SeedAsync(db, Tenant);
            Assert.True(roles >= 8);        // the built-ins
            Assert.Equal(3, assignments);   // Till, Refund20, Admin (force-logout skipped)
        }

        // idempotent: second run adds nothing
        using (var db = Ctx(conn))
        {
            var (roles2, assignments2) = await RbacSeeder.SeedAsync(db, Tenant);
            Assert.Equal(0, roles2);
            Assert.Equal(0, assignments2);
        }

        using (var db = Ctx(conn))
        {
            var svc = new EffectivePermissionsService(db);
            var atTill = await svc.ResolveAsync(emp, ScopeNode.Till(TillA1), DateTime.Now);

            Assert.Contains(atTill, p => p.Code == PermissionCatalogue.PosSell);                  // Cashier
            Assert.Contains(atTill, p => p.Code == PermissionCatalogue.PortalTillsEnrol);         // Company Admin
            // Company Admin grants unlimited refunds, which beats Refund20's £20 ceiling
            Assert.Null(Assert.Single(atTill, p => p.Code == PermissionCatalogue.PosRefund).MaxPence);
        }

        // a cashier-only employee keeps the £20 ceiling from Refund20
        var emp2 = Guid.NewGuid();
        using (var ctx = Ctx(conn))
        {
            ctx.EmpAuthActions.AddRange(
                new Emp_AuthActions { EmpId = emp2, AuthAId = 1 },
                new Emp_AuthActions { EmpId = emp2, AuthAId = 2 });
            ctx.SaveChanges();
        }
        using (var db = Ctx(conn))
        {
            await RbacSeeder.SeedAsync(db, Tenant);
            var svc = new EffectivePermissionsService(db);
            var atTill = await svc.ResolveAsync(emp2, ScopeNode.Till(TillA1), DateTime.Now);
            Assert.Equal(2000L, Assert.Single(atTill, p => p.Code == PermissionCatalogue.PosRefund).MaxPence);
            Assert.DoesNotContain(atTill, p => p.Code == PermissionCatalogue.PortalUsersManage);
        }
    }

    [Fact]
    public async Task BuiltIn_roles_grant_customers_manage_to_managers_and_supervisors_not_cashier()
    {
        using var conn = OpenSeeded();
        using (var db = Ctx(conn))
            await RbacSeeder.EnsureBuiltInRolesAsync(db, Tenant);

        using var ctx = Ctx(conn);
        var roles = await ctx.RbacRoles.Include(r => r.Grants)
            .Where(r => r.TenantId == Tenant).ToDictionaryAsync(r => r.Name);

        bool Has(string role) =>
            roles[role].Grants.Any(g => g.PermissionCode == PermissionCatalogue.CustomersManage);

        // supervisor/manager surfaces carry it (loyalty usability decision)…
        Assert.True(Has("Owner"));
        Assert.True(Has("Company Admin"));
        Assert.True(Has("Store Manager"));
        Assert.True(Has("Supervisor"));
        // …the front-line cashier does not.
        Assert.False(Has("Cashier"));

        // and it is a known catalogue code (else grants are rejected at write time)
        Assert.True(PermissionCatalogue.IsKnown(PermissionCatalogue.CustomersManage));
    }

    /// <summary>FE5.3: inventory.bulk is a management-only permission — a single call can move
    /// thousands of items, so it must NOT reach Supervisor or Cashier.</summary>
    [Fact]
    public async Task BuiltIn_roles_grant_inventory_bulk_to_managers_only()
    {
        using var conn = OpenSeeded();
        using (var db = Ctx(conn))
            await RbacSeeder.EnsureBuiltInRolesAsync(db, Tenant);

        using var ctx = Ctx(conn);
        var roles = await ctx.RbacRoles.Include(r => r.Grants)
            .Where(r => r.TenantId == Tenant).ToDictionaryAsync(r => r.Name);

        bool Has(string role) =>
            roles[role].Grants.Any(g => g.PermissionCode == PermissionCatalogue.InventoryBulk);

        Assert.True(Has("Owner"));
        Assert.True(Has("Company Admin"));
        Assert.True(Has("Store Manager"));
        Assert.False(Has("Supervisor"));
        Assert.False(Has("Cashier"));
        Assert.True(PermissionCatalogue.IsKnown(PermissionCatalogue.InventoryBulk));
    }

    [Fact]
    public void ScopeNode_parses_the_endpoint_scope_format()
    {
        Assert.True(ScopeNode.TryParse(null, out var t) && t == ScopeNode.Tenant);
        Assert.True(ScopeNode.TryParse("tenant", out _));
        Assert.True(ScopeNode.TryParse($"company:{BusinessId}", out var c) && c.Type == RbacScopeType.Company);
        Assert.True(ScopeNode.TryParse("store:7", out var s) && s.Id == "7");
        Assert.True(ScopeNode.TryParse($"till:{TillA1}", out var till) && till.Type == RbacScopeType.Till);
        Assert.False(ScopeNode.TryParse("store:abc", out _));
        Assert.False(ScopeNode.TryParse("warehouse:1", out _));
    }
}
