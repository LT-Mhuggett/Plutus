using Plutus.Entities.Models;

namespace Plutus.Repository.FormBodies
{
    public class RefundBody : FormBody<Refund>
    {
        public string Reason { get; set; }
        public int Amount { get; set; }
        public string ItemId { get; set; }
        public string BusinessId { get; set; }
        public string AuthoriserId { get; set; }
        public string SaleId { get; set; }
        public string SaleIdReturned { get; set; }
        public int? CheckoutItemChangeId { get; set; }

        public override Refund GenerateEntity() => new Refund
        {
            Reason = Reason,
            Amount = Amount,
            ItemIdOne = ItemId,
            ItemIdTwo = BusinessId,
            AuthoriserId = AuthoriserId,
            SaleId = SaleId,
            SaleIdReturned = SaleIdReturned,
            CheckoutItemChangeId = CheckoutItemChangeId
        };
    }
}
