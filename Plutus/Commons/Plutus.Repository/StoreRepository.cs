using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class StoreRepository : RepositoryBase<Store, int>, IStoreRepository
    {
        public StoreRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
