using System;

namespace Plutus.Entities.Models.Interface
{
    public interface ISale : IBase<Guid>
    {
        decimal Total { get; set; }
        decimal TotalExTax { get; set; }
        DateTime DateOfSale { get; set; }
    }
}
