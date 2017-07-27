using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Plutus.Data;
using Plutus.Models;
using System.Linq;

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
            _db.SaveChangesAsync();
        }

        internal void Init()
        {
            VatModel vat = new VatModel() {Name = "20%", Rate = 1.2};
            Add(vat);
            VatModel vat2 = new VatModel() {Name = "0%", Rate = 1};
            Add(vat2);
            VatModel vat3 = new VatModel() {Name = "No VAT", Rate = 1};
            Add(vat3);
            Save();
        }

        internal EmployeeModel Login(string idEmail, string password)
        {
            var emp = _db.Employees
                .Where(e => e.Id == idEmail || e.Email == idEmail)
                .SingleOrDefault();
            if (emp == null) return null;
            if (!Password.Verify(password, Convert.FromBase64String(emp.Salt),
                Convert.FromBase64String(emp.HashedPassword)))
            {
                emp = null;
                return null;
            }
            return emp;
        }

        internal StoreModel GetStore(string id)
        {
            var store = _db.Stores
                .Where(s => s.StoreId == id)
                .SingleOrDefault();
            if (store == null) return null;
            return store;
        }

        internal List<VatModel> GetVat()
        {
            List<VatModel> vats = _db.Vats.ToList();
            if (vats == null) return null;
            return vats;
        }

        internal List<CategoryModel> GetCats()
        {
            List<CategoryModel> cats = _db.Category.ToList();
            if (cats == null) return null;
            return cats;
        }

        internal string getCatName(int id)
        {
            var catName = _db.Category
                .Where(c => c.Id == id)
                .Select(c => c.Name)
                .SingleOrDefault();
            if (catName == null) return null;
            return catName;
        }

        internal bool isIdSame(string testId)
        {
            var test = _db.Items
                .Where(i => i.ItemId == testId)
                .Select(i => i).Any();
            return test;
        }
    }
}
