using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    public sealed record CreateTillRequest(int StoreId, string Name);
    public sealed record EnrolRequest(string EnrolmentCode);
    public sealed record RenameTillRequest(string Name);
    public sealed record UnenrolRequestBody(Guid DeviceId);
    public sealed record RemovalDecisionBody(bool Approve);

    /// <summary>T1.2 till lifecycle: portal creates tills + enrolment codes; a device redeems a
    /// code anonymously. Business logic lives in <see cref="EnrolmentService"/>.</summary>
    [ApiController]
    [Route("api/v1/tills")]
    public sealed class TillsController : ControllerBase
    {
        private readonly EnrolmentService _enrolment;
        private readonly ITenantContext _tenant;
        private readonly MySqlDbContext _db;

        public TillsController(EnrolmentService enrolment, ITenantContext tenant, MySqlDbContext db)
        {
            _enrolment = enrolment;
            _tenant = tenant;
            _db = db;
        }

        private string ActingUser => User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "portal";
        private Guid Actor => Guid.TryParse(ActingUser, out var g) ? g : Guid.Empty;

        /// <summary>WP3.2: till fleet listing — tills with their enrolled-device states.</summary>
        [HttpGet]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] int? storeId)
        {
            var tills = await _db.Till.AsNoTracking()
                .Where(t => storeId == null || t.StoreId == storeId).ToListAsync();
            var tillIds = tills.Select(t => t.Id).ToList();
            var devices = await _db.Devices.AsNoTracking()
                .Where(d => tillIds.Contains(d.TillId)).ToListAsync();
            // WP11.1: names live in the TillDetails side table (older tills may have none yet).
            var names = await _db.TillDetails.AsNoTracking()
                .Where(t => tillIds.Contains(t.TillId)).ToDictionaryAsync(t => t.TillId, t => t.Name);
            return Ok(tills.Select(t => new
            {
                id = t.Id,
                name = names.TryGetValue(t.Id, out var n) ? n : $"Till {t.Id.ToString()[..8]}",
                storeId = t.StoreId,
                lastOnline = t.LastOnline,
                devices = devices.Where(d => d.TillId == t.Id).Select(d => new
                {
                    id = d.Id, status = d.Status.ToString(), lastSeenSeq = d.LastSeenSeq, createdAtUtc = d.CreatedAtUtc,
                }),
            }));
        }

        [HttpPost]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateTill([FromBody] CreateTillRequest body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Name)) return BadRequest("storeId and name are required.");
            var result = await _enrolment.CreateTillAsync(_tenant.TenantId, body.StoreId, body.Name.Trim(), ActingUser);
            _db.Audit(_tenant.TenantId, Actor, "till.create", "Till", result.TillId.ToString(), new { body.StoreId, body.Name });
            await _db.SaveChangesAsync();
            return Created($"/api/v1/tills/{result.TillId}", result);
        }

        [HttpPost("enrol")]
        [AllowAnonymous]
        [EnableRateLimiting("enrol")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status410Gone)] // reused/expired/unknown code
        public async Task<IActionResult> Enrol([FromBody] EnrolRequest body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.EnrolmentCode)) return BadRequest("enrolmentCode is required.");
            try
            {
                var result = await _enrolment.EnrolAsync(body.EnrolmentCode, "enrol");
                return Ok(result);
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }

        /// <summary>WP11.1: a till reads its OWN name (sales.ingest — any operator or the device),
        /// so it can show it in the header without the portal.tills.enrol the fleet list needs.</summary>
        [HttpGet("{id}/name")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetName([FromRoute] Guid id)
        {
            var name = await _db.TillDetails.AsNoTracking()
                .Where(t => t.TillId == id).Select(t => t.Name).FirstOrDefaultAsync();
            return Ok(new { id, name = name ?? $"Till {id.ToString()[..8]}" });
        }

        /// <summary>WP11.1: rename a till. Gated so BOTH a portal admin and the till's own device
        /// token can call it; tenant-unique name check → 409 either way.</summary>
        [HttpPut("{id}/name")]
        [Authorize(Policy = PlutusPolicies.TillsName)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Rename([FromRoute] Guid id, [FromBody] RenameTillRequest body)
        {
            try
            {
                await _enrolment.RenameTillAsync(_tenant.TenantId, id, body?.Name, ActingUser);
                _db.Audit(_tenant.TenantId, Actor, "till.rename", "Till", id.ToString(), new { body?.Name });
                await _db.SaveChangesAsync();
                return NoContent();
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }

        /// <summary>WP: delete an empty till (no sales). Refuses tills with recorded sales (409).</summary>
        [HttpDelete("{id}")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            try
            {
                await _enrolment.DeleteTillAsync(id, ActingUser);
                _db.Audit(_tenant.TenantId, Actor, "till.delete", "Till", id.ToString(), new { });
                await _db.SaveChangesAsync();
                return NoContent();
            }
            catch (EnrolmentException ex)
            {
                return Problem(detail: ex.Message, statusCode: ex.StatusCode);
            }
        }

        [HttpPost("{id}/revoke")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Revoke([FromRoute] Guid id)
        {
            var revoked = await _enrolment.RevokeTillAsync(id, ActingUser);
            _db.Audit(_tenant.TenantId, Actor, "till.revoke", "Till", id.ToString(), new { devicesRevoked = revoked });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── WP6.2 un-enrol with portal approval ─────────────────────────────────────────────────
        // A till admin requests removal → the device is marked PendingRemoval (keeps trading) → a
        // portal admin approves (→ Revoked, the till then forgets its credential) or rejects (→
        // Active). Devices are UNSCOPED by the tenant filter, so every action verifies TenantId.

        /// <summary>A till asks to be un-enrolled — marks its device PendingRemoval (still trades).</summary>
        [HttpPost("unenrol-request")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RequestUnenrol([FromBody] UnenrolRequestBody body)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == body.DeviceId && d.TenantId == _tenant.TenantId);
            if (device == null) return NotFound(new { detail = "Unknown device." });
            if (device.Status == DeviceStatus.Active) device.Status = DeviceStatus.PendingRemoval;
            _db.CurrentUser = Actor.ToString();
            _db.Audit(_tenant.TenantId, Actor, "device.unenrol-request", "Device", device.Id.ToString(), new { device.TillId });
            await _db.SaveChangesAsync();
            return Ok(new { status = device.Status.ToString() });
        }

        /// <summary>Portal decision on a pending removal: approve → revoke, reject → active.</summary>
        [HttpPost("devices/{deviceId}/removal")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DecideRemoval([FromRoute] Guid deviceId, [FromBody] RemovalDecisionBody body)
        {
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId);
            if (device == null) return NotFound(new { detail = "Unknown device." });
            device.Status = body.Approve ? DeviceStatus.Revoked : DeviceStatus.Active;
            _db.CurrentUser = Actor.ToString();
            _db.Audit(_tenant.TenantId, Actor, body.Approve ? "device.unenrol-approve" : "device.unenrol-reject",
                "Device", device.Id.ToString(), new { device.TillId });
            await _db.SaveChangesAsync();
            return Ok(new { status = device.Status.ToString() });
        }

        /// <summary>The till polls its own device status to know when an approved removal has taken
        /// effect (Revoked) so it can forget the local credential. Readable by operator OR device.</summary>
        [HttpGet("devices/{deviceId}/status")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDeviceStatus([FromRoute] Guid deviceId)
        {
            var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId);
            if (device == null) return NotFound(new { detail = "Unknown device." });
            return Ok(new { status = device.Status.ToString() });
        }
    }
}
