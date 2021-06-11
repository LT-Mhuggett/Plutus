using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class TaxRepository : RepositoryBase<Tax, int>, ITaxRepository
    {
        public TaxRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
