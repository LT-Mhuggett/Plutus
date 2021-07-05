using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;

namespace Plutus.Repository
{
    public class StockRepository : TriCompositeRepositoryBase<Stock, string, string, string>, IStockRepository
    {
        public StockRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
