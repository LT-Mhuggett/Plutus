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

        public AdminUsersController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        // ── users ──

        [HttpGet("api/v1/users")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListUsers()
        {
            var users = await _db.Employees.AsNoTracking()
                .Select(e => new { id = e.Id, fName = e.FName, lName = e.LName, email = e.Email, active = e.Active, storeId = e.StoreId })
                .ToListAsync();
            var assignments = await _db.RbacRoleAssignments.AsNoTracking()
                .Include(a => a.Role)
                .Select(a => new { a.UserId, roleName = a.Role.Name })
                .ToListAsync();
            return Ok(users.Select(u => new
            {
                u.id, u.fName, u.lName, u.email, u.active, u.storeId,
                roles = assignments.Where(a => a.UserId == u.id).Select(a => a.roleName).Distinct(),
            }));
        }

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

        // ── roles + assignments ──

        [HttpGet("api/v1/roles")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListRoles() =>
            Ok(await _db.RbacRoles.AsNoTracking().Include(r => r.Grants)
                .Select(r => new
                {
                    id = r.Id, name = r.Name, isBuiltIn = r.IsBuiltIn,
                    grants = r.Grants.Select(g => new { code = g.PermissionCode, maxPence = g.MaxPence }),
                })
                .ToListAsync());

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
