using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class ItemRepository : CompositeRepositoryBase<Item, string, string>, IItemRepository
    {
        public ItemRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
