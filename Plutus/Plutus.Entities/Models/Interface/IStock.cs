namespace Plutus.Entities.Models.Interface
{
    public interface IStock : IAuditable
    {
        int Quantity { get; set; }

        string ItemId { get; set; }
        string StoreId { get; set; }
    }
}
