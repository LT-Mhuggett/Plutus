using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository.Contracts
{
    public interface IItemRepository : ICompositeRepositoryBase<Item, string, Guid, ItemBody, ItemParameters>, Plutus.Contracts.IItemRepository
    {
    }
}
