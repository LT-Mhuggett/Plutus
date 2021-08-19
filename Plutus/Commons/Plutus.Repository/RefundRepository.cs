using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class RefundRepository : RepositoryBase<Refund, int>, IRefundRepository
    {
        public RefundRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
