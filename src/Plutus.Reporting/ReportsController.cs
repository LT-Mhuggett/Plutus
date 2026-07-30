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

        /// <summary>Every period key in [from,to] at the granularity — the zero-fill spine so the
        /// chart shows quiet days/months too.</summary>
        /// <summary>Barcode carried in a web-till line's DiscountsJson metadata
        /// (<c>{"itemIdOne":"…"}</c>) — the same key the ingest reads onto SaleLine.ItemIdOne.
        /// Null on parse failure / absent key.</summary>
        private static string BarcodeFromDiscountsJson(string discountsJson)
        {
            if (string.IsNullOrWhiteSpace(discountsJson)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(discountsJson);
                return doc.RootElement.TryGetProperty("itemIdOne", out var v) ? v.GetString() : null;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }

        private static List<string> EnumeratePeriods(string granularity, DateOnly from, DateOnly to)
        {
            var list = new List<string>();
            switch ((granularity ?? "day").ToLowerInvariant())
            {
                case "week":
                    var monday = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
                    for (var d = monday; d <= to; d = d.AddDays(7)) list.Add(d.ToString("yyyy-MM-dd"));
                    break;
                case "month":
                    for (var d = new DateOnly(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1)) list.Add(d.ToString("yyyy-MM"));
                    break;
                case "year":
                    for (var y = from.Year; y <= to.Year; y++) list.Add(y.ToString());
                    break;
                default: // day
                    for (var d = from; d <= to; d = d.AddDays(1)) list.Add(d.ToString("yyyy-MM-dd"));
                    break;
            }
            return list;
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
            var byPeriod = rows
                .GroupBy(r => { TryPeriod(granularity, r.BusinessDay, out var p); return p; })
                .ToDictionary(g => g.Key, g => (Gross: g.Sum(r => r.GrossPence), Vat: g.Sum(r => r.VatPence), Txn: g.Sum(r => r.TxnCount)));

            // Zero-fill EVERY period in the range so a day-granularity month shows every day and a
            // month-granularity year shows every month, whether or not there was a sale.
            var buckets = EnumeratePeriods(granularity, from, to)
                .Select(p =>
                {
                    byPeriod.TryGetValue(p, out var v);
                    return new
                    {
                        period = p,
                        grossPence = v.Gross,
                        vatPence = v.Vat,
                        txnCount = v.Txn,
                        avgBasketPence = v.Txn == 0 ? 0 : v.Gross / v.Txn,
                    };
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

        /// <summary>WP2.1 dashboard KPIs — the pills on the portal home. One call (the underlying
        /// sources have mixed policies; counts are not sensitive and are computed server-side under
        /// this one policy). "This week" = Monday→today of the CURRENT week (server-local = store tz),
        /// not a trailing 7 days. All counts auto-scope to the tenant via the global query filter.</summary>
        [HttpGet("api/v1/reports/dashboard")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Dashboard()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // ISO week start

            var weekRows = await _db.SalesRollups.AsNoTracking()
                .Where(r => r.BusinessDay >= monday && r.BusinessDay <= today)
                .Select(r => new { r.BusinessDay, r.GrossPence })
                .ToListAsync();

            var activeTills = await _db.Devices.AsNoTracking()
                .Where(d => d.Status == DeviceStatus.Active)
                .Select(d => d.TillId).Distinct().CountAsync();

            return Ok(new
            {
                salesTodayPence = weekRows.Where(r => r.BusinessDay == today).Sum(r => r.GrossPence),
                salesWeekPence = weekRows.Sum(r => r.GrossPence),
                weekStart = monday.ToString("yyyy-MM-dd"),
                activeUsers = await _db.People.AsNoTracking().CountAsync(),
                activeTills,
                activeStores = await _db.Stores.AsNoTracking().CountAsync(),
                activeWarehouses = await _db.StockLocations.AsNoTracking().CountAsync(l => l.Type == StockLocationType.Warehouse),
                activeWebstores = await _db.WebStores.AsNoTracking().CountAsync(w => w.Enabled),
            });
        }

        /// <summary>The rich sales summary the till's Summary view renders (totals, by-day,
        /// top items, by-payment-method, by-tax-rate) — from v1 SalesV2/SaleLines/SaleTenders so it
        /// shows the FULL history. Same JSON shape as the legacy /api/Sale/Summary (amounts in
        /// pounds). Gated on reports.view (the shopkeeper reporting permission).</summary>
        [HttpGet("api/v1/reports/summary-rich")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SummaryRich([FromQuery] DateOnly from, [FromQuery] DateOnly to)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            decimal P(long pence) => pence / 100m;

            var sales = await _db.SalesV2.AsNoTracking()
                .Where(s => s.BusinessDay >= from && s.BusinessDay <= to)
                .Select(s => new { s.Id, s.BusinessDay, s.GrossPence, s.VatPence }).ToListAsync();

            var byDay = sales.GroupBy(s => s.BusinessDay).OrderBy(g => g.Key).Select(g => new
            {
                date = g.Key.ToString("yyyy-MM-dd"),
                total = P(g.Sum(s => s.GrossPence)),
                totalExTax = P(g.Sum(s => s.GrossPence - s.VatPence)),
                orders = g.Count(),
            });

            // ALL lines (incl. ItemIdOne == null): the by-tax-rate table must cover every line or
            // legacy barcode-less lines fall out of their band (found 2026-07-30: the 19.81% band
            // disappeared entirely and the zero band under-reported ~£4.8k gross; the VAT landed in
            // the report's "unallocated" row). Only topItems needs a barcode to group by.
            var lines = await (from l in _db.SaleLines.AsNoTracking()
                               join s in _db.SalesV2.AsNoTracking() on l.SaleId equals s.Id
                               where s.BusinessDay >= @from && s.BusinessDay <= to
                               select new { l.ItemIdOne, l.Qty, l.LineGrossPence, l.VatAmountPence, l.VatRateBp }).ToListAsync();
            var itemLines = lines.Where(l => l.ItemIdOne != null).ToList();

            var barcodes = itemLines.Select(l => l.ItemIdOne).Distinct().ToList();
            var names = (await _db.Items.AsNoTracking().IgnoreQueryFilters()
                    .Where(i => barcodes.Contains(i.IdOne)).Select(i => new { i.IdOne, i.Name }).ToListAsync())
                .GroupBy(i => i.IdOne).ToDictionary(g => g.Key, g => g.First().Name);

            var topItems = itemLines.GroupBy(l => l.ItemIdOne).Select(g => new
            {
                itemId = g.Key,
                name = names.TryGetValue(g.Key, out var n) ? n : g.Key,
                quantity = g.Sum(l => l.Qty),
                gross = P(g.Sum(l => l.LineGrossPence)),
                grossExTax = P(g.Sum(l => l.LineGrossPence - l.VatAmountPence)),
            }).OrderByDescending(x => x.gross).Take(10);

            var byTaxRate = lines.GroupBy(l => l.VatRateBp).Select(g => new
            {
                tax = g.Key == 0 ? "Zero" : $"{g.Key / 100m:0.##}%",
                gross = P(g.Sum(l => l.LineGrossPence)),
                net = P(g.Sum(l => l.LineGrossPence - l.VatAmountPence)),
                vat = P(g.Sum(l => l.VatAmountPence)),
            }).OrderByDescending(x => x.gross);

            var tenders = await (from t in _db.SaleTenders.AsNoTracking()
                                 join s in _db.SalesV2.AsNoTracking() on t.SaleId equals s.Id
                                 where s.BusinessDay >= @from && s.BusinessDay <= to
                                 select new { t.TenderType, t.AmountPence, t.ChangePence }).ToListAsync();
            var byPayMethod = tenders.GroupBy(t => t.TenderType).Select(g => new
            {
                method = g.Key.ToString(),
                total = P(g.Sum(t => t.AmountPence - t.ChangePence)),
            }).OrderByDescending(x => x.total);

            return Ok(new
            {
                totalSales = P(sales.Sum(s => s.GrossPence)),
                totalSalesExTax = P(sales.Sum(s => s.GrossPence - s.VatPence)),
                totalOrders = sales.Count,
                byDay, topItems, byPayMethod, byTaxRate,
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

        /// <summary>VAT off-band CATALOGUE check (moved off legacy /api/Sale/VatIntegrity, 2026-07-27):
        /// items whose stored inc-VAT Price disagrees (by &gt;2p) with ExPrice × their tax rate.
        /// Reads the catalogue (Items+Tax) — nothing to do with sales/the bridge. Reachable by any
        /// report viewer (till or portal).</summary>
        [HttpGet("api/v1/reports/vat-integrity")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView + "," + PermissionCatalogue.PosReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> VatIntegrity()
        {
            var items = await _db.Items.AsNoTracking().Include(i => i.Tax)
                .Where(i => i.Tax != null)
                .Select(i => new { i.IdOne, i.Name, i.Price, i.ExPrice, Band = i.Tax.Name, Rate = i.Tax.Rate })
                .ToListAsync();

            var offBand = items
                .Select(i => new
                {
                    id = i.IdOne, name = i.Name, band = i.Band, price = i.Price, exPrice = i.ExPrice,
                    expectedPrice = Math.Round(i.ExPrice * (decimal)i.Rate, 2),
                })
                .Where(i => Math.Abs(i.price - i.expectedPrice) > 0.02m)
                .OrderByDescending(i => Math.Abs(i.price - i.expectedPrice))
                .ToList();

            return Ok(new { offBandCount = offBand.Count, offBandItems = offBand });
        }

        /// <summary>WP3.5 drill-down support: the sales in a day range (day-level view between
        /// the rollup buckets and the single-sale detail). Capped at 500 rows per call.</summary>
        // Sale HEADERS only (id/day/time/gross/vat/till/channel — no lines, no PII): same
        // sensitivity tier as summary-rich, so gated at ReportsView (WP12.1: the till's Custom
        // report reads this, and must be reachable by whoever can see the Summary beside it).
        // The deeper per-line drill-down (`/{saleId}`) stays FinancialsView.
        [HttpGet("api/v1/sales")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
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
        // Reachable by a portal financials drill-down AND a till operator viewing/returning a sale
        // (WP12.2: this replaces the legacy /api/Sale/Detail, which was open to any authenticated
        // user — a supervisor doing a return holds pos.refund, not portal.financials.view).
        [HttpGet("api/v1/sales/{saleId}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalFinancialsView + "," + PermissionCatalogue.PosReportsView + "," + PermissionCatalogue.PosRefund)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SaleDetail([FromRoute] Guid saleId)
        {
            var sale = await _db.SalesV2.AsNoTracking()
                .Include(s => s.Lines).Include(s => s.Tenders)
                .FirstOrDefaultAsync(s => s.Id == saleId);
            if (sale == null) return NotFound();

            // WP12.2 enrichment (ADDITIVE — the portal's existing fields are untouched): resolve
            // per-line item NAMES (barcode → Items.Name), the operator NAME, and the sale's refund
            // ADJUSTMENTS, so the TILL can read this instead of the legacy /api/Sale/Detail (the
            // last bridge-fed reader). The barcode is on SaleLine.ItemIdOne for migrated sales, but
            // web-till sales carry it in DiscountsJson ({"itemIdOne":"…"}) — resolve both.
            var lineBarcode = sale.Lines.ToDictionary(l => l.LineNo, l => l.ItemIdOne ?? BarcodeFromDiscountsJson(l.DiscountsJson));
            var barcodes = lineBarcode.Values.Where(b => b != null).Select(b => b!).Distinct().ToList();
            var names = (await _db.Items.AsNoTracking().IgnoreQueryFilters()
                    .Where(i => barcodes.Contains(i.IdOne)).Select(i => new { i.IdOne, i.Name }).ToListAsync())
                .GroupBy(i => i.IdOne).ToDictionary(g => g.Key, g => g.First().Name);

            string operatorName = null;
            if (sale.OperatorUserId is { } op)
                operatorName = (await _db.People.AsNoTracking().IgnoreQueryFilters()
                    .Where(p => p.Id == op).Select(p => $"{p.FName} {p.LName}").FirstOrDefaultAsync())?.Trim();

            var adjustments = await _db.SaleAdjustments.AsNoTracking().IgnoreQueryFilters()
                .Where(a => a.OriginalSaleId == saleId)
                .Select(a => new { type = a.Type.ToString(), itemId = a.ItemId, qty = a.Qty, amountPence = a.AmountPence, reason = a.Reason, createdAtUtc = a.CreatedAtUtc })
                .ToListAsync();

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
                operatorName,
                legacyRef = sale.LegacyRef,
                note = sale.Note,
                vatReconstructed = sale.VatReconstructed,
                lines = sale.Lines.OrderBy(l => l.LineNo).Select(l => new
                {
                    lineNo = l.LineNo, itemId = l.ItemId,
                    itemIdOne = lineBarcode[l.LineNo],
                    itemName = lineBarcode[l.LineNo] is { } bc && names.TryGetValue(bc, out var n) ? n : lineBarcode[l.LineNo],
                    qty = l.Qty,
                    unitPricePence = l.UnitPricePence, discountPence = l.DiscountPence,
                    lineGrossPence = l.LineGrossPence, vatRateBp = l.VatRateBp, vatAmountPence = l.VatAmountPence,
                    overriddenFromPence = l.OverriddenFromPence, discountsJson = l.DiscountsJson,
                }),
                tenders = sale.Tenders.Select(t => new
                {
                    tenderType = t.TenderType.ToString(), amountPence = t.AmountPence,
                    changePence = t.ChangePence, providerRef = t.ProviderRef,
                }),
                adjustments,
            });
        }

        public sealed record ItemSoldRow(
            DateTime dateSold, string itemIdOne, string itemName, string category, int storeId, Guid tillId,
            string tillName, Guid staffId, string staffName,
            int qty, long unitPricePence, long discountPence, long lineGrossPence);

        /// <summary>WP11.4 items-sold report (NatApp "Stock Outtake" parity). Reads the v1
        /// SaleLines + SalesV2 (the full history); item name via SaleLine.ItemIdOne → Items.IdOne
        /// (barcode preserved by the migration). Reconciliation sentinel lines (no barcode) are
        /// excluded — they aren't sellable items. Filters: date + optional store/till/item/staff.</summary>
        private async Task<List<ItemSoldRow>> ItemsSoldAsync(
            DateOnly from, DateOnly to, int? storeId, Guid? tillId, Guid? operatorUserId, string itemIdOne, int take)
        {
            var q = from l in _db.SaleLines.AsNoTracking()
                    join s in _db.SalesV2.AsNoTracking() on l.SaleId equals s.Id
                    where s.BusinessDay >= @from && s.BusinessDay <= to && l.ItemIdOne != null
                    select new
                    {
                        l.ItemIdOne, l.Qty, l.UnitPricePence, l.DiscountPence, l.LineGrossPence,
                        s.OccurredAtUtc, s.TillId, s.OperatorUserId,
                    };
            if (tillId != null) q = q.Where(x => x.TillId == tillId);
            if (operatorUserId != null) q = q.Where(x => x.OperatorUserId == operatorUserId);
            if (!string.IsNullOrWhiteSpace(itemIdOne)) q = q.Where(x => x.ItemIdOne == itemIdOne);

            var lines = await q.OrderByDescending(x => x.OccurredAtUtc).Take(take).ToListAsync();
            if (lines.Count == 0) return new List<ItemSoldRow>();

            var barcodes = lines.Select(l => l.ItemIdOne).Distinct().ToList();
            var items = await _db.Items.AsNoTracking().IgnoreQueryFilters()
                    .Where(i => barcodes.Contains(i.IdOne)).Select(i => new { i.IdOne, i.Name, i.CatId }).ToListAsync();
            var names = items.GroupBy(i => i.IdOne).ToDictionary(g => g.Key, g => g.First().Name);
            var itemCat = items.GroupBy(i => i.IdOne).ToDictionary(g => g.Key, g => g.First().CatId);
            // barcode → category name (WP3.3): Item.CatId → Category.Name.
            var catIds = items.Select(i => i.CatId).Distinct().ToList();
            var catNames = (await _db.Category.AsNoTracking().IgnoreQueryFilters()
                    .Where(c => catIds.Contains(c.IdOne)).Select(c => new { c.IdOne, c.Name }).ToListAsync())
                .GroupBy(c => c.IdOne).ToDictionary(g => g.Key, g => g.First().Name);

            var tillIds = lines.Select(l => l.TillId).Distinct().ToList();
            var tillNames = await _db.TillDetails.AsNoTracking()
                .Where(td => tillIds.Contains(td.TillId)).ToDictionaryAsync(td => td.TillId, td => td.Name);
            // Migrated sales carry a synthetic TillId not in the Till table → attribute to the
            // tenant's primary store so a store filter (single-store tenant) still includes them.
            var tillStore = await _db.Till.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.StoreId);
            var primaryStore = await _db.Stores.AsNoTracking().Select(s => (int?)s.Id).FirstOrDefaultAsync() ?? 0;

            var staffIds = lines.Where(l => l.OperatorUserId != null).Select(l => l.OperatorUserId.Value).Distinct().ToList();
            var staffNames = (await _db.People.AsNoTracking().IgnoreQueryFilters()
                    .Where(p => staffIds.Contains(p.Id)).Select(p => new { p.Id, p.FName, p.LName }).ToListAsync())
                .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => $"{g.First().FName} {g.First().LName}".Trim());

            var rows = lines.Select(l =>
            {
                var staffId = l.OperatorUserId ?? Guid.Empty;
                return new ItemSoldRow(
                    l.OccurredAtUtc, l.ItemIdOne, names.TryGetValue(l.ItemIdOne, out var n) ? n : l.ItemIdOne,
                    itemCat.TryGetValue(l.ItemIdOne, out var cid) && catNames.TryGetValue(cid, out var cn) ? cn : null,
                    tillStore.TryGetValue(l.TillId, out var st) ? st : primaryStore, l.TillId,
                    tillNames.TryGetValue(l.TillId, out var tn) ? tn : "(historic till)",
                    staffId, staffNames.TryGetValue(staffId, out var sn) && !string.IsNullOrWhiteSpace(sn) ? sn : "—",
                    l.Qty, l.UnitPricePence, l.DiscountPence, l.LineGrossPence);
            });
            if (storeId != null) rows = rows.Where(r => r.storeId == storeId);
            return rows.ToList();
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
            sb.AppendLine("dateSold,itemBarcode,itemName,category,storeId,till,staff,qty,unitPricePence,discountPence,lineGrossPence");
            foreach (var r in rows)
                sb.AppendLine(string.Format(inv, "{0:yyyy-MM-dd HH:mm},{1},\"{2}\",\"{3}\",{4},\"{5}\",\"{6}\",{7},{8},{9},{10}",
                    r.dateSold, r.itemIdOne, r.itemName.Replace("\"", "\"\""), (r.category ?? "").Replace("\"", "\"\""), r.storeId,
                    r.tillName.Replace("\"", "\"\""), r.staffName.Replace("\"", "\"\""),
                    r.qty, r.unitPricePence, r.discountPence, r.lineGrossPence));
            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
                $"items-sold-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        /// <summary>Sold quantity/gross/discount per item (barcode) in range — grouped in SQL so
        /// ~83k lines collapse to #distinct-items before the in-memory name/category resolve.</summary>
        private async Task<List<(string barcode, long qty, long gross, long discount)>> SoldByItemAsync(DateOnly from, DateOnly to)
        {
            var g = await (from l in _db.SaleLines.AsNoTracking()
                           join s in _db.SalesV2.AsNoTracking() on l.SaleId equals s.Id
                           where s.BusinessDay >= @from && s.BusinessDay <= to && l.ItemIdOne != null
                           group l by l.ItemIdOne into grp
                           select new
                           {
                               barcode = grp.Key,
                               qty = grp.Sum(x => (long)x.Qty),
                               gross = grp.Sum(x => x.LineGrossPence),
                               discount = grp.Sum(x => x.DiscountPence),
                           }).ToListAsync();
            return g.Select(x => (x.barcode, x.qty, x.gross, x.discount)).ToList();
        }

        /// <summary>barcode → (name, category name) — the same resolve the items-sold report uses.</summary>
        private async Task<(Dictionary<string, string> names, Dictionary<string, string> cats)> ItemMetaAsync(List<string> barcodes)
        {
            var items = await _db.Items.AsNoTracking().IgnoreQueryFilters()
                .Where(i => barcodes.Contains(i.IdOne)).Select(i => new { i.IdOne, i.Name, i.CatId }).ToListAsync();
            var names = items.GroupBy(i => i.IdOne).ToDictionary(g => g.Key, g => g.First().Name);
            var catId = items.GroupBy(i => i.IdOne).ToDictionary(g => g.Key, g => g.First().CatId);
            var catIds = items.Select(i => i.CatId).Distinct().ToList();
            var catName = (await _db.Category.AsNoTracking().IgnoreQueryFilters()
                .Where(c => catIds.Contains(c.IdOne)).Select(c => new { c.IdOne, c.Name }).ToListAsync())
                .GroupBy(c => c.IdOne).ToDictionary(g => g.Key, g => g.First().Name);
            var cats = catId.ToDictionary(kv => kv.Key, kv => catName.TryGetValue(kv.Value, out var n) ? n : null);
            return (names, cats);
        }

        /// <summary>WP3.7 category-sales: sold gross/qty grouped by item category (+ share of gross).
        /// Uncategorised lines roll into "(no category)".</summary>
        [HttpGet("api/v1/reports/category-sales")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CategorySales([FromQuery] DateOnly from, [FromQuery] DateOnly to)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            var byItem = await SoldByItemAsync(from, to);
            var (_, cats) = await ItemMetaAsync(byItem.Select(x => x.barcode).Distinct().ToList());
            var grouped = byItem
                .GroupBy(x => cats.TryGetValue(x.barcode, out var c) && c != null ? c : "(no category)")
                .Select(gr => new { category = gr.Key, qty = gr.Sum(x => x.qty), grossPence = gr.Sum(x => x.gross), discountPence = gr.Sum(x => x.discount) })
                .OrderByDescending(r => r.grossPence).ToList();
            var total = grouped.Sum(r => r.grossPence);
            return Ok(new
            {
                from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"),
                totals = new { grossPence = total, qty = grouped.Sum(r => r.qty), categories = grouped.Count },
                rows = grouped.Select(r => new { r.category, r.qty, r.grossPence, r.discountPence, sharePct = total == 0 ? 0 : Math.Round(100.0 * r.grossPence / total, 1) }),
            });
        }

        /// <summary>WP3.8 best-sellers: top items by qty (default) or gross, with category + share.</summary>
        [HttpGet("api/v1/reports/best-sellers")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalReportsView)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> BestSellers([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string by = "qty", [FromQuery] int take = 25)
        {
            if (to < from) return BadRequest(new { detail = "to must be >= from." });
            take = Math.Clamp(take, 1, 200);
            var byGross = string.Equals(by, "gross", StringComparison.OrdinalIgnoreCase);
            var byItem = await SoldByItemAsync(from, to);
            var (names, cats) = await ItemMetaAsync(byItem.Select(x => x.barcode).Distinct().ToList());
            long totalQty = byItem.Sum(x => x.qty), totalGross = byItem.Sum(x => x.gross);
            var ranked = byItem
                .OrderByDescending(x => byGross ? x.gross : x.qty)
                .Take(take)
                .Select((x, i) => new
                {
                    rank = i + 1,
                    itemIdOne = x.barcode,
                    itemName = names.TryGetValue(x.barcode, out var n) ? n : x.barcode,
                    category = cats.TryGetValue(x.barcode, out var c) ? c : null,
                    qty = x.qty,
                    grossPence = x.gross,
                    sharePct = byGross ? (totalGross == 0 ? 0 : Math.Round(100.0 * x.gross / totalGross, 1))
                                       : (totalQty == 0 ? 0 : Math.Round(100.0 * x.qty / totalQty, 1)),
                }).ToList();
            return Ok(new { from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), by = byGross ? "gross" : "qty", rows = ranked });
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
