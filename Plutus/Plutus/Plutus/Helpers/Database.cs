using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Plutus.Data;
using Plutus.Models;
using System.Linq;
using System.Threading.Tasks;

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

        internal void Add<T>(T tmp) where T : class
        {
            _db.Set<T>().AddAsync(tmp);
        }

        internal void Save()
        {
            try
            {
                _db.SaveChangesAsync();
            }
            catch(Exception e)
            {
                Console.Write(e);
            }
            
        }

        internal void Init()
        {
            var vat = new VatModel() {Name = "20%", Rate = 1.2};
            Add(vat);
            var vat2 = new VatModel() {Name = "0%", Rate = 1};
            Add(vat2);
            var vat3 = new VatModel() {Name = "No VAT", Rate = 1};
            Add(vat3);
            Save();
        }

        internal async Task<EmployeeModel> Login(string idEmail, string password)
        {
            var emp = _db.Employees
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
    }
}
