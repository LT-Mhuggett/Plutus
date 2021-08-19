namespace Plutus.Entities.Models.Interface
{
    public interface ITransaction : IBase<int>
    {
        int Amount { get; set; }
        decimal ItemsCostPrice { get; set; }
        decimal ItemsCostExPrice { get; set; }

        string ItemIdOne { get; set; }
        string ItemIdTwo { get; set; }
        string SaleId { get; set; }
        int? CheckoutItemChangeId { get; set; }
    }
}
