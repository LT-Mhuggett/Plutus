using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Plutus.Models;

namespace Plutus.Data
{
    public sealed class Context : DbContext
    {
        public DbSet<EmployeeModel> Employees { get; set; }
        public DbSet<StoreModel> Stores { get; set; }

        private readonly string _databasePath;

        public Context(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Filename={_databasePath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }
    }
}
