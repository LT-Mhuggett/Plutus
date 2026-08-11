using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Tenancy;

namespace Plutus.Identity
{
    /// <summary>
    /// Brings every tenant's built-in roles up to date with the CODE-DEFINED permission catalogue,
    /// once, at start-up.
    ///
    /// ⚠ WHY THIS EXISTS. Matt, 2026-08-11: *"why do I need to run this? Is this not something that
    /// can be added when the app is compiled, or pushed from the back end?"* — and he was right.
    ///
    /// Adding a permission is TWO things, and only one of them is code:
    ///   • the CATALOGUE entry is compiled in (`PermissionCatalogue`), so it ships with the binary;
    ///   • the GRANT — which role holds it — is a ROW in each tenant's `RbacRoleGrants`. Rows are
    ///     data. A compiler cannot write them, because they live in a database the compiler has
    ///     never seen and which each tenant may have customised.
    ///
    /// So it cannot be "added at compile time". It CAN be pushed from the back end, which is what
    /// this does — and until now was not. `EnsureBuiltInRolesAsync`'s own doc comment describes
    /// itself as *"how new catalogue permissions reach already-seeded tenants on redeploy"*, but
    /// nothing called it on redeploy: it ran only from `Plutus.SeedMigrator`, a tool somebody had
    /// to remember. That is a step that gets forgotten, and its failure is invisible — the
    /// permission simply does not exist, so every operator is refused politely and the message
    /// reads like deliberate policy.
    ///
    /// ⚠ SAFE TO RUN EVERY BOOT, and that is what makes this the right place for it:
    ///   • **Additive only.** `EnsureBuiltInRolesAsync` ADDS grants a built-in role is missing. It
    ///     never removes one and never overwrites a ceiling — a tenant that has re-ceilinged
    ///     `pos.refund` to £50 keeps £50.
    ///   • **It skips tenant-defined roles** that shadow a built-in name.
    ///   • **Idempotent.** A second run finds nothing missing and writes nothing.
    ///
    /// ⚠ IT MUST NEVER STOP THE HOST. A tenant table that cannot be read, a migration mid-flight, a
    /// database briefly unreachable — none of those are a reason for a backend not to serve tills.
    /// Every failure is logged and swallowed, and the worst case is the old behaviour: run the tool.
    ///
    /// ⚠ IT RUNS ONCE, not on a timer. The catalogue is compiled in, so it cannot change while the
    /// process is alive — a loop would re-ask a question whose answer cannot have moved.
    /// </summary>
    public sealed class RolePermissionReconciler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<RolePermissionReconciler> _logger;

        public RolePermissionReconciler(IServiceScopeFactory scopes, ILogger<RolePermissionReconciler> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // ⚠ Yield first so a slow or failing reconcile cannot delay the host coming up. A
            // BackgroundService that blocks in ExecuteAsync blocks start-up on some hosts, and a
            // backend that will not start is far worse than one whose roles are a minute stale.
            await Task.Yield();

            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                if (db is null)
                {
                    // The SQLite dev host — nothing to reconcile against.
                    return;
                }

                var tenants = await TenantIdsAsync(db, stoppingToken);
                if (tenants.Count == 0) return;

                var added = 0;
                foreach (var tenantId in tenants)
                {
                    if (stoppingToken.IsCancellationRequested) return;

                    try
                    {
                        // ⚠ A FRESH CONTEXT PER TENANT. `MySqlDbContext` carries an ambient tenant
                        // for its global query filters — reusing one scoped to tenant A while
                        // seeding tenant B would filter B's existing roles out and try to create
                        // them again, which is a primary-key collision at best.
                        var options = new DbContextOptionsBuilder<MySqlDbContext>()
                            .UseMySql(db.Database.GetConnectionString(),
                                      MySqlServerVersion.LatestSupportedServerVersion)
                            .Options;

                        using var tenantDb = new MySqlDbContext(options, new FixedTenantContext(tenantId));
                        tenantDb.CurrentUser = "role-reconciler";

                        added += await RbacSeeder.EnsureBuiltInRolesAsync(tenantDb, tenantId);
                    }
                    catch (Exception ex)
                    {
                        // ⚠ One bad tenant must not stop the others. In a multi-tenant estate the
                        // alternative is that a single corrupt row denies every other shop its new
                        // permissions.
                        _logger.LogError(ex, "Role reconcile failed for tenant {TenantId}.", tenantId);
                    }
                }

                // ⚠ Logged ONLY when something changed. A line on every boot saying "0 added" is a
                // line nobody reads, and this one matters on the boot after a permission is added.
                if (added > 0)
                    _logger.LogInformation(
                        "Role reconcile: added {Count} missing built-in grant(s) across {Tenants} tenant(s).",
                        added, tenants.Count);
            }
            catch (Exception ex)
            {
                // ⚠ THE HOST KEEPS RUNNING. See the header — a backend that will not serve tills
                // because it could not tidy a permission table is a worse outcome than stale roles.
                _logger.LogError(ex, "Role reconcile did not run. Roles may be missing new catalogue permissions.");
            }
        }

        /// <summary>
        /// Every tenant to reconcile.
        ///
        /// ⚠ READ FROM THE DATABASE, not from `KnownTenants`. The seeder tool hardcodes Kapow
        /// because it was written when Kapow was the only tenant; a startup task that did the same
        /// would silently skip every tenant onboarded since, and the symptom would be one shop's
        /// supervisors working and another's not.
        ///
        /// ⚠ Falls back to the founding tenant if the table cannot be read — better to reconcile
        /// the one we know than none at all.
        /// </summary>
        private async Task<List<Guid>> TenantIdsAsync(MySqlDbContext db, CancellationToken ct)
        {
            try
            {
                var ids = await db.Tenants.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Select(t => t.Id)
                    .ToListAsync(ct);

                if (ids.Count > 0) return ids;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not list tenants; reconciling the founding tenant only.");
            }

            return new List<Guid> { KnownTenants.Kapow };
        }
    }
}
