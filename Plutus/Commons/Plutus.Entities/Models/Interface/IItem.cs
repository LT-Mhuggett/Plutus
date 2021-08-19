using System;

namespace Plutus.Entities.Models.Interface
{
    public interface IItem : ICompositeBase<string, string>
    {
        string Name { get; set; }
        string Brand { get; set; }
        string Desc { get; set; }
        decimal Cost { get; set; }
        decimal ExPrice { get; set; }
        decimal Price { get; set; }
        byte[] Image { get; set; }
        int Amount { get; set; }

        Guid TaxId { get; set; }
        int CatId { get; set; }
    }
}
