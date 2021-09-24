using Plutus.Entities.Models;

namespace Plutus.Contracts
{
    public interface IItemRepository : ICompositeRepositoryBase<Item, string, string>
    {
    }
}