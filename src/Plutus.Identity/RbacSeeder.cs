#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// WP3.1 seeds. Two idempotent steps (re-run = no-op):
    ///  1. Built-in roles per tenant (architecture §7.2): Owner, Company Admin, Store Manager,
    ///     Supervisor, Cashier, Auditor — plus the Kapow-shaped capability roles the legacy
    ///     AuthActions map onto (refund ceilings, stock &amp; items, staff admin).
    ///  2. Kapow mapping (WP3.1 DoD): each employee's legacy AuthActions become RoleAssignments
    ///     at COMPANY scope — refund actions carry their pence ceiling from AuthActions.Amount
    ///     (Refund20 → pos.refund.max:2000; "Refund Unlimited" → no ceiling).
    /// Run via `Plutus.SeedMigrator rbac --mysql "&lt;conn&gt;"` at deploy; provisioning calls
    /// EnsureBuiltInRolesAsync for new tenants.
    /// </summary>
    public static class RbacSeeder
    {
        // Legacy AuthActions.Name → what it grants. Force-logout actions have no platform
        // equivalent yet (till sessions arrive with the MAUI fleet work) and are skipped.
        private static readonly Dictionary<string, string> ActionToRole = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = "Company Admin",
            ["Management"] = "Company Admin",
            ["Till"] = "Cashier",
            ["Report"] = "Auditor",
            ["Item"] = "Stock & Items",
            ["Staff"] = "Staff Admin",
        };

        public static async Task<(int Roles, int Assignments)> SeedAsync(MySqlDbContext db, Guid tenantId, string actingUser = "rbac-seeder")
        {
            db.CurrentUser = actingUser;
            var rolesAdded = await EnsureBuiltInRolesAsync(db, tenantId);
            var assignmentsAdded = await MapKapowAuthActionsAsync(db, tenantId);
            return (rolesAdded, assignmentsAdded);
        }

        /// <summary>Creates any missing built-in roles for the tenant, and ADDS any template
        /// grants an existing built-in is missing (how new catalogue permissions — e.g.
        /// WP3.2's portal.company.manage — reach already-seeded tenants on redeploy). Grants a
        /// tenant added or re-ceilinged are never removed or overwritten.</summary>
        public static async Task<int> EnsureBuiltInRolesAsync(MySqlDbContext db, Guid tenantId)
        {
            var wanted = BuiltInRoles();
            var existing = await db.RbacRoles.Include(r => r.Grants)
                .Where(r => r.TenantId == tenantId).ToDictionaryAsync(r => r.Name);
            var added = 0;

            foreach (var (name, grants) in wanted)
            {
                if (existing.TryGetValue(name, out var current))
                {
                    if (!current.IsBuiltIn) continue; // a tenant-defined role shadowing the name
                    foreach (var g in grants.Where(g => current.Grants.All(x => x.PermissionCode != g.Code)))
                        db.RbacRoleGrants.Add(new RbacRoleGrant
                        {
                            Id = Uuid7.New(), TenantId = tenantId, RoleId = current.Id,
                            PermissionCode = g.Code, MaxPence = g.MaxPence,
                        });
                    continue;
                }
                var role = new RbacRole
                {
                    Id = Uuid7.New(), TenantId = tenantId, Name = name, IsBuiltIn = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    Grants = grants.Select(g => new RbacRoleGrant
                    {
                        Id = Uuid7.New(), TenantId = tenantId,
                        PermissionCode = g.Code, MaxPence = g.MaxPence,
                    }).ToList(),
                };
                db.RbacRoles.Add(role);
                added++;
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
            return added;
        }

        private static List<(string Name, List<EffectivePermission> Grants)> BuiltInRoles()
        {
            var allPortal = new[]
            {
                PermissionCatalogue.PortalFinancialsView, PermissionCatalogue.PortalUsersManage,
                PermissionCatalogue.PortalStockAdjust, PermissionCatalogue.PortalPricesManage,
                PermissionCatalogue.PortalTillsEnrol, PermissionCatalogue.PortalReportsView,
                PermissionCatalogue.PortalCompanyManage,
            };
            var allPos = new[]
            {
                PermissionCatalogue.PosSell, PermissionCatalogue.PosRefund, PermissionCatalogue.PosVoid,
                PermissionCatalogue.PosDiscount, PermissionCatalogue.PosPriceOverride,
                PermissionCatalogue.PosNoSale, PermissionCatalogue.PosReportsView,
            };
            static List<EffectivePermission> G(params string[] codes) =>
                codes.Select(c => new EffectivePermission(c, null)).ToList();

            // Loyalty usability: customer management is a supervisor/manager capability on both
            // surfaces — granted to Owner, Company Admin, Store Manager and Supervisor, never the
            // front-line Cashier. EnsureBuiltInRolesAsync backfills it onto already-seeded tenants.
            // WP6.3: pos.settings.manage → Owner / Company Admin / Store Manager (device settings);
            // support.tickets → EVERY role (appended below).
            var roles = new List<(string, List<EffectivePermission>)>
            {
                // FE5.3: inventory.bulk → Owner / Company Admin / Store Manager only (a single
                // bulk action can move thousands of items, so it stays off Supervisor downwards).
                ("Owner", G(allPortal.Concat(allPos).Append(PermissionCatalogue.CustomersManage)
                    .Append(PermissionCatalogue.PosSettingsManage).Append(PermissionCatalogue.InventoryBulk).ToArray())),
                ("Company Admin", G(allPortal.Concat(allPos).Append(PermissionCatalogue.CustomersManage)
                    .Append(PermissionCatalogue.PosSettingsManage).Append(PermissionCatalogue.InventoryBulk).ToArray())),
                ("Store Manager", G(new[]
                {
                    PermissionCatalogue.PortalFinancialsView, PermissionCatalogue.PortalReportsView,
                    PermissionCatalogue.PortalStockAdjust, PermissionCatalogue.PortalTillsEnrol,
                    PermissionCatalogue.CustomersManage, PermissionCatalogue.PosSettingsManage,
                    PermissionCatalogue.InventoryBulk,
                }.Concat(allPos).ToArray())),
                ("Supervisor", new List<EffectivePermission>
                {
                    new(PermissionCatalogue.PosSell, null),
                    new(PermissionCatalogue.PosRefund, 10_000),   // £100 without a manager
                    new(PermissionCatalogue.PosVoid, null),
                    new(PermissionCatalogue.PosDiscount, null),
                    new(PermissionCatalogue.PosPriceOverride, null),
                    new(PermissionCatalogue.PosNoSale, null),
                    new(PermissionCatalogue.PosReportsView, null),
                    new(PermissionCatalogue.CustomersManage, null),
                }),
                ("Cashier", G(PermissionCatalogue.PosSell)),
                ("Auditor", G(PermissionCatalogue.PortalFinancialsView, PermissionCatalogue.PortalReportsView,
                              PermissionCatalogue.PosReportsView)),
                ("Stock & Items", G(PermissionCatalogue.PortalStockAdjust, PermissionCatalogue.PortalPricesManage)),
                ("Staff Admin", G(PermissionCatalogue.PortalUsersManage)),
            };

            // WP6.3: everyone can raise/read support tickets (a lone cashier with a dead till must
            // be able to shout for help). Backfilled onto already-seeded roles by EnsureBuiltInRolesAsync.
            foreach (var (_, grants) in roles)
                if (grants.All(x => x.Code != PermissionCatalogue.SupportTickets))
                    grants.Add(new EffectivePermission(PermissionCatalogue.SupportTickets, null));

            return roles;
        }

        /// <summary>Maps every employee's legacy AuthActions to role assignments at COMPANY
        /// scope (the legacy model had no finer scoping). Refund actions become per-ceiling
        /// roles ("Refunds to £20" = pos.refund.max:2000 from AuthActions.Amount; an action
        /// named "Refund Unlimited" or zero/negative amount = unlimited).</summary>
        public static async Task<int> MapKapowAuthActionsAsync(MySqlDbContext db, Guid tenantId)
        {
            var businessId = await db.Business.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync();
            if (businessId == Guid.Empty) return 0;
            var companyScope = new ScopeNode(RbacScopeType.Company, businessId.ToString("D").ToLowerInvariant());

            var links = await (
                from ea in db.EmpAuthActions.AsNoTracking()
                join a in db.AuthActions.AsNoTracking() on ea.AuthAId equals a.Id
                select new { ea.EmpId, a.Name, a.Amount }).ToListAsync();

            var rolesByName = await db.RbacRoles.Include(r => r.Grants)
                .Where(r => r.TenantId == tenantId).ToDictionaryAsync(r => r.Name);
            var existing = await db.RbacRoleAssignments
                .Where(x => x.TenantId == tenantId)
                .Select(x => new { x.UserId, x.RoleId, x.ScopeType, x.ScopeId }).ToListAsync();
            var seen = existing.Select(x => (x.UserId, x.RoleId, x.ScopeType, x.ScopeId)).ToHashSet();

            var added = 0;
            foreach (var link in links)
            {
                RbacRole role = null;

                if (link.Name != null && link.Name.StartsWith("Refund", StringComparison.OrdinalIgnoreCase))
                {
                    var unlimited = link.Name.Contains("Unlimited", StringComparison.OrdinalIgnoreCase) || link.Amount <= 0;
                    long? ceiling = unlimited ? null : (long)Math.Round(link.Amount * 100);
                    var roleName = unlimited ? "Refunds (unlimited)" : $"Refunds to £{link.Amount:0.##}";
                    if (!rolesByName.TryGetValue(roleName, out role))
                    {
                        role = new RbacRole
                        {
                            Id = Uuid7.New(), TenantId = tenantId, Name = roleName, IsBuiltIn = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            Grants = { new RbacRoleGrant { Id = Uuid7.New(), TenantId = tenantId, PermissionCode = PermissionCatalogue.PosRefund, MaxPence = ceiling } },
                        };
                        db.RbacRoles.Add(role);
                        rolesByName[roleName] = role;
                    }
                }
                else if (link.Name != null && ActionToRole.TryGetValue(link.Name, out var mapped))
                {
                    rolesByName.TryGetValue(mapped, out role);
                }

                if (role == null) continue; // unmapped legacy action (e.g. force-logout)

                var key = (link.EmpId, role.Id, RbacScopeType.Company, companyScope.Id);
                if (seen.Contains(key)) continue;
                seen.Add(key);
                db.RbacRoleAssignments.Add(new RbacRoleAssignment
                {
                    Id = Uuid7.New(), TenantId = tenantId, UserId = link.EmpId, RoleId = role.Id,
                    ScopeType = RbacScopeType.Company, ScopeId = companyScope.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                added++;
            }

            if (added > 0 || db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
            return added;
        }
    }
}
