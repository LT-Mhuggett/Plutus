using Microsoft.AppCenter.Crashes;
using Microsoft.EntityFrameworkCore;
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

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class SaleRepository : Base.RepositoryBase<Sale, Guid, SaleBody, SaleParameters>, ISaleRepository
    {
        public SaleRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Sale";
        }

        public async Task<bool> SaleTransactionsCreate(Sale sale)
        {
            await Semaphore.WaitAsync();
            try
            {
                if (sale == default || sale == null)
                    throw new ArgumentException("Sale is Empty or null!");

                await RepositoryContext.Set<Sale>().AddAsync(sale);
                await RepositoryContext.SaveChangesAsync();

                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Post, $"{EndpointEntity}/SaleTransaction?IsSync=True");
                    message.Content = new StringContent(JsonConvert.SerializeObject(sale));
                    var response = await Client.SendAsync(message);
                    if (!response.IsSuccessStatusCode)
                        SaveDBAction(DatabaseActions.Created, sale);
                }
                SaveDBAction(DatabaseActions.Created, sale);

                if(RepositoryContext.ChangeTracker.HasChanges())
                {
                    RepositoryContext.SetSyncState(true);
                    await RepositoryContext.SaveChangesAsync();
                    RepositoryContext.SetSyncState(false);
                }

                DetachEntity(sale);
            }
            catch (Exception unhandleableEx) when (unhandleableEx is ArgumentException or InvalidOperationException)
            {
                Crashes.TrackError(unhandleableEx);
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Sale).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return false;
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Sale).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database rolled back" }
                });
                return false;
            }
            finally
            {
                Semaphore.Release();
            }
            return true;
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
