using Microsoft.AppCenter.Crashes;
using Microsoft.EntityFrameworkCore;
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
using System.Text;

namespace Plutus.Frontend.ClientUI.Services.Repository
{
    public class EmployeeRepository : Plutus.Repository.EmployeeRepository, IEmployeeRepository
    {
        private bool _disposedValue;
        internal SemaphoreSlim Semaphore { get; init; }
        protected ILogger Logger { get; init; }
        protected HttpClient Client { get; init; }
        protected virtual string EndpointEntity { get; init; }
        protected IAppState AppState { get; init; }
        public EmployeeRepository(RepositoryContext repositoryContext, SemaphoreSlim globalReposoitorySemaphore, AppSettings appSettings, ILogger logger, IAppState appState) : base(repositoryContext)
        {
            Semaphore = globalReposoitorySemaphore;
            Logger = logger;
            EndpointEntity = "Employee";
            Client = new HttpClient
            {
                BaseAddress = new Uri($"{appSettings.DBServiceURL}/api/")
            };
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
        /// <param name="employee">Employee to create</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If entity is null or empty</exception>
        public override async Task<bool> Create(Employee employee)
        {
            try
            {
                await Semaphore.WaitAsync();
            }catch(Exception ex)
            {
                Crashes.TrackError(ex);
            }

            try
            {
                if(employee == default || employee == null)
                    throw new ArgumentException("Entity is Empty or null!");

                await base.Create(employee);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Post, $"{EndpointEntity}?IsSync=True");
                    message.Headers.Add("businessId", AppState.ToString());
                    message.Content = new StringContent(JsonConvert.SerializeObject(new EmployeeBody(employee)), Encoding.UTF8, "application/json");
                    var response = await Client.SendAsync(message);
                    if (!response.IsSuccessStatusCode)
                        SaveDBAction(DatabaseActions.Created, employee);
                }
                else
                    SaveDBAction(DatabaseActions.Created, employee);
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Employee).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "Saving DBAction locally for API push later" }
                });
                SaveDBAction(DatabaseActions.Created, employee);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                DetachEntity(employee);
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Employee).FullName },
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

                DetachEntity(employee);

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
        /// <param name="employee">Entity to update</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If entity is null or empty</exception>
        public override async Task<bool> Update(Employee employee)
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
                if (employee == default || employee == null)
                    throw new ArgumentException("Entity is Empty or null!");

                await base.Update(employee);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var getMessage = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{employee.Id}");
                    var getResponse = await Client.SendAsync(getMessage);
                    if (getResponse.IsSuccessStatusCode)
                    {
                        var entityToChange = JsonConvert.DeserializeObject<Employee>(await getResponse.Content.ReadAsStringAsync());
                        var patchMessage = CreateHttpMessage(HttpMethod.Patch, $"{EndpointEntity}/{employee.Id}?IsSync=True");
                        var patchDocument = entityToChange.CreatePatch(employee);
                        var jsonPatchDocument = JsonConvert.SerializeObject(patchDocument);
                        patchMessage.Content = new StringContent(jsonPatchDocument, Encoding.UTF8, "application/json-patch+json");
                        var response = await Client.SendAsync(patchMessage);
                        if (!response.IsSuccessStatusCode)
                        {
                            SaveDBAction(DatabaseActions.Modified, employee);
                        }
                    }
                    else
                    {
                        SaveDBAction(DatabaseActions.Modified, employee);
                    }
                }
                else
                {
                    SaveDBAction(DatabaseActions.Modified, employee);
                }

                if (RepositoryContext.ChangeTracker.HasChanges())
                    await RepositoryContext.SaveChangesAsync();
                DetachEntity(employee);
            }
            catch (Exception unhandleableEx) when (unhandleableEx is ArgumentException or InvalidOperationException)
            {
                Crashes.TrackError(unhandleableEx);
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Employee).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "Saving DBAction locally for API push later" }
                });
                SaveDBAction(DatabaseActions.Modified, employee);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                DetachEntity(employee);
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Employee).FullName },
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

                DetachEntity(employee);

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
        /// <param name="employee">Entity to delete</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If entity is null or empty</exception>
        public override async Task<bool> Delete(Employee employee)
        {
            var employeeId = employee.Id;
            var convertedIdStrings = ConvertIdToString(employee.Id, AppState.Business.Id);

            var typeEntity = typeof(Employee).FullName;
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
                if (employee == default || employee == null)
                    throw new ArgumentException("Entity is Empty or null!");

                await base.Delete(employee);
                await RepositoryContext.SaveChangesAsync();

                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Delete, $"{EndpointEntity}/{employeeId}");
                    message.Headers.Add("BusinessId", AppState.Business.Id.ToString());
                    var response = await Client.SendAsync(message);
                    if (!response.IsSuccessStatusCode)
                    {
                        SaveDBAction(DatabaseActions.Deleted, typeEntity, convertedIdStrings);
                    }
                }
                else
                {
                    SaveDBAction(DatabaseActions.Deleted, typeEntity, convertedIdStrings);
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
                    { "Entity", typeof(Employee).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "Saving DBAction locally for API push later" }
                });
                SaveDBAction(DatabaseActions.Deleted, typeEntity, convertedIdStrings);
            }
            catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
            {
                RepositoryContext.Database.RollbackTransaction();
                DetachEntity(employee);
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Employee).FullName },
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

                DetachEntity(employee);

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

        public override async Task<IEnumerable<Employee>> GetAll()
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(default);
                return await base.GetAll();
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public new virtual async Task<IQueryable<Employee>> GetAllQueryable()
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(default);
                return base.GetAllQueryable();
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public async Task<IEnumerable<Employee>> FindAllByCondition(EmployeeParameters queryParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters);
                return await base.FindAllByCondition(queryParameters.GetExpression());
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public async Task<IQueryable<Employee>> FindAllByConditionQueryable(EmployeeParameters queryParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters);
                return base.FindAllByConditionQueryable(queryParameters.GetExpression());
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public async Task<Employee> FindFirstByCondition(EmployeeParameters queryParameters)
        {
            try
            {
                await Semaphore.WaitAsync();
                await HttpGetQithQueryParametersAsync(queryParameters);
                return await base.FindFirstByCondition(queryParameters.GetExpression());
            }
            finally
            {
                Semaphore.Release();
            }
        }

        public override async Task<Employee> FindById(Guid employeeId, Guid businessId)
        {
            try
            {
                await Semaphore.WaitAsync();

                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}/{employeeId}");
                    message.Headers.Add("BusinessId", businessId.ToString());
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var employee = JsonConvert.DeserializeObject<Employee>(await response.Content.ReadAsStringAsync());
                        if (await Exists(employeeId, businessId))
                            RepositoryContext.Set<Employee>().Update(employee);
                        else
                            await RepositoryContext.Set<Employee>().AddAsync(employee);

                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);
                        DetachEntity(employee);
                    }
                }
                return RepositoryContext.Set<Employee>().First(e=>e.Id.Equals(employeeId) && e.BusinessId.Equals(businessId));
            }
            finally
            {
                Semaphore.Release();
            }
        }
        #endregion

        /// <summary>
        /// Detach the entity from <see cref="Microsoft.EntityFrameworkCore.ChangeTracking"/>
        /// </summary>
        /// <param name="employee">Entity to detach</param>
        private void DetachEntity(Employee employee) => SetState(employee, EntityState.Detached);

        /// <inheritdoc/>
        public new virtual void SetState(Employee employee, EntityState entityState = EntityState.Modified) => RepositoryContext.Entry(employee).State = entityState;

        private void SaveDBAction(DatabaseActions databaseAction, string typeName, string jsonConvertedId)
        {
            var query = RepositoryContext.Set<DBAction>().Where(dA => dA.TypeName.Equals(typeName)
                                                                      && dA.RecordId.Equals(jsonConvertedId));

            if (query.Any())
            {
                foreach (var record in query)
                    RepositoryContext.Set<DBAction>().Remove(record);
            }

            var dbAction = new DBAction(typeof(Employee).FullName, jsonConvertedId, databaseAction);
            RepositoryContext.Set<DBAction>().Add(dbAction);
        }

        private void SaveDBAction(DatabaseActions databaseAction, Employee employee)
        {
            SaveDBAction(databaseAction, typeof(Employee).FullName, ConvertIdToString(employee.Id, AppState.Business.Id));
        }

        /// <summary>
        /// Create the common <see cref="HttpRequestMessage"/> to be sent to the DBService
        /// </summary>
        /// <param name="httpMethod">The HTTP method to use</param>
        /// <param name="endpoint">The endpoint to send to</param>
        /// <returns>The HTTP Request Message</returns>
        private HttpRequestMessage CreateHttpMessage(HttpMethod httpMethod, string endpoint)
        {
            var message = new HttpRequestMessage(httpMethod, endpoint);
            message.Headers.Add("Accept", "application/json");
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("bearer", AppState.CurrentActiveUser.Value);
            return message;
        }

        /// <summary>
        /// Get all entities that satisfy <see cref="EmployeeParameters"/> and commite them locally overriding local cache
        /// </summary>
        /// <param name="queryParameters">Query Parameters to be sent to DBService</param>
        /// <returns><see cref="bool"/></returns>
        private async Task<bool> HttpGetQithQueryParametersAsync(EmployeeParameters queryParameters)
        {
            try
            {
                if(Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    var message = CreateHttpMessage(HttpMethod.Get, $"{EndpointEntity}?{JsonConvert.SerializeObject(queryParameters)}");
                    message.Headers.Add("BusinessId", AppState.Business.Id.ToString());
                    var response = await Client.SendAsync(message);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var employees = JsonConvert.DeserializeObject<IEnumerable<Employee>>(json);
                        foreach (var employee in employees)
                        {
                            if (await Exists(employee.Id, employee.BusinessId))
                            {
                                RepositoryContext.Set<Employee>().Update(employee);
                            }
                            else
                            {
                                await RepositoryContext.Set<Employee>().AddAsync(employee);
                            }
                        }

                        RepositoryContext.SetSyncState(true);
                        await RepositoryContext.SaveChangesAsync();
                        RepositoryContext.SetSyncState(false);
                        foreach (var employee in employees)
                            DetachEntity(employee);
                    }
                    else
                        throw new Exception("Response error");
                }
            }
            catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                {
                    { "Entity", typeof(Employee).FullName },
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
                    { "Entity", typeof(Employee).FullName },
                    { "Exception Type", handleableEx.GetType().FullName },
                    { "Exception Message", handleableEx.Message },
                    { "Mitigating Action", "User informed of error, database rolled back" }
                });
                return false;
            }
            catch (Exception unhandleableEx) when (unhandleableEx is ArgumentException or InvalidOperationException)
            {
                Crashes.TrackError(unhandleableEx);
                return false;
            }

            return true;
        }

        public new virtual async Task<bool> Exists(Guid employeeId, Guid businessId) => await RepositoryContext.Set<Employee>().AnyAsync(e => e.Id.Equals(employeeId) && e.BusinessId.Equals(businessId));

        private async Task LocalToServerSync()
        {
            try
            {
                await Semaphore.WaitAsync();
            }
            catch(Exception ex)
            {
                Crashes.TrackError(ex);
            }

            if (Connectivity.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    var query = RepositoryContext.Set<DBAction>().Where(dA => dA.TypeName.Equals(typeof(Employee).FullName));
                    if (query.Any())
                    {
                        foreach (var dBAction in query)
                        {
                            var ids = ConvertStringToId(dBAction.RecordId);
                            var entity = await base.FindById(ids.id1, ids.id2);
                            HttpRequestMessage message;
                            HttpResponseMessage response;
                            switch (dBAction.Action)
                            {
                                case DatabaseActions.Created:
                                    message = CreateHttpMessage(HttpMethod.Post, $"{EndpointEntity}?IsSync=True");
                                    message.Headers.Add("BusinessId", ids.id2.ToString());
                                    message.Content = new StringContent(JsonConvert.SerializeObject(new EmployeeBody(entity)));
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    }
                                    break;
                                case DatabaseActions.Modified:
                                    message = CreateHttpMessage(HttpMethod.Put, $"{EndpointEntity}?IsSync=True");
                                    message.Headers.Add("BusinessId", ids.id2.ToString());
                                    message.Content = new StringContent(JsonConvert.SerializeObject(new EmployeeBody(entity)));
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    }
                                    break;
                                case DatabaseActions.Deleted:
                                    message = CreateHttpMessage(HttpMethod.Delete, $"{EndpointEntity}/{entity.Id}");
                                    message.Headers.Add("BusinessId", ids.id2.ToString());
                                    response = await Client.SendAsync(message);
                                    if (response.IsSuccessStatusCode)
                                        RepositoryContext.Set<DBAction>().Remove(dBAction);
                                    break;
                            }
                        }
                    }
                    await RepositoryContext.SaveChangesAsync();
                }
                catch (Exception handleableEx) when (handleableEx is HttpRequestException or TaskCanceledException)
                {
                    Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Network error", new Dictionary<string, string>
                    {
                        { "Entity", typeof(Employee).FullName },
                        { "Exception Type", handleableEx.GetType().FullName },
                        { "Exception Message", handleableEx.Message },
                        { "Mitigating Action", "User informed of error, database roll back" }
                    });
                }
                catch (Exception handleableEx) when (handleableEx is DbUpdateException or DbUpdateConcurrencyException)
                {
                    RepositoryContext.Database.RollbackTransaction();
                    Logger.LogEvent(AppLogLevel.Info, $"{GetType().FullName}: Database update error", new Dictionary<string, string>
                    {
                        { "Entity", typeof(Employee).FullName },
                        { "Exception Type", handleableEx.GetType().FullName },
                        { "Exception Message", handleableEx.Message },
                        { "Mitigating Action", "User informed of error, database rolled back" }
                    });
                }
                catch (Exception unhandleableEx) when (unhandleableEx is ArgumentException or InvalidOperationException)
                {
                    Crashes.TrackError(unhandleableEx);
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

        protected string ConvertIdToString(Guid id1, Guid id2)
        {
            return JsonConvert.SerializeObject(new { id1 = id1, id2 = id2 });
        }
        protected (Guid id1, Guid id2) ConvertStringToId(string id)
        {
            return JsonConvert.DeserializeObject<(Guid id1, Guid id2)>(id);
        }

        #region IDisposable implementation
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {

                }

                Client.Dispose();
                _disposedValue = true;
            }
        }

        ~EmployeeRepository()
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