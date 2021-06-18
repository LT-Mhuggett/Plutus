using Microsoft.AspNetCore.Mvc;
using Plutus.Reports;
using System;
using Plutus.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System.Data;

namespace Plutus.DBService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SaleController : ApiControllerReadOnlyBase<Sale, string, SaleParameters>
    {
        protected override IRepositoryBase<Sale, string> Repository => repositoryWrapper.SaleRepository;

        public SaleController(IRepositoryWrapper repositoryWrapper) : base(repositoryWrapper)
        {
        }

        [HttpGet]
        [ApiConventionMethod(typeof(DefaultApiConventions),
                             nameof(DefaultApiConventions.Get))]
        public async Task<FileResult> salesReport([FromQuery] SaleParameters queryParameters, [FromQuery(Name = "minDate")] DateTime startDate, [FromQuery(Name = "maxDate")] DateTime endDate)
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

            var dailySalesSummaries = new List<IDictionary<string, Object>>();

            var salesBreakdowns = new List<SalesBreakdown>();

            var currentDate = startDate;
            do
            {
                var dailySalesSummary = new Dictionary<string, Object>();
                dailySalesSummary.Add("Date", currentDate.ToShortDateString());

                foreach (var payMethod in repositoryWrapper.PaymentMethodRepository.FindAll())
                {
                    dailySalesSummary.Add(
                        payMethod.Name + $" ({"ExTax"})",
                            data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date) && s.Total != decimal.Zero && s.TotalExTax != decimal.Zero)
                                .Sum(s => s.TotalExTax * (s.PaySales.Where(ps => ps.PayMethod.Id.Equals(payMethod.Id)).Sum(ps => ps.Amount - ps.Change) / s.Total)).Normalize());
                }

                dailySalesSummary.Add(
                    "Daily (ex Tax)",
                        data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date)).Sum(s => s.TotalExTax));
                dailySalesSummary.Add(
                    "Daily (inc Tax)",
                        data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date)).Sum(s => s.Total));

                dailySalesSummaries.Add(dailySalesSummary);

                currentDate = currentDate.AddDays(1);
            } while (currentDate <= endDate);

            currentDate = startDate;
            do
            {
                foreach (var salesData in data.Where(s => s.DateOfSale.Date.Equals(currentDate.Date)))
                {
                    foreach (var trans in salesData.Transactions)
                    {
                        //Record Transaction
                        salesBreakdowns.Add(new SalesBreakdown()
                        {
                            RecordDate = currentDate.ToShortDateString(),
                            SaleId = salesData.Id,
                            ItemId = trans.Item.Id,
                            ItemName = trans.Item.Name,
                            UnitPriceAtCheckout = trans.CheckoutItemChangeId == null ? trans.ItemsCostPrice : trans.CheckoutItemChange.Price,
                            UnitPriceAtCheckoutExTax = trans.CheckoutItemChangeId == null ? trans.ItemsCostExPrice : trans.CheckoutItemChange.ExPrice,
                            Qty = trans.Amount,
                            TotalSalePrice = (trans.CheckoutItemChangeId == null ? trans.ItemsCostPrice : trans.CheckoutItemChange.Price) * trans.Amount,
                            TotalSalePriceExTax = (trans.CheckoutItemChangeId == null ? trans.ItemsCostExPrice : trans.CheckoutItemChange.ExPrice) * trans.Amount,
                            EmployeeName = salesData.Employee.FullName
                        });

                        foreach (var transDiscount in trans.Transaction_Discounts)
                        {
                            var discountPrice = -decimal.Round(Math.Abs(transDiscount.Discount.Type == 0 ?
                                transDiscount.DiscountRate :
                                (trans.CheckoutItemChangeId == null ?
                                    trans.ItemsCostPrice :
                                    trans.CheckoutItemChange.Price)
                                * transDiscount.DiscountRate), 2, MidpointRounding.AwayFromZero);
                            var discountExPrice = -decimal.Round(Math.Abs(transDiscount.Discount.Type == 0 ?
                                transDiscount.DiscountRate :
                                (trans.CheckoutItemChangeId == null ?
                                    trans.ItemsCostExPrice :
                                    trans.CheckoutItemChange.ExPrice)
                                * transDiscount.DiscountRate), 2, MidpointRounding.AwayFromZero);
                            //Record Discount
                            salesBreakdowns.Add(new SalesBreakdown()
                            {
                                RecordDate = currentDate.ToShortDateString(),
                                SaleId = salesData.Id,
                                ItemId = $"{"Discount"}",
                                ItemName = $"{transDiscount.Discount.Name}, {trans.Item.Id}",
                                UnitPriceAtCheckout = discountPrice,
                                UnitPriceAtCheckoutExTax = discountExPrice,
                                Qty = trans.Amount,
                                TotalSalePrice = discountPrice * trans.Amount,
                                TotalSalePriceExTax = discountExPrice * trans.Amount
                            });
                        }
                    }

                    foreach (var refund in salesData.Refunds)
                        salesBreakdowns.Add(new SalesBreakdown()
                        {
                            RecordDate = currentDate.ToShortDateString(),
                            SaleId = salesData.Id,
                            ItemId = refund.Item.Id,
                            ItemName = refund.Item.Name,
                            UnitPriceAtCheckout = -Math.Abs(refund.CheckoutItemChangeId == null ? refund.Item.Price : refund.CheckoutItemChange.Price),
                            UnitPriceAtCheckoutExTax = -Math.Abs(refund.CheckoutItemChangeId == null ? refund.Item.ExPrice : refund.CheckoutItemChange.ExPrice),
                            Qty = refund.Amount,
                            TotalSalePrice = -Math.Abs((refund.CheckoutItemChangeId == null ? refund.Item.Price : refund.CheckoutItemChange.Price)) * refund.Amount,
                            TotalSalePriceExTax = -Math.Abs((refund.CheckoutItemChangeId == null ? refund.Item.ExPrice : refund.CheckoutItemChange.ExPrice)) * refund.Amount,
                            EmployeeName = refund.AuthoriserId == null ? salesData.Employee.FullName : refund.Authoriser.FullName
                        });
                }

                currentDate = currentDate.AddDays(1);
            } while (currentDate <= endDate);

            dataSet.Tables.Add(dailySalesSummaries.ToDataTable("DailySales"));
            dataSet.Tables.Add(salesBreakdowns.ToDataTable("SalesBreakdown"));

            return await new SalesReport().createExcel(dataSet, startDate, endDate); ;

            /*using (var docHandler = new ExcelHandling())
            {
                docHandler.DataTableToWorksheet(dataSet);
                var fileStream = docHandler.Finalize();
                await DependencyService.Get<IFile>().SaveAndView(
                        $"{"SalesReports".Translate()} - {startDate.ToShortDateString()}-{endDate.ToShortDateString()}",
                        "application/vnd.ms-excel",
                        fileStream,
                        new Dictionary<string, IList<string>>() { { "Excel", new List<string>() { ".xlsx", ".xls" } } }
                        );
            }*/

            //throw new NotImplementedException();
        }
    }
}
