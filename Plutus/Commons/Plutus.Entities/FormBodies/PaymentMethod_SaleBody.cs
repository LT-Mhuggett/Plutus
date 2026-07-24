using Plutus.Entities.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Entities.FormBodies
{
    public class PaymentMethod_SaleBody : FormBody<PaymentMethod_Sale>
    {
        public decimal Amount { get; set; }
        public decimal Change { get; set; }
        public int PayId { get; set; }
        public Guid SaleId { get; set; }

        public PaymentMethod_SaleBody()
        {

        }

        public PaymentMethod_SaleBody(PaymentMethod_Sale entity) : base(entity)
        {
            Amount = entity.Amount;
            Change = entity.Change;
            PayId = entity.PayId;
            SaleId = entity.SaleId;
        }

        public override PaymentMethod_Sale GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.Amount = Amount;
            entity.Change = Change;
            entity.PayId = PayId;
            entity.SaleId = SaleId;
            return entity;
        }
    }
}
