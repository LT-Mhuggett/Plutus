using Database;
using Database.Enums;
using Database.Models;
using Database.Models.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Essentials;

namespace NatApp.Plutus.Helpers.Database
{
    internal class Database : IDisposable
    {
        private AppDBContext _db;
        private bool _disposed;

        /// <summary>
        /// Constructor, Sets up Database Context
        /// </summary>
        /// <param name="connString">Connection string</param>
        /// <param name="provider">Database provider</param>
        internal Database(DatabaseProvider provider, string empId = null)
        {
            string connString = "";
            switch (provider)
            {
                case DatabaseProvider.Sqlite:
                    connString = Path.Combine(FileSystem.AppDataDirectory, "Database.db");
                    break;
                case DatabaseProvider.Cloud:
                    throw new NotImplementedException();
            }
            if(App.GetViewModel().Store != null)
                _db = new AppDBContext(connString, provider, empId, App.GetViewModel().Store.Id);
            else
                _db = new AppDBContext(connString, provider, empId, null);
            if (provider == DatabaseProvider.Sqlite)
                if(!_db.CurrentVersion())
                {
                    //Perform DB backup

                    _db.Database.Migrate();
                }
        }

        /// <summary>
        /// Returns if Local DB exists
        /// </summary>
        /// <returns>True, Exists; False, Doesn't Exist</returns>
        internal static bool LocalDbExist()
        {
            if (File.Exists(Path.Combine(FileSystem.AppDataDirectory, "Database.db")))
                return true;
            else
                return false;
        }

        #region Basic add, update and delete functions
        /// <summary>
        /// Add object to ChangeTracking/set object ChangeTracking to Add
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <param name="tmp">object</param>
        /// <returns></returns>
        internal void Add<T>(T tmp) where T : class => _db.Set<T>().Add(tmp);

        /// <summary>
        /// Add array of objects to ChangeTracking/set objects ChangeTracking to Add
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <param name="tmp">array of objects</param>
        /// <returns></returns>
        internal void AddRangeAsync<T>(params T[] tmp) where T : class => _db.Set<T>().AddRange(tmp);

        /// <summary>
        /// Update object to ChangeTracking/set object ChangeTracking to Modified
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <param name="tmp">object</param>
        internal void Update<T>(T tmp) where T : class => _db.Set<T>().Update(tmp);

        /// <summary>
        /// Update array of objects to ChangeTracking/set objects ChangeTracking to Modified
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <param name="tmp">array of objects</param>
        internal void UpdateRange<T>(params T[] tmp) where T : class => _db.Set<T>().UpdateRange(tmp);

        /// <summary>
        /// Add object to ChangeTracking and/or set object ChangeTracking to Remove
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <param name="tmp">object</param>
        internal void Delete<T>(T tmp) where T : class => _db.Set<T>().Remove(tmp);

        /// <summary>
        /// Add objects to ChangeTracking and/or set objects ChangeTracking to Remove
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <param name="tmp">array of objects</param>
        internal void DeleteRange<T>(params T[] tmp) where T : class => _db.Set<T>().RemoveRange(tmp);
        #endregion

        #region Advanced Entity operations
        /// <summary>
        /// Revert all ChangeTracking states
        /// </summary>
        /// <returns>Task</returns>
        internal Task RevertDbContextChanges()
        {
            return Task.Run(() =>
            {
                foreach (var entity in _db.ChangeTracker.Entries())
                {
                    switch (entity.State)
                    {
                        case EntityState.Added:
                            entity.State = EntityState.Detached;
                            break;
                        case EntityState.Deleted:
                            entity.Reload();
                            break;
                        case EntityState.Modified:
                            entity.State = EntityState.Unchanged;
                            break;
                    }
                }
            });
        }

        /// <summary>
        /// Set object ChangeTracking to detached
        /// </summary>
        /// <param name="tmpEntity">object</param>
        internal void DetachEntity(object tmpEntity) => _db.Entry(tmpEntity).State = EntityState.Detached;

        /// <summary>
        /// Set all objects ChangeTracking to detached
        /// </summary>
        /// <param name="tmpEntities">List of objects</param>
        internal void DetachEntries(List<object> tmpEntities) => tmpEntities.ForEach(entity => _db.Entry(entity).State = EntityState.Detached);

        /// <summary>
        /// Add Entity to Tracking and set state to modified
        /// </summary>
        /// <param name="tmpEntity">object</param>
        internal void AttachEntity(object tmpEntity) => _db.Attach(tmpEntity).State = EntityState.Modified;

