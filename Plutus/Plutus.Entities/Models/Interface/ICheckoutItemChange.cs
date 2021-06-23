namespace Plutus.Entities.Models.Interface
{
    interface ICheckoutItemChange : IBase<int>
    {
        decimal Price { get; set; }
        decimal ExPrice { get; set; }
        string ItemIdOne { get; set; }
        string ItemIdTwo { get; set; }
    }
}
