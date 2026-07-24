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
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using Plutus.Repository.QueryParameters;
using System.Reflection;
using System.Text;

namespace Plutus.Frontend.ClientUI.Services.Repository.Base
{
    public abstract class CompositeRepositoryBase<TEntity, TId1, TId2, TFormBody, TQueryParameters> 
        : Plutus.Repository.Base.CompositeRepositoryBase<TEntity, TId1, TId2>, IDisposable, ICompositeRepositoryBase<TEntity, TId1, TId2, TFormBody, TQueryParameters>
        where TEntity : CompositeBase<TId1, TId2>, new()
        where TFormBody : FormBody<TEntity>
        where TQueryParameters : CompositeQueryParameters<TEntity, TId1, TId2>
    {
        private bool disposedValue;
        protected readonly SemaphoreSlim Semaphore;
        protected ILogger Logger { get; }
        protected HttpClient Client { get; }
        protected virtual string EndpointEntity { get; init; }
        protected virtual IAppState AppState { get; init; }

        public CompositeRepositoryBase(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext)
        {
            Semaphore = globalRepositorySemaphore;
            Logger = logger;
#if DEBUG
            Client = new HttpClient(GetInsecureHandler())
            {
                BaseAddress = new Uri($"{appSettings.DBServiceURL}/api/")
            };
#else
            Client = new HttpClient
            {
                BaseAddress = new Uri($"{appSettings.DBServiceURL}/api/")
            };
#endif
            AppState = appState;

            Connectivity.ConnectivityChanged += async (sender, args) =>
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                    await LocalToServerSync();
            };
        }

#region Entity Server Operations
        /// <summary>
        /// Create the entity locally, then make the API call.
        /// if API call failed, create a DBAction
        /// </summary>
        /// <param name="entity">Entity to create</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If entity is null or empty</exception>
        public new virtual async Task<bool> Create(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            await Semaphore.WaitAsync();
            try
            {
                //Ensure Entity is set
                if (entity == default || entity == null)
                    throw new ArgumentException("Entity is Empty or null!");

                //Create locally first
                await base.Create(entity);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Post, $"{EndpointEntity}?IsSync=True");
                    message.Headers.Add("BusinessId", AppState.Business.Id.ToString());
                    message.Content = new StringContent(JsonConvert.SerializeObject(Activator.CreateInstance(typeof(TFormBody),entity)), Encoding.UTF8, "application/json");
                    var response = await Client.SendAsync(message);
                    if (!response.IsSuccessStatusCode)
                    {
                        SaveDBAction(DatabaseActions.Created, entity);
                    }
                }
                else
                {
                    SaveDBAction(DatabaseActions.Created, entity);
                }

                if (RepositoryContext.ChangeTracker.HasChanges())
                {
                    RepositoryContext.SetSyncState(true);
                    await RepositoryContext.SaveChangesAsync();
                    RepositoryContext.SetSyncState(false);
                }
                DetachEntity(entity);
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
                return false;
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
                return false;
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
                return false;
            }
            finally
            {
                Semaphore.Release();
            }
            return true;
        }

        /// <summary>
        /// Update the entity locally, then make the API call.
        /// if API call failed, create a DBAction
        /// </summary>
        /// <param name="entity">Entity to update</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If entity is null or empty</exception>
        public new virtual async Task<bool> Update(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            await Semaphore.WaitAsync();
            try
            {
                //Ensure Entity is set
                if (entity == default || entity == null)
                    throw new ArgumentException("Entity is Empty or null!");

                await base.Update(entity);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var getMessage = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{entity.IdOne}");
                    getMessage.Headers.Add("BusinessId", entity.IdTwo.ToString());
                    var getResponse = await Client.SendAsync(getMessage);
                    if (getResponse.IsSuccessStatusCode)
                    {
                        var entityToChange = JsonConvert.DeserializeObject<TEntity>(await getResponse.Content.ReadAsStringAsync());
                        var patchMessage = CreateHttpMessage(HttpMethod.Patch, $"{EndpointEntity}/{entity.IdOne}?IsSync=True");
                        var patchDocument = entityToChange.CreatePatch(entity);
                        var jsonPatchDocument = JsonConvert.SerializeObject(patchDocument);
                        patchMessage.Content = new StringContent(jsonPatchDocument, Encoding.UTF8, "application/json-patch+json");
                        var response = await Client.SendAsync(patchMessage);
                        if (!response.IsSuccessStatusCode)
                        {
                            SaveDBAction(DatabaseActions.Modified, entity);
                        }
                    }
                    else
                    {
                        SaveDBAction(DatabaseActions.Modified, entity);
                    }
                }
                else
                {
                    SaveDBAction(DatabaseActions.Modified, entity);
                }

                if (RepositoryContext.ChangeTracker.HasChanges())
                {
                    RepositoryContext.SetSyncState(true);
                    await RepositoryContext.SaveChangesAsync();
                    RepositoryContext.SetSyncState(false);
                }
                DetachEntity(entity);
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
                return false;
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
                return false;
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
                return false;
            }
            finally
            {
                Semaphore.Release();
            }

            return true;
        }

        /// <summary>
        /// Delete the entity locally, then make the API call.
        /// if API call failed, create a DBAction
        /// </summary>
        /// <param name="entity">Entity to delete</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If entity is null or empty</exception>
        public new virtual Task<bool> Delete(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            throw new NotSupportedException("Delete is not supported! 😰");
        }

        public virtual async Task<IEnumerable<TEntity>> GetAll(TId2 businessId)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(default, businessId);
                return await base.GetAll(businessId);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual async Task<IQueryable<TEntity>> GetAllQueryable(TId2 businessId)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(default, businessId);
                return await base.GetAllQueryable(businessId);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual async Task<IEnumerable<TEntity>> FindAllByCondition(TQueryParameters queryParameters, TId2 businessId)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters, businessId);
                return await base.FindAllByCondition(queryParameters.GetExpression(), businessId);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual async Task<IQueryable<TEntity>> FindAllByConditionQueryable(TQueryParameters queryParameters, TId2 businessId)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters, businessId);
                return base.FindAllByConditionQueryable(queryParameters.GetExpression(), businessId);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public virtual async Task<TEntity> FindFirstByCondition(TQueryParameters queryParameters, TId2 businessId)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters, businessId);
                return await base.FindFirstByCondition(queryParameters.GetExpression(), businessId);
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public override async Task<TEntity> FindById(TId1 idOne, TId2 businessId)
        {
            try
            {
                await Semaphore.WaitAsync();
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{idOne}");
                    message.Headers.Add("businessId", businessId.ToString());
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var entity = JsonConvert.DeserializeObject<TEntity>(await response.Content.ReadAsStringAsync());
                        if(await Exists(idOne, businessId))
                        {
                            await base.Update(entity);
                        }
                        else
                        {
                            await base.Create(entity);
                        }

                        if (RepositoryContext.ChangeTracker.HasChanges())
                        {
                            RepositoryContext.SetSyncState(true);
                            await RepositoryContext.SaveChangesAsync();
                            RepositoryContext.SetSyncState(false);
                        }
                    }
                }
                return await base.FindById(idOne, businessId);
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
                return await base.FindById(idOne, businessId);
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
                return await base.FindById(idOne, businessId);
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
                return await base.FindById(idOne, businessId);
            }
            finally
            {
                Semaphore.Release();
            }
        }
