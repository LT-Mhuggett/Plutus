#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Plutus.Reporting
{
    /// <summary>
    /// WP3.3 report surface (architecture §7.1): summary + VAT from the pre-aggregated
    /// rollups (any level of the spine = SUM over till-grain rows), full sale drill-down
    /// from SalesV2, and a platform-admin rebuild trigger.
    /// level = company|store|till (default company); granularity = day|week|month|year.
    /// </summary>
    [ApiController]
    public sealed class ReportsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public ReportsController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private static bool TryPeriod(string granularity, DateOnly day, out string period)
        {
            switch ((granularity ?? "day").ToLowerInvariant())
            {
                case "day": period = day.ToString("yyyy-MM-dd"); return true;
                case "week":
                {
                    // ISO week, keyed by the Monday of the week.
                    var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                    period = monday.ToString("yyyy-MM-dd");
                    return true;
                }
                case "month": period = day.ToString("yyyy-MM"); return true;
                case "year": period = day.ToString("yyyy"); return true;
                default: period = null; return false;
            }
        }

        private IQueryable<SalesRollup> Filtered(string level, string id, DateOnly from, DateOnly to)
        {
            var q = _db.SalesRollups.AsNoTracking().Where(r => r.BusinessDay >= from && r.BusinessDay <= to);
            switch ((level ?? "company").ToLowerInvariant())
            {
                case "till" when Guid.TryParse(id, out var tillId): return q.Where(r => r.TillId == tillId);
                case "store" when int.TryParse(id, out var storeId): return q.Where(r => r.StoreId == storeId);
                case "company" when Guid.TryParse(id, out var companyId): return q.Where(r => r.CompanyId == companyId);
                default: return q; // whole tenant (company view with a single company)
            }
        }

        [HttpGet("api/v1/reports/summary")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Summary(
            [FromQuery] string level, [FromQuery] string id,
            [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string granularity = "day")
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            if (!TryPeriod(granularity, from, out _))
                return BadRequest(new { detail = "granularity must be day|week|month|year." });

            var rows = await Filtered(level, id, from, to).ToListAsync();
            var buckets = rows
                .GroupBy(r => { TryPeriod(granularity, r.BusinessDay, out var p); return p; })
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new
                {
                    period = g.Key,
                    grossPence = g.Sum(r => r.GrossPence),
                    vatPence = g.Sum(r => r.VatPence),
                    txnCount = g.Sum(r => r.TxnCount),
                    avgBasketPence = g.Sum(r => r.TxnCount) == 0 ? 0 : g.Sum(r => r.GrossPence) / g.Sum(r => r.TxnCount),
                })
                .ToList();

            return Ok(new
            {
                level = (level ?? "company").ToLowerInvariant(),
                id,
                from = from.ToString("yyyy-MM-dd"),
                to = to.ToString("yyyy-MM-dd"),
                granularity = (granularity ?? "day").ToLowerInvariant(),
                totals = new
                {
                    grossPence = rows.Sum(r => r.GrossPence),
                    vatPence = rows.Sum(r => r.VatPence),
                    txnCount = rows.Sum(r => r.TxnCount),
                    avgBasketPence = rows.Sum(r => r.TxnCount) == 0 ? 0 : rows.Sum(r => r.GrossPence) / rows.Sum(r => r.TxnCount),
                },
                buckets,
            });
        }

        [HttpGet("api/v1/reports/vat")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Vat(
            [FromQuery] string level, [FromQuery] string id,
            [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string granularity = "month")
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            if (!TryPeriod(granularity, from, out _))
                return BadRequest(new { detail = "granularity must be day|week|month|year." });

            var q = _db.VatRollups.AsNoTracking().Where(r => r.BusinessDay >= from && r.BusinessDay <= to);
            switch ((level ?? "company").ToLowerInvariant())
            {
                case "store" when int.TryParse(id, out var storeId): q = q.Where(r => r.StoreId == storeId); break;
                case "company" when Guid.TryParse(id, out var companyId): q = q.Where(r => r.CompanyId == companyId); break;
            }
            var rows = await q.ToListAsync();

            var buckets = rows
                .GroupBy(r => { TryPeriod(granularity, r.BusinessDay, out var p); return (Period: p, r.VatRateBp); })
                .OrderBy(g => g.Key.Period, StringComparer.Ordinal).ThenBy(g => g.Key.VatRateBp)
                .Select(g => new
                {
                    period = g.Key.Period,
                    vatRateBp = g.Key.VatRateBp,
                    grossPence = g.Sum(r => r.GrossPence),
                    netPence = g.Sum(r => r.NetPence),
                    vatPence = g.Sum(r => r.VatPence),
                })
                .ToList();

            return Ok(new
            {
                totals = new { grossPence = rows.Sum(r => r.GrossPence), netPence = rows.Sum(r => r.NetPence), vatPence = rows.Sum(r => r.VatPence) },
                buckets,
            });
        }

        /// <summary>WP3.5 drill-down support: the sales in a day range (day-level view between
        /// the rollup buckets and the single-sale detail). Capped at 500 rows per call.</summary>
        [HttpGet("api/v1/sales")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SalesList(
            [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? tillId, [FromQuery] int take = 200)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            take = Math.Clamp(take, 1, 500);
            var rows = await _db.SalesV2.AsNoTracking()
                .Where(s => s.BusinessDay >= from && s.BusinessDay <= to)
                .Where(s => tillId == null || s.TillId == tillId)
                .OrderByDescending(s => s.OccurredAtUtc).Take(take)
                .Select(s => new
                {
                    id = s.Id, businessDay = s.BusinessDay, occurredAtUtc = s.OccurredAtUtc,
                    tillId = s.TillId, channel = s.Channel.ToString(),
                    grossPence = s.GrossPence, vatPence = s.VatPence,
                    operatorUserId = s.OperatorUserId, legacyRef = s.LegacyRef,
                })
                .ToListAsync();
            return Ok(rows);
        }

        /// <summary>Full drill-down of one platform sale (the immutable record: lines,
        /// tenders, device, operator).</summary>
        [HttpGet("api/v1/sales/{saleId}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SaleDetail([FromRoute] Guid saleId)
        {
            var sale = await _db.SalesV2.AsNoTracking()
                .Include(s => s.Lines).Include(s => s.Tenders)
                .FirstOrDefaultAsync(s => s.Id == saleId);
            if (sale == null) return NotFound();

            return Ok(new
            {
                id = sale.Id,
                tillId = sale.TillId,
                deviceId = sale.DeviceId,
                deviceSeq = sale.DeviceSeq,
                channel = sale.Channel.ToString(),
                businessDay = sale.BusinessDay.ToString("yyyy-MM-dd"),
                occurredAtUtc = sale.OccurredAtUtc,
                receivedAtUtc = sale.ReceivedAtUtc,
                grossPence = sale.GrossPence,
                vatPence = sale.VatPence,
                operatorUserId = sale.OperatorUserId,
                legacyRef = sale.LegacyRef,
                note = sale.Note,
                vatReconstructed = sale.VatReconstructed,
                lines = sale.Lines.OrderBy(l => l.LineNo).Select(l => new
                {
                    lineNo = l.LineNo, itemId = l.ItemId, qty = l.Qty,
                    unitPricePence = l.UnitPricePence, discountPence = l.DiscountPence,
                    lineGrossPence = l.LineGrossPence, vatRateBp = l.VatRateBp, vatAmountPence = l.VatAmountPence,
                    overriddenFromPence = l.OverriddenFromPence, discountsJson = l.DiscountsJson,
                }),
                tenders = sale.Tenders.Select(t => new
                {
                    tenderType = t.TenderType.ToString(), amountPence = t.AmountPence,
                    changePence = t.ChangePence, providerRef = t.ProviderRef,
                }),
            });
        }

        public sealed record ItemSoldRow(
            DateTime dateSold, string itemIdOne, string itemName, int storeId, Guid tillId,
            string tillName, Guid staffId, string staffName,
            int qty, long unitPricePence, long discountPence, long lineGrossPence);

        /// <summary>WP11.4 items-sold report (NatApp "Stock Outtake" parity). Sourced from the
        /// LEGACY Trans+Sales tables (they carry ItemIdOne for name joins and uniformly cover
        /// 2019→today; re-point to SaleLines when legacy retires — contract unchanged). Filters:
        /// date range + optional store/till/item/staff. Capped.</summary>
        private async Task<List<ItemSoldRow>> ItemsSoldAsync(
            DateOnly from, DateOnly to, int? storeId, Guid? tillId, Guid? operatorUserId, string itemIdOne, int take)
        {
            var fromDt = from.ToDateTime(TimeOnly.MinValue);
            var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue); // inclusive end day

            var q = from t in _db.Trans.AsNoTracking()
                    join s in _db.Sales.AsNoTracking() on t.IdTwo equals s.Id
                    where s.DateOfSale >= fromDt && s.DateOfSale < toDt
                    select new
                    {
                        t.IdOne, SaleId = t.IdTwo, t.ItemIdOne, t.Amount, t.ItemCostPrice,
                        t.TillId, s.DateOfSale, s.StoreId, s.EmployeeId,
                    };
            if (storeId != null) q = q.Where(x => x.StoreId == storeId);
            if (tillId != null) q = q.Where(x => x.TillId == tillId);
            if (operatorUserId != null) q = q.Where(x => x.EmployeeId == operatorUserId);
            if (!string.IsNullOrWhiteSpace(itemIdOne)) q = q.Where(x => x.ItemIdOne == itemIdOne);

            var lines = await q.OrderByDescending(x => x.DateOfSale).Take(take).ToListAsync();
            if (lines.Count == 0) return new List<ItemSoldRow>();

            var itemIds = lines.Select(l => l.ItemIdOne).Distinct().ToList();
            var names = await _db.Items.AsNoTracking().IgnoreQueryFilters()
                .Where(i => itemIds.Contains(i.IdOne))
                .Select(i => new { i.IdOne, i.Name })
                .ToDictionaryAsync(i => i.IdOne, i => i.Name);

            var tillIds = lines.Select(l => l.TillId).Distinct().ToList();
            var tillNames = await _db.TillDetails.AsNoTracking()
                .Where(td => tillIds.Contains(td.TillId))
                .ToDictionaryAsync(td => td.TillId, td => td.Name);

            var staffIds = lines.Select(l => l.EmployeeId).Distinct().ToList();
            var staffNames = await _db.People.AsNoTracking().IgnoreQueryFilters()
                .Where(p => staffIds.Contains(p.Id))
                .Select(p => new { p.Id, p.FName, p.LName })
                .ToDictionaryAsync(p => p.Id, p => $"{p.FName} {p.LName}".Trim());

            // Σ discount rate per legacy transaction line (TransactionId + SaleId composite).
            var saleIds = lines.Select(l => l.SaleId).Distinct().ToList();
            var discRows = await _db.Transaction_Discounts.AsNoTracking()
                .Where(d => saleIds.Contains(d.SaleId))
                .Select(d => new { d.TransactionId, d.SaleId, d.DiscountRate })
                .ToListAsync();
            var discByLine = discRows
                .GroupBy(d => (d.TransactionId, d.SaleId))
                .ToDictionary(g => g.Key, g => g.Sum(d => d.DiscountRate));

            return lines.Select(l =>
            {
                var unitPence = (long)Math.Round(l.ItemCostPrice * 100m, MidpointRounding.AwayFromZero);
                var beforeDiscount = unitPence * l.Amount;
                discByLine.TryGetValue((l.IdOne, l.SaleId), out var rate);
                var discountPence = (long)Math.Round(beforeDiscount * rate, MidpointRounding.AwayFromZero);
                return new ItemSoldRow(
                    l.DateOfSale, l.ItemIdOne, names.TryGetValue(l.ItemIdOne, out var n) ? n : "?",
                    l.StoreId, l.TillId,
                    tillNames.TryGetValue(l.TillId, out var tn) ? tn : $"Till {l.TillId.ToString()[..8]}",
                    l.EmployeeId, staffNames.TryGetValue(l.EmployeeId, out var sn) && !string.IsNullOrWhiteSpace(sn) ? sn : "—",
                    l.Amount, unitPence, discountPence, beforeDiscount - discountPence);
            }).ToList();
        }

        [HttpGet("api/v1/reports/items-sold")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ItemsSold(
            [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? storeId,
            [FromQuery] Guid? tillId, [FromQuery] Guid? operatorUserId, [FromQuery] string itemIdOne, [FromQuery] int take = 1000)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            take = Math.Clamp(take, 1, 5000);
            var rows = await ItemsSoldAsync(from, to, storeId, tillId, operatorUserId, itemIdOne, take);
            return Ok(new
            {
                from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), count = rows.Count,
                totals = new
                {
                    qty = rows.Sum(r => r.qty),
                    grossPence = rows.Sum(r => r.lineGrossPence),
                    discountPence = rows.Sum(r => r.discountPence),
                },
                rows,
            });
        }

        [HttpGet("api/v1/reports/items-sold.csv")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [Produces("text/csv")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ItemsSoldCsv(
            [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? storeId,
            [FromQuery] Guid? tillId, [FromQuery] Guid? operatorUserId, [FromQuery] string itemIdOne, [FromQuery] int take = 5000)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            take = Math.Clamp(take, 1, 5000);
            var rows = await ItemsSoldAsync(from, to, storeId, tillId, operatorUserId, itemIdOne, take);
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("dateSold,itemBarcode,itemName,storeId,till,staff,qty,unitPricePence,discountPence,lineGrossPence");
            foreach (var r in rows)
                sb.AppendLine(string.Format(inv, "{0:yyyy-MM-dd HH:mm},{1},\"{2}\",{3},\"{4}\",\"{5}\",{6},{7},{8},{9}",
                    r.dateSold, r.itemIdOne, r.itemName.Replace("\"", "\"\""), r.storeId,
                    r.tillName.Replace("\"", "\"\""), r.staffName.Replace("\"", "\"\""),
                    r.qty, r.unitPricePence, r.discountPence, r.lineGrossPence));
            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
                $"items-sold-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        /// <summary>Staff who have sold (for the report filters), optionally scoped to a store.
        /// Distinct sellers with names — the store-level filter on the POS and the cross-store
        /// filter in the portal both read this.</summary>
        [HttpGet("api/v1/reports/staff")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Staff([FromQuery] int? storeId)
        {
            var sellers = _db.Sales.AsNoTracking().AsQueryable();
            if (storeId != null) sellers = sellers.Where(s => s.StoreId == storeId);
            var ids = await sellers.Select(s => s.EmployeeId).Distinct().ToListAsync();
            var staff = await _db.People.AsNoTracking().IgnoreQueryFilters()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new { id = p.Id, name = (p.FName + " " + p.LName).Trim() })
                .ToListAsync();
            return Ok(staff.Where(s => !string.IsNullOrWhiteSpace(s.name)).OrderBy(s => s.name));
        }

        /// <summary>Rebuild the tenant's rollups from SalesV2 (platform-admin; also run at
        /// cutover to fold in migrated rows, which carry no outbox events).</summary>
        [HttpPost("api/v1/reports/rebuild")]
        [Authorize(Policy = PlutusPolicies.PlatformAdmin)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Rebuild()
        {
            var tenantId = _tenant.TenantId == Guid.Empty ? WellKnownTenants.Kapow : _tenant.TenantId;
            var (salesRollups, vatRollups, scanned) = await RollupRebuilder.RebuildAsync(_db, tenantId);
            var actor = Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
            _db.Audit(tenantId, actor, "reports.rebuild", "SalesRollup", "*",
                new { salesRollups, vatRollups, scanned });
            await _db.SaveChangesAsync();
            return Ok(new { salesRollups, vatRollups, scanned });
        }
    }
}
