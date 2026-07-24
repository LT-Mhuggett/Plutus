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
        public ICollection<TransactionBody> Transactions { get; set; }
        public ICollection<PaymentMethod_SaleBody> PaymentSales { get; set; }
        public ICollection<RefundBody>? Refunds { get; set; }
        public ICollection<RefundBody>? Refunded { get; set; }
        public ICollection<NoteBody>? Notes { get; set; }

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
            entity.Transactions = Transactions.Select(t => t.GenerateEntity()).ToList();
            entity.PaySales = PaymentSales.Select(pS => pS.GenerateEntity()).ToList();
            entity.Refunds = Refunds?.Select(r => r.GenerateEntity()).ToList();
            entity.Refunded = Refunded?.Select(r => r.GenerateEntity()).ToList();
            entity.Notes = Notes?.Select(n => n.GenerateEntity()).ToList();
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
            Transactions = entity.Transactions.Select(e => new TransactionBody(e)).ToList();
            PaymentSales = entity.PaySales.Select(e => new PaymentMethod_SaleBody(e)).ToList();
            Refunds = entity.Refunds.Select(e => new RefundBody(e)).ToList();
            Refunded = entity.Refunded.Select(e => new RefundBody(e)).ToList();
            Notes = entity.Notes.Select(e => new NoteBody(e)).ToList();
        }
    }
}
