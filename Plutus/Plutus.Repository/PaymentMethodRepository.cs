using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class PaymentMethodRepository : RepositoryBase<PaymentMethod, int>, IPaymentMethodRepository
    {
        public PaymentMethodRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
