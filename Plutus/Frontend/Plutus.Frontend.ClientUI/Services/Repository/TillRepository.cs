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
    public class TillRepository : Base.RepositoryBase<Till, Guid, TillBody, TillParameters>, ITillRepository
    {
        public TillRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Till";
        }

        protected override string ConvertIdToString(Guid id)
        {
            return id.ToString();
        }

        protected override Guid ConvertStringToId(string id)
        {
            return Guid.Parse(id);
        }
    }
}
