using System;
using System.Collections.Generic;
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

namespace Plutus.Reporting
{
    /// <summary>
    /// Which reports the portal has published to a till — ruling 5b(a), 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"Portal shows which reports a till can show. Separate permissions need to be created
    /// for viewing them."* The permission half shipped as 5b(b). This is the other half: an owner
    /// deciding that the counter till shows Takings and nothing else, while the back office shows
    /// everything.
    ///
    /// ⚠⚠ **THE PUBLISH DECIDES THE MENU, THE PERMISSION DECIDES THE DOOR.** They are independent, and
    /// the client applies both: a report is listed only when it is published to that till AND readable by
    /// that operator. A published report the operator may not read shows **nothing** — never a refusal,
    /// because a greyed-out row leaks what other roles can see.
    ///
    /// ⚠⚠ **NO ROW MEANS EVERY REPORT.** A tenant who has never opened the portal screen must see the
    /// menu they saw yesterday. The rule is <c>ReportCatalogue.PublishedOr</c> and it is shared, so the
    /// server, the MAUI till and the web till cannot disagree about what silence means.
    ///
    /// ⚠ Modelled on <c>GatewayConfigController</c>, which is the established shape for "portal writes,
    /// till reads": a permissive read the till can call, and a <c>portal.company.manage</c> write.
    /// </summary>
    [Route("api/v1/reports")]
    [ApiController]
    public sealed class ReportPublicationController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public ReportPublicationController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor =>
            Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>
        /// Every report that CAN be published — the superset the portal ticks boxes against.
        ///
        /// ⚠ Any authenticated caller. It is a list of report names and descriptions; it contains no
        /// figures and no tenant data, and both the portal and the tills need it. Gating it behind a
        /// portal permission would stop a till validating a key it had been sent.
        /// </summary>
        [HttpGet("catalogue")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult Catalogue() =>
            Ok(ReportCatalogue.All.Select(e => new { key = e.Key, label = e.Label, blurb = e.Blurb }));

        /// <summary>
        /// What THIS TILL may offer. The till calls this on the settings cadence and filters its own menu.
        ///
        /// ⚠ Any authenticated caller, like <c>payments/gateway/active</c>: it says which report NAMES are
        /// on the menu, not what is in them. Every report endpoint is separately gated (5b(b)), so this
        /// cannot widen access to a figure.
        ///
        /// ⚠⚠ IT NEVER 404s AND NEVER RETURNS AN ERROR FOR "NOT CONFIGURED". A till that cannot get an
        /// answer must not lose its Reports tab, so silence resolves to the full catalogue — the same rule
        /// the clients apply when offline.
        /// </summary>
        /// <param name="tillId">The till asking. Omitted or unknown falls back to the tenant default.</param>
        [HttpGet("published")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Published([FromQuery] Guid? tillId)
        {
            var rows = await _db.ReportPublications.AsNoTracking().ToListAsync();

            // ⚠ THE MORE SPECIFIC WINS. A row for this till overrides the tenant default; that is the
            // whole point of having two levels.
            var row = (tillId.HasValue ? rows.FirstOrDefault(r => r.TillId == tillId.Value) : null)
                      ?? rows.FirstOrDefault(r => r.TillId == null);

            var keys = ReportCatalogue.PublishedOr(Parse(row?.KeysJson));

            return Ok(new
            {
                keys,
                // ⚠ Told to the client so a portal screen can say "this till follows the shop default"
                // rather than implying somebody chose this list for it.
                scope = row == null ? "default-all" : row.TillId.HasValue ? "till" : "tenant",
                updatedAtUtc = row?.UpdatedAtUtc,
            });
        }

        /// <summary>
        /// What the portal screen needs: the catalogue, the tenant default, and every per-till override.
        /// </summary>
        [HttpGet("publication")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Get()
        {
            var rows = await _db.ReportPublications.AsNoTracking().ToListAsync();
            var tenantRow = rows.FirstOrDefault(r => r.TillId == null);

            return Ok(new
            {
                catalogue = ReportCatalogue.All.Select(e => new { key = e.Key, label = e.Label, blurb = e.Blurb }),
                // ⚠ NULL, not an empty array, when nothing has been chosen — the portal must be able to
                // show "everything (nothing chosen yet)" differently from "nothing", because a shop that
                // ticked every box and a shop that has never looked are not in the same state.
                tenantKeys = tenantRow == null ? null : ReportCatalogue.PublishedOr(Parse(tenantRow.KeysJson)),
                tenantUpdatedAtUtc = tenantRow?.UpdatedAtUtc,
                tills = rows.Where(r => r.TillId.HasValue).Select(r => new
                {
                    tillId = r.TillId,
                    keys = ReportCatalogue.PublishedOr(Parse(r.KeysJson)),
                    updatedAtUtc = r.UpdatedAtUtc,
                }),
            });
        }

        /// <summary>
        /// Publish a set of reports — to the whole tenant, or to one till.
        ///
        /// ⚠ Audited. "Who turned the VAT report off on the counter till, and when" is the first question
        /// after somebody cannot find it.
        /// </summary>
        [HttpPut("publication")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Set([FromBody] SetPublicationBody body)
        {
            if (body?.Keys == null)
                return BadRequest(new { detail = "Send the list of report keys to publish." });

            // ⚠ REFUSE AN UNKNOWN KEY rather than silently dropping it. `PublishedOr` drops unknown keys
            // when READING, because old stored data must not break a till — but accepting one on the way
            // IN would let a typo look saved and then quietly do nothing.
            var unknown = body.Keys.Where(k => !ReportCatalogue.IsKnown(k)).Distinct().ToList();
            if (unknown.Count > 0)
                return BadRequest(new { detail = $"Not reports this platform knows about: {string.Join(", ", unknown)}." });

            // ⚠ Stored in catalogue order and de-duplicated, so what comes back out is stable however the
            // screen happened to send it.
            var keys = ReportCatalogue.Keys.Where(k => body.Keys.Contains(k)).ToList();

            var row = await _db.ReportPublications
                .FirstOrDefaultAsync(r => r.TillId == body.TillId);

            _db.CurrentUser = Actor.ToString();

            if (row == null)
            {
                row = new ReportPublication
                {
                    Id = Uuid7.New(),
                    TenantId = _tenant.TenantId,
                    TillId = body.TillId,
                };
                _db.ReportPublications.Add(row);
            }

            row.KeysJson = JsonSerializer.Serialize(keys);
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = Actor.ToString();

            _db.Audit(_tenant.TenantId, Actor, "reports.publication", nameof(ReportPublication),
                row.Id.ToString(),
                $"till={row.TillId?.ToString() ?? "(all)"} keys={string.Join("|", keys)}");

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Stop overriding one till — it goes back to following the tenant default.
        ///
        /// ⚠ Deleting the TENANT row (no tillId) is allowed and means "nobody has chosen", which resolves
        /// to every report. That is the documented way back to the out-of-the-box state.
        /// </summary>
        [HttpDelete("publication")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Clear([FromQuery] Guid? tillId)
        {
            var row = await _db.ReportPublications.FirstOrDefaultAsync(r => r.TillId == tillId);
            if (row == null) return NoContent();   // ⚠ Idempotent: already following the default.

            _db.CurrentUser = Actor.ToString();
            _db.ReportPublications.Remove(row);
            _db.Audit(_tenant.TenantId, Actor, "reports.publication.clear", nameof(ReportPublication),
                row.Id.ToString(), $"till={tillId?.ToString() ?? "(all)"}");
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// ⚠ Returns NULL for "nothing stored" and an empty list for "stored, and empty" — the distinction
        /// the whole default rests on. ⚠ A malformed value is treated as nothing stored rather than
        /// throwing: a till must not lose its Reports tab over a bad row.
        /// </summary>
        private static IReadOnlyList<string> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public sealed class SetPublicationBody
        {
            /// <summary>Null for the tenant default; a till id to override that one till.</summary>
            public Guid? TillId { get; set; }

            public List<string> Keys { get; set; }
        }
    }
}
