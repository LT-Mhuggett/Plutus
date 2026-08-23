using System;
using System.Threading.Tasks;

namespace Plutus.SharedKernel;

/// <summary>
/// **Gives a newly provisioned tenant its built-in roles, and makes its first admin an Owner.**
///
/// ⚠⚠ THIS EXISTS BECAUSE PROVISIONING SILENTLY DID NOT DO IT — traced 2026-08-23. `ProvisionAsync`
/// created a tenant, a Business, a Store and an admin login, and stopped. No roles, no assignment.
/// So the new admin signed in holding `pos.sell` and nothing else (`EffectivePermissionsService`
/// falls to the legacy branch when a user has no assignments, and a fresh tenant has no
/// `EmpAuthActions` rows to make them a legacy admin either) — unable to create a till, add a user,
/// or manage the company.
///
/// ⚠⚠ AND THAT IS WHY `Demo Store` WAS AN EMPTY SHELL: 0 stores, 0 tills, 0 employees, 0 role
/// assignments was not an abandoned experiment, it was what the endpoint produced.
///
/// ⚠ `RbacSeeder`'s own docstring already CLAIMED provisioning called it. It did not. The interface
/// is here rather than the call being direct because **`Plutus.Tenancy` must not reference
/// `Plutus.Identity`** — the same boundary that put `Pbkdf2` in `Crypto.cs`. ⚠⚠ Do NOT close this by
/// copying the role catalogue into Tenancy: two lists of what an Owner may do is a C2-shaped drift
/// with PERMISSIONS as the thing that drifts, and nothing would flag the day they disagreed.
/// </summary>
public interface ITenantRoleProvisioner
{
    /// <summary>
    /// Seed the tenant's built-in roles and assign <paramref name="adminUserId"/> the **Owner** role
    /// at company scope. Idempotent — safe to re-run against a tenant that already has both.
    ///
    /// ⚠ <paramref name="companyId"/> IS PASSED IN, NOT LOOKED UP. Provisioning runs on an
    /// **unscoped** platform-admin context where "the first Business row" is whichever tenant's
    /// happens to come back first — a role assignment scoped to the wrong company is a silent
    /// cross-tenant grant, and the ambient filter is not there to catch it.
    ///
    /// ⚠ Must join the caller's transaction rather than opening its own: provisioning is one
    /// transaction, and a tenant that exists with no Owner is the exact half-built state this
    /// interface was added to prevent.
    /// </summary>
    Task EnsureRolesAndOwnerAsync(Guid tenantId, Guid companyId, Guid adminUserId);
}
