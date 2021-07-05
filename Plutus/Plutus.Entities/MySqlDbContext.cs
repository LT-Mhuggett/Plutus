using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plutus.Entities.Models;

namespace Plutus.Entities
{
    public class MySqlDbContext : RepositoryContext
    {
        #region Fields
        private readonly string _connString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus;Port=3306;Persist Security Info=false;Connect Timeout=300";
        //public int? BussinessId;
        //public readonly BussinessIdProvider _bussinessIdProvider;
        
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
            //_bussinessIdProvider = bussinessIdProvider;
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

        /*protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Works as expected
            modelBuilder.Entity<Item>().HasQueryFilter(_ => _.IdTwo == _bussinessIdProvider.BussinessId);

            // Does not work
            //modelBuilder.ApplyConfiguration(new BussinessConfiguration(_bussinessIdProvider));
        }*/
    }

   /* public class BussinessIdProvider : IBussinessIdProvider
    {
        public string BussinessId { get; set; }

        public BussinessIdProvider(string bussinessId)
        {
            BussinessId = bussinessId;
        }
    }*/

    /*public interface IBussinessIdProvider
    {
        public string BussinessId { get; set; }
    }

    public class BussinessConfiguration : IEntityTypeConfiguration<Bussiness>
    {
        private readonly BussinessIdProvider _bussinessIdProvider;

        public BussinessConfiguration(BussinessIdProvider bussinessIdProvider)
        {
            _bussinessIdProvider = bussinessIdProvider;
        }

        public void Configure(EntityTypeBuilder<Bussiness> builder)
        {
            builder.HasQueryFilter(_ => _.Id == _bussinessIdProvider.BussinessId);
        }
    }*/
}
