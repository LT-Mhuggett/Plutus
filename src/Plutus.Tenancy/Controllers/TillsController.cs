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
    public sealed record MoveTillRequest(int StoreId);
    public sealed record UnenrolRequestBody(Guid DeviceId);
    public sealed record RemovalDecisionBody(bool Approve);
    /// <summary>FE3.0: what the till found when it polled its local hardware agent. Null
    /// AgentVersion = it looked and found none (still worth recording — "no agent installed" is a
    /// fleet fact the portal shows). Everything but the device id is nullable ON PURPOSE — this
    /// project has NRT enabled, and [ApiController] turns a null in a non-nullable property into an
    /// automatic 400 before the action runs.</summary>
    public sealed record AgentStatusBody(Guid DeviceId, string? AgentVersion, string? PrinterName, bool? PrinterOnline);

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
            // A webstore connection provisions a VIRTUAL till/device to carry its channel's
            // sales — flag those so the portal can badge them and hide enrol/revoke actions.
            var webstoreTillIds = await _db.WebStores.AsNoTracking()
                .Select(w => w.TillId).ToListAsync();
            return Ok(tills.Select(t => new
            {
                id = t.Id,
                name = names.TryGetValue(t.Id, out var n) ? n : $"Till {t.Id.ToString()[..8]}",
                storeId = t.StoreId,
                lastOnline = t.LastOnline,
                isWebstore = webstoreTillIds.Contains(t.Id),
                devices = devices.Where(d => d.TillId == t.Id).Select(d => new
                {
                    id = d.Id, status = d.Status.ToString(), lastSeenSeq = d.LastSeenSeq, createdAtUtc = d.CreatedAtUtc,
                    // FE3.0 agent telemetry — reportedAt null = never reported (native till / old web
                    // till); reported with a null version = "web till, no agent installed".
                    agentVersion = d.AgentVersion,
                    agentPrinterName = d.AgentPrinterName,
                    agentPrinterOnline = d.AgentPrinterOnline,
                    agentReportedAtUtc = d.AgentReportedAtUtc,
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

        /// <summary>
        /// FE6.1: a fresh single-use code for an EXISTING till — use this when a till's browser has
        /// lost its credential (cleared site data, replacement PC). The till keeps its identity and
        /// its sales history; redeeming the code retires the previous device.
        /// This is the operation whose absence produced throwaway duplicate tills.
        /// </summary>
        [HttpPost("{id:guid}/enrol-code")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReissueEnrolCode([FromRoute] Guid id)
        {
            try
            {
                var result = await _enrolment.ReissueEnrolCodeAsync(_tenant.TenantId, id, ActingUser);
                _db.Audit(_tenant.TenantId, Actor, "till.enrol-code.reissue", "Till", id.ToString(),
                    new { result.ExpiresAtUtc });
                await _db.SaveChangesAsync();
                return Ok(result);
            }
            catch (EnrolmentException ex)
            {
                return StatusCode(ex.StatusCode, new { detail = ex.Message });
            }
        }

        /// <summary>FE6.2: move a till to a different store.</summary>
        [HttpPut("{id:guid}/store")]
        [Authorize(Policy = PlutusPolicies.PortalTillsEnrol)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MoveTill([FromRoute] Guid id, [FromBody] MoveTillRequest body)
        {
            if (body == null) return BadRequest(new { detail = "storeId is required." });
            try
            {
                await _enrolment.MoveTillToStoreAsync(_tenant.TenantId, id, body.StoreId, ActingUser);
                _db.Audit(_tenant.TenantId, Actor, "till.move", "Till", id.ToString(), new { body.StoreId });
                await _db.SaveChangesAsync();
                return NoContent();
            }
            catch (EnrolmentException ex)
            {
                return StatusCode(ex.StatusCode, new { detail = ex.Message });
            }
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
        /// so it can show it in the header without the portal.tills.enrol the fleet list needs.
        /// Also returns the till's storeId (additive): the web till uses it to fetch ITS store's
        /// receipt template + info rather than a hardcoded store — different stores print
        /// different addresses. Null when the till row is unknown to this tenant.</summary>
        [HttpGet("{id}/name")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetName([FromRoute] Guid id)
        {
            var name = await _db.TillDetails.AsNoTracking()
                .Where(t => t.TillId == id).Select(t => t.Name).FirstOrDefaultAsync();
            var storeId = await _db.Till.AsNoTracking()
                .Where(t => t.Id == id).Select(t => (int?)t.StoreId).FirstOrDefaultAsync();
            return Ok(new { id, name = name ?? $"Till {id.ToString()[..8]}", storeId });
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

        /// <summary>
        /// FE3.0: the till reports what its local hardware agent said (or that none was found).
        /// Telemetry, not audit — it overwrites in place and writes no AuditLogs row (a poller must
        /// never grow an audit table). Same gate as the device-status read: a device token or an
        /// operator holding pos.sell, and the device must belong to this tenant.
        /// </summary>
        [HttpPost("agent-status")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReportAgentStatus([FromBody] AgentStatusBody body)
        {
            if (body == null || body.DeviceId == Guid.Empty) return NotFound(new { detail = "Unknown device." });
            var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == body.DeviceId && d.TenantId == _tenant.TenantId);
            if (device == null) return NotFound(new { detail = "Unknown device." });

            _db.CurrentUser = ActingUser;
            // clamp to the column widths — a malformed agent reply must not fault the report
            static string Clamp(string s, int max) =>
                string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
            device.AgentVersion = Clamp(body.AgentVersion, 32);
            device.AgentPrinterName = Clamp(body.PrinterName, 128);
            device.AgentPrinterOnline = body.PrinterOnline;
            device.AgentReportedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>The till polls its own device status to know when an approved removal has taken
        /// effect (Revoked) so it can forget the local credential. Readable by operator OR device.
        ///
        /// ⚠ ALSO ANSWERS "WHICH TILL AM I?" (added 2026-08-09). A device knows its own id because
        /// it holds the secret, but everything else per-till — the operator roster above all — is
        /// keyed by <c>tillId</c>, and until now nothing could tell a device what its till was. A
        /// till whose local record predates that being stored had a working device identity and no
        /// way to use it: enrolled, authenticated, and unable to fetch a single operator. Its only
        /// escape was re-enrolment, which mints a second device row for a machine that was already
        /// perfectly well enrolled.
        ///
        /// Answering here costs nothing — the row is already loaded — and it is not a disclosure:
        /// the caller has just proved it IS this device, and the tenant filter below is unchanged.
        /// </summary>
        [HttpGet("devices/{deviceId}/status")]
        [Authorize(Policy = PlutusPolicies.SalesIngest)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDeviceStatus([FromRoute] Guid deviceId)
        {
            var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId);
            if (device == null) return NotFound(new { detail = "Unknown device." });
            return Ok(new { status = device.Status.ToString(), tillId = device.TillId });
        }
    }
}
