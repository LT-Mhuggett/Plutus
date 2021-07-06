using System;

namespace Plutus.Entities.Models.Interface
{
    public interface ITax : ICompositeBase<Guid, string>
    {
        string Name { get; set; }
        double Rate { get; set; }
    }
}
