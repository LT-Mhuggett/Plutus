namespace Plutus.Entities.Models.Interface
{
    public interface IStock : IAuditable
    {
        int Quantity { get; set; }

        string ItemIdOne { get; set; }
        string ItemIdTwo { get; set; }
        string StoreId { get; set; }
    }
}
