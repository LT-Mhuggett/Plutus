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
    public class PaymentMethodRepository : Base.RepositoryBase<PaymentMethod, int, PaymentMethodBody, PaymentMethodsParameters>, IPaymentMethodRepository
    {
        public PaymentMethodRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "PaymentMethod";
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
