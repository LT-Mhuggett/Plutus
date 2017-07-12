using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Plutus.Models;

namespace Plutus.Data
{
    public sealed class EmpStoreContext : DbContext
    {
        public DbSet<EmployeeModel> Employees { get; set; }
        public DbSet<StoreModel> Stores { get; set; }

        private readonly string _databasePath;

        public EmpStoreContext(string databasePath)
        {
            _databasePath = databasePath;
            Database.Migrate();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Filename={_databasePath}");
        }
    }
}
