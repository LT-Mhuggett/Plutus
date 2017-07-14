using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Microsoft.EntityFrameworkCore;
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
            _db.Set<T>().Add(tmp);
        }

        internal void Save()
        {
            _db.SaveChanges();
        }

        internal void Init()
        {
            VatModel vat = new VatModel() {Name = "20%", Rate = .8};
            Add(vat);
            VatModel vat2 = new VatModel() {Name = "0%", Rate = 1};
            Add(vat2);
            VatModel vat3 = new VatModel() {Name = "No VAT", Rate = 1};
            Add(vat3);
            Save();
        }

        internal static object Login(string idEmail, string password)
        {
            EmployeeModel emp = _db.Employees.FirstOrDefault(
                e => e.Id.Equals(idEmail) ||
                     e.Email.Equals(idEmail));
            if (emp == null) return false;
            if (!Password.Verify(password, Convert.FromBase64String(emp.Salt),
                Convert.FromBase64String(emp.HashedPassword))) return false;
            return emp;
        }
    }
}
