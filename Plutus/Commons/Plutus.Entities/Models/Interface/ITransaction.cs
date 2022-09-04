namespace Plutus.Entities.Models.Interface
{
    public interface ITransaction : ICompositeBase<int, Guid>
    {
        int Amount { get; set; }
        decimal ItemCostPrice { get; set; }
        decimal ItemCostExPrice { get; set; }

        string ItemIdOne { get; set; }
        Guid ItemIdTwo { get; set; }
        int? CheckoutItemChangeId { get; set; }
    }
}
