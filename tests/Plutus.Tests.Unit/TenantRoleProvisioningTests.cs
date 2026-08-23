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
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **A provisioned tenant must arrive usable — roles seeded, admin an Owner.**
///
/// ⚠⚠ THIS IS PINNED BECAUSE IT WAS SILENTLY ABSENT AND NOTHING NOTICED FOR MONTHS. Provisioning
/// created a tenant, a Business, a Store and an admin login and stopped there. The admin could sign
/// in and then do nothing: with no role assignment `ResolveLoginScopesAsync` falls to its legacy
/// branch and returns `pos.sell` alone, so no till could be created, no user added, no company
/// managed. **`Demo Store` — 0 stores, 0 tills, 0 employees, 0 assignments — was not an abandoned
/// experiment; it was the endpoint's output.**
///
/// ⚠ `RbacSeeder`'s docstring CLAIMED provisioning called `EnsureBuiltInRolesAsync`. A docstring is
/// what failed here, so the replacement is a test.
/// </summary>
public class TenantRoleProvisioningTests
{
    private static MySqlDbContext Ctx(SqliteConnection conn, Guid tenant, string user = "test")
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(tenant)) { CurrentUser = user };

    private static async Task<(SqliteConnection conn, ProvisionResult res)> ProvisionAsync(string name = "Test Shop")
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var ctx = Ctx(conn, Guid.Empty)) ctx.Database.EnsureCreated();

        using var c = Ctx(conn, Guid.Empty, "platform-admin");   // unscoped, as the real caller is
        var res = await new ProvisioningService(c, new TenantRoleProvisioner(c))
            .ProvisionAsync(new ProvisionRequest(name, "standard", $"admin@{name.Replace(" ", "")}.test", "S3cret!"),
                            "platform-admin");
        return (conn, res);
    }

    /// <summary>⚠ The headline: the admin comes out of provisioning holding Owner at company scope.</summary>
    [Fact]
    public async Task Provisioned_admin_is_an_Owner_at_company_scope()
    {
        var (conn, res) = await ProvisionAsync();
        using var ctx = Ctx(conn, res.TenantId);

        var owner = await ctx.RbacRoles.FirstOrDefaultAsync(r => r.TenantId == res.TenantId && r.Name == "Owner");
        Assert.NotNull(owner);

        var assignment = await ctx.RbacRoleAssignments.FirstOrDefaultAsync(a =>
            a.TenantId == res.TenantId && a.UserId == res.AdminUserId && a.RoleId == owner!.Id);

        Assert.True(assignment is not null,
            "The provisioned admin has no Owner assignment. Without one ResolveLoginScopesAsync falls "
            + "to the legacy branch and hands them pos.sell alone — they can sign in and then not "
            + "create a till, add a user, or manage the company. That is the Demo Store shell.");

        Assert.Equal(RbacScopeType.Company, assignment!.ScopeType);

        // ⚠ Scoped to the company provisioning actually created — see the next test for why that
        // is asserted by ID rather than "the only company in the database".
        Assert.Equal(res.CompanyId.ToString("D").ToLowerInvariant(), assignment.ScopeId);

        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE CROSS-TENANT TRAP, PINNED. `MapKapowAuthActionsAsync` finds its company with
    /// `db.Business.First()`, which is right under a tenant-scoped filter and very wrong during
    /// provisioning — that context is UNSCOPED, so "first" is whichever row the database returns.
    /// Provision twice and the second tenant's Owner assignment must not be scoped to the first
    /// tenant's company.
    /// </summary>
    [Fact]
    public async Task A_second_tenants_owner_is_not_scoped_to_the_first_tenants_company()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var ctx = Ctx(conn, Guid.Empty)) ctx.Database.EnsureCreated();

        ProvisionResult first, second;
        using (var c = Ctx(conn, Guid.Empty, "platform-admin"))
            first = await new ProvisioningService(c, new TenantRoleProvisioner(c))
                .ProvisionAsync(new ProvisionRequest("First Shop", "standard", "a@first.test", "pw1"), "platform-admin");
        using (var c = Ctx(conn, Guid.Empty, "platform-admin"))
            second = await new ProvisioningService(c, new TenantRoleProvisioner(c))
                .ProvisionAsync(new ProvisionRequest("Second Shop", "standard", "a@second.test", "pw2"), "platform-admin");

        Assert.NotEqual(first.CompanyId, second.CompanyId);

        using var ctx2 = Ctx(conn, Guid.Empty);
        var secondAssignment = await ctx2.RbacRoleAssignments
            .FirstAsync(a => a.TenantId == second.TenantId && a.UserId == second.AdminUserId);

        Assert.Equal(second.CompanyId.ToString("D").ToLowerInvariant(), secondAssignment.ScopeId);
        Assert.NotEqual(first.CompanyId.ToString("D").ToLowerInvariant(), secondAssignment.ScopeId);

        conn.Dispose();
    }

    /// <summary>
    /// ⚠ Owner must carry `portal.tills.enrol` — the ONE permission that unblocks creating a till,
    /// and the reason the old chain stalled. ⚠⚠ It is a scope policy (`RequireClaim("scope", …)`),
    /// so a platform admin does NOT satisfy it: this grant is the only route.
    /// </summary>
    [Fact]
    public async Task Owner_carries_the_permission_that_lets_a_till_be_created()
    {
        var (conn, res) = await ProvisionAsync();
        using var ctx = Ctx(conn, res.TenantId);

        var grants = await ctx.RbacRoles.Include(r => r.Grants)
            .Where(r => r.TenantId == res.TenantId && r.Name == "Owner")
            .SelectMany(r => r.Grants.Select(g => g.PermissionCode))
            .ToListAsync();

        Assert.Contains(PermissionCatalogue.PortalTillsEnrol, grants);
        Assert.Contains(PermissionCatalogue.PortalCompanyManage, grants);
        Assert.Contains(PermissionCatalogue.PortalUsersManage, grants);

        conn.Dispose();
    }

    /// <summary>
    /// ⚠ The whole point of resolving scopes: a provisioned admin must now resolve to real portal
    /// permissions, NOT the legacy `pos.sell`-only branch. This is the assertion that would have
    /// caught the original bug — the others describe rows, this one describes the CONSEQUENCE.
    /// </summary>
    [Fact]
    public async Task Provisioned_admin_resolves_to_portal_scopes_not_the_legacy_pos_sell_fallback()
    {
        var (conn, res) = await ProvisionAsync();
        using var ctx = Ctx(conn, res.TenantId);

        var scopes = await new EffectivePermissionsService(ctx)
            .ResolveLoginScopesAsync(res.AdminUserId, DateTime.Now);

        Assert.Contains(PermissionCatalogue.PortalTillsEnrol, scopes);
        Assert.True(scopes.Count > 1,
            "The admin resolved to a single scope, which is the legacy no-assignments branch "
            + $"returning pos.sell alone. Got: {string.Join(", ", scopes)}");

        conn.Dispose();
    }

    /// <summary>⚠ Idempotent — provisioning is not re-run, but the seeder is, and a second call
    /// must not produce a duplicate Owner assignment.</summary>
    [Fact]
    public async Task Re_running_the_provisioner_adds_no_duplicate_assignment()
    {
        var (conn, res) = await ProvisionAsync();

        using (var c = Ctx(conn, Guid.Empty, "platform-admin"))
            await new TenantRoleProvisioner(c).EnsureRolesAndOwnerAsync(res.TenantId, res.CompanyId, res.AdminUserId);

        using var ctx = Ctx(conn, res.TenantId);
        var count = await ctx.RbacRoleAssignments
            .CountAsync(a => a.TenantId == res.TenantId && a.UserId == res.AdminUserId);
        Assert.Equal(1, count);

        conn.Dispose();
    }

    /// <summary>
    /// ⚠⚠ THE DEPENDENCY IS REQUIRED, AND THAT IS LOAD-BEARING. Making it optional would let the
    /// original bug back in silently — a tenant provisioned with no roles looks identical to a
    /// working one until somebody tries to use it.
    /// </summary>
    [Fact]
    public void Provisioning_cannot_be_constructed_without_a_role_provisioner()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = Ctx(conn, Guid.Empty);
        Assert.Throws<ArgumentNullException>(() => new ProvisioningService(ctx, null!));
        conn.Dispose();
    }
}
