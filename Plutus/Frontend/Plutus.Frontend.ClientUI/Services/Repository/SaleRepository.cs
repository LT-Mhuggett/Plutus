using Microsoft.AppCenter.Crashes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json;
using Plutus.Entities;
using Plutus.Entities.Enums;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Repository.QueryParameters;
using System.Text;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class SaleRepository : Base.RepositoryBase<Sale, Guid, SaleBody, SaleParameters>, ISaleRepository
    {
        public SaleRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Sale";
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
