#nullable disable

using System;
using System.Globalization;
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

namespace Plutus.Reporting
{
    public sealed record CreatePeriodBody(Guid? CompanyId, string Name, DateOnly StartDay, DateOnly EndDay);

    /// <summary>
    /// WP3.4 financial periods (architecture §7.1): explicit period close — snapshots the
    /// rollup totals and locks the range (late events redirect to the next open day, see
    /// RollupProjection). Reads gated on portal.financials.view; create/close on
    /// portal.company.manage (period discipline is a company-admin act). CSV export draws
    /// from the same rollups the dashboards read, so totals always match.
    /// </summary>
    [ApiController]
    public sealed class PeriodsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public PeriodsController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        [HttpGet("api/v1/periods")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List() =>
            Ok(await _db.FinancialPeriods.AsNoTracking()
                .OrderBy(p => p.StartDay)
                .Select(p => new
                {
                    id = p.Id, companyId = p.CompanyId, name = p.Name,
                    startDay = p.StartDay, endDay = p.EndDay, status = p.Status.ToString(),
                    closedAtUtc = p.ClosedAtUtc, snapshotJson = p.SnapshotJson,
                })
                .ToListAsync());

        [HttpPost("api/v1/periods")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreatePeriodBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Name)) return BadRequest(new { detail = "name is required." });
            if (body.EndDay < body.StartDay) return BadRequest(new { detail = "endDay must be >= startDay." });
            var companyId = body.CompanyId
                ?? await _db.Business.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync();
            if (await _db.FinancialPeriods.AnyAsync(p =>
                    p.CompanyId == companyId && p.StartDay <= body.EndDay && body.StartDay <= p.EndDay))
                return BadRequest(new { detail = "The period overlaps an existing one." });

            _db.CurrentUser = Actor.ToString();
            var period = new FinancialPeriod
            {
                Id = Uuid7.New(), TenantId = _tenant.TenantId, CompanyId = companyId,
                Name = body.Name.Trim(), StartDay = body.StartDay, EndDay = body.EndDay,
                Status = PeriodStatus.Open, CreatedAtUtc = DateTime.UtcNow,
            };
            _db.FinancialPeriods.Add(period);
            _db.Audit(_tenant.TenantId, Actor, "period.create", nameof(FinancialPeriod), period.Id.ToString(), body);
            await _db.SaveChangesAsync();
            return Created($"/api/v1/periods/{period.Id}", new { id = period.Id });
        }

        /// <summary>Close = snapshot the rollup totals for the range and lock it. Idempotent
        /// guard: an already-closed period returns 409 with its snapshot.</summary>
        [HttpPost("api/v1/periods/{id}/close")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Close([FromRoute] Guid id)
        {
            var period = await _db.FinancialPeriods.FirstOrDefaultAsync(p => p.Id == id);
            if (period == null) return NotFound();
            if (period.Status == PeriodStatus.Closed)
                return Conflict(new { detail = "Period is already closed.", snapshotJson = period.SnapshotJson });

            var rollups = await _db.SalesRollups.AsNoTracking()
                .Where(r => r.BusinessDay >= period.StartDay && r.BusinessDay <= period.EndDay)
                .ToListAsync();
            var vat = await _db.VatRollups.AsNoTracking()
                .Where(r => r.BusinessDay >= period.StartDay && r.BusinessDay <= period.EndDay)
                .ToListAsync();

            // Phase 8 (architecture §7.1): outstanding store-credit liability at close — the
            // running Σ of every credit entry up to the period end (issues − redeems − expiries).
            var creditLiabilityPence = await _db.CreditEntries.AsNoTracking()
                .Where(e => e.CreatedAtUtc < period.EndDay.AddDays(1).ToDateTime(TimeOnly.MinValue))
                .SumAsync(e => (long?)e.AmountPence) ?? 0;

            var snapshot = new
            {
                grossPence = rollups.Sum(r => r.GrossPence),
                vatPence = rollups.Sum(r => r.VatPence),
                txnCount = rollups.Sum(r => r.TxnCount),
                outstandingCreditLiabilityPence = creditLiabilityPence,
                vatByRate = vat.GroupBy(v => v.VatRateBp).OrderBy(g => g.Key).Select(g => new
                {
                    vatRateBp = g.Key,
                    grossPence = g.Sum(v => v.GrossPence),
                    netPence = g.Sum(v => v.NetPence),
                    vatPence = g.Sum(v => v.VatPence),
                }),
            };

            _db.CurrentUser = Actor.ToString();
            period.Status = PeriodStatus.Closed;
            period.ClosedAtUtc = DateTime.UtcNow;
            period.ClosedBy = Actor;
            period.SnapshotJson = JsonSerializer.Serialize(snapshot);
            _db.Audit(_tenant.TenantId, Actor, "period.close", nameof(FinancialPeriod), id.ToString(), snapshot);
            await _db.SaveChangesAsync();
            return Ok(new { id, snapshot });
        }

        /// <summary>CSV export from the same rollups the dashboards read — per-day summary
        /// (type=summary) or per-day-per-rate VAT (type=vat) for the range.</summary>
        [HttpGet("api/v1/reports/export.csv")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [Produces("text/csv")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ExportCsv(
            [FromQuery] string type, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;

            switch ((type ?? "summary").ToLowerInvariant())
            {
                case "summary":
                {
                    var rows = await _db.SalesRollups.AsNoTracking()
                        .Where(r => r.BusinessDay >= from && r.BusinessDay <= to).ToListAsync();
                    sb.AppendLine("businessDay,grossPence,vatPence,txnCount");
                    foreach (var g in rows.GroupBy(r => r.BusinessDay).OrderBy(g => g.Key))
                        sb.AppendLine(string.Format(inv, "{0:yyyy-MM-dd},{1},{2},{3}",
                            g.Key, g.Sum(r => r.GrossPence), g.Sum(r => r.VatPence), g.Sum(r => r.TxnCount)));
                    sb.AppendLine(string.Format(inv, "TOTAL,{0},{1},{2}",
                        rows.Sum(r => r.GrossPence), rows.Sum(r => r.VatPence), rows.Sum(r => r.TxnCount)));
                    break;
                }
                case "vat":
                {
                    var rows = await _db.VatRollups.AsNoTracking()
                        .Where(r => r.BusinessDay >= from && r.BusinessDay <= to).ToListAsync();
                    sb.AppendLine("businessDay,vatRateBp,grossPence,netPence,vatPence");
                    foreach (var g in rows.GroupBy(r => (r.BusinessDay, r.VatRateBp)).OrderBy(g => g.Key.BusinessDay).ThenBy(g => g.Key.VatRateBp))
                        sb.AppendLine(string.Format(inv, "{0:yyyy-MM-dd},{1},{2},{3},{4}",
                            g.Key.BusinessDay, g.Key.VatRateBp, g.Sum(r => r.GrossPence), g.Sum(r => r.NetPence), g.Sum(r => r.VatPence)));
                    sb.AppendLine(string.Format(inv, "TOTAL,,{0},{1},{2}",
                        rows.Sum(r => r.GrossPence), rows.Sum(r => r.NetPence), rows.Sum(r => r.VatPence)));
                    break;
                }
                default:
                    return BadRequest(new { detail = "type must be summary|vat." });
            }

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
                $"{type}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }
    }
}
