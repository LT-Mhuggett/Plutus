using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class TransactionBody : FormBody<Transaction>
    {
        public int Amount { get; set; }
        public decimal ItemsCostExPrice { get; set; }
        public decimal ItemsCostPrice { get; set; }
        public string ItemId { get; set; }
        public string BusinessId { get; set; }
        public string TillId { get; set; }
        public string SaleId { get; set; }
        public int? CheckoutItemChangeId { get; set; }

        public override Transaction GenerateEntity() => new Transaction
        {
            Amount = Amount,
            ItemsCostExPrice = ItemsCostExPrice,
            ItemsCostPrice = ItemsCostPrice,
            ItemIdOne = ItemId,
            ItemIdTwo = BusinessId,
            TillId = TillId,
            SaleId = SaleId,
            CheckoutItemChangeId = CheckoutItemChangeId
        };
    }
}
