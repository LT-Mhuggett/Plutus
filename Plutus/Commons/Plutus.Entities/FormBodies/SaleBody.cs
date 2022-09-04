using Plutus.Entities.Models;
using System;

namespace Plutus.Entities.FormBodies
{
    public class SaleBody : FormBody<Sale>
    {
        public Guid Id { get; set; }
        public decimal Total { get; set; }
        public decimal TotalExTax { get; set; }
        public DateTime DateOfSale { get; set; }
        public Guid EmployeeId { get; set; }
        public int StoreId { get; set; }
        public Guid TillId { get; set; }

        public override Sale GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Id = Id;
            entity.Total = Total;
            entity.TotalExTax = TotalExTax;
            entity.DateOfSale = DateOfSale;
            entity.EmployeeId = EmployeeId;
            entity.StoreId = StoreId;
            entity.TillId = TillId;
            return entity;
        }

        public SaleBody() : base()
        {

        }

        public SaleBody(Sale entity) : base(entity)
        {
            Id = entity.Id;
            Total = entity.Total;
            TotalExTax = entity.TotalExTax;
            DateOfSale = entity.DateOfSale;
            EmployeeId = entity.EmployeeId;
            StoreId = entity.StoreId;
            TillId = entity.TillId;
        }
    }
}
