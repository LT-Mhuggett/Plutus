using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using Plutus.Authentication;
using Plutus.Contracts;
using Plutus.DBService.Controllers.Bases;
using Plutus.Entities.Models;
using Plutus.Reports;
using Plutus.Entities.FormBodies;
using Plutus.Repository.QueryParameters;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SaleController : ApiControllerBaseCR<Sale, SaleBody, Guid, SaleParameters>
    {
        protected override IRepositoryBase<Sale, Guid> Repository => RepositoryWrapper.SaleRepository;

        public SaleController(IRepositoryWrapper repositoryWrapper, IHttpContextAccessor httpContextAccessor) : base(repositoryWrapper, httpContextAccessor)
        {
        }
        /// <summary>
        /// Full drill-down of one sale for the reporting recall view: lines (with item
        /// names and applied discounts), payments, refunds and notes in one call.
        /// </summary>
        [Authorize]
        [HttpGet("Detail/{id}")]
        public ActionResult Detail([FromRoute] Guid id)
        {
            var sale = Repository.FindAllByConditionQueryable(s => s.Id == id)
                .Include(s => s.Employee)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Item)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Transaction_Discounts)
                        .ThenInclude(td => td.Discount)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.CheckoutItemChange)
                .Include(s => s.PaySales)
                    .ThenInclude(ps => ps.PayMethod)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Item)
                .Include(s => s.Notes)
                .FirstOrDefault();

            if (sale == null) return NotFound();

            return Ok(new
            {
                id = sale.Id,
                dateOfSale = sale.DateOfSale,
                total = sale.Total,
                totalExTax = sale.TotalExTax,
                employee = sale.Employee == null ? null : $"{sale.Employee.FName} {sale.Employee.LName}".Trim(),
                lines = (sale.Transactions ?? Enumerable.Empty<Transaction>()).Select(t => new
                {
                    itemId = t.ItemIdOne,
                    name = t.Item?.Name ?? t.ItemIdOne,
                    quantity = t.Amount,
                    unitPrice = t.ItemCostPrice,
                    unitExPrice = t.ItemCostExPrice,
                    priceAdjusted = t.CheckoutItemChangeId != null,
                    discounts = (t.Transaction_Discounts ?? Enumerable.Empty<Transaction_Discount>())
                        .Select(td => new { name = td.Discount?.Name ?? $"#{td.DiscountId}", rate = td.DiscountRate }),
                }),
                payments = (sale.PaySales ?? Enumerable.Empty<PaymentMethod_Sale>())
                    .Select(p => new { method = p.PayMethod?.Name ?? p.PayId.ToString(), amount = p.Amount, change = p.Change }),
                refunds = (sale.Refunds ?? Enumerable.Empty<Refund>())
                    .Select(r => new { itemId = r.ItemIdOne, name = r.Item?.Name ?? r.ItemIdOne, quantity = r.Amount, reason = r.Reason, originalSaleId = r.SaleIdReturned }),
                notes = (sale.Notes ?? Enumerable.Empty<Note>()).Select(n => n.Text),
            });
        }

        /// <summary>
        /// VAT integrity check (VAT-Investigation plan §5.4): items whose stored prices are
        /// inconsistent with their tax band. Read-only — surfaces the damage, never repairs it.
        /// </summary>
        [Authorize]
        [HttpGet("VatIntegrity")]
        public ActionResult VatIntegrity([FromHeader] Guid businessId)
        {
            var items = RepositoryWrapper.ItemRepository
                .FindAllByConditionQueryable(i => true, businessId)
                .Include(i => i.Tax)
                .ToList();

            var offBand = items
                .Where(i => i.Tax != null && Math.Abs(i.Price - Math.Round(i.ExPrice * (decimal)i.Tax.Rate, 2)) > 0.02m)
                .Select(i => new
                {
                    id = i.IdOne,
                    name = i.Name,
                    band = i.Tax.Name,
                    price = i.Price,
                    exPrice = i.ExPrice,
                    expectedPrice = Math.Round(i.ExPrice * (decimal)i.Tax.Rate, 2),
                })
                .OrderByDescending(x => Math.Abs(x.price - x.expectedPrice))
                .ToList();

            return Ok(new { offBandCount = offBand.Count, offBandItems = offBand });
        }

        /// <summary>
        /// Aggregated reporting summary for the webapp dashboard (added 2026-07-23):
        /// totals, per-day series, top items and payment-method breakdown in one call —
        /// avoids the N-per-sale round trips a client-side aggregation would need.
        /// </summary>
        [Authorize]
        [HttpGet("Summary")]
        public ActionResult Summary([FromQuery] DateTime minDate, [FromQuery] DateTime maxDate)
        {
            var sales = Repository.FindAllByConditionQueryable(s => s.DateOfSale >= minDate.Date && s.DateOfSale < maxDate.Date.AddDays(1))
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Item)
                        .ThenInclude(i => i.Tax)
                .Include(s => s.PaySales)
                    .ThenInclude(ps => ps.PayMethod)
                .ToList();

            var byDay = sales
                .GroupBy(s => s.DateOfSale.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    total = g.Sum(s => s.Total),
                    totalExTax = g.Sum(s => s.TotalExTax),
                    orders = g.Count(),
                });

            var topItems = sales
                .SelectMany(s => s.Transactions ?? Enumerable.Empty<Transaction>())
                .GroupBy(t => t.ItemIdOne)
                .Select(g => new
                {
                    itemId = g.Key,
                    name = g.Select(t => t.Item?.Name).FirstOrDefault(n => n != null) ?? g.Key,
                    quantity = g.Sum(t => t.Amount),
                    gross = g.Sum(t => t.ItemCostPrice * t.Amount),
                    grossExTax = g.Sum(t => t.ItemCostExPrice * t.Amount),
                })
                .OrderByDescending(x => x.gross)
                .Take(10);

            var byPayMethod = sales
                .SelectMany(s => s.PaySales ?? Enumerable.Empty<PaymentMethod_Sale>())
                .GroupBy(p => p.PayMethod?.Name ?? p.PayId.ToString())
                .Select(g => new { method = g.Key, total = g.Sum(p => p.Amount - p.Change) })
                .OrderByDescending(x => x.total);

            // VAT split by the item's tax band, from transaction line values. NOTE:
            // line values pre-date discounts — the authoritative period VAT is the
            // Sale-totals difference below; the per-rate rows are the record split.
            var byTaxRate = sales
                .SelectMany(s => s.Transactions ?? Enumerable.Empty<Transaction>())
                .GroupBy(t => t.Item?.Tax?.Name ?? "Unknown")
                .Select(g => new
                {
                    tax = g.Key,
                    gross = g.Sum(t => t.ItemCostPrice * t.Amount),
                    net = g.Sum(t => t.ItemCostExPrice * t.Amount),
                    vat = g.Sum(t => (t.ItemCostPrice - t.ItemCostExPrice) * t.Amount),
                })
                .OrderByDescending(x => x.gross);

            return Ok(new
            {
                totalSales = sales.Sum(s => s.Total),
                totalSalesExTax = sales.Sum(s => s.TotalExTax),
                totalOrders = sales.Count,
                byDay,
                topItems,
                byPayMethod,
                byTaxRate,
            });
        }

        [Authorize]
        [RequiredScope(RequiredScopesConfigurationKey = "OpenAPI:Scopes:APIRead:Name")]
        [HttpGet("SaleReport")]
        [ApiConventionMethod(typeof(APIConventions),
                             nameof(APIConventions.Get))]
        public FileResult SalesReport([FromQuery] SaleParameters queryParameters, [FromQuery(Name = "minDate")] DateTime startDate, [FromQuery(Name = "maxDate")] DateTime endDate)
        {
            //Check Min and Max date are viable
            var entities = Repository.FindAllByConditionQueryable(queryParameters.GetExpression());
            var dataSet = new DataSet();

            var data = entities
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Item)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.CheckoutItemChange)
                .Include(s => s.Transactions)
                    .ThenInclude(t => t.Transaction_Discounts)
                        .ThenInclude(td => td.Discount)
                .Include(s => s.PaySales)
                    .ThenInclude(ps => ps.PayMethod)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Item)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.Authoriser)
                .Include(s => s.Refunds)
                    .ThenInclude(r => r.CheckoutItemChange)
                .Include(s => s.Employee).ToList();

            var currentDate = startDate;

            // Fetch Sales Breakdowns
            var salesBreakdowns = new SalesReport().fetchSalesBreakdown(currentDate, endDate, data);

            // Fetch Sales Summaries
            var dailySalesSummaries = new SalesReport().fetchdailySalesSummaries(currentDate, endDate, RepositoryWrapper.PaymentMethodRepository.GetAllQueryable(), data);

            // Generate and get File Stream for Excel File containing Sales Breakdowns and Sales Summaries
            var stream = new SalesReport().fetchSalesReportStream(dataSet, dailySalesSummaries, salesBreakdowns);

            string fileName = $"{startDate:yyyy-MM-dd}-{endDate:yyyy-MM-dd}-SalesReport";
            string fileType = "application/vnd.ms-excel";

            return File(stream.ToArray(), fileType, fileName);
        }
    }
}
