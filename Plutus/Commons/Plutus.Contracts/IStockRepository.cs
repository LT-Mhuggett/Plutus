using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface IStockRepository : ITriCompositeRepositoryBase<Stock, string, Guid, int>
    {
    }
}
