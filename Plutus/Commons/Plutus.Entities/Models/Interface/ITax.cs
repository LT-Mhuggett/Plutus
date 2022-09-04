using System;

namespace Plutus.Entities.Models.Interface
{
    public interface ITax : ICompositeBase<int, Guid>
    {
        string Name { get; set; }
        double Rate { get; set; }
    }
}