#endregion

#region Operations
        /// <summary>
        /// Create the common <see cref="HttpRequestMessage"/> to be sent to the DBService
        /// </summary>
        /// <param name="httpMethod">The HTTP method to use</param>
        /// <param name="endpoint">The endpoint to send to</param>
        /// <returns>The HTTP Request Message</returns>
        protected HttpRequestMessage CreateHttpMessage(HttpMethod httpMethod, string endpoint)
        {
            var message = new HttpRequestMessage(httpMethod, endpoint);
            message.Headers.Add("Accept", "application/json");
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", AppState.CurrentActiveUser.Value);
            return message;
        }

        /// <summary>
        /// Get all entities that satisfy <typeparamref name="TQueryParameters"/> and commite them locally overriding local cache
        /// </summary>
        /// <param name="queryParameters">Query Parameters to be sent to DBService</param>
        /// <returns><see cref="Task"/></returns>
        protected async Task<bool> HttpGetQithQueryParametersAsync(TQueryParameters queryParameters, TId2 businessId)
        {
            try
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    HttpRequestMessage message;
                    if (queryParameters == default)
                        message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/Index");
                    else
                        message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/Index?{queryParameters.GetStringRepresentation()}");
                    message.Headers.Add("businessId", businessId.ToString());
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var entities = JsonConvert.DeserializeObject<IEnumerable<TEntity>>(json);
                        //Consider adding a check for when DbAction exists
                        foreach (var entity in entities)
                        {
                            if (await Exists(entity.IdOne, entity.IdTwo))
                            {
                                await base.Update(entity);
                            }
                            else
                            {
                                await base.Create(entity);
                            }
                        }

                        if (RepositoryContext.ChangeTracker.HasChanges())
                        {
                            RepositoryContext.SetSyncState(true);
                            await RepositoryContext.SaveChangesAsync();
                            RepositoryContext.SetSyncState(false);
                        }

                        foreach (var entity in entities)
                            DetachEntity(entity);
                    }
                    else
                        throw new Exception("Response error");
                }
            }
            catch (Exception unhandleableEx) when (unhandleableEx is ArgumentException or InvalidOperationException)
            {
                Crashes.TrackError(unhandleableEx);
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(TEntity).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return false;
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                if(RepositoryContext.Database.CurrentTransaction != null)
                    RepositoryContext.Database.RollbackTransaction();

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(TEntity).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database rolled back" }
                });
                return false;
            }
            return true;
        }

        /// <summary>
        /// <see cref="string"/> safe get <see cref="Type.FullName"/>
        /// </summary>
        /// <param name="type"><see cref="Type"/> of Entity</param>
        /// <returns><see cref="string"/> representation of <see cref="Type.FullName"/></returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected static string GetTypeFullName(Type type)
        {
            if(string.IsNullOrEmpty(type.FullName))
                throw new ArgumentNullException("'Type.FullName' is Null 🤷‍");
            return type.FullName;
        }

        /// <summary>
        /// Create a <see cref="DBAction"/> entity
        /// </summary>
        /// <param name="databaseAction">Type of <see cref="DatabaseActions"/></param>
        /// <param name="entity">Entity to save</param>
        private void SaveDBAction(DatabaseActions databaseAction, TEntity entity)
        {
            SaveDBAction(databaseAction, GetTypeFullName(typeof(TEntity)), ConvertIdToString(entity.IdOne, entity.IdTwo));
        }

        /// <summary>
        /// Create a <see cref="DBAction"/> entity
        /// </summary>
        /// <param name="databaseAction">Type of <see cref="DatabaseActions"/></param>
        /// <param name="typeName"><see cref="string"/> representation of <see cref="Type.FullName"/></param>
        /// <param name="entityId">JSON <see cref="string"/> represenation of <typeparamref name="TId1"/></param>
        private void SaveDBAction(DatabaseActions databaseAction, string typeName, string entityId)
        {
            var query = RepositoryContext.Set<DBAction>().Where(dA => dA.TypeName.Equals(typeName) && dA.RecordId.Equals(entityId));

            if (query.Any())
            {
                foreach (var record in query)
                    RepositoryContext.Set<DBAction>().Remove(record);
            }

            var dbAction = new DBAction(typeName, entityId, databaseAction);
            RepositoryContext.Set<DBAction>().Add(dbAction);
        }

        /// <summary>
        /// Detach the entity from <see cref="Microsoft.EntityFrameworkCore.ChangeTracking"/>
        /// </summary>
        /// <param name="entity">Entity to detach</param>
        private void DetachEntity(TEntity entity) => SetState(entity, Microsoft.EntityFrameworkCore.EntityState.Detached);

        /// <summary>
        /// Sync all local data to server
        /// </summary>
        /// <returns></returns>
        private async Task LocalToServerSync()
        {
            await Semaphore.WaitAsync();
            if (Connectivity.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    var query = RepositoryContext.Set<DBAction>().Where(dA => dA.TypeName.Equals(GetTypeFullName(typeof(TEntity))));
                    if (query.Any())
                    {
                        foreach (var dBAction in query)
                        {
                            var (idOne, idTwo) = ConvertStringToId(dBAction.RecordId);
                            var entity = await base.FindById(idOne, idTwo);
                            HttpRequestMessage message;
                            HttpResponseMessage response;
                            switch (dBAction.Action)
                            {
                                case DatabaseActions.Created:
                                    message = CreateHttpMessage(HttpMethod.Post, $"{EndpointEntity}?IsSync=True");
                                    message.Content = new StringContent(JsonConvert.SerializeObject(Activator.CreateInstance(typeof(TFormBody), entity)));
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    break;
                                case DatabaseActions.Modified:
                                    message = CreateHttpMessage(HttpMethod.Put, $"{EndpointEntity}/{entity.IdOne}?IsSync=True");
                                    message.Headers.Add("BusinessId", entity.IdTwo.ToString());
                                    message.Content = new StringContent(JsonConvert.SerializeObject(Activator.CreateInstance(typeof(TFormBody), entity)));
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    break;
                                case DatabaseActions.Deleted:
                                    message = CreateHttpMessage(HttpMethod.Delete, $"{EndpointEntity}/{entity.IdOne}");
                                    message.Headers.Add("BusinessId", entity.IdTwo.ToString());
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    break;
                            }
                        }
                    }
                }
                finally
                {
                    Semaphore.Release();
                }
            }
        }

