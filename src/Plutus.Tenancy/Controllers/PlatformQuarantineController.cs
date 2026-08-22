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
using Plutus.SharedKernel;

namespace Plutus.Tenancy.Controllers
{
    /// <summary>
    /// **The sales that did not get in, and what can be done about each.**
    ///
    /// ⚠⚠ THIS EXISTS BECAUSE THE HEALTH DOT COULD ACCUSE AND NOT EXPLAIN. `quarantineOpen > 0` turns
    /// a tenant red on the operator dashboard and holds it there for ever, and until 2026-08-22 there
    /// was NO list endpoint and NO screen anywhere — the platform could say "seven sales are stuck"
    /// and offer no way to learn which, why, or what to do. Matt: *"Where is quarentine? What can I do
    /// to view and resolve these issues?"*
    ///
    /// ⚠ A QUARANTINED SALE IS NOT A LOST SALE AND NOT AN ACCEPTED ONE. The ingest parks anything that
    /// fails T1.3 rather than rewriting it (which would change what a customer was charged) or
    /// dropping it (which would lose money silently). Everything here is therefore a decision somebody
    /// has to make, not a fault to clear.
    ///
    /// ⚠⚠ AND NOT EVERY ROW CAN BE RETRIED — see <see cref="SourceOf"/>. The payload column means three
    /// different things depending on who wrote it, and offering "retry" on a row that cannot be
    /// retried is worse than offering nothing.
    /// </summary>
    [ApiController]
    public sealed class PlatformQuarantineController : ControllerBase
    {
        private readonly MySqlDbContext _db;

        public PlatformQuarantineController(MySqlDbContext db) => _db = db;

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
        private string ActorName => User?.FindFirst(ClaimTypes.Email)?.Value ?? User?.Identity?.Name ?? "operator";

        public sealed record ResolveBody(string Note);

