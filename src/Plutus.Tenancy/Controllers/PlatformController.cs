using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
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
    public sealed record SetStatusBody(byte Status);
    public sealed record SetEntitlementsBody(string[] Entitlements, string Plan);
    public sealed record RequestDeletionBody(int GraceDays);

    /// <summary>
    /// Phase 10 platform admin: tenant lifecycle (status + entitlements), offboarding (scheduled
    /// deletion + data export), and the billing webhook. All lifecycle mutations are platform-admin
    /// and audited; the webhook is anonymous but HMAC-verified by the billing provider seam.
    /// </summary>
    [ApiController]
    public sealed class PlatformController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly TenantLifecycleService _lifecycle;
        private readonly IBillingProvider _billing;

        public PlatformController(MySqlDbContext db, ITenantContext tenant, TenantLifecycleService lifecycle, IBillingProvider billing)
        {
            _db = db;
            _tenant = tenant;
            _lifecycle = lifecycle;
            _billing = billing;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/tenants")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List() =>
            Ok(await _db.Tenants.AsNoTracking().OrderBy(t => t.Name).Select(t => new
            {
                id = t.Id, name = t.Name, status = t.Status, plan = t.Plan,
                entitlements = EntitlementService.Parse(t.Entitlements), createdAtUtc = t.CreatedAtUtc,
            }).ToListAsync());

        [HttpPut("api/v1/tenants/{id}/status")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> SetStatus([FromRoute] Guid id, [FromBody] SetStatusBody body) =>
            Guarded(async () =>
            {
                await _lifecycle.SetStatusAsync(id, body.Status, Actor.ToString());
                _db.Audit(id, Actor, "tenant.status", nameof(Tenant), id.ToString(), body);
                await _db.SaveChangesAsync();
                return NoContent();
            });

        [HttpPut("api/v1/tenants/{id}/entitlements")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> SetEntitlements([FromRoute] Guid id, [FromBody] SetEntitlementsBody body) =>
            Guarded(async () =>
            {
                await _lifecycle.SetEntitlementsAsync(id, body.Entitlements, body.Plan, Actor.ToString());
                _db.Audit(id, Actor, "tenant.entitlements", nameof(Tenant), id.ToString(), body);
                await _db.SaveChangesAsync();
                return NoContent();
            });

        [HttpPost("api/v1/tenants/{id}/deletion")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public Task<IActionResult> RequestDeletion([FromRoute] Guid id, [FromBody] RequestDeletionBody body) =>
            Guarded(async () =>
            {
                var schedule = await _lifecycle.RequestDeletionAsync(id, body?.GraceDays ?? 30, Actor);
                _db.Audit(id, Actor, "tenant.delete.requested", nameof(DeletionSchedule), schedule.Id.ToString(),
                    new { schedule.ExecuteAfterUtc });
                await _db.SaveChangesAsync();
                return (IActionResult)Created($"/api/v1/tenants/deletion/{schedule.Id}",
                    new { id = schedule.Id, executeAfterUtc = schedule.ExecuteAfterUtc });
            });

        [HttpPost("api/v1/tenants/deletion/{scheduleId}/cancel")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public Task<IActionResult> CancelDeletion([FromRoute] Guid scheduleId) =>
            Guarded(async () =>
            {
                var schedule = await _db.DeletionSchedules.AsNoTracking().FirstOrDefaultAsync(d => d.Id == scheduleId);
                await _lifecycle.CancelDeletionAsync(scheduleId, Actor.ToString());
                if (schedule != null) // reopen the portal (Closed → Active) on cancel
                    await _lifecycle.SetStatusAsync(schedule.TenantId, TenantStatus.Active, Actor.ToString());
                _db.Audit(schedule?.TenantId ?? Guid.Empty, Actor, "tenant.delete.cancelled", nameof(DeletionSchedule), scheduleId.ToString(), new { });
                await _db.SaveChangesAsync();
                return NoContent();
            });

        /// <summary>WP10.3: export every tenant-owned table as CSV + a manifest, streamed as a ZIP.
        /// Tables are discovered from information_schema (any TenantId column), so the export tracks
        /// the schema. Row counts + the SalesV2 gross total go in the manifest for round-trip checks.</summary>
        [HttpGet("api/v1/tenants/{id}/export")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [Produces("application/zip")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Export([FromRoute] Guid id)
        {
            var tenant = await _lifecycle.FindAsync(id);
            if (tenant == null) return NotFound();

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            var tables = new List<string>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT table_name FROM information_schema.columns " +
                                  "WHERE table_schema = DATABASE() AND column_name = 'TenantId' ORDER BY table_name";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) tables.Add(r.GetString(0));
            }

            using var buffer = new MemoryStream();
            var counts = new Dictionary<string, int>();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var table in tables)
                {
                    var (csv, rows) = await DumpTableCsvAsync(conn, table, id);
                    counts[table] = rows;
                    var entry = zip.CreateEntry($"data/{table}.csv");
                    await using var es = entry.Open();
                    await using var sw = new StreamWriter(es, Encoding.UTF8);
                    await sw.WriteAsync(csv);
                }

                var manifest = new
                {
                    tenant = new { tenant.Id, tenant.Name, tenant.Status, tenant.Plan },
                    exportedAtUtc = DateTime.UtcNow,
                    rowCounts = counts,
                    salesGrossPence = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                        .Where(s => s.TenantId == id).SumAsync(s => (long?)s.GrossPence) ?? 0,
                };
                var mEntry = zip.CreateEntry("manifest.json");
                await using var ms = mEntry.Open();
                await JsonSerializer.SerializeAsync(ms, manifest, new JsonSerializerOptions { WriteIndented = true });
            }

            _db.Audit(id, Actor, "tenant.export", nameof(Tenant), id.ToString(), new { tables = counts.Count });
            await _db.SaveChangesAsync();
            return File(buffer.ToArray(), "application/zip", $"tenant-{id}-export-{DateTime.UtcNow:yyyyMMddHHmm}.zip");
        }

        private static async Task<(string Csv, int Rows)> DumpTableCsvAsync(System.Data.Common.DbConnection conn, string table, Guid tenantId)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM `{table}` WHERE `TenantId` = @tid";
            var p = cmd.CreateParameter(); p.ParameterName = "@tid"; p.Value = tenantId.ToString(); cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync();

            var sb = new StringBuilder();
            for (var i = 0; i < reader.FieldCount; i++) sb.Append(i > 0 ? "," : "").Append(Csv(reader.GetName(i)));
            sb.AppendLine();
            var rows = 0;
            while (await reader.ReadAsync())
            {
                rows++;
                for (var i = 0; i < reader.FieldCount; i++)
                    sb.Append(i > 0 ? "," : "").Append(Csv(reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? ""));
                sb.AppendLine();
            }
            return (sb.ToString(), rows);
        }

        private static string Csv(string v) =>
            v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

        // ── billing ──

        [HttpGet("api/v1/billing/entitlements")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> MyEntitlements()
        {
            var tenantId = _tenant.TenantId == Guid.Empty ? WellKnownTenants.Kapow : _tenant.TenantId;
            var json = await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Entitlements).FirstOrDefaultAsync();
            return Ok(new { tenantId, provider = _billing.Name, entitlements = EntitlementService.Parse(json) });
        }

        /// <summary>Billing webhook — anonymous but HMAC-verified by the provider seam, then applied
        /// (entitlements/plan/status) in one audited save. Invalid signature → 400.</summary>
        [HttpPost("api/v1/billing/webhook")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Webhook()
        {
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = Request.Headers["X-Billing-Signature"].ToString();

            if (!_billing.TryHandleWebhook(payload, signature, out var change))
                return BadRequest(new { detail = "Invalid or unhandled billing webhook." });

            await _lifecycle.ApplyBillingChangeAsync(change, $"billing:{_billing.Name}");
            _db.Audit(change.TenantId, Guid.Empty, "billing.webhook", nameof(Tenant), change.TenantId.ToString(),
                new { change.Entitlements, change.Plan, change.Status });
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (EnrolmentException ex) { return Problem(detail: ex.Message, statusCode: ex.StatusCode); }
        }
    }
}
