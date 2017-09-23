using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Plutus.Data;
using Plutus.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

        internal async Task<bool> Save()
        {
            try
            {
                await _db.SaveChangesAsync();
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
            var bag = new ItemModel() { ItemId = "BAG001", Name = "Bag", Desc = "Item to allow Customers to carry things", CatId = 1, VatId = 2, Price = .05m, Cost = 0.0m };
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
            var emp = _db.Employees
                .Include(e=>e.EmpAuths)
                .SingleOrDefault(e => e.Id.Equals(idEmail) || e.Email.Equals(idEmail));
            if (emp == null) return null;
            if (await Task.Run(() => Password.Verify(password, Convert.FromBase64String(emp.Salt),
                Convert.FromBase64String(emp.HashedPassword)))) return emp;
            emp = null;
            return null;
        }

        internal StoreModel GetStore(string id)
        {
            var store = _db.Stores
                .SingleOrDefault(s => s.StoreId.Equals(id));
            return store ?? null;
        }

        internal List<VatModel> GetVat()
        {
            var vats = _db.Vats.ToList();
            return vats ?? null;
        }

        internal List<CategoryModel> GetCats()
        {
            var cats = _db.Category.ToList();
            return cats ?? null;
        }

        internal string GetCatName(int id)
        {
            var catName = _db.Category
                .Where(c => c.Id.Equals(id))
                .Select(c => c.Name)
                .SingleOrDefault();
            return catName ?? null;
        }

        internal bool IsIdSame(string testId)
        {
            var test = _db.Items
                .Where(i => i.ItemId.Equals(testId))
                .Select(i => i).Any();
            return test;
        }

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

        internal List<ItemModel> GetItem(string temp)
        {
            if (temp == "")
                return null;
            var item = _db.Items
                .Include(a=>a.Vat)
                .Where(i => i.ItemId.Equals(temp) ||
                    i.Name.Contains(temp))
                .ToList();
            return item ?? null;
        }

        internal void UpdateItem(ItemModel item)
        {
            var query = from fItem in _db.Items
                        where fItem.ItemId.Equals(item.ItemId)
                        select fItem;
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

        internal PaymentMethodModel GetPayM(string name)
        {
            var payM = _db.PayMethods
                .Where(p => p.Name.Equals(name))
                .SingleOrDefault();
            return payM ?? null;
        }

        internal List<AuthActions> GetAllActions()
        {
            var Actions = _db.AuthActions.ToList();
            return Actions ?? null;
        }

        internal bool CheckSaleID(string tempId)
        {
            var test = _db.Sales
                .Where(i => i.SaleId.Equals(tempId))
                .Select(i => i).Any();
            return test;
        }

        internal TransactionModel CheckItemExistInSale(string saleId, string itemId)
        {
            var trans = _db.Trans
                .Include(t=>t.Sale)
                .Where(t => t.SaleId.Equals(saleId) && t.ItemId.Equals(itemId))
                .FirstOrDefault();
            return trans??null;
        }

        internal NoteModel GetNote(string noteTemp)
        {
            var note = _db.Notes
                .Where(n => n.Note.Equals(noteTemp))
                .FirstOrDefault();
            return note ?? null;
        }

        internal List<ItemModel> GetAllItems()
        {
            var items = _db.Items
                .Include(i => i.Vat)
                .Include(i=>i.Cat)
                .Include(i=>i.Transactions)
                .Include(i=>i.Stock)
                .ToList();
            return items ?? null;
        }

        internal List<DateTime> GetAllDatesOfSale()
        {
            var dOS = _db.Sales
                .Select(s => s.DateOfSale)
                .Distinct()
                .ToList();
            return dOS ?? null;
        }

        internal List<SaleModel> GetSales(string condition)
        {
            var sales = _db.Sales
                .Include(s => s.Notes)
                .Include(s => s.Refunded)
                .Include(s => s.Refunds)
                .Include(s => s.Transactions)
                .Include(s => s.PaySales)
                .Where(s => s.DateOfSale.ToString().Contains(condition)
                    || s.EmployeeId.Equals(condition))
                .ToList();
            return sales ?? null;
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