        /// <summary>
        /// Detach all Entities from context
        /// </summary>
        internal void DetachAllEntities()
        {
            foreach (var entity in _db.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added ||
                            e.State == EntityState.Modified ||
                            e.State == EntityState.Deleted ||
                            e.State == EntityState.Unchanged)
                .ToList())
            {
                DetachEntity(entity);
            }
        }
        #endregion

        /// <summary>
        /// Saves all ChangeTracking changes to DB
        /// </summary>
        /// <returns>If save was successfull</returns>
        internal bool Save()
        {
            try
            {
                _db.SaveChanges();
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return false;
            }
        }

        /// <summary>
        /// Init database for first time use
        /// </summary>
        public void Init()
        {
            var authAction = new AuthActions() { Name = "Till" };
            Add(authAction);
            var authAction1 = new AuthActions() { Name = "Refund20", Amount = 20 };
            Add(authAction1);
            var authAction2 = new AuthActions() { Name = "Refund100", Amount = 100 };
            Add(authAction2);
            var authAction3 = new AuthActions() { Name = "Staff" };
            Add(authAction3);
            var authAction4 = new AuthActions() { Name = "Item" };
            Add(authAction4);
            var authAction5 = new AuthActions() { Name = "Force Loggout All Users" };
            Add(authAction5);
            var authAction6 = new AuthActions() { Name = "Force Loggout Single User" };
            Add(authAction6);
            var authAction7 = new AuthActions() { Name = "Refund Unlimited", Amount = decimal.MaxValue };
            Add(authAction7);
            var authAction8 = new AuthActions() { Name = "Report" };
            Add(authAction8);
            var authAction9 = new AuthActions() { Name = "Admin" };
            Add(authAction9);
            var authAction10 = new AuthActions() { Name = "Management" };
            Add(authAction10);

            var payM = new PaymentMethodModel()
            {
                Name = "Card",
                Charge = 0.0m,
                MinimumCharge = 0.0m,
                IsChangeable = false,
                IsCashBackable = true
            };
            Add(payM);
            var payM2 = new PaymentMethodModel()
            {
                Name = "Cash",
                Charge = 0.0m,
                MinimumCharge = 0.0m,
                IsChangeable = true,
                IsCashBackable = false
            };
            Add(payM2);
        }

        #region Load operations
        /// <summary>
        /// Get the table instance for the model, without laoding in the data
        /// </summary>
        /// <typeparam name="T">Model representation of Table</typeparam>
        /// <returns>IQuerable</returns>
        internal IQueryable<T> Get<T>() where T : class => _db.Set<T>();

        /// <summary>
        /// Get any Object in the database by their id
        /// </summary>
        /// <typeparam name="TOne">Object to find type</typeparam>
        /// <typeparam name="TTwo">type of id to find</typeparam>
        /// <param name="id">id used to find Object</param>
        /// <returns>IQuerable</returns>
        internal IQueryable<TOne> GetById<TOne, TTwo>(TTwo id) where TOne : class => Get<TOne>()
            .OfType<IBase<TTwo>>()
            .Where(m => m.Id.Equals(id)).Cast<TOne>();

        internal IQueryable<SaleModel> GetAllBetweenDates(DateTime startDate, DateTime endDate) => Get<SaleModel>()
            .Where(entity => entity.DateOfSale.Date >= startDate.Date && entity.DateOfSale.Date <= endDate.Date);

        internal IQueryable<T> GetAllBetweenDates<T>(DateTime startDate, DateTime endDate) where T : class => Get<T>()
            .OfType<IAuditable>()
            .Where(entity => entity.Created.Date >= startDate.Date && entity.Created.Date <= endDate.Date).Cast<T>();

        /// <summary>
        /// check to see if item exists in database, does not load into context
        /// </summary>
        /// <typeparam name="TOne">Object to find type</typeparam>
        /// <typeparam name="TTwo">type of id to find</typeparam>
        /// <param name="id">id used to find Object</param>
        /// <returns>Whether or not an item exists</returns>
        internal bool IsExists<TOne, TTwo>(TTwo id) where TOne : class => GetById<TOne, TTwo>(id).Select(i => i).Any();

