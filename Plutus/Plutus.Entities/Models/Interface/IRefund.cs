namespace Plutus.Entities.Models.Interface
{
    public interface IRefund : IBase<int>
    {
        string Reason { get; set; }
        int Amount { get; set; }

        string ItemId { get; set; }
        string AuthoriserId { get; set; }
        string SaleId { get; set; }
        string SaleIdReturned { get; set; }
        int? CheckoutItemChangeId { get; set; }
    }
}
