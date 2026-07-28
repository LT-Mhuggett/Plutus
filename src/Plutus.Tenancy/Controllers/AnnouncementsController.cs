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
    public sealed record AnnouncementBody(byte Severity, string Title, string Body, DateTime StartsAtUtc, DateTime EndsAtUtc, Guid[] TenantIds);

    /// <summary>
    /// WP15.1 announcements. /active is tenant-scoped and readable by ANY authenticated token
    /// (portal + till poll it) — it returns only currently-live announcements targeting the
    /// caller's tenant (or all). Authoring is platform-admin + audited.
    /// </summary>
    [ApiController]
    public sealed class AnnouncementsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public AnnouncementsController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/announcements/active")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Active()
        {
            var now = DateTime.UtcNow;
            var tid = _tenant.TenantId;
            var live = await _db.PlatformAnnouncements.AsNoTracking()
                .Where(a => a.StartsAtUtc <= now && a.EndsAtUtc >= now).ToListAsync();
            return Ok(live.Where(a => Targets(a.TenantIds, tid))
                .OrderByDescending(a => a.Severity).ThenByDescending(a => a.StartsAtUtc)
                .Select(a => new
                {
                    a.Id, severity = a.Severity.ToString(), a.Title, a.Body, a.StartsAtUtc, a.EndsAtUtc,
                }));
        }

        /// <summary>Null/empty TenantIds = every tenant; else the caller's tenant must be listed.</summary>
        private static bool Targets(string tenantIdsJson, Guid tid)
        {
            if (string.IsNullOrWhiteSpace(tenantIdsJson)) return true;
            try
            {
                var ids = JsonSerializer.Deserialize<Guid[]>(tenantIdsJson);
                return ids == null || ids.Length == 0 || ids.Contains(tid);
            }
            catch { return true; } // malformed target → treat as global rather than hide it
        }

        // ── authoring (platform-admin) ──

        [HttpGet("api/v1/platform/announcements")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List() =>
            Ok(await _db.PlatformAnnouncements.AsNoTracking().OrderByDescending(a => a.StartsAtUtc)
                .Select(a => new
                {
                    a.Id, severity = a.Severity.ToString(), a.Title, a.Body, a.StartsAtUtc, a.EndsAtUtc,
                    a.TenantIds, a.CreatedBy, a.CreatedAtUtc,
                }).Take(200).ToListAsync());

        [HttpPost("api/v1/platform/announcements")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] AnnouncementBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Title)) return BadRequest(new { detail = "title required." });
            if (body.EndsAtUtc <= body.StartsAtUtc) return BadRequest(new { detail = "endsAtUtc must be after startsAtUtc." });
            _db.CurrentUser = Actor.ToString();
            var a = new PlatformAnnouncement
            {
                Id = Uuid7.New(), Severity = (AnnouncementSeverity)body.Severity, Title = body.Title.Trim(), Body = body.Body,
                StartsAtUtc = body.StartsAtUtc, EndsAtUtc = body.EndsAtUtc,
                TenantIds = body.TenantIds is { Length: > 0 } ? JsonSerializer.Serialize(body.TenantIds) : null,
                CreatedBy = Actor, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.PlatformAnnouncements.Add(a);
            _db.Audit(Guid.Empty, Actor, "announcement.create", "PlatformAnnouncement", a.Id.ToString(), new { a.Severity, a.Title });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/platform/announcements", new { id = a.Id });
        }

        [HttpDelete("api/v1/platform/announcements/{id}")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var a = await _db.PlatformAnnouncements.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();
            _db.CurrentUser = Actor.ToString();
            _db.PlatformAnnouncements.Remove(a);
            _db.Audit(Guid.Empty, Actor, "announcement.delete", "PlatformAnnouncement", id.ToString());
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
