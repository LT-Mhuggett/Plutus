using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class SaleRepository : RepositoryBase<Sale, string>, ISaleRepository
    {
        public SaleRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
