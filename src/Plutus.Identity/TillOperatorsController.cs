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
            // ⚠⚠ STEP 28'S SERVER HALF. The till names the operators it can ALREADY verify offline
            // (it minted a device-local verifier when they last signed in online here), and this reply
            // omits their platform password hash. That hash is the operator's real platform password —
            // it works on the web till and the portal — and shipping it to every till for every member
            // of staff is what makes a stolen till worth stealing.
            //
            // ⚠⚠ NO CUTOVER DATE, AND THAT IS THE DESIGN. The ordering could not be reversed: stop
            // sending hashes before a till is minting verifiers and every operator who has not signed
            // in since is locked out — during exactly the outage that made them need the till. Letting
            // each till say what it holds makes the change self-sequencing: one (till, operator) pair
            // at a time, the sync after that operator first signs in online there.
            //
            // ⚠ EVERY FAILURE DIRECTION SHIPS THE HASH. No header, an empty header, an unparseable id,
            // an older till: all mean "holds nothing", and the roster is exactly what it always was.
            var covered = ReadHaveVerifiers();

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

            // ⚠ WHO COUNTS AS AN OPERATOR OF THIS TILL — CORRECTED 2026-08-09, and the previous
            // rule locked the tenant's OWNER out of his own till.
            //
            // It used to require that the person's home store matched this till's store (or that
            // they held a Till/Store-scoped grant). The concern behind it was real: company- and
            // tenant-scope assignments sit on every till's chain, so "everyone reachable" would
            // mirror a whole company's roster — password hashes included — onto every counter.
            //
            // But it contradicted the two things it depended on. `RbacSeeder` maps every legacy
            // employee's AuthActions to assignments at **COMPANY scope** (see its §2), and a
            // company-scoped grant means "every till in this company" by definition — that is what
            // the scope chain is for. So the filter discarded precisely what the seeder creates:
            // on a till in a store nobody is *based* at, the roster came back EMPTY, the till said
            // "no staff on this till yet", and there was no way to sign in. Found in a screen test
            // where the enrolled till sat in store 4 and both staff were based in store 1.
            //
            // The replacement keeps the privacy goal and drops the wrong instrument: ship the
            // people a grant reaching this till lets do something **at a till**, i.e. who hold any
            // `pos.*` permission here. That is strictly TIGHTER than "everyone reachable" — a
            // Staff Admin or a Stock & Items role is portal-only and never reaches a counter — and
            // it no longer silently overrides what the grant says.
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
                var grants = reachable
                    .Where(a => a.UserId == e.Id)
                    .SelectMany(a => (a.Role?.Grants ?? new List<RbacRoleGrant>())
                        .Select(g => new OperatorGrantDto(
                            g.PermissionCode, g.MaxPence,
                            a.ValidFromUtc, a.ValidToUtc,
                            a.DaysOfWeekMask, a.WindowStartLocal, a.WindowEndLocal)))
                    .ToArray();

                // ⚠ The gate. Someone with no `pos.*` permission cannot do anything at a counter,
                // so putting their name — and their password hash — on one is pure exposure for no
                // capability. Prefix test matches `PermissionCatalogue.GroupOf`, which already
                // treats "pos." as the till family.
                if (!grants.Any(g => g.Code != null
                        && g.Code.StartsWith("pos.", StringComparison.Ordinal)))
                    continue;

                credByUser.TryGetValue(e.Id, out var cred);

                operators.Add(new TillOperatorDto(
                    UserId: e.Id,
                    DisplayName: $"{e.FName} {e.LName}".Trim(),
                    Email: e.Email,
                    // Null = staff with no web login yet. Listed so the till can show who exists;
                    // they simply cannot sign in.
                    // ⚠ THE SALT GOES WITH IT. A salt on its own is useless, and leaving it behind
                    // would let a reader tell which accounts had been withheld.
                    CredentialHashBase64: covered.Contains(e.Id) ? null : cred?.HashedPassword,
                    CredentialSaltBase64: covered.Contains(e.Id) ? null : cred?.Salt,
                    Grants: grants));
            }

            // ⚠ AsOfUtc is the SERVER's clock, and the staleness horizons are measured from it. A
            // till with a drifted clock would otherwise think its roster was fresh for ever, or
            // expire it the moment it arrived.
            return Ok(new TillOperatorsResult(tillId, DateTime.UtcNow, operators.ToArray()));
        }

        /// <summary>
        /// The operators the CALLING till says it can already verify offline.
        ///
        /// ⚠ TOLERANT BY DESIGN. Anything unparseable is skipped rather than rejected: a malformed
        /// header must not fail a roster fetch, because the roster is how a shop signs in. The worst a
        /// bad value can do is leave a hash in the reply that could have been left out.
        ///
        /// ⚠ The claim is made by an AUTHENTICATED DEVICE about ITSELF, and it can only ever REMOVE
        /// credentials from the response. A till that lied would receive less than it needs and
        /// inconvenience only itself.
        /// </summary>
        private HashSet<Guid> ReadHaveVerifiers()
        {
            var covered = new HashSet<Guid>();

            if (!Request.Headers.TryGetValue("X-Plutus-Have-Verifiers", out var values)) return covered;

            foreach (var raw in values)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (Guid.TryParse(part, out var id) && id != Guid.Empty) covered.Add(id);
            }

            return covered;
        }
    }
}
