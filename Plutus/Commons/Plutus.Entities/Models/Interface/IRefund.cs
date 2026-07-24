namespace Plutus.Entities.Models.Interface
{
    public interface IRefund : IBase<int>
    {
        string Reason { get; set; }
        int Amount { get; set; }

        string ItemIdOne { get; set; }
        Guid ItemIdTwo { get; set; }
        Guid AuthoriserId { get; set; }
        Guid SaleId { get; set; }
        Guid SaleIdReturned { get; set; }
        int? CheckoutItemChangeId { get; set; }
    }
}
