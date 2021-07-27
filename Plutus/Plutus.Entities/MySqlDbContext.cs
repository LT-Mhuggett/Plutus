using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plutus.Entities.Models;
using System.Net.Http;

namespace Plutus.Entities
{
    public class MySqlDbContext : RepositoryContext
    {
        #region Fields
        private readonly string _connString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus;Port=3306;Persist Security Info=false;Connect Timeout=300";
        //public int? BussinessId;
        //public readonly ObjectIdProvider _objectIdProvider;

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
            //CurrentUser = "Sean";
            //_objectIdProvider = objectIdProvider;
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
            var test = _objectIdProvider.ObjectId;

            //RefreshUserDataAsync
            // Works as expected
            //modelBuilder.Entity<Item>().HasQueryFilter(_ => _.IdTwo == _bussinessIdProvider.ObjectId);

            // Does not work
            // modelBuilder.ApplyConfiguration(new BussinessConfiguration(_bussinessIdProvider));
        }*/
    }

   /* public class ObjectIdProvider : IObjectIdProvider
    {
        public string ObjectId { get; set; }

        public ObjectIdProvider(string objectId)
        {
            ObjectId = objectId;
        }
    }
*/
    /*public interface IObjectIdProvider
    {
        public string ObjectId { get; set; }
    }*/

    /*public class ObjectConfiguration : IEntityTypeConfiguration<Employee>
    {
        private readonly ObjectIdProvider _objectIdProvider;

        public ObjectConfiguration(ObjectIdProvider objectIdProvider)
        {
            _objectIdProvider = objectIdProvider;
        }

        public void Configure(EntityTypeBuilder<Employee> builder)
        {
            builder.HasQueryFilter(_ => _.Id == _objectIdProvider.ObjectId);
        }
    }*/
}
