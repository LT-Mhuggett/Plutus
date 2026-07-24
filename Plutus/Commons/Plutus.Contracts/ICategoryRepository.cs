using Plutus.Entities.Models;
using System;

namespace Plutus.Contracts
{
    public interface ICategoryRepository : ICompositeRepositoryBase<Category, Guid, Guid>
    {
    }
}
