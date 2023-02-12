using Microsoft.AppCenter.Crashes;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Plutus.Entities;
using Plutus.Entities.FormBodies;
using Plutus.Entities.Models;
using Plutus.Entities.Models.Interface;
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

        public async Task<Till> FindById(Guid id, TillParameters tillParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{id}?{tillParameters.GetStringRepresentation()}");
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var entity = JsonConvert.DeserializeObject<Till>(json);
                        if (await Exists(entity.Id))
                            RepositoryContext.Set<Till>().Update(entity);
                        else
                            RepositoryContext.Set<Till>().Add(entity);

                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);
                        DetachEntity(entity);
                    }
                }
                return await RepositoryContext.Till.FindAsync(id);
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Business).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return await RepositoryContext.Till.FindAsync(id);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Business).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return await RepositoryContext.Till.FindAsync(id);
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
                return await RepositoryContext.Till.FindAsync(id);
            }
            finally
            {
                Semaphore.Release();
            }
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
