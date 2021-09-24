using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface ITaxRepository : ICompositeRepositoryBase<Tax, Guid, string>
    {
    }
}
