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

        #region DbSets for MySql DB only (server-side; NOT on the MAUI Sqlite context)
        // Platform tenancy (T1.1, evolve-in-place). Kept on MySqlDbContext so the shared
        // model + the MAUI SqliteDbContext are unaffected.
        public DbSet<Tenant> Tenants { get; set; }
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

        public MySqlDbContext(string connString) : base()
        {
            _systemName = "Plutus.DBService";
            _connString = connString;
            //CurrentUser = "Sean";
            //_objectIdProvider = objectIdProvider;
        }

        //public new object Business { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var serverVersion = new MySqlServerVersion(new System.Version(8, 0, 23));
                optionsBuilder.UseMySql(_connString, serverVersion);
            }
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // T1.1 tenancy (server-side only). TenantId shadow properties + query filters
            // are added in the next increment; this establishes the Tenants table.
            modelBuilder.Entity<Tenant>(e =>
            {
                e.ToTable("Tenants");
                e.HasKey(t => t.Id);
                e.Property(t => t.Name).HasMaxLength(200);
                e.Property(t => t.Plan).HasMaxLength(50);
                e.Property(t => t.ConnectionRef).HasMaxLength(100);
            });
        }
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
