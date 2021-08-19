using System;

namespace Plutus.Entities.Models.Interface
{
    public interface IDiscount_Item : IBase<int>
    {
        DateTime StartDateTime { get; set; }
        DateTime EndDateTime { get; set; }
    }
}
