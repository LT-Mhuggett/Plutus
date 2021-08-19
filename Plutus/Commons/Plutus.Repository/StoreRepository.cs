using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class StoreRepository : RepositoryBase<Store, string>, IStoreRepository
    {
        public StoreRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
