using Plutus.Contracts;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Repository.Base;
using System;

namespace Plutus.Repository
{
    public class StockRepository : TriCompositeRepositoryBase<Stock, string, Guid, int>, IStockRepository
    {
        public StockRepository(RepositoryContext repositoryContext) : base(repositoryContext)
        {

        }
    }
}
