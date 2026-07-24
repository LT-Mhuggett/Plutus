using Plutus.Entities.Models;

namespace Plutus.Entities.FormBodies
{
    public class Transaction_DiscountBody : FormBody<Transaction_Discount>
    {
        public Guid SaleId { get; set; }
        public int TransactionId { get; set; }
        public decimal DiscountRate { get; set; } 
        public int DiscountId { get; set; }

        public override Transaction_Discount GenerateEntity()
        {
            var entity = base.GenerateEntity();
            entity.TransactionId = TransactionId;
            entity.SaleId = SaleId;
            entity.DiscountId = DiscountId;
            entity.DiscountRate = DiscountRate;
            return entity;
        }

        public Transaction_DiscountBody() : base()
        {

        }

        public Transaction_DiscountBody(Transaction_Discount entity) : base(entity)
        {
            SaleId = entity.SaleId;
            TransactionId = entity.TransactionId;
            DiscountRate = entity.DiscountRate;
            DiscountId = entity.DiscountId;
        }
    }
}
