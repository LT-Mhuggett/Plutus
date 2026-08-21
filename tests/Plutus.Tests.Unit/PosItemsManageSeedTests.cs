using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// `pos.items.manage` and the **"Add/edit stock"** role — Matt, 2026-08-21.
///
/// Two asks, one day apart, and they are answered by the same pair of codes:
///
///  1. *"I also need editing of items to be a supervisor and above permission across all tills."*
///  2. *"I think I need a 'Add/edit stock' permission. Which can be turned on for individuals. So long
///     as all edits to stock items are tracked for each item (History)."*
///
/// ⚠⚠ THE SECOND NEEDED NO NEW MECHANISM, WHICH IS THE FINDING. In this model what you grant to a
/// PERSON is a role, and role grants **union** — so a capability role holding exactly these two codes,
/// assigned to one cashier in the portal's Users → Access screen, IS "turned on for individuals".
///
/// ⚠ It carries the **till** codes on purpose. The pre-existing "Stock & Items" role grants
/// `portal.stock.adjust` + `portal.prices.manage`, which also carry the central price list and
/// category create/delete — so granting THAT to one cashier would hand them the portal.
/// </summary>
public class PosItemsManageSeedTests
{
    /// <summary>
    /// ⚠ Reads the SEED TEMPLATE, which is what actually reaches a tenant — `EnsureBuiltInRolesAsync`
    /// backfills any grant a built-in role is missing. Asserting against a hand-written list here
    /// would pin the test to itself.
    /// </summary>
    private static IReadOnlyList<string> GrantsFor(string role)
    {
        var method = typeof(RbacSeeder).GetMethod("BuiltInRoles",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var roles = (System.Collections.IEnumerable)method!.Invoke(null, null)!;
        foreach (var entry in roles)
        {
            // ⚠ ValueTuple exposes Item1/Item2 as FIELDS, not properties.
            var name = (string)entry!.GetType().GetField("Item1")!.GetValue(entry)!;
            if (!string.Equals(name, role, StringComparison.Ordinal)) continue;

            var grants = (System.Collections.IEnumerable)entry.GetType().GetField("Item2")!.GetValue(entry)!;
            return grants.Cast<object>()
                .Select(g => (string)g.GetType().GetProperty("Code")!.GetValue(g)!)
                .ToList();
        }

        throw new Xunit.Sdk.XunitException($"No built-in role named '{role}'");
    }

    // ── the permission ──

    [Theory]
    [InlineData("Owner")]
    [InlineData("Company Admin")]
    [InlineData("Store Manager")]
    [InlineData("Supervisor")]
    public void Supervisor_and_above_may_edit_an_item(string role)
    {
        Assert.Contains(PermissionCatalogue.PosItemsManage, GrantsFor(role));
    }

    /// <summary>
    /// ⚠⚠ THE LOAD-BEARING HALF. A price is what the customer is charged; a cashier changing one
    /// unsupervised is a discount with no reason, no ceiling and no audit row. If this ever passes,
    /// the ruling has been undone.
    /// </summary>
    [Fact]
    public void A_CASHIER_cannot_by_role()
    {
        Assert.DoesNotContain(PermissionCatalogue.PosItemsManage, GrantsFor("Cashier"));
    }

    [Fact]
    public void The_code_is_in_the_catalogue_or_every_grant_of_it_is_rejected_at_write_time()
    {
        // ⚠ Unknown codes are refused when a grant is saved, so a permission missing from `All` is one
        // that cannot be granted at all — and the seeder would fail silently on that role.
        Assert.Contains(PermissionCatalogue.PosItemsManage, PermissionCatalogue.All);
    }

    [Fact]
    public void It_is_NOT_ceiling_capable()
    {
        // Editing an item has no money amount, so a pence ceiling would be meaningless — and a
        // meaningless ceiling in the role editor invites somebody to set one and believe it.
        Assert.DoesNotContain(PermissionCatalogue.PosItemsManage, PermissionCatalogue.CeilingCapable);
    }

    [Fact]
    public void It_has_a_description_or_the_role_editor_shows_a_bare_code()
    {
        Assert.True(PermissionCatalogue.Descriptions.ContainsKey(PermissionCatalogue.PosItemsManage),
            "A permission with no description renders as a raw code in the portal's role editor, and "
            + "an owner cannot grant safely what nobody has described.");
    }

    // ── the "Add/edit stock" role: what Matt asked to be able to switch on per person ──

    /// <summary>
    /// ⚠⚠ EXACTLY TWO CODES, AND THAT IS THE TEST. The whole reason this role exists rather than
    /// reusing "Stock & Items" is that the latter also carries the portal price list and category
    /// create/delete. If a portal code ever appears here, turning this on for one cashier quietly
    /// hands them the back office.
    /// </summary>
    [Fact]
    public void The_Add_edit_stock_role_grants_the_TILL_codes_and_nothing_else()
    {
        var grants = GrantsFor("Add/edit stock");

        Assert.Contains(PermissionCatalogue.PosItemsManage, grants);
        Assert.Contains(PermissionCatalogue.PosStockAdjust, grants);

        Assert.DoesNotContain(PermissionCatalogue.PortalPricesManage, grants);
        Assert.DoesNotContain(PermissionCatalogue.PortalStockAdjust, grants);
        Assert.DoesNotContain(PermissionCatalogue.InventoryBulk, grants);
    }

    /// <summary>
    /// ⚠ `support.tickets` is appended to EVERY built-in role by the seeder (a lone cashier with a
    /// dead till must be able to shout for help), so it is expected here and is not a leak. Stated so
    /// the assertion above is not "fixed" by somebody who finds a third code and panics.
    /// </summary>
    [Fact]
    public void Support_tickets_rides_along_and_that_is_deliberate()
    {
        Assert.Contains(PermissionCatalogue.SupportTickets, GrantsFor("Add/edit stock"));
    }

    /// <summary>
    /// ⚠⚠ THE ROLE IS THE MECHANISM. Matt asked for something switchable per individual; grants union
    /// across a user's roles, so Cashier + this = a cashier who may do stock and nobody else changes.
    /// This pins that the two are DISJOINT — if the Cashier template ever gained these codes, the
    /// individual grant would stop being individual.
    /// </summary>
    [Fact]
    public void It_adds_to_a_Cashier_rather_than_overlapping_one()
    {
        var cashier = GrantsFor("Cashier");
        var addEdit = GrantsFor("Add/edit stock");

        Assert.DoesNotContain(PermissionCatalogue.PosItemsManage, cashier);
        Assert.DoesNotContain(PermissionCatalogue.PosStockAdjust, cashier);
        Assert.Contains(PermissionCatalogue.PosItemsManage, addEdit);
        Assert.Contains(PermissionCatalogue.PosStockAdjust, addEdit);
    }

    /// <summary>
    /// ⚠ "Stock & Items" is deliberately LEFT AS IT WAS — it is what `MapKapowAuthActionsAsync` maps
    /// the legacy `AuthActions["Item"]` onto, so narrowing it would strip capability from every
    /// employee already mapped to it. ⚠ And it could not be narrowed anyway:
    /// `EnsureBuiltInRolesAsync` only ever ADDS template grants, so removing a code changes new
    /// tenants and not existing ones — the worst of both.
    /// </summary>
    [Fact]
    public void The_legacy_Stock_and_Items_role_is_untouched()
    {
        var grants = GrantsFor("Stock & Items");

        Assert.Contains(PermissionCatalogue.PortalStockAdjust, grants);
        Assert.Contains(PermissionCatalogue.PortalPricesManage, grants);
    }
}