        /// <summary>
        /// Where a row came from, decided from the payload — because the writers disagree about what
        /// `PayloadJson` holds and nothing records it.
        ///
        /// ⚠ `migration` rows hold the bare LEGACY REF, not a sale (`KapowMigrator`:
        /// `PayloadJson = input.LegacyId`). There is nothing to re-ingest, so retry is impossible and
        /// the only honest action is to look the reference up in the old system and dismiss with a
        /// note. All seven of Kapow's current rows are these.
        /// ⚠ `ingest` rows hold a full serialised IngestSaleRequest and CAN be replayed.
        /// ⚠ `webstore` rows hold a Woo order and are replayed by `POST /webstores/{id}/retry`, which
        /// owns the mapping and the credentials — not by this controller.
        /// </summary>
        private static string SourceOf(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return "unknown";
            if (payload[0] != '{') return "migration";      // a bare legacy reference
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("SaleId", out _) || root.TryGetProperty("saleId", out _)) return "ingest";
                if (root.TryGetProperty("id", out _) || root.TryGetProperty("line_items", out _)) return "webstore";
                return "unknown";
            }
            catch (JsonException) { return "unknown"; }
        }

        /// <summary>The human-readable handle for a row — the legacy ref where that is all there is.</summary>
        private static string ReferenceOf(string payload, string source, Guid saleId) =>
            source == "migration" ? payload : saleId.ToString();

        private static string HintFor(string source) => source switch
        {
            "migration" => "No sale was stored — only the legacy reference. Look it up in the old system, then dismiss with a note.",
            "webstore" => "Replay this from the webstore connector: Webstores → the store → Retry parked.",
            "ingest" => "Replayable here once the reason no longer applies.",
            _ => "Unrecognised payload — dismiss with a note.",
        };

        /// <summary>
        /// Every quarantined sale across every tenant, newest first.
        ///
        /// ⚠ `state=open` by default. A resolved row is kept for ever on purpose — "we looked at this
        /// and decided" is the answer to the same question being asked again next year — but the
        /// working list is the open one.
        /// </summary>
        [HttpGet("api/v1/platform/quarantine")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] string state = "open", [FromQuery] Guid? tenantId = null)
        {
            var q = _db.SaleQuarantine.IgnoreQueryFilters().AsNoTracking();

            if (string.Equals(state, "open", StringComparison.OrdinalIgnoreCase))
                q = q.Where(r => r.ResolvedAtUtc == null);
            else if (string.Equals(state, "resolved", StringComparison.OrdinalIgnoreCase))
                q = q.Where(r => r.ResolvedAtUtc != null);
            if (tenantId is { } t) q = q.Where(r => r.TenantId == t);

            var rows = await q.OrderByDescending(r => r.ReceivedAtUtc).Take(500).ToListAsync();

            var names = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name);

            return Ok(rows.Select(r =>
            {
                var source = SourceOf(r.PayloadJson);
                return new
                {
                    id = r.Id,
                    tenantId = r.TenantId,
                    tenantName = names.TryGetValue(r.TenantId, out var n) ? n : null,
                    saleId = r.SaleId,
                    reason = r.Reason,
                    source,
                    reference = ReferenceOf(r.PayloadJson, source, r.SaleId),
                    // ⚠ Retry is offered ONLY where this controller can actually do it. A webstore row
                    // is replayable, but through its own connector endpoint, so it is false here and
                    // the hint says where to go instead.
                    canRetry = source == "ingest",
                    retryHint = HintFor(source),
                    receivedAtUtc = r.ReceivedAtUtc,
                    resolvedAtUtc = r.ResolvedAtUtc,
                    resolvedBy = r.ResolvedBy,
                    resolutionNote = r.ResolutionNote,
                };
            }));
        }

        /// <summary>The stored payload, for the one row an operator has opened. Never in the list —
        /// a Woo order is kilobytes and five hundred of them is not a page.</summary>
        [HttpGet("api/v1/platform/quarantine/{id:guid}/payload")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Payload([FromRoute] Guid id)
        {
            var row = await _db.SaleQuarantine.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return NotFound();
            return Ok(new { id = row.Id, source = SourceOf(row.PayloadJson), payload = row.PayloadJson });
        }

        /// <summary>
        /// Dismiss a row: somebody looked, decided, and said why.
        ///
        /// ⚠⚠ THE NOTE IS REQUIRED. A stuck sale waved through with no reason is exactly the one
        /// somebody asks about at year end, and "it was already like that" is not an answer. This is
        /// also the difference between this and the webstore retry endpoint, which stamps
        /// `ResolvedAtUtc` automatically and records neither who nor why.
        ///
        /// ⚠ RESOLVING DOES NOT INGEST THE SALE. It records that the platform is no longer waiting on
        /// it — so the tenant's health dot goes green while the sale stays absent from reporting. That
        /// is the honest outcome for a legacy row that cannot be reconstructed, and it must never be
        /// confused with the sale having been recovered.
        /// </summary>
        [HttpPost("api/v1/platform/quarantine/{id:guid}/resolve")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Resolve([FromRoute] Guid id, [FromBody] ResolveBody body)
        {
            var note = body?.Note?.Trim();
            if (string.IsNullOrWhiteSpace(note))
                return BadRequest(new { detail = "A note is required — say what was decided and why." });

            var row = await _db.SaleQuarantine.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return NotFound();
            if (row.ResolvedAtUtc != null) return NoContent();   // idempotent

            // ⚠ The context refuses to save without an actor (RepositoryContext.SaveMethods).
            _db.CurrentUser = Actor.ToString();
            row.ResolvedAtUtc = DateTime.UtcNow;
            row.ResolvedBy = ActorName;
            row.ResolutionNote = note.Length <= 500 ? note : note.Substring(0, 500);

            _db.Audit(row.TenantId, Actor, "quarantine.resolve", "SaleQuarantine", id.ToString(),
                new { row.Reason, note = row.ResolutionNote });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Re-run an `ingest` row through validation — for when the reason has since been fixed.
        ///
        /// ⚠ 409, NOT A SILENT NO-OP, when the row cannot be replayed. A migration row holds a legacy
        /// reference and no sale; pretending to retry it and reporting success would be the worst
        /// possible answer, because the operator would believe the sale had been recovered.
        /// </summary>
        [HttpPost("api/v1/platform/quarantine/{id:guid}/retry")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Retry([FromRoute] Guid id)
        {
            var row = await _db.SaleQuarantine.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (row == null) return NotFound();

            var source = SourceOf(row.PayloadJson);
            if (source != "ingest")
                return Conflict(new { detail = HintFor(source), source });

            // ⚠ DELIBERATELY NOT WIRED, AND IT SAYS SO RATHER THAN PRETENDING. Replaying an ingest row
            // means reconstructing the tenant and device context the sale arrived on, which this
            // controller does not hold. No tenant has a row of this kind today — all of Kapow's are
            // migration rows — so wiring it now would ship an untestable path against no data.
            return Ok(new
            {
                detail = "Replay of ingest rows is not implemented yet — no tenant has one. Raised as a follow-up.",
                source,
            });
        }
    }
}
