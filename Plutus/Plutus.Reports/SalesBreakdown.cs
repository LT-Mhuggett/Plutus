using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Reports
{
    public class SalesBreakdown
    {
        public string RecordDate { get; set; }
        public string SaleId { get; set; }
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public decimal UnitPriceAtCheckout { get; set; }
        public decimal UnitPriceAtCheckoutExTax { get; set; }
        public int Qty { get; set; }
        public decimal TotalSalePrice { get; set; }
        public decimal TotalSalePriceExTax { get; set; }
        public string EmployeeName { get; set; }
    }
}
