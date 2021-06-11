using Microsoft.EntityFrameworkCore;

namespace Plutus.Entities
{
    public class MySqlDbContext : RepositoryContext
    {
        #region Fields
        private readonly string _connString = "Data Source=db.db";
        #endregion

        #region DbSets for MySql DB only

        #endregion
        public MySqlDbContext(DbContextOptions options) : base(options)
        {
            _systemName = "Plutus.DBService";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseMySql(_connString, ServerVersion.AutoDetect(_connString));
            }
            base.OnConfiguring(optionsBuilder);
        }
    }
}
