using System;
using System.Collections.Generic;
using System.IO;
using Plutus.Data;
using Plutus.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Plutus.Models.Interface;
using System.Globalization;

namespace Plutus.Helpers
{
    internal class Database
    {
        private static Context _db;

        internal Database()
        {
            _db = new Context(Path.Combine(FileIO.GetLib(), "Database.db"));
            _db.Database.EnsureCreated();
        }

        internal async void Add<T>(T tmp) where T : class
        {
            await _db.Set<T>().AddAsync(tmp);
        }

        internal bool Save()
        {
            try
            {
                _db.SaveChanges();
                return true;
            }
            catch(Exception e)
            {
                Console.Write(e);
                return false;
            }
        }

        internal void RevertDbContextChanges()
        {
            foreach(EntityEntry entry in _db.ChangeTracker.Entries())
            {
                switch (entry.State)
                {
                    case EntityState.Modified:
                        entry.State = EntityState.Unchanged;
                        break;
                    case EntityState.Added:
                        entry.State = EntityState.Detached;
                        break;
                    case EntityState.Deleted:
                        entry.Reload();
                        break;
                    default:
                        break;
                }
            }
        }

        internal IQueryable<T> Get<T>() where T : class => _db.Set<T>();

        internal void Init()
        {
            var vat = new VatModel() { Name = "0%", Rate = 1 };
            Add(vat);
            var vat2 = new VatModel() {Name = "20%", Rate = 1.2};
            Add(vat2);
            var vat3 = new VatModel() {Name = "No VAT", Rate = 1};
            Add(vat3);

            //AuthActions Initalization
            var AuthAction = new AuthActions() { Name = "Till" };
            Add(AuthAction);
            var AuthAction1 = new AuthActions() { Name = "Refund20", Amount = 20 };
            Add(AuthAction1);
            var AuthAction2 = new AuthActions() { Name = "Refund100", Amount = 100 };
            Add(AuthAction2);
            var AuthAction3 = new AuthActions() { Name = "Staff" };
            Add(AuthAction3);
            var AuthAction4 = new AuthActions() { Name = "Item" };
            Add(AuthAction4);
            var AuthAction5 = new AuthActions() { Name = "Force Loggout All Users" };
            Add(AuthAction5);
            var AuthAction6 = new AuthActions() { Name = "Force Loggout Single User" };
            Add(AuthAction6);
            var AuthAction7 = new AuthActions() { Name = "Refund Unlimited", Amount = 100000 };
            Add(AuthAction7);
            var AuthAction8 = new AuthActions() { Name = "Report" };
            Add(AuthAction8);
            var AuthAction9 = new AuthActions() { Name = "Admin" };
            Add(AuthAction9);

            //Will be removed as only applies to UK, User will have to add manually
            var cat = new CategoryModel() { Name = "Customer Care", Description = "Items such as Bags etc." };
            Add(cat);
            _db.SaveChanges();
            var bag = new ItemModel() { Id = "BAG001", Name = "Bag", Desc = "Item to allow Customers to carry things", CatId = 1, VatId = 2, Price = .05m, Cost = 0.0m };
            Add(bag);

            //There will be a more detailed setup page this temporay
            var payM = new PaymentMethodModel() { Name = "Card", Charge = 0.5m, MinimumCharge = 5.0m };
            Add(payM);
            var payM2 = new PaymentMethodModel() { Name = "Cash", Charge = 0.0m, MinimumCharge = 0.0m };
            Add(payM2);
            _db.SaveChanges();
        }

        internal async Task<EmployeeModel> Login(string idEmail, string password)
        {
            var emp = Get<EmployeeModel>()
                .Include(e=>e.EmpAuths)
                .SingleOrDefault(e => e.Id.Equals(idEmail) || e.Email.Equals(idEmail));
            if (emp == null)
                return null;
            if (await Task.Run(() => 
                    Password.Verify(password, Convert.FromBase64String(emp.Salt), Convert.FromBase64String(emp.HashedPassword))))
                return emp;
            emp = null;
            return null;
        }

        internal IQueryable GetById<TOne, TTwo>(TTwo id) where TOne : class => Get<TOne>()
            .OfType<IBase<TTwo>>()
            .Where(m => m.Id.Equals(id));

        internal bool IsIdSame<TOne, TTwo>(TTwo id) where TOne : class => GetById<TOne, TTwo>(id)
            .OfType<TOne>().Select(i => i).Any();

        internal IQueryable<ItemModel> Search(string temp) => Get<ItemModel>()
            .Include(a => a.Vat)
            .Where(i => i.Id.Equals(temp) ||
                CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                    i.Name, temp, CompareOptions.IgnoreCase) >= 0);

        internal void UpdateStock(StockModel toUpdateModel)
        {
            var query = from stock in _db.Stocks
                        where stock.ItemId.Equals(toUpdateModel.ItemId) &&
                            stock.StoreId.Equals(toUpdateModel.StoreId)
                        select stock;
            foreach(StockModel stock in query)
            {
                stock.Quantity += toUpdateModel.Quantity;
            }
        }

        internal void UpdateItem(ItemModel item)
        {
            var query = GetById<ItemModel, string>(item.Id)
                .OfType<ItemModel>()
                .Select(i => i);

            foreach(ItemModel fItem in query)
            {
                fItem.Name = item.Name;
                fItem.Image = item.Image;
                fItem.Desc = item.Desc;
                fItem.Brand = item.Brand;
                fItem.CatId = item.CatId;
                fItem.Cost = item.Cost;
                fItem.VatId = item.VatId;
                fItem.Price = item.Price;
            }
        }

        internal IQueryable<PaymentMethodModel> GetPayM(string name) => Get<PaymentMethodModel>()
                .Where(p => p.Name.Equals(name));

        internal IQueryable<TransactionModel> CheckItemExistInSale(string saleId, string itemId) => Get<TransactionModel>()
                .Include(t => t.Sale)
                .Where(t => t.SaleId.Equals(saleId) && t.ItemId.Equals(itemId));

        internal NoteModel GetNote(string noteTemp) => Get<NoteModel>()
            .Where(n => n.Note.Equals(noteTemp))
            .FirstOrDefault();

        internal IQueryable<ItemModel> GetAllItems() => Get<ItemModel>()
            .Include(i => i.Vat)
            .Include(i => i.Cat)
            .Include(i => i.Transactions)
            .Include(i => i.Stock);
        
        internal IQueryable<SaleModel> GetSales(string condition) => Get<SaleModel>()
                .Include(s => s.Notes)
                .Include(s => s.Refunded)
                .Include(s => s.Refunds)
                .Include(s => s.Transactions)
                .Include(s => s.PaySales)
                .Where(s => s.DateOfSale.ToString().Contains(condition)
                    || s.EmployeeId.Equals(condition));

        internal List<DateTime> GetDateOfSales()
        {
            var data = Get<SaleModel>().ToList();
            return data.Select(s => s.DateOfSale).ToList();
        }

        internal IIncludableQueryable<EmployeeModel, StoreModel> GetAllEmps()
        {
            var emps = _db.Employees
                .Include(i => i.EmpAuths)
                .Include(i => i.Store);
            return emps;
        }

        internal EmployeeModel GetEmp(string id)
        {
            var emp = _db.Employees
                .Include(e => e.EmpAuths)
                .Include(e => e.Store)
                .Where(e => e.Id.Equals(id))
                .FirstOrDefault();
            return emp ?? null;
        }
    }
}
