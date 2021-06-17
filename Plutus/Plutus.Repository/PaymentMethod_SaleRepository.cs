using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class PaymentMethod_SaleRepository : RepositoryBase<PaymentMethod_Sale, int>, IPaymentMethod_SaleRepository
    {
        public PaymentMethod_SaleRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
