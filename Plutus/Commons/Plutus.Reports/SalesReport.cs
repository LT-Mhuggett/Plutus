using Microsoft.AspNetCore.Mvc;
using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Reports
{
    public class SalesReport
    {
        public List<IDictionary<string, Object>> fetchdailySalesSummaries(DateTime currentDate, DateTime endDate, IQueryable<PaymentMethod> paymentMethods, List<Sale> data)
        {
            var dailySalesSummaries = new List<IDictionary<string, Object>>();

            do
            {
                var dailySalesSummary = new Dictionary<string, Object>();
                dailySalesSummary.Add("Date", currentDate.ToShortDateString());

                foreach (var payMethod in paymentMethods)
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

            return dailySalesSummaries;
        }
        
        public List<SalesBreakdown> fetchSalesBreakdown(DateTime currentDate, DateTime endDate, List<Sale> data)
        {
            var salesBreakdowns = new List<SalesBreakdown>();

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
                            ItemId = trans.Item.IdOne,
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
                                ItemName = $"{transDiscount.Discount.Name}, {trans.Item.IdOne}",
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
                            ItemId = refund.Item.IdOne,
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

            return salesBreakdowns;
        }
        public MemoryStream fetchSalesReportStream(DataSet dataSet, List<IDictionary<string, Object>> dailySalesSummaries, List<SalesBreakdown> salesBreakdowns)
        {
            dataSet.Tables.Add(dailySalesSummaries.ToDataTable("DailySales"));
            dataSet.Tables.Add(salesBreakdowns.ToDataTable("SalesBreakdown"));

            var stream = new MemoryStream();
            using (var docHandler = new ExcelHandling())
            {
                docHandler.DataTableToWorksheet(dataSet);
                stream = docHandler.Finalize();
              
            }

            return stream;
        } 
    }
}
