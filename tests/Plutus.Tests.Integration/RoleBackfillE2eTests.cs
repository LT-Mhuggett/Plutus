using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// How a NEW permission reaches a tenant that was seeded months ago.
///
/// ⚠ WHY THIS MATTERS, and why it is now a startup task rather than a tool somebody runs. Matt,
/// 2026-08-11: *"why do I need to run this? Is this not something that can be added when the app is
/// compiled, or pushed from the back end?"*
///
/// Adding a permission is TWO things and only one is code: the CATALOGUE entry compiles into the
/// binary, but the GRANT — which role holds it — is a ROW in each tenant's database. A compiler
/// cannot write rows. So it cannot happen at compile time; it CAN be pushed from the backend, and
/// `RolePermissionReconciler` now does that on every boot.
///
/// These tests pin the three properties that make running it automatically SAFE. If any of them
/// stops holding, a boot could quietly take a permission away from a live shop.
/// </summary>
public class RoleBackfillE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public RoleBackfillE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    private MySqlDbContext Db(IServiceScope scope)
    {
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "role-backfill-test";
        return db;
    }

    private static async Task<RbacRole> RoleAsync(MySqlDbContext db, string name) =>
        await db.RbacRoles.Include(r => r.Grants).FirstAsync(r => r.Name == name);

    [Fact]
    public async Task A_role_seeded_BEFORE_a_permission_existed_gains_it_on_the_next_reconcile()
    {
        // ⚠ THE WHOLE POINT. This is what `pos.stock.adjust` needed on 2026-08-11 and what every
        // future permission will need: the grant did not exist when Kapow's roles were created, and
        // no amount of redeploying a binary puts a row in a table.
        using var scope = _f.Services.CreateScope();
        var db = Db(scope);

        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        // Simulate the "seeded before the permission existed" state by removing the grant.
        var supervisor = await RoleAsync(db, "Supervisor");
        var existing = supervisor.Grants
            .Where(g => g.PermissionCode == PermissionCatalogue.PosStockAdjust).ToList();
        if (existing.Count > 0)
        {
            db.RbacRoleGrants.RemoveRange(existing);
            await db.SaveChangesAsync();
        }

        db.ChangeTracker.Clear();
        Assert.DoesNotContain((await RoleAsync(db, "Supervisor")).Grants,
            g => g.PermissionCode == PermissionCatalogue.PosStockAdjust);

        // The reconcile — exactly what the startup task calls.
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        db.ChangeTracker.Clear();
        Assert.Contains((await RoleAsync(db, "Supervisor")).Grants,
            g => g.PermissionCode == PermissionCatalogue.PosStockAdjust);
    }

    [Fact]
    public async Task A_tenants_OWN_ceiling_is_never_overwritten()
    {
        // ⚠ THE PROPERTY THAT MAKES RUNNING THIS ON EVERY BOOT SAFE. A shop that has raised its
        // Supervisor refund ceiling from the £100 default to £500 must not have it silently reset
        // by a deploy — that is money authority changing because somebody shipped a build.
        using var scope = _f.Services.CreateScope();
        var db = Db(scope);

        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        var supervisor = await RoleAsync(db, "Supervisor");
        var refund = supervisor.Grants.First(g => g.PermissionCode == PermissionCatalogue.PosRefund);
        refund.MaxPence = 50_000;                      // the shop raised it to £500
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        db.ChangeTracker.Clear();
        var after = (await RoleAsync(db, "Supervisor")).Grants
            .First(g => g.PermissionCode == PermissionCatalogue.PosRefund);

        Assert.Equal(50_000, after.MaxPence);
    }

    [Fact]
    public async Task An_EXTRA_grant_a_tenant_added_by_hand_survives()
    {
        // ⚠ Additive means additive in both directions: the reconcile adds what is missing from the
        // template, and removes NOTHING. A shop that gave its Cashiers `pos.no-sale` keeps it.
        using var scope = _f.Services.CreateScope();
        var db = Db(scope);

        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        var cashier = await RoleAsync(db, "Cashier");
        if (cashier.Grants.All(g => g.PermissionCode != PermissionCatalogue.PosNoSale))
        {
            db.RbacRoleGrants.Add(new RbacRoleGrant
            {
                Id = Uuid7.New(), TenantId = Kapow, RoleId = cashier.Id,
                PermissionCode = PermissionCatalogue.PosNoSale, MaxPence = null,
            });
            await db.SaveChangesAsync();
        }

        db.ChangeTracker.Clear();
        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        db.ChangeTracker.Clear();
        Assert.Contains((await RoleAsync(db, "Cashier")).Grants,
            g => g.PermissionCode == PermissionCatalogue.PosNoSale);
    }

    [Fact]
    public async Task Running_it_TWICE_adds_nothing_the_second_time()
    {
        // ⚠ Idempotence is what lets it run on every boot rather than once. A reconcile that added
        // a duplicate row per start-up would grow the grant table for ever and, worse, make "which
        // grant is authoritative" a question with more than one answer.
        using var scope = _f.Services.CreateScope();
        var db = Db(scope);

        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);
        db.ChangeTracker.Clear();

        var added = await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        Assert.Equal(0, added);
    }

    [Fact]
    public async Task The_CASHIER_does_not_gain_stock_adjustment_from_a_reconcile()
    {
        // ⚠ A reconcile pushes the TEMPLATE, so the template is the thing that decides who gets
        // what — and if the template ever gained this for Cashier, every tenant would take it on
        // the next boot, silently, everywhere at once. That is the blast radius this test guards.
        using var scope = _f.Services.CreateScope();
        var db = Db(scope);

        await RbacSeeder.EnsureBuiltInRolesAsync(db, Kapow);

        db.ChangeTracker.Clear();
        Assert.DoesNotContain((await RoleAsync(db, "Cashier")).Grants,
            g => g.PermissionCode == PermissionCatalogue.PosStockAdjust);
    }
}
