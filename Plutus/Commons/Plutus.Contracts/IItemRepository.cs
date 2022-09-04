using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface IItemRepository : ICompositeRepositoryBase<Item, string, Guid>
    {
    }
}