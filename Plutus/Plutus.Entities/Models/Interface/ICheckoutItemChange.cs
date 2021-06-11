namespace Plutus.Entities.Models.Interface
{
    interface ICheckoutItemChange : IBase<int>
    {
        decimal Price { get; set; }
        decimal ExPrice { get; set; }
        string ItemId { get; set; }
    }
}
