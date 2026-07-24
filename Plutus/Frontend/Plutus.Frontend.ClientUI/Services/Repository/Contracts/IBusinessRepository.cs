using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IBusinessRepository : IRepositoryBase<Business, Guid, BusinessBody, BusinessParameters>, Plutus.Contracts.IBusinessRepository
    {
        Task<Business> FindById(Guid id, BusinessParameters businessParameters);
    }
}
