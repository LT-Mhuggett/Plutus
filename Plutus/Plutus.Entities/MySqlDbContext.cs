using Microsoft.EntityFrameworkCore;

namespace Plutus.Entities
{
    public class MySqlDbContext : RepositoryContext
    {
        #region Fields
        private readonly string _connString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus;Port=3306;Persist Security Info=false;Connect Timeout=300";
        #endregion

        #region DbSets for MySql DB only

        #endregion
        public MySqlDbContext() : base()
        {
            _systemName = "Plutus.DBService";
        }
        public MySqlDbContext(DbContextOptions options) : base(options)
        {
            _systemName = "Plutus.DBService";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var serverVersion = new MySqlServerVersion(new System.Version(8, 0, 23));
                optionsBuilder.UseMySql(_connString, serverVersion);
            }
            base.OnConfiguring(optionsBuilder);
        }
    }
}
