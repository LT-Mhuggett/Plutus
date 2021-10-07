using Plutus.Entities.Models;
using System;

namespace Plutus.Repository.FormBodies
{
    public class SaleBody : FormBody<Sale>
    {
        public decimal Total { get; set; }
        public decimal TotalExTax { get; set; }
        public DateTime DateOfSale { get; set; }
        public string EmployeeId { get; set; }
        public string StoreId { get; set; }

        public override Sale GenerateEntity() => new Sale
        {
            Total = Total,
            TotalExTax = TotalExTax,
            DateOfSale = DateOfSale,
            EmployeeId = EmployeeId,
            StoreId = StoreId
        };
    }
}
