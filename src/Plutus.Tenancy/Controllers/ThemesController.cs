#nullable disable

using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record ThemeBody(string Name, string BaseMode, string ColorsJson);
    public sealed record ThemeAssignmentBody(byte Scope, string ScopeKey, string ThemeKey);
    public sealed record TillGroupBody(string Name, Guid[] TillIds);

    /// <summary>
    /// FE10 till theming — colour schemes managed in the portal, pushed to tills. Follows the
    /// receipt-template shape exactly: writes are perm:portal.company.manage and audited; the
    /// till-facing read (/effective) is sales.ingest so an operator OR device token can fetch
    /// the theme it renders with. Colours travel as an opaque JSON blob owned by the frontends;
    /// scoping (tenant/store/group/till) is real columns resolved by ThemeResolution.
    /// </summary>
    [ApiController]
    [Route("api/v1/themes")]
    public sealed class ThemesController : ControllerBase
    {
        private const int ColorsJsonCap = 2048;

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public ThemesController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>Everything the portal editor needs in one call: themes, groups (with
        /// members), and the current assignments.</summary>
        [HttpGet]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var themes = await _db.TillThemes.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new { id = t.Id, name = t.Name, baseMode = t.BaseMode, colorsJson = t.ColorsJson, updatedAtUtc = t.UpdatedAtUtc })
                .ToListAsync();
            var groups = await _db.TillGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
            var members = await _db.TillGroupMembers.AsNoTracking().ToListAsync();
            var assignments = await _db.TillThemeAssignments.AsNoTracking()
                .Select(a => new { scope = a.Scope, scopeKey = a.ScopeKey, themeKey = a.ThemeKey, updatedAtUtc = a.UpdatedAtUtc })
                .ToListAsync();
            return Ok(new
            {
                themes,
                groups = groups.Select(g => new
                {
                    id = g.Id, name = g.Name,
                    tillIds = members.Where(m => m.GroupId == g.Id).Select(m => m.TillId).ToArray(),
                }),
                assignments,
            });
        }

        [HttpPost]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] ThemeBody body)
        {
            var problem = await ValidateThemeAsync(body, exceptId: null);
            if (problem != null) return problem;

            _db.CurrentUser = Actor.ToString();
            var theme = new TillTheme
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId,
                Name = body.Name.Trim(), BaseMode = body.BaseMode,
                ColorsJson = string.IsNullOrWhiteSpace(body.ColorsJson) ? null : body.ColorsJson,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _db.TillThemes.Add(theme);
            _db.Audit(_tenant.TenantId, Actor, "theme.create", nameof(TillTheme), theme.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/themes/{theme.Id}", new { id = theme.Id });
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] ThemeBody body)
        {
            var theme = await _db.TillThemes.FirstOrDefaultAsync(t => t.Id == id);
            if (theme == null) return NotFound();
            var problem = await ValidateThemeAsync(body, exceptId: id);
            if (problem != null) return problem;

            _db.CurrentUser = Actor.ToString();
            theme.Name = body.Name.Trim();
            theme.BaseMode = body.BaseMode;
            theme.ColorsJson = string.IsNullOrWhiteSpace(body.ColorsJson) ? null : body.ColorsJson;
            theme.UpdatedAtUtc = DateTime.UtcNow;
            _db.Audit(_tenant.TenantId, Actor, "theme.update", nameof(TillTheme), id.ToString(), body);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Deleting a theme also removes every assignment pointing at it — the affected
        /// tills fall back to the next scope up (or the built-in default) on their next poll.</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var theme = await _db.TillThemes.FirstOrDefaultAsync(t => t.Id == id);
            if (theme == null) return NotFound();

            _db.CurrentUser = Actor.ToString();
            var key = ThemeResolution.KeyFor(id);
            var orphaned = await _db.TillThemeAssignments.Where(a => a.ThemeKey == key).ToListAsync();
            _db.TillThemeAssignments.RemoveRange(orphaned);
            _db.TillThemes.Remove(theme);
            _db.Audit(_tenant.TenantId, Actor, "theme.delete", nameof(TillTheme), id.ToString(),
                new { name = theme.Name, unassigned = orphaned.Count });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Assign (upsert) or clear (ThemeKey null/empty) the theme for one target.</summary>
        [HttpPut("assignments")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Assign([FromBody] ThemeAssignmentBody body)
        {
            if (body == null || body.Scope > ThemeResolution.ScopeTill)
                return BadRequest(new { detail = "scope must be 0 (tenant), 1 (store), 2 (group) or 3 (till)." });

            // Canonicalise + verify the target exists (a typo'd store id should fail loudly now,
            // not silently theme nothing).
            string scopeKey;
            switch (body.Scope)
            {
                case ThemeResolution.ScopeTenant:
                    scopeKey = "";
                    break;
                case ThemeResolution.ScopeStore:
                    if (!int.TryParse(body.ScopeKey, out var storeId) || !await _db.Stores.AnyAsync(s => s.Id == storeId))
                        return BadRequest(new { detail = "scopeKey must be an existing store id." });
                    scopeKey = storeId.ToString();
                    break;
                case ThemeResolution.ScopeGroup:
                    if (!Guid.TryParse(body.ScopeKey, out var groupId) || !await _db.TillGroups.AnyAsync(g => g.Id == groupId))
                        return BadRequest(new { detail = "scopeKey must be an existing till-group id." });
                    scopeKey = ThemeResolution.KeyFor(groupId);
                    break;
                default:
                    if (!Guid.TryParse(body.ScopeKey, out var tillId) || !await _db.Till.AnyAsync(t => t.Id == tillId))
                        return BadRequest(new { detail = "scopeKey must be an existing till id." });
                    scopeKey = ThemeResolution.KeyFor(tillId);
                    break;
            }

            _db.CurrentUser = Actor.ToString();
            var existing = await _db.TillThemeAssignments
                .FirstOrDefaultAsync(a => a.Scope == body.Scope && a.ScopeKey == scopeKey);

            if (string.IsNullOrWhiteSpace(body.ThemeKey))
            {
                if (existing != null) _db.TillThemeAssignments.Remove(existing);
            }
            else
            {
                var themeKey = body.ThemeKey.Trim();
                if (!ThemeResolution.BuiltIns.Contains(themeKey))
                {
                    if (!Guid.TryParse(themeKey, out var themeId) || !await _db.TillThemes.AnyAsync(t => t.Id == themeId))
                        return BadRequest(new { detail = "themeKey must be builtin:system, builtin:light, builtin:dark, or an existing theme id." });
                    themeKey = ThemeResolution.KeyFor(themeId);
                }
                if (existing == null)
                    _db.TillThemeAssignments.Add(new TillThemeAssignment
                    {
                        Id = Uuid7.New(), TenantId = _tenant.TenantId,
                        Scope = body.Scope, ScopeKey = scopeKey, ThemeKey = themeKey, UpdatedAtUtc = DateTime.UtcNow,
                    });
                else
                {
                    existing.ThemeKey = themeKey;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            _db.Audit(_tenant.TenantId, Actor, "theme.assign", nameof(TillThemeAssignment),
                $"{body.Scope}:{scopeKey}", body);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>The resolved theme for one till — what the till actually renders with.
        /// tillId optional (un-enrolled tills pass only their storeId; neither = tenant default).
        /// When tillId is given, its store membership comes from the Till row, not the caller.</summary>
        [HttpGet("effective")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Effective([FromQuery] Guid? tillId, [FromQuery] int? storeId)
        {
            var groupIds = Array.Empty<Guid>();
            if (tillId is Guid t)
            {
                // An unknown till (stale credential, mid-move) falls through to store/tenant scope
                // rather than erroring — the till must never lose its colours over a lookup.
                var sid = await _db.Till.AsNoTracking().Where(x => x.Id == t).Select(x => (int?)x.StoreId).FirstOrDefaultAsync();
                if (sid != null) storeId = sid;
                groupIds = await _db.TillGroupMembers.AsNoTracking()
                    .Where(m => m.TillId == t).Select(m => m.GroupId).ToArrayAsync();
            }

            var assignments = await _db.TillThemeAssignments.AsNoTracking().ToListAsync();
            var (picked, source) = ThemeResolution.Pick(assignments, tillId, storeId, groupIds);
            if (picked == null)
                return Ok(new { source, themeKey = (string)null, name = (string)null, baseMode = "system", colorsJson = (string)null });

            if (ThemeResolution.BuiltIns.Contains(picked.ThemeKey))
            {
                var mode = picked.ThemeKey["builtin:".Length..];
                return Ok(new
                {
                    source, themeKey = picked.ThemeKey,
                    name = mode == "system" ? "Plutus" : mode == "light" ? "Plutus Light" : "Plutus Dark",
                    baseMode = mode, colorsJson = (string)null,
                });
            }

            var theme = Guid.TryParse(picked.ThemeKey, out var themeId)
                ? await _db.TillThemes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == themeId)
                : null;
            if (theme == null) // deleted out from under the assignment — behave like default
                return Ok(new { source = "default", themeKey = (string)null, name = (string)null, baseMode = "system", colorsJson = (string)null });

            return Ok(new
            {
                source, themeKey = picked.ThemeKey,
                name = theme.Name, baseMode = theme.BaseMode, colorsJson = theme.ColorsJson,
            });
        }

        /// <summary>400 with a reason, or null when the body is a valid theme.</summary>
        private async Task<IActionResult> ValidateThemeAsync(ThemeBody body, Guid? exceptId)
        {
            if (string.IsNullOrWhiteSpace(body?.Name) || body.Name.Trim().Length > 60)
                return BadRequest(new { detail = "name is required (max 60 chars)." });
            if (body.BaseMode != "light" && body.BaseMode != "dark")
                return BadRequest(new { detail = "baseMode must be 'light' or 'dark' — only the built-in default follows the device." });
            if (!string.IsNullOrWhiteSpace(body.ColorsJson))
            {
                if (body.ColorsJson.Length > ColorsJsonCap)
                    return BadRequest(new { detail = $"colorsJson too large (max {ColorsJsonCap} chars)." });
                try
                {
                    using var doc = JsonDocument.Parse(body.ColorsJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                        return BadRequest(new { detail = "colorsJson must be a JSON object." });
                }
                catch (JsonException)
                {
                    return BadRequest(new { detail = "colorsJson is not valid JSON." });
                }
            }
            var lowered = body.Name.Trim().ToLowerInvariant();
            if (await _db.TillThemes.AnyAsync(t => t.Name.ToLower() == lowered && t.Id != (exceptId ?? Guid.Empty)))
                return Conflict(new { detail = $"A theme named '{body.Name.Trim()}' already exists." });
            return null;
        }
    }

    /// <summary>FE10: named till groups — pure theming targets for now (nothing else reads
    /// them), managed from the same portal screen, so the same permission.</summary>
    [ApiController]
    [Route("api/v1/till-groups")]
    public sealed class TillGroupsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public TillGroupsController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpPost]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] TillGroupBody body)
        {
            var problem = await ValidateAsync(body, exceptId: null);
            if (problem != null) return problem;

            _db.CurrentUser = Actor.ToString();
            var group = new TillGroup { Id = Uuid7.New(), TenantId = _tenant.TenantId, Name = body.Name.Trim() };
            _db.TillGroups.Add(group);
            foreach (var tillId in body.TillIds.Distinct())
                _db.TillGroupMembers.Add(new TillGroupMember { GroupId = group.Id, TillId = tillId, TenantId = _tenant.TenantId });
            _db.Audit(_tenant.TenantId, Actor, "till-group.create", nameof(TillGroup), group.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/till-groups/{group.Id}", new { id = group.Id });
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] TillGroupBody body)
        {
            var group = await _db.TillGroups.FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();
            var problem = await ValidateAsync(body, exceptId: id);
            if (problem != null) return problem;

            _db.CurrentUser = Actor.ToString();
            group.Name = body.Name.Trim();
            var members = await _db.TillGroupMembers.Where(m => m.GroupId == id).ToListAsync();
            _db.TillGroupMembers.RemoveRange(members);
            foreach (var tillId in body.TillIds.Distinct())
                _db.TillGroupMembers.Add(new TillGroupMember { GroupId = id, TillId = tillId, TenantId = _tenant.TenantId });
            _db.Audit(_tenant.TenantId, Actor, "till-group.update", nameof(TillGroup), id.ToString(), body);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Deleting a group removes its membership rows AND its theme assignment (if
        /// any) — member tills fall back to store/tenant scope on their next poll.</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = "perm:portal.company.manage")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var group = await _db.TillGroups.FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();

            _db.CurrentUser = Actor.ToString();
            var key = ThemeResolution.KeyFor(id);
            _db.TillGroupMembers.RemoveRange(await _db.TillGroupMembers.Where(m => m.GroupId == id).ToListAsync());
            _db.TillThemeAssignments.RemoveRange(await _db.TillThemeAssignments
                .Where(a => a.Scope == ThemeResolution.ScopeGroup && a.ScopeKey == key).ToListAsync());
            _db.TillGroups.Remove(group);
            _db.Audit(_tenant.TenantId, Actor, "till-group.delete", nameof(TillGroup), id.ToString(), new { name = group.Name });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private async Task<IActionResult> ValidateAsync(TillGroupBody body, Guid? exceptId)
        {
            if (string.IsNullOrWhiteSpace(body?.Name) || body.Name.Trim().Length > 60)
                return BadRequest(new { detail = "name is required (max 60 chars)." });
            if (body.TillIds == null)
                return BadRequest(new { detail = "tillIds is required (may be empty)." });
            var distinct = body.TillIds.Distinct().ToArray();
            var known = await _db.Till.AsNoTracking().Where(t => distinct.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            if (known.Count != distinct.Length)
                return BadRequest(new { detail = "tillIds contains a till that does not exist." });
            var lowered = body.Name.Trim().ToLowerInvariant();
            if (await _db.TillGroups.AnyAsync(g => g.Name.ToLower() == lowered && g.Id != (exceptId ?? Guid.Empty)))
                return Conflict(new { detail = $"A till group named '{body.Name.Trim()}' already exists." });
            return null;
        }
    }
}
