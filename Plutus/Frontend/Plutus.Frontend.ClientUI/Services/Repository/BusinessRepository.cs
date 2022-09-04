using Microsoft.AppCenter.Crashes;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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
    public class BusinessRepository : Base.RepositoryBase<Business, Guid, BusinessBody, BusinessParameters>, IBusinessRepository
    {
        public BusinessRepository(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext, globalRepositorySemaphore, appSettings, logger, appState)
        {
            EndpointEntity = "Business";
        }

        public async Task<Business> FindById(Guid id, BusinessParameters businessParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{id}?{businessParameters.GetStringRepresentation()}");
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var entity = JsonConvert.DeserializeObject<Business>(json);
                        if (await Exists(entity.Id))
                            RepositoryContext.Set<Business>().Update(entity); //Had to change because is is the wrong base class 
                        else
                            RepositoryContext.Set<Business>().Add(entity);

                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);
                    }
                    else
                    {
                        var query = await GetAllQueryable();
                        if (businessParameters.WithRoles)
                            query.Include(b => b.Roles);
                        return await FindById(query, id);
                    }
                }
                return await RepositoryContext.Business.FindAsync(id);
            }
            catch(Exception handleableEx) when(handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Business).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return await RepositoryContext.Business.FindAsync(id);
            }
            catch(Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Business).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return await RepositoryContext.Business.FindAsync(id);
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
                return await RepositoryContext.Business.FindAsync(id);
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
