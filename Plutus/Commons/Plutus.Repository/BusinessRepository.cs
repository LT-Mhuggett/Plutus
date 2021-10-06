using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class BusinessRepository : RepositoryBase<Business, string>, IBusinessRepository
    {
        public BusinessRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {
        }
    }
}
