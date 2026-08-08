using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    // ⚠ Twins of Plutus.Contracts.Client.OperatorContracts — same convention as EnrolResult and the
    // rest (the contracts project ships onto tills, so no backend module references it). Change one
    // shape, change both. Recorded in till-design C2.

    public sealed record OperatorGrantDto(
        string Code, long? MaxPence, DateTime? ValidFromUtc, DateTime? ValidToUtc,
        byte? DaysOfWeekMask, TimeOnly? WindowStartLocal, TimeOnly? WindowEndLocal);

    public sealed record TillOperatorDto(
        Guid UserId, string DisplayName, string? Email,
        string? CredentialHashBase64, string? CredentialSaltBase64,
        OperatorGrantDto[] Grants);

    public sealed record TillOperatorsResult(Guid TillId, DateTime AsOfUtc, TillOperatorDto[] Operators);

    /// <summary>
    /// WP8 — GET /api/v1/tills/{tillId}/operators: the roster a till caches so staff can sign in
    /// with the network off.
    ///
    /// Until this existed, an enrolled till was connected and unusable: it had a device identity
    /// and nobody to sign in as.
    /// </summary>
    [ApiController]
    public sealed class TillOperatorsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly EffectivePermissionsService _permissions;

        public TillOperatorsController(MySqlDbContext db, ITenantContext tenant, EffectivePermissionsService permissions)
        {
            _db = db;
            _tenant = tenant;
            _permissions = permissions;
        }

        /// <summary>
        /// ⚠ Gated <c>sales.ingest</c> so a DEVICE token can call it. That is deliberate and it is
        /// the sharp edge of this endpoint: the till must be able to refresh its roster before
        /// anyone has signed in — otherwise the first sign-in of the day needs a sign-in.
        /// </summary>
        [HttpGet("api/v1/tills/{tillId:guid}/operators")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get([FromRoute] Guid tillId)
        {
            var till = await _db.Till.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tillId);
            if (till == null) return NotFound(new { detail = "Till not found." });

            // Which scopes reach this till: [till, store, company, tenant].
            var chain = await _permissions.AncestorChainAsync(ScopeNode.Till(tillId));
            var chainKeys = chain.Select(c => (c.Type, c.Id)).ToHashSet();

            // ⚠ ONE query for every assignment, not one per user. ResolveAsync does a round trip per
            // user, which is fine for a single lookup and an N+1 for a roster.
            var assignments = await _db.RbacRoleAssignments.AsNoTracking()
                .Include(a => a.Role).ThenInclude(r => r.Grants)
                .Where(a => a.TenantId == _tenant.TenantId)
                .ToListAsync();

            var reachable = assignments
                .Where(a => chainKeys.Contains((a.ScopeType, a.ScopeId)))
                .ToList();

            // ⚠ WHO COUNTS AS AN OPERATOR OF THIS TILL. Company- and tenant-scope assignments are on
            // every till's chain, so "everyone RBAC-reachable" would mirror the WHOLE company roster
            // — and its password hashes — onto every counter. Narrowed to people who actually work
            // here: their home store is this till's store, OR they were granted something at this
            // till or this store specifically.
            //
            // This is a product decision as much as a technical one, and it is the one to revisit
            // first if a manager reports they cannot sign in at a shop they were covering.
            var here = new HashSet<Guid>(
                reachable.Where(a => a.ScopeType == RbacScopeType.Till || a.ScopeType == RbacScopeType.Store)
                         .Select(a => a.UserId));

            var candidateIds = reachable.Select(a => a.UserId).Distinct().ToList();

            // Employees are tenant-filtered by the global query filter; WebCredentials are NOT
            // (they are global — login is by email before a tenant is known). ⚠ So the join goes
            // through Employee, or this endpoint would hand a till another tenant's password hashes.
            var employees = await _db.Employees.AsNoTracking()
                .Where(e => candidateIds.Contains(e.Id) && e.Active)
                .Select(e => new { e.Id, e.FName, e.LName, e.Email, e.StoreId })
                .ToListAsync();

            var emails = employees.Select(e => e.Email).Where(x => x != null).ToList();
            var creds = await _db.WebCredentials.AsNoTracking()
                .Where(w => emails.Contains(w.Email))
                .Select(w => new { w.Email, w.EmployeeId, w.HashedPassword, w.Salt })
                .ToListAsync();
            var credByUser = creds
                .GroupBy(c => c.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First());

            var operators = new List<TillOperatorDto>();
            foreach (var e in employees)
            {
                if (!here.Contains(e.Id) && e.StoreId != till.StoreId) continue;

                var grants = reachable
                    .Where(a => a.UserId == e.Id)
                    .SelectMany(a => (a.Role?.Grants ?? new List<RbacRoleGrant>())
                        .Select(g => new OperatorGrantDto(
                            g.PermissionCode, g.MaxPence,
                            a.ValidFromUtc, a.ValidToUtc,
                            a.DaysOfWeekMask, a.WindowStartLocal, a.WindowEndLocal)))
                    .ToArray();

                credByUser.TryGetValue(e.Id, out var cred);

                operators.Add(new TillOperatorDto(
                    UserId: e.Id,
                    DisplayName: $"{e.FName} {e.LName}".Trim(),
                    Email: e.Email,
                    // Null = staff with no web login yet. Listed so the till can show who exists;
                    // they simply cannot sign in.
                    CredentialHashBase64: cred?.HashedPassword,
                    CredentialSaltBase64: cred?.Salt,
                    Grants: grants));
            }

            // ⚠ AsOfUtc is the SERVER's clock, and the staleness horizons are measured from it. A
            // till with a drifted clock would otherwise think its roster was fresh for ever, or
            // expire it the moment it arrived.
            return Ok(new TillOperatorsResult(tillId, DateTime.UtcNow, operators.ToArray()));
        }
    }
}
