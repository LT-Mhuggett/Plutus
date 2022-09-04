using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities.Models;

namespace Plutus.Entities
{
    public class SqliteDbContext : RepositoryContext
    {
        #region Fields
        //private readonly string _connString = "Data Source=db.db";
        #endregion

        #region DbSets for SQLite DB only
        public DbSet<DBAction> DbActions { get; set; }
        #endregion

        public SqliteDbContext(string connString, string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                //_connString = new SqliteConnectionStringBuilder(connString)
                //{
                //    Mode = SqliteOpenMode.ReadWriteCreate
                //}.ToString();
                _systemName = "Plutus.App";
            }
        }

        public SqliteDbContext(DbContextOptions options) : base(options)
        {
            _systemName = "Plutus.App";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //if(!optionsBuilder.IsConfigured)
            //{
            //  optionsBuilder.UseSqlite(_connString);
            //}
            base.OnConfiguring(optionsBuilder);
        }
    }
}
