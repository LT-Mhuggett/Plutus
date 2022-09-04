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
using System.Text;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class StockRepository : Base.TriCompositeRepositoryBase<Stock, string, Guid, int, StockBody, StockParameters>, IStockRepository
    {
        public StockRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Stock";
        }

        public async Task<bool> StockUpdateByQuantityChange(Stock entity, int quantityChange)
        {
            await Semaphore.WaitAsync();
            try
            {
                if (entity == default || entity == null)
                    throw new ArgumentException("Entity is Empty or null!");

                entity.Quantity += quantityChange;
                RepositoryContext.Set<Stock>().Update(entity);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var patchMessage = CreateHttpMessage(HttpMethod.Patch, $"{EndpointEntity}/UpdateQuantity/{entity.IdOne}?ISync=True");
                    patchMessage.Headers.Add("BusinessId", entity.IdTwo.ToString());
                    patchMessage.Headers.Add("StoreId", entity.IdThree.ToString());
                    patchMessage.Content = new StringContent(JsonConvert.SerializeObject(quantityChange), Encoding.UTF8, "application/json");
                    var patchReponse = await Client.SendAsync(patchMessage);
                    if (patchReponse.IsSuccessStatusCode)
                    {
                        var json = await patchReponse.Content.ReadAsStringAsync();
                        entity = JsonConvert.DeserializeObject<Stock>(json);
                        if (entity.Quantity != entity.Quantity)
                            RepositoryContext.Set<Stock>().Update(entity);
                    }

                    if (RepositoryContext.ChangeTracker.HasChanges())
                    {
                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);
                    }
                    DetachEntity(entity);
                    return true;
                }
                SaveDBAction(DatabaseActions.Modified, entity);
                return true;
            }
            catch (Exception unhandleableEx) when (unhandleableEx is ArgumentException or InvalidOperationException)
            {
                Crashes.TrackError(unhandleableEx);
                return false;
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Stock).FullName },
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
                    { "Entity", typeof(Stock).FullName },
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
        }

        protected override string ConvertIdToString(string id1, Guid id2, int id3)
        {
            return JsonConvert.SerializeObject(new { id1 = id1, id2 = id2, id3 = id3 });
        }

        protected override (string, Guid, int) ConvertStringToId(string id)
        {
            return JsonConvert.DeserializeObject<(string, Guid, int)>(id);
        }
    }
}
