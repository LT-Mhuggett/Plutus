using Plutus.Entities;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Repository.QueryParameters;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class ItemRepository : Base.CompositeRepositoryBase<Item, string, Guid, ItemBody, ItemParameters>, IItemRepository
    {
        public ItemRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Item";
        }

        protected override string ConvertIdToString(string id1, Guid id2)
        {
            throw new NotImplementedException();
        }

        protected override (string id1, Guid id2) ConvertStringToId(string id)
        {
            throw new NotImplementedException();
        }
    }
}
