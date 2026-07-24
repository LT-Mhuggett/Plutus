using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class ItemRepository : CompositeRepositoryBase<Item, string, Guid>, IItemRepository
    {
        public ItemRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {
           
        }
    }
}
