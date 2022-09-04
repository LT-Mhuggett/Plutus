using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ISaleRepository : IRepositoryBase<Sale, Guid, SaleBody, SaleParameters>, Plutus.Contracts.ISaleRepository
    {
        public Task<bool> SaleTransactionsCreate(Sale sale);
    }
}
