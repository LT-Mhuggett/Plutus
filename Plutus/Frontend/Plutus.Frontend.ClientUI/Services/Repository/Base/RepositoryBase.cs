using Microsoft.AppCenter.Crashes;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Plutus.Entities;
using Plutus.Entities.Enums;
using Plutus.Entities.Models;
using Plutus.Frontend.ClientUI.Core.AppSettings;
using Plutus.Frontend.ClientUI.Core.Extensions;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Repository.QueryParameters;
using System.Text;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;
using System.Diagnostics;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Entities.FormBodies;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;

namespace Plutus.Frontend.ClientUI.Services.Repository.Base
{
    public abstract class RepositoryBase<TEntity, TId, TFormBody, TQueryParameters>
        : Plutus.Repository.Base.RepositoryBase<TEntity, TId>, IDisposable, IRepositoryBase<TEntity, TId, TFormBody, TQueryParameters>
        where TEntity : Base<TId>, new()
        where TFormBody : FormBody<TEntity>
        where TQueryParameters : QueryParameters<TEntity, TId>
    {
        private bool disposedValue;
        internal readonly SemaphoreSlim Semaphore;
        protected ILogger Logger { get; }
        protected HttpClient Client { get; }
        protected virtual string EndpointEntity { get; init; }
        protected virtual IAppState AppState { get; init; }

        public RepositoryBase(RepositoryContext repositoryContext, SemaphoreSlim globalRepositorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext)
        {
            Semaphore = globalRepositorySemaphore;
            Logger = logger;
            AppState = appState;
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

            Connectivity.ConnectivityChanged += async (sender, args) => 
            {
                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
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
        public override async Task<bool> Create(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            try
            {
                await Semaphore.WaitAsync();
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }

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
                    message.Content = new StringContent(JsonConvert.SerializeObject(Activator.CreateInstance(typeof(TFormBody), entity)), Encoding.UTF8, "application/json");
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
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(TEntity).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "Saving DBAction locally for API push later" }
                });
                SaveDBAction(DatabaseActions.Created, entity);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                DetachEntity(entity);
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(TEntity).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database rolled back" }
                });
                return false;
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
            }
            finally
            {
                if (RepositoryContext.ChangeTracker.HasChanges())
                    await RepositoryContext.SaveChangesAsync();

                DetachEntity(entity);

                try
                {
                    Semaphore.Release();
                }
                catch (Exception ex)
                {
                    Crashes.TrackError(ex);
                }
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
        public override async Task<bool> Update(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            try
            {
                await Semaphore.WaitAsync();
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }

            try
            {
                //Ensure Entity is set
                if (entity == default || entity == null)
                    throw new ArgumentException("Entity is Empty or null!");

                await base.Update(entity);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var getMessage = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{entity.Id}");
                    var getResponse = await Client.SendAsync(getMessage);
                    if (getResponse.IsSuccessStatusCode)
                    {
                        var entityToChange = JsonConvert.DeserializeObject<TEntity>(await getResponse.Content.ReadAsStringAsync());
                        var patchMessage = CreateHttpMessage(HttpMethod.Patch, $"{EndpointEntity}/{entity.Id}?IsSync=True");
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
                    await RepositoryContext.SaveChangesAsync();
                DetachEntity(entity);
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
                    { "Mitigating Action", "Saving DBAction locally for API push later" }
                });
                SaveDBAction(DatabaseActions.Modified, entity);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                DetachEntity(entity);
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(TEntity).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database rolled back" }
                });
                return false;
            }
            finally
            {
                if (RepositoryContext.ChangeTracker.HasChanges())
                    await RepositoryContext.SaveChangesAsync();

                DetachEntity(entity);

                try
                {
                    Semaphore.Release();
                }
                catch (Exception ex)
                {
                    Crashes.TrackError(ex);
                }
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
        public override async Task<bool> Delete(TEntity entity, IDbContextTransaction dbTransaction = default)
        {
            var entityId = entity.Id.ToString();
            var typeEntity = GetTypeFullName(typeof(TEntity));
            try
            {
                await Semaphore.WaitAsync();
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }

            try
            {
                //Ensure Entity is set
                if (entity == default || entity == null)
                    throw new ArgumentException("Entity is Empty or null!");

                await base.Delete(entity);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Delete, $"{EndpointEntity}/{entityId}");
                    var response = await Client.SendAsync(message);
                    if (!response.IsSuccessStatusCode)
                    {
                        SaveDBAction(DatabaseActions.Deleted, typeEntity, entityId);
                    }
                }
                else
                {
                    SaveDBAction(DatabaseActions.Deleted, typeEntity, entityId);
                }

                if (RepositoryContext.ChangeTracker.HasChanges())
                    await RepositoryContext.SaveChangesAsync();
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
                    { "Mitigating Action", "Saving DBAction locally for API push later" }
                });
                SaveDBAction(DatabaseActions.Deleted, typeEntity, entityId);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                DetachEntity(entity);
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(TEntity).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database rolled back" }
                });
                return false;
            }
            finally
            {
                if (RepositoryContext.ChangeTracker.HasChanges())
                    await RepositoryContext.SaveChangesAsync();

                DetachEntity(entity);

                try
                {
                    Semaphore.Release();
                }
                catch (Exception ex)
                {
                    Crashes.TrackError(ex);
                }
            }
            return true;
        }

        public override async Task<IEnumerable<TEntity>> GetAll()
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(default);
                return await base.GetAll();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                Semaphore.Release();
            }
            return await base.GetAll();
        }

        public new virtual async Task<IQueryable<TEntity>> GetAllQueryable()
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(default);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                Semaphore.Release();
            }
            return base.GetAllQueryable();
        }

        public virtual async Task<IEnumerable<TEntity>> FindAllByCondition(TQueryParameters queryParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                Semaphore.Release();
            }
            return await base.FindAllByCondition(queryParameters.GetExpression());
        }

        public virtual async Task<IQueryable<TEntity>> FindAllByConditionQueryable(TQueryParameters queryParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                Semaphore.Release();
            }
            return base.FindAllByConditionQueryable(queryParameters.GetExpression());
        }

        public virtual async Task<TEntity> FindFirstByCondition(TQueryParameters queryParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters);
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                Semaphore.Release();
            }
            return await base.FindFirstByCondition(queryParameters.GetExpression());
        }

        public override async Task<TEntity> FindById(TId id)
        {
            try
            {
                await Semaphore.WaitAsync();
                
                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{id}");
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var entity = JsonConvert.DeserializeObject<TEntity>(await response.Content.ReadAsStringAsync());
                        if (await Exists(id))
                        {
                            await base.Update(entity);
                        }
                        else
                        {
                            await base.Create(entity);
                        }

                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);

                        DetachEntity(entity);
                    }
                }
                return await base.FindById(id);
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
                return await base.FindById(id);
            }
            catch(Exception handleableEx) when(handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Business).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database roll back" }
                });
                return await base.FindById(id);
            }
            catch (Exception unhandleableEx)
            {
                Crashes.TrackError(unhandleableEx);
                return await base.FindById(id);
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
        protected async Task<bool> HttpGetQithQueryParametersAsync(TQueryParameters queryParameters)
        {
            try
            {
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    HttpRequestMessage message;
                    if(queryParameters == default)
                        message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/Index");
                    else
                        message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/Index?{queryParameters.GetStringRepresentation()}");
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var entities = JsonConvert.DeserializeObject<IEnumerable<TEntity>>(json);
                        //Consider adding a check for when DbAction exists
                        foreach (var entity in entities)
                        {
                            if (await Exists(entity.Id))
                            {
                                await base.Update(entity);
                            }
                            else
                            {
                                await base.Create(entity);
                            }
                        }
                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);
                        foreach (var entity in entities)
                            DetachEntity(entity);
                    }

                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return false;
                    
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
            if (string.IsNullOrEmpty(type.FullName))
                throw new ArgumentNullException(nameof(type), "'Type.FullName' is Null 🤷‍");
            return type.FullName;
        }

        /// <summary>
        /// Create a <see cref="DBAction"/> entity
        /// </summary>
        /// <param name="databaseAction">Type of <see cref="DatabaseActions"/></param>
        /// <param name="entity">Entity to save</param>
        protected void SaveDBAction(DatabaseActions databaseAction, TEntity entity)
        {
            SaveDBAction(databaseAction, GetTypeFullName(typeof(TEntity)), ConvertIdToString(entity.Id));
        }

        /// <summary>
        /// Create a <see cref="DBAction"/> entity
        /// </summary>
        /// <param name="databaseAction">Type of <see cref="DatabaseActions"/></param>
        /// <param name="typeName"><see cref="string"/> representation of <see cref="Type.FullName"/></param>
        /// <param name="entityId">JSON <see cref="string"/> represenation of <typeparamref name="TId"/></param>
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
        protected void DetachEntity(TEntity entity) => SetState(entity, Microsoft.EntityFrameworkCore.EntityState.Detached);

        /// <summary>
        /// Sync all local data to server
        /// </summary>
        /// <returns></returns>
        private async Task LocalToServerSync()
        {
            try
            {
                await Semaphore.WaitAsync();
            }
            catch (Exception ex)
            {
                Crashes.TrackError(ex);
            }

            if (Connectivity.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    var query = RepositoryContext.Set<DBAction>().Where(dA => dA.TypeName.Equals(GetTypeFullName(typeof(TEntity))));
                    if (query.Any())
                    {
                        foreach (var dBAction in query)
                        {
                            var entity = await base.FindById(ConvertStringToId(dBAction.RecordId));
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
                                    message = CreateHttpMessage(HttpMethod.Put, $"{EndpointEntity}/{entity.Id}?IsSync=True");
                                    message.Content = new StringContent(JsonConvert.SerializeObject(Activator.CreateInstance(typeof(TFormBody), entity)));
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    break;
                                case DatabaseActions.Deleted:
                                    message = CreateHttpMessage(HttpMethod.Delete, $"{EndpointEntity}/{entity.Id}");
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
                    try
                    {
                        Semaphore.Release();
                    }
                    catch (Exception ex)
                    {
                        Crashes.TrackError(ex);
                    }
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
        /// Convert <typeparamref name="TId"/> to <see cref="string"/>
        /// </summary>
        /// <param name="id">JSON serialised <see cref="string"/> to convert</param>
        /// <returns>Converted <typeparamref name="TId"/></returns>
        protected abstract TId ConvertStringToId(string id);

        /// <summary>
        /// Convert <see cref="string"/> to <typeparamref name="TId"/>
        /// </summary>
        /// <param name="id"><typeparamref name="TId"/> to convert</param>
        /// <returns>Converted JSON <see cref="string"/></returns>
        protected abstract string ConvertIdToString(TId id);

#region IDisposable implementation
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {

                }

                Client.Dispose();
                disposedValue = true;
            }
        }

        ~RepositoryBase()
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