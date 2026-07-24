using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ITaxRepository : ICompositeRepositoryBase<Tax, int, Guid, TaxBody, TaxParameters>, Plutus.Contracts.ITaxRepository
    {
    }
}
