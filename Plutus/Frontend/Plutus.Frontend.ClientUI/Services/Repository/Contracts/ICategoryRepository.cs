using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface ICategoryRepository : ICompositeRepositoryBase<Category, Guid, Guid, CategoryBody, CategoryParameters>, Plutus.Contracts.ICategoryRepository
    {
    }
}
