using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Plutus.Data;
using Plutus.Models;

namespace Plutus.Helpers
{
    internal class Database
    {
        private static DbContext _db;

        internal Database()
        {
            _db = new Context(Path.Combine(FileIO.GetLib(), "Database.db"));
            _db.Database.EnsureCreated();
        }

        internal void AddEmployee(EmployeeModel emp)
        {
            _db.Add(emp);
            _db.SaveChanges();
        }

        internal void AddStore(StoreModel store)
        {
            _db.Add(store);
            _db.SaveChanges();
        }
    }
}
