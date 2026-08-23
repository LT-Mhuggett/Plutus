#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// The Identity-side half of <see cref="ITenantRoleProvisioner"/> — see that interface for why
    /// this indirection exists at all.
    ///
    /// ⚠ It is a thin wrapper over `RbacSeeder.EnsureBuiltInRolesAsync` plus ONE assignment, and
    /// deliberately so: the role catalogue stays in exactly one place.
    /// </summary>
    public sealed class TenantRoleProvisioner : ITenantRoleProvisioner
    {
        /// <summary>⚠ The role a tenant's first admin gets. **Owner**, not Company Admin — they carry
        /// identical grants today, but Owner is the one that reads as "this is the account the shop
        /// was created with", and the two are free to diverge later.</summary>
        public const string OwnerRole = "Owner";

        private readonly MySqlDbContext _db;

        /// <summary>⚠ The SCOPED context, so this joins the provisioning transaction rather than
        /// opening its own. A tenant that exists with no Owner is the half-built state this whole
        /// class was added to prevent, and a second connection would make it reachable.</summary>
        public TenantRoleProvisioner(MySqlDbContext db) => _db = db;

        public async Task EnsureRolesAndOwnerAsync(Guid tenantId, Guid companyId, Guid adminUserId)
        {
            if (tenantId == Guid.Empty) throw new ArgumentException("tenantId is required.", nameof(tenantId));
            if (companyId == Guid.Empty) throw new ArgumentException("companyId is required.", nameof(companyId));
            if (adminUserId == Guid.Empty) throw new ArgumentException("adminUserId is required.", nameof(adminUserId));

            await RbacSeeder.EnsureBuiltInRolesAsync(_db, tenantId);

            // ⚠ IgnoreQueryFilters is NOT used and must not be: this reads the tenant's OWN roles by
            // an explicit TenantId, which is the safe direction. The danger here is the opposite one
            // — an unscoped context matching another tenant's row — and the explicit predicate is
            // what rules that out.
            var owner = await _db.RbacRoles
                .Where(r => r.TenantId == tenantId && r.Name == OwnerRole)
                .Select(r => new { r.Id })
                .FirstOrDefaultAsync();

            // ⚠ EnsureBuiltInRolesAsync SaveChanges'd above, but only `if (HasChanges())`. On a
            // re-run with the roles already present it saves nothing, so the row may be in the
            // change tracker rather than the database on the first pass — check Local too, or a
            // freshly seeded tenant gets no Owner and the re-run silently "succeeds".
            var ownerId = owner?.Id ?? _db.RbacRoles.Local
                .FirstOrDefault(r => r.TenantId == tenantId && r.Name == OwnerRole)?.Id;

            if (ownerId is null)
                throw new InvalidOperationException(
                    $"Tenant {tenantId} has no '{OwnerRole}' role after seeding. Provisioning cannot "
                    + "leave an admin with no way to configure the shop, so this fails loudly rather "
                    + "than producing another empty tenant.");

            // ⚠⚠ COMPANY SCOPE, FROM THE PASSED-IN companyId. `MapKapowAuthActionsAsync` takes the
            // FIRST Business row it finds, which is correct under a tenant-scoped filter and very
            // wrong here — provisioning runs unscoped, so "first" could be another tenant's company
            // and the assignment would be a silent cross-tenant grant.
            var scopeId = companyId.ToString("D").ToLowerInvariant();

            var already = await _db.RbacRoleAssignments.AnyAsync(a =>
                a.TenantId == tenantId && a.UserId == adminUserId &&
                a.RoleId == ownerId.Value && a.ScopeType == RbacScopeType.Company && a.ScopeId == scopeId);

            if (already) return;

            _db.RbacRoleAssignments.Add(new RbacRoleAssignment
            {
                Id = Uuid7.New(),
                TenantId = tenantId,
                UserId = adminUserId,
                RoleId = ownerId.Value,
                ScopeType = RbacScopeType.Company,
                ScopeId = scopeId,
                CreatedAtUtc = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync();
        }
    }
}
