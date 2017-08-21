using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Plutus.Data;
using Plutus.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

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

        internal async void Save()
        {
            try
            {
                await _db.SaveChangesAsync();
            }
            catch(Exception e)
            {
                Console.Write(e);
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
            var AuthAction = new AuthActions() { Id = "Till", Name = "Till" };
            Add(AuthAction);
            var AuthAction1 = new AuthActions() { Id = "Refund20", Name = "Refund of 20", Amount = 20 };
            Add(AuthAction1);
            var AuthAction2 = new AuthActions() { Id = "Refund100", Name = "Refund of 100", Amount = 100 };
            Add(AuthAction2);
            var AuthAction3 = new AuthActions() { Id = "StaffARU", Name = "Staff Records Add, Read, Update" };
            Add(AuthAction3);
            var AuthAction4 = new AuthActions() { Id = "ItemARU", Name = "ITem Records Add, Read, Update" };
            Add(AuthAction4);
            var AuthAction5 = new AuthActions() { Id = "StaffV", Name = "Staff Records Read" };
            Add(AuthAction5);
            var AuthAction6 = new AuthActions() { Id = "StockU", Name = "Stock Update" };
            Add(AuthAction6);

            //Will be removed as only applies to UK, User will have to add manually
            var cat = new CategoryModel() { Name = "Customer Care", Description = "Items such as Bags etc." };
            Add(cat);
            Save();
            var bag = new ItemModel() { ItemId = "BAG001", Name = "Bag", Desc = "Item to allow Customers to carry things", CatId = 1, VatId = 2, Price = .05m, Cost = 0.0m };
            Add(bag);

            //There will be a more detailed setup page this temporay
            var payM = new PaymentMethodModel() { Name = "Card", Charge = 0.5m };
            Add(payM);
            var payM2 = new PaymentMethodModel() { Name = "Cash", Charge = 0.0m };
            Add(payM2);
            Save();
        }

        internal async Task<EmployeeModel> Login(string idEmail, string password)
        {
            var emp = _db.Employees
                .Include(e=>e.Actions)
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
    }
}
