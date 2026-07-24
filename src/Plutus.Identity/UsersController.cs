#nullable disable

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// WP3.1: effective-permission resolution surface (architecture §7.2). Consumed by the
    /// portal (users &amp; roles screens) and by tills, which download a user's effective POS
    /// permission set with the catalogue sync for offline enforcement.
    /// </summary>
    [ApiController]
    [Route("api/v1/users")]
    public sealed class UsersController : ControllerBase
    {
        private readonly EffectivePermissionsService _permissions;
        public UsersController(EffectivePermissionsService permissions) => _permissions = permissions;

        /// <summary>Effective permissions for a user at a scope node — the union of role
        /// grants at that node or above. scope = "tenant" | "company:{guid}" | "store:{int}"
        /// | "till:{guid}" (default tenant). Self-service for the caller's own id; anyone
        /// else requires portal.users.manage.</summary>
        [HttpGet("{id}/effective-permissions")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> EffectivePermissions([FromRoute] Guid id, [FromQuery] string scope)
        {
            if (!ScopeNode.TryParse(scope, out var node))
                return BadRequest(new { detail = "scope must be tenant | company:{guid} | store:{int} | till:{guid}." });

            var callerId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isSelf = Guid.TryParse(callerId, out var caller) && caller == id;
            if (!isSelf)
            {
                var callerCanManage = Guid.TryParse(callerId, out var c) &&
                    await _permissions.HasAnywhereAsync(c, PermissionCatalogue.PortalUsersManage, DateTime.Now);
                var isPlatformAdmin = User.HasClaim("scope", "platform-admin");
                if (!callerCanManage && !isPlatformAdmin)
                    return StatusCode(403, new { detail = "Requires portal.users.manage (or your own user id)." });
            }

            var effective = await _permissions.ResolveAsync(id, node, DateTime.Now);
            return Ok(new
            {
                userId = id,
                scope = node.ToString(),
                permissions = effective.Select(p => new { code = p.Code, maxPence = p.MaxPence, display = p.ToString() }),
            });
        }
    }
}
