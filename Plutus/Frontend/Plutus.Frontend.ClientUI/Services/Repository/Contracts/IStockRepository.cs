using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IStockRepository : ITriCompositeRepositoryBase<Stock, string, Guid, int, StockBody, StockParameters>, Plutus.Contracts.ITriCompositeRepositoryBase<Stock, string, Guid, int>
    {
        public Task<bool> StockUpdateByQuantityChange(Stock stock, int quantityChange);
    }
}
