using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Entities
{
    public class MSSqlLocalDbContext : RepositoryContext
    {
        #region Fields
        private readonly string _connString = @"Server=(localdb)\mssqllocaldb; database=plutus";
        #endregion

        public MSSqlLocalDbContext(DbContextOptions options) : base(options)
        {
            _systemName = "Plutus.DBService";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(_connString);
            }
            base.OnConfiguring(optionsBuilder);
        }
    }
}