        /// <summary>
        /// Load in all items that satisfy the needle, uses Id and Name 
        /// </summary>
        /// <param name="needle">string used for searches</param>
        /// <returns></returns>
        internal IQueryable<ItemModel> Search(string needle) => Get<ItemModel>()
            .Include(a => a.Vat)
            .Where(i => i.Id.Equals(needle) ||
                CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                    i.Name, needle, CompareOptions.IgnoreCase) >= 0);

        /// <summary>
        /// find a signle item that satisfies the needle, uses id
        /// </summary>
        /// <param name="needle">string used for searching</param>
        /// <returns></returns>
        internal IQueryable<ItemModel> SearchId(string needle) => Get<ItemModel>()
            .Include(a => a.Vat)
            .Where(i => i.Id.Equals(needle));

        /// <summary>
        /// Load a single PaymentMehtod that satisfies the name param
        /// </summary>
        /// <param name="name">string used for search</param>
        /// <returns></returns>
        internal PaymentMethodModel GetPaymentMethod(string name) => Get<PaymentMethodModel>()
            .Where(p => p.Name.Equals(name))
            .FirstOrDefault();

        /// <summary>
        /// Get all transactions that satisfy the requirments
        /// </summary>
        /// <param name="saleId">id of sale to check</param>
        /// <param name="itemId">id of item to look for</param>
        /// <returns></returns>
        internal IQueryable<TransactionModel> GetTransactions(string saleId, string itemId) => Get<TransactionModel>()
            .Include(t => t.Sale)
            .Include(t => t.CheckoutItemChange)
            .Where(t => t.SaleId.Equals(saleId) && t.ItemId.Equals(itemId));

        /// <summary>
        /// Get a single note that satisfies the needle
        /// </summary>
        /// <param name="needle">string to search for</param>
        /// <returns></returns>
        internal NoteModel GetNote(string needle) => Get<NoteModel>()
            .FirstOrDefault(n => n.Note.Equals(needle));

        /// <summary>
        /// load all items
        /// </summary>
        /// <returns></returns>
        internal IQueryable<ItemModel> GetAllItems() => Get<ItemModel>()
            .Include(i => i.Vat)
            .Include(i => i.Cat)
            .Include(i => i.Transactions)
            .Include(i => i.Stock);

        /// <summary>
        /// load all items that satisfies the condition
        /// </summary>
        /// <param name="condition">Condition either Date of Sale, Employee id</param>
        /// <returns></returns>
        internal IQueryable<SaleModel> GetSales(string condition) => Get<SaleModel>()
            .Include(s => s.Notes)
            .Include(s => s.Refunded)
            .Include(s => s.Refunds)
            .Include(s => s.Transactions)
            .Include(s => s.PaySales)
            .ThenInclude(ps => ps.PayMethod)
            .Where(s => s.DateOfSale.ToString().Contains(condition)
                        || s.EmployeeId.Equals(condition));

        /// <summary>
        /// load all sales
        /// </summary>
        /// <returns></returns>
        internal IQueryable<SaleModel> GetSales() => Get<SaleModel>()
            .Include(s => s.Notes)
            .Include(s => s.Refunded)
            .Include(s => s.Refunds)
            .Include(s => s.Transactions)
            .ThenInclude(t => t.Item)
            .Include(s => s.PaySales)
            .ThenInclude(ps => ps.PayMethod);

        /// <summary>
        /// get all DateTime of sales 
        /// </summary>
        /// <returns></returns>
        internal IQueryable<DateTime> GetDateTimesOfSale() => Get<SaleModel>().Select(s => s.DateOfSale);

        /// <summary>
        /// load all employees
        /// </summary>
        /// <returns></returns>
        internal IIncludableQueryable<EmployeeModel, StoreModel> GetAllEmployees() => Get<EmployeeModel>()
            .Include(e => e.EmpAuths)
            .Include(e => e.Store);

        /// <summary>
        /// get single employee that satisfies the condtion
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal EmployeeModel GetEmployee(string id) => Get<EmployeeModel>()
            .Include(e => e.EmpAuths)
            .Include(e => e.Store)
            .FirstOrDefault(e => e.Id.Equals(id));

        /// <summary>
        /// get single employee using id or email
        /// </summary>
        /// <param name="idEmail"></param>
        /// <returns></returns>
        internal async Task<EmployeeModel> Login(string idEmail) => await Get<EmployeeModel>()
            .Include(e => e.EmpAuths)
                .ThenInclude(ea => ea.Auth)
            .SingleOrDefaultAsync(e =>
                e.Id.Equals(idEmail) || e.Email.Equals(idEmail, StringComparison.CurrentCultureIgnoreCase));
        #endregion

        internal void SetTrackingBehavior(QueryTrackingBehavior queryTrackingBehavior)
        {
            _db.ChangeTracker.QueryTrackingBehavior = queryTrackingBehavior;
        }

        #region Disposeable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _db.Dispose();
                }
                _disposed = true;
            }
        }

        ~Database()
        {
            Dispose(false);
        }
        #endregion
    }
}
