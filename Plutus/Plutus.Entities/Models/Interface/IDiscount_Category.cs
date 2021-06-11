using System;

namespace Plutus.Entities.Models.Interface
{
    public interface IDiscount_Category : IBase<int>
    {
        DateTime StartDateTime { get; set; }
        DateTime EndDateTime { get; set; }
    }
}
