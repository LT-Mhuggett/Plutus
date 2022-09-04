using Plutus.Entities;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Repository.Base;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Repository.QueryParameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class RoleRepository : RepositoryBase<Role, int, RoleBody, RoleParameters>, IRoleRepository
    {
        public RoleRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Role";
        }

        protected override string ConvertIdToString(int id)
        {
            return id.ToString();
        }

        protected override int ConvertStringToId(string id)
        {
            return int.Parse(id);
        }
    }
}
