using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IStoreRepository : IRepositoryBase<Store, int, StoreBody, StoreParameters>, Plutus.Contracts.IStoreRepository
    {
    }
}
