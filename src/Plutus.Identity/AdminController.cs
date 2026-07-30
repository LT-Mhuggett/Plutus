#nullable disable

using System;
using System.Linq;
using System.Security.Claims;
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
    public sealed record CreateUserBody(
        string FName, string LName, string Email, string Mobile, int? StoreId, string Password);

    public sealed record AssignRoleBody(
        Guid RoleId, string Scope, byte? DaysOfWeekMask,
        string WindowStartLocal, string WindowEndLocal,
        DateTime? ValidFromUtc, DateTime? ValidToUtc);

    public sealed record SetPasswordBody(string Password);

    /// <summary>
    /// WP3.2 users + roles admin (permission-gated on portal.users.manage; tenant isolation
    /// via the context's query filters; mutations audit-logged in the same SaveChanges).
    /// The role CATALOGUE stays code-defined (WP3.1) — this surface manages who holds which
    /// role at which scope, not what permissions exist.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "perm:" + PermissionCatalogue.PortalUsersManage)]
    public sealed class AdminUsersController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly PasswordResetService _reset;
        private readonly IMessageSender _mail;

        public AdminUsersController(MySqlDbContext db, ITenantContext tenant, PasswordResetService reset, IMessageSender mail)
        {
            _db = db;
            _tenant = tenant;
            _reset = reset;
            _mail = mail;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        // ── users ──

        [HttpGet("api/v1/users")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListUsers([FromQuery] bool includeRemoved = false)
        {
            // FE9.2: removed users are Active=false and hidden by default; the portal's
            // "include removed" toggle asks for them so they can be restored.
            var users = await _db.Employees.AsNoTracking()
                .Where(e => includeRemoved || e.Active)
                .Select(e => new
                {
                    id = e.Id, fName = e.FName, lName = e.LName, email = e.Email, active = e.Active,
                    storeId = e.StoreId, lastLoginAtUtc = e.LastLoginAtUtc,
                })
                .ToListAsync();
            var assignments = await _db.RbacRoleAssignments.AsNoTracking()
                .Include(a => a.Role)
                .Select(a => new { a.UserId, roleName = a.Role.Name })
                .ToListAsync();
            // FE9.1: whether they can sign in at all (no credential = staff-only, no web login yet).
            var withLogin = (await _db.WebCredentials.AsNoTracking().Select(c => c.EmployeeId).ToListAsync())
                .ToHashSet();
            return Ok(users.Select(u => new
            {
                u.id, u.fName, u.lName, u.email, u.active, u.storeId, u.lastLoginAtUtc,
                hasLogin = withLogin.Contains(u.id),
                roles = assignments.Where(a => a.UserId == u.id).Select(a => a.roleName).Distinct(),
            }));
        }

        /// <summary>FE9.3: the permission catalogue with plain-English descriptions + surface groups,
        /// so the portal can explain what a role actually grants.</summary>
        [HttpGet("api/v1/permissions")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult ListPermissions() =>
            Ok(PermissionCatalogue.All
                .Select(code => new
                {
                    code,
                    group = PermissionCatalogue.GroupOf(code),
                    description = PermissionCatalogue.DescribeOf(code),
                    ceilingCapable = PermissionCatalogue.CeilingCapable.Contains(code),
                })
                .OrderBy(p => p.group).ThenBy(p => p.code));

        [HttpPost("api/v1/users")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.FName) || string.IsNullOrWhiteSpace(body?.Email))
                return BadRequest(new { detail = "fName and email are required." });

            var businessId = await _db.Business.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync();
            var storeId = body.StoreId
                ?? await _db.Stores.AsNoTracking().Select(s => (int?)s.Id).FirstOrDefaultAsync();
            if (businessId == Guid.Empty || storeId == null)
                return BadRequest(new { detail = "The tenant has no company/store to attach the user to." });

            _db.CurrentUser = Actor.ToString();
            var user = new Employee
            {
                Id = Uuid7.New(),
                FName = body.FName.Trim(),
                LName = (body.LName ?? "").Trim(),
                Email = body.Email.Trim(),
                Mobile = string.IsNullOrWhiteSpace(body.Mobile) ? "-" : body.Mobile.Trim(),
                NIN = "-", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
                Active = true,
                BusinessId = businessId,
                StoreId = storeId.Value,
            };
            _db.Employees.Add(user);

            if (!string.IsNullOrEmpty(body.Password))
            {
                if (body.Password.Length < 8) return BadRequest(new { detail = "Password must be at least 8 characters." });
                var (hash, salt) = Pbkdf2.Hash(body.Password);
                _db.WebCredentials.Add(new WebCredential
                {
                    Email = user.Email,
                    EmployeeId = user.Id,
                    HashedPassword = Convert.ToBase64String(hash),
                    Salt = Convert.ToBase64String(salt),
                });
            }

            _db.Audit(_tenant.TenantId, Actor, "user.create", nameof(Employee), user.Id.ToString(),
                new { body.FName, body.LName, body.Email, hasLogin = !string.IsNullOrEmpty(body.Password) });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/users/{user.Id}", new { id = user.Id });
        }

        [HttpPost("api/v1/users/{id}/deactivate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Deactivate([FromRoute] Guid id)
        {
            var user = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (user == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            user.Active = false;
            _db.Audit(_tenant.TenantId, Actor, "user.deactivate", nameof(Employee), id.ToString());
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── FE9.1 passwords ──

        /// <summary>Admin sets a password for a user — also GRANTS a web login to a staff member who
        /// never had one. The password itself is never audited or logged.</summary>
        [HttpPost("api/v1/users/{id}/password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetPassword([FromRoute] Guid id, [FromBody] SetPasswordBody body)
        {
            if (body == null || string.IsNullOrEmpty(body.Password) || body.Password.Length < PasswordResetService.MinPasswordLength)
                return BadRequest(new { detail = $"Password must be at least {PasswordResetService.MinPasswordLength} characters." });
            var user = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (user == null) return NotFound();
            if (string.IsNullOrWhiteSpace(user.Email))
                return BadRequest(new { detail = "This user has no email address, so they cannot have a web login." });

            _db.CurrentUser = Actor.ToString();
            await _reset.SetPasswordAsync(id, user.Email, body.Password);
            // an admin-set password supersedes any outstanding reset link
            await _reset.InvalidateOutstandingAsync(id);
            _db.Audit(_tenant.TenantId, Actor, "user.password.set", nameof(Employee), id.ToString(), new { user.Email });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Email the user a single-use reset link (or an invite, when they have no login yet).
        /// Delivery rides the platform IMessageSender — SIMULATED until an Email provider is enabled
        /// in Platform → Notifications, where the attempt is still journaled in MessageEvents.</summary>
        [HttpPost("api/v1/users/{id}/password-reset")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> SendPasswordReset([FromRoute] Guid id)
        {
            var user = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (user == null) return NotFound();
            if (string.IsNullOrWhiteSpace(user.Email))
                return Conflict(new { detail = "This user has no email address to send a reset to." });
            if (!user.Active)
                return Conflict(new { detail = "This user is removed — restore them before sending a reset." });

            var hasLogin = await _db.WebCredentials.AnyAsync(c => c.EmployeeId == id);

            _db.CurrentUser = Actor.ToString();
            await _reset.InvalidateOutstandingAsync(id);
            var (token, row) = _reset.Mint(_tenant.TenantId, id, user.Email, isInvite: !hasLogin, requestedBy: Actor);
            _db.PasswordResetTokens.Add(row);
            _db.Audit(_tenant.TenantId, Actor, hasLogin ? "user.password.reset.send" : "user.invite.send",
                nameof(Employee), id.ToString(), new { user.Email, row.ExpiresAtUtc });
            await _db.SaveChangesAsync();

            var sent = await PasswordResetMail.SendAsync(_db, _mail, _tenant.TenantId, user.Email,
                $"{user.FName} {user.LName}".Trim(), token, row.IsInvite, Request);
            return Ok(new { sent, expiresAtUtc = row.ExpiresAtUtc, isInvite = row.IsInvite });
        }

        // ── FE9.2 remove / restore ──

        /// <summary>
        /// "Delete" a user WITHOUT destroying history: deactivate, revoke the web login, and drop
        /// every role assignment. Sales, audit rows and reports keep pointing at a real person — a
        /// hard DELETE would orphan all of that. Restorable.
        /// Guards: never yourself (instant lock-out) and never the tenant's last Owner-holder.
        /// </summary>
        [HttpPost("api/v1/users/{id}/remove")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RemoveUser([FromRoute] Guid id)
        {
            var user = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (user == null) return NotFound();
            if (id == Actor)
                return BadRequest(new { detail = "You cannot remove your own account." });

            // last-Owner guard: count OTHER active users holding a role that grants users.manage
            var ownerRoleIds = await _db.RbacRoles.AsNoTracking()
                .Where(r => r.Name == "Owner").Select(r => r.Id).ToListAsync();
            if (ownerRoleIds.Count > 0)
            {
                var owners = await _db.RbacRoleAssignments.AsNoTracking()
                    .Where(a => ownerRoleIds.Contains(a.RoleId))
                    .Select(a => a.UserId).Distinct().ToListAsync();
                if (owners.Contains(id))
                {
                    var activeOtherOwners = await _db.Employees.AsNoTracking()
                        .CountAsync(e => e.Active && e.Id != id && owners.Contains(e.Id));
                    if (activeOtherOwners == 0)
                        return Conflict(new { detail = "This is the last Owner — grant Owner to someone else first." });
                }
            }

            _db.CurrentUser = Actor.ToString();
            user.Active = false;
            var creds = await _db.WebCredentials.Where(c => c.EmployeeId == id).ToListAsync();
            _db.WebCredentials.RemoveRange(creds);
            var assignments = await _db.RbacRoleAssignments.Where(a => a.UserId == id).ToListAsync();
            _db.RbacRoleAssignments.RemoveRange(assignments);
            await _reset.InvalidateOutstandingAsync(id);
            _db.Audit(_tenant.TenantId, Actor, "user.remove", nameof(Employee), id.ToString(),
                new { user.Email, loginRevoked = creds.Count > 0, rolesRemoved = assignments.Count });
            await _db.SaveChangesAsync();
            return Ok(new { removed = true, loginRevoked = creds.Count > 0, rolesRemoved = assignments.Count });
        }

        /// <summary>Undo a removal: reactivate the person. Roles and login are NOT restored — those
        /// are granted again deliberately (FE9 decision 10).</summary>
        [HttpPost("api/v1/users/{id}/restore")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RestoreUser([FromRoute] Guid id)
        {
            var user = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
            if (user == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            user.Active = true;
            _db.Audit(_tenant.TenantId, Actor, "user.restore", nameof(Employee), id.ToString(), new { user.Email });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── roles + assignments ──

        /// <summary>FE9.3: roles with their grants (now described + grouped) and a live member count —
        /// the "what does each role give access to?" reference.</summary>
        [HttpGet("api/v1/roles")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListRoles()
        {
            var roles = await _db.RbacRoles.AsNoTracking().Include(r => r.Grants).ToListAsync();
            // members = distinct ACTIVE users holding the role (a removed user holds nothing)
            var activeUsers = (await _db.Employees.AsNoTracking().Where(e => e.Active).Select(e => e.Id).ToListAsync())
                .ToHashSet();
            var assignments = await _db.RbacRoleAssignments.AsNoTracking()
                .Select(a => new { a.RoleId, a.UserId }).ToListAsync();

            return Ok(roles.Select(r => new
            {
                id = r.Id, name = r.Name, isBuiltIn = r.IsBuiltIn,
                memberCount = assignments.Where(a => a.RoleId == r.Id && activeUsers.Contains(a.UserId))
                    .Select(a => a.UserId).Distinct().Count(),
                grants = r.Grants.Select(g => new
                {
                    code = g.PermissionCode,
                    maxPence = g.MaxPence,
                    group = PermissionCatalogue.GroupOf(g.PermissionCode),
                    description = PermissionCatalogue.DescribeOf(g.PermissionCode),
                }).OrderBy(g => g.group).ThenBy(g => g.code),
            }).OrderBy(r => r.name));
        }

        [HttpGet("api/v1/users/{id}/role-assignments")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAssignments([FromRoute] Guid id) =>
            Ok(await _db.RbacRoleAssignments.AsNoTracking().Include(a => a.Role)
                .Where(a => a.UserId == id)
                .Select(a => new
                {
                    id = a.Id, roleId = a.RoleId, roleName = a.Role.Name,
                    scopeType = a.ScopeType.ToString(), scopeId = a.ScopeId,
                    daysOfWeekMask = a.DaysOfWeekMask,
                    windowStartLocal = a.WindowStartLocal, windowEndLocal = a.WindowEndLocal,
                    validFromUtc = a.ValidFromUtc, validToUtc = a.ValidToUtc,
                })
                .ToListAsync());

        [HttpPost("api/v1/users/{id}/role-assignments")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Assign([FromRoute] Guid id, [FromBody] AssignRoleBody body)
        {
            if (body == null || body.RoleId == Guid.Empty)
                return BadRequest(new { detail = "roleId is required." });
            if (!ScopeNode.TryParse(body.Scope, out var scope))
                return BadRequest(new { detail = "scope must be tenant | company:{guid} | store:{int} | till:{guid}." });
            if (await _db.RbacRoles.AllAsync(r => r.Id != body.RoleId))
                return BadRequest(new { detail = "Unknown roleId." });

            TimeOnly? start = null, end = null;
            if (body.WindowStartLocal != null && !TryTime(body.WindowStartLocal, out start))
                return BadRequest(new { detail = "windowStartLocal must be HH:mm." });
            if (body.WindowEndLocal != null && !TryTime(body.WindowEndLocal, out end))
                return BadRequest(new { detail = "windowEndLocal must be HH:mm." });

            _db.CurrentUser = Actor.ToString();
            var assignment = new RbacRoleAssignment
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, UserId = id, RoleId = body.RoleId,
                ScopeType = scope.Type, ScopeId = scope.Id,
                DaysOfWeekMask = body.DaysOfWeekMask,
                WindowStartLocal = start, WindowEndLocal = end,
                ValidFromUtc = body.ValidFromUtc, ValidToUtc = body.ValidToUtc,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.RbacRoleAssignments.Add(assignment);
            _db.Audit(_tenant.TenantId, Actor, "role.assign", nameof(RbacRoleAssignment), assignment.Id.ToString(),
                new { userId = id, body.RoleId, scope = scope.ToString(), body.DaysOfWeekMask, body.WindowStartLocal, body.WindowEndLocal });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/users/{id}/role-assignments/{assignment.Id}", new { id = assignment.Id });
        }

        [HttpDelete("api/v1/users/{id}/role-assignments/{assignmentId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Unassign([FromRoute] Guid id, [FromRoute] Guid assignmentId)
        {
            var assignment = await _db.RbacRoleAssignments
                .FirstOrDefaultAsync(a => a.Id == assignmentId && a.UserId == id);
            if (assignment == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            _db.RbacRoleAssignments.Remove(assignment);
            _db.Audit(_tenant.TenantId, Actor, "role.unassign", nameof(RbacRoleAssignment), assignmentId.ToString(),
                new { userId = id, assignment.RoleId, scope = new ScopeNode(assignment.ScopeType, assignment.ScopeId).ToString() });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── audit trail ──

        [HttpGet("api/v1/audit")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> AuditTrail([FromQuery] string entityType, [FromQuery] int take = 100)
        {
            take = Math.Clamp(take, 1, 500);
            return Ok(await _db.AuditLogs.AsNoTracking()
                .Where(a => entityType == null || a.EntityType == entityType)
                .OrderByDescending(a => a.Id).Take(take)
                .Select(a => new
                {
                    id = a.Id, actorUserId = a.ActorUserId, action = a.Action,
                    entityType = a.EntityType, entityId = a.EntityId, detailJson = a.DetailJson, atUtc = a.AtUtc,
                })
                .ToListAsync());
        }

        private static bool TryTime(string s, out TimeOnly? value)
        {
            value = null;
            if (!TimeOnly.TryParse(s, out var t)) return false;
            value = t;
            return true;
        }
    }
}
