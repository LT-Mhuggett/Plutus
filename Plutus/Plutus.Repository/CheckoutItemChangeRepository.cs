using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class CheckoutItemChangeRepository : RepositoryBase<CheckoutItemChange, int>, ICheckoutItemChangeRepository
    {
        public CheckoutItemChangeRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
