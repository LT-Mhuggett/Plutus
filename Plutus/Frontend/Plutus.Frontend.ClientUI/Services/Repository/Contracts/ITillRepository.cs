using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ITillRepository : IRepositoryBase<Till, Guid, TillBody, TillParameters>, Plutus.Contracts.ITillRepository
    {
    }
}