#if DEBUG
        private HttpClientHandler GetInsecureHandler()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => {
                    if (cert.Issuer.Equals("CN=localhost"))
                        return true;
                    return errors == System.Net.Security.SslPolicyErrors.None;
                }
            };

            return handler;
        }
#endif
#endregion

        /// <summary>
        /// Convert <see cref="string"/> to <see cref="Tuple{TId1, TId2}"/>
        /// </summary>
        /// <param name="id">JSON serialised <see cref="string"/> to convert</param>
        /// <returns>Converted <see cref="Tuple{TId1, TId2}"/></returns>
        protected abstract (TId1 id1, TId2 id2) ConvertStringToId(string id);

        /// <summary>
        /// Convert <see cref="Tuple{TId1, TId2}"/> to <see cref="string"/>
        /// </summary>
        /// <param name="id1"><typeparamref name="TId1"/> to convert</param>
        /// <param name="id2"><typeparamref name="TId2"/> to convert</param>
        /// <returns>Converted JSON <see cref="string"/></returns>
        protected abstract string ConvertIdToString(TId1 id1, TId2 id2);

#region IDisposable implementation
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {

                }

                Client.Dispose();
                Semaphore.Dispose();
                disposedValue = true;
            }
        }

        ~CompositeRepositoryBase()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
#endregion
    }
}
