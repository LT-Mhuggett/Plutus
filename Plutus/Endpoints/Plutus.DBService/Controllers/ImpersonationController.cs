#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plutus.Entities;
using Plutus.Identity;
using Plutus.SharedKernel;

namespace Plutus.DBService.Controllers
{
    /// <summary>
    /// WP14.1 support impersonation. A platform operator mints a short-lived HMAC token that "acts
    /// as" a tenant user — the target's effective RBAC scopes MINUS a deny-list (nothing
    /// destructive), plus impersonating/actor claims and a hard expiry with no refresh. Minting is
    /// platform-admin and audited; every request the token makes is logged by the audit middleware.
    /// Stays on the HMAC token path so it works identically once Keycloak lands.
    /// </summary>
    [ApiController]
    public sealed class ImpersonationController : ControllerBase
    {
        public sealed record ImpersonateBody(Guid UserId, int Minutes);

        private readonly MySqlDbContext _db;
        private readonly EffectivePermissionsService _permissions;
        private readonly IConfiguration _config;

        public ImpersonationController(MySqlDbContext db, EffectivePermissionsService permissions, IConfiguration config)
        {
            _db = db;
            _permissions = permissions;
            _config = config;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpPost("api/v1/platform/tenants/{tenantId}/impersonate")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Impersonate([FromRoute] Guid tenantId, [FromBody] ImpersonateBody body)
        {
            var secret = _config["TEST_TOKEN_SECRET"];
            if (string.IsNullOrEmpty(secret)) return StatusCode(503, "Token issuing is not configured.");
            if (body == null || body.UserId == Guid.Empty) return BadRequest(new { detail = "userId is required." });
            var minutes = Math.Clamp(body.Minutes <= 0 ? 30 : body.Minutes, 1, 60);

            // The target must belong to the named tenant (People.TenantId is a shadow column).
            var person = await _db.People.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == body.UserId)
                .Select(p => new { p.FName, p.LName, Tid = EF.Property<Guid>(p, "TenantId") })
                .FirstOrDefaultAsync();
            if (person == null || person.Tid != tenantId)
                return NotFound(new { detail = "User not found in this tenant." });

            var scopes = await _permissions.ResolveLoginScopesAsync(body.UserId, DateTime.Now);
            var granted = scopes.Where(s => !PermissionCatalogue.ImpersonationDenied.Contains(s)).ToArray();

            var payload = new TestTokenAuth.TokenPayload
            {
                EmployeeId = body.UserId,
                Name = $"{person.FName} {person.LName}".Trim(),
                Scope = string.Join(" ", granted),
                Tid = tenantId,
                Impersonating = true,
                Actor = Actor,
                Exp = DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds(),
            };
            var token = TestTokenAuth.Issue(payload, secret);

            _db.CurrentUser = Actor.ToString();
            _db.Audit(tenantId, Actor, "impersonation.start", "Person", body.UserId.ToString(),
                new { actor = Actor, target = body.UserId, minutes, grantedScopes = granted, deniedScopes = scopes.Except(granted).ToArray() });
            await _db.SaveChangesAsync();

            return Ok(new
            {
                token,
                name = payload.Name,
                impersonating = true,
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp),
            });
        }
    }
}
