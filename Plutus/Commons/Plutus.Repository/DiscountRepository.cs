using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class DiscountRepository : RepositoryBase<Discount, int>, IDiscountRepository
    {
        public DiscountRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
