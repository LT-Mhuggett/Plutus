namespace Plutus.Entities.Models.Interface
{
    public interface ITransaction : IBase<int>
    {
        int Amount { get; set; }
        decimal ItemsCostPrice { get; set; }
        decimal ItemsCostExPrice { get; set; }

        string ItemId { get; set; }
        string SaleId { get; set; }
        int? CheckoutItemChangeId { get; set; }
    }
}
