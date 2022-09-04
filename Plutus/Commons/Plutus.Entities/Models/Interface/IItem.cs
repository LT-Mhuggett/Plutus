using System;

namespace Plutus.Entities.Models.Interface
{
    public interface IItem : ICompositeBase<string, Guid>
    {
        string Name { get; set; }
        string Brand { get; set; }
        string Desc { get; set; }
        decimal Cost { get; set; }
        decimal ExPrice { get; set; }
        decimal Price { get; set; }
        byte[] Image { get; set; }
        int Amount { get; set; }

        int TaxId { get; set; }
        Guid CatId { get; set; }
    }
}
