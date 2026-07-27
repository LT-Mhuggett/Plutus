using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Plutus.Entities.Exceptions;
using Plutus.Entities.Models;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;

namespace Plutus.Entities
{
    public class RepositoryContext : DbContext
    {
        #region Fields
        protected bool _syncState = false;
        protected string _systemName;
        #endregion

        #region Properties
        public string CurrentUser { get; set; }
        #endregion

        #region DbSets
        public DbSet<AuthActions> AuthActions { get; set; }
        public DbSet<Business> Business { get; set; }
        public DbSet<Category> Category { get; set; }
        public DbSet<Discount_Category> DiscountCats { get; set; }
        public DbSet<Discount_Item> DiscountItems { get; set; }
        public DbSet<Discount> Discounts { get; set; }
        public DbSet<Emp_AuthActions> EmpAuthActions { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<PaymentMethod> PayMethods { get; set; }
        public DbSet<PaymentMethod_Sale> PaySales { get; set; }
        public DbSet<Person> People { get; set; }
        public DbSet<Refund> Refunds { get; set; }
        public DbSet<Role> Role { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SavedTransaction> SavedTransactions { get; set; }
        public DbSet<Stock> Stocks { get; set; }
        public DbSet<Store> Stores { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<Till> Till { get; set; }
        public DbSet<Transaction> Trans { get; set; }
        public DbSet<Transaction_Discount> Transaction_Discounts { get; set; }
        #endregion

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public RepositoryContext()
        {
        }

        public RepositoryContext(DbContextOptions options) : base(options)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
        }

        /*
        public bool CurrentVersion()
        {
            var migrationsAssembly = this.GetService<IMigrationsAssembly>();
            var historyRepository = this.GetService<IHistoryRepository>();

            var all = migrationsAssembly.Migrations.Keys;
            var applied = historyRepository.GetAppliedMigrations().Select(r => r.MigrationId);
            var pending = all.Except(applied);
            return !pending.Any();
        }
        */

        public override int SaveChanges()
        {
            SaveMethods();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            SaveMethods();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveMethods();
            return base.SaveChangesAsync(cancellationToken);
        }

        // send current user to context
        // sending the current user
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            SaveMethods();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        public void SetSyncState(bool syncState) => _syncState = syncState;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //Filters
            /*if(!string.IsNullOrEmpty(_storeId))
                modelBuilder.Entity<StockModel>().HasQueryFilter(s => s.StoreId == _storeId);
            */
            //Relationships

            #region Keys
            modelBuilder.Entity<Tax>()
                .HasKey(t => new { t.IdOne, t.IdTwo });

            modelBuilder.Entity<AuthActionAPIMapping>()
                .HasKey(a => new { a.IdOne, a.IdTwo });

            modelBuilder.Entity<Tax>()
                .Property(t => t.IdOne)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Category>()
                .HasKey(c => new { c.IdOne, c.IdTwo });

            modelBuilder.Entity<Item>()
                .HasKey(i => new { i.IdOne, i.IdTwo });

            modelBuilder.Entity<Stock>()
                .HasKey(s => new { s.IdOne, s.IdTwo, s.IdThree });

            modelBuilder.Entity<Transaction_Discount>()
                .HasKey(tD => new { tD.TransactionId, tD.DiscountId, tD.SaleId });

            modelBuilder.Entity<Note>()
                .HasKey(ns => new { ns.IdOne, ns.IdTwo });

            modelBuilder.Entity<Employee>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Employee>()
                .HasIndex(e => new { e.Id, e.BusinessId })
                .IsUnique();

            modelBuilder.Entity<Emp_AuthActions>()
                .HasKey(k => new { k.AuthAId, k.EmpId });

            modelBuilder.Entity<Transaction>()
                .HasKey(k => new { k.IdOne, k.IdTwo });

            modelBuilder.Entity<PaymentMethod_Sale>()
                .HasKey(k => new { k.PayId, k.SaleId });
            #endregion

            #region Relationships
            modelBuilder.Entity<AuthActionAPIMapping>()
                .HasOne(am => am.Role)
                .WithMany(r => r.AuthActionAPIMappings)
                .HasForeignKey(am => am.IdOne);

            modelBuilder.Entity<AuthActionAPIMapping>()
                .HasOne(am => am.AuthAction)
                .WithMany(a => a.AuthActionAPIMappings)
                .HasForeignKey(am => am.IdTwo);

            modelBuilder.Entity<Business>()
                .HasMany(b => b.Discounts)
                .WithOne(d => d.Business)
                .HasForeignKey(d => d.BusinessId);

            modelBuilder.Entity<Business>()
                .HasMany(b => b.Stores)
                .WithOne(s => s.Business)
                .HasForeignKey(s => s.BusinessId);

            modelBuilder.Entity<Business>()
                .HasMany(b => b.Items)
                .WithOne(i => i.Business)
                .HasForeignKey(i => i.IdTwo);

            modelBuilder.Entity<Business>()
                .HasMany(b => b.Employees)
                .WithOne(e => e.Business)
                .HasForeignKey(e => e.BusinessId);

            modelBuilder.Entity<Business>()
                .HasMany(b => b.Categories)
                .WithOne(c => c.Business)
                .HasForeignKey(c => c.IdTwo);

            modelBuilder.Entity<Category>()
                .HasMany(c => c.Discount_Categories)
                .WithOne(dC => dC.Cat)
                .HasForeignKey(dC => new { dC.CatIdOne, dC.CatIdTwo });

            modelBuilder.Entity<CheckoutItemChange>()
                .HasOne(cIC => cIC.Item)
                .WithMany(i => i.CheckoutItemChanges)
                .HasForeignKey(cIC => new { cIC.ItemIdOne, cIC.ItemIdTwo });

            modelBuilder.Entity<CheckoutItemChange>()
                .HasOne(cIC => cIC.Transaction)
                .WithOne(t => t.CheckoutItemChange)
                .HasForeignKey<Transaction>(t => t.CheckoutItemChangeId)
                .IsRequired(false);

            modelBuilder.Entity<CheckoutItemChange>()
                .HasOne(cIC => cIC.Refund)
                .WithOne(r => r.CheckoutItemChange)
                .HasForeignKey<Refund>(r => r.CheckoutItemChangeId)
                .IsRequired(false);

            modelBuilder.Entity<Discount>()
                .HasMany(d => d.DisCategoryList)
                .WithOne(dc => dc.Discount);

            modelBuilder.Entity<Discount>()
                .HasMany(d => d.DisItemList)
                .WithOne(di => di.Discount);

            modelBuilder.Entity<Discount_Item>()
                .HasOne(di => di.Item)
                .WithMany(i => i.DisItems)
                .HasForeignKey(dI => new { dI.ItemIdOne, dI.ItemIdTwo });

            /*modelBuilder.Entity<Discount_Category>()
                .HasOne(dc => dc.Cat)
                .WithMany(c => c.DisCats)
                .HasForeignKey(c => new { c.CatIdOne, c.businessId });*/

            modelBuilder.Entity<Employee>()
               .HasOne(e => e.ManagedBy);

            modelBuilder.Entity<Employee>()
                .HasMany(e => e.Sale)
                .WithOne(sm => sm.Employee)
                .HasForeignKey(k => k.EmployeeId);

            modelBuilder.Entity<Employee>()
                .HasOne(e => e.Business)
                .WithMany(b => b.Employees)
                .HasForeignKey(e => e.BusinessId);

            modelBuilder.Entity<Emp_AuthActions>()
                .HasOne(ea => ea.Emp)
                .WithMany(e => e.EmpAuths)
                .HasForeignKey(ea => ea.EmpId);

            modelBuilder.Entity<Emp_AuthActions>()
                .HasOne(ea => ea.AuthA)
                .WithMany(a => a.EmpAuths)
                .HasForeignKey(ea => ea.AuthAId);

            modelBuilder.Entity<Item>()
                .HasOne(i => i.Tax)
                .WithMany(t => t.Items)
                .HasForeignKey(i => new { i.TaxId, i.IdTwo });

            modelBuilder.Entity<Item>()
                .HasOne(i => i.Cat)
                .WithMany(c => c.Items)
                .HasForeignKey(i => new { i.CatId, i.IdTwo });

            modelBuilder.Entity<Item>()
                .HasOne(i => i.Stock)
                .WithOne(s => s.Item)
                .HasForeignKey<Stock>(s => new { s.IdOne, s.IdTwo });

            modelBuilder.Entity<Note>()
                .HasOne(ns => ns.Sale)
                .WithMany(s => s.Notes)
                .HasForeignKey(n => n.IdTwo);

            modelBuilder.Entity<PaymentMethod_Sale>()
                .HasOne(ps => ps.PayMethod)
                .WithMany(pm => pm.PaySales)
                .HasForeignKey(ps => ps.PayId);

            modelBuilder.Entity<PaymentMethod_Sale>()
                .HasOne(ps => ps.Sale)
                .WithMany(s => s.PaySales)
                .HasForeignKey(ps => ps.SaleId);

            modelBuilder.Entity<Refund>()
                .HasOne(r => r.Item)
                .WithMany(i => i.Refunds)
                .HasForeignKey(r => new { r.ItemIdOne, r.ItemIdTwo });

            modelBuilder.Entity<Refund>()
                .HasOne(r => r.SaleReturned)
                .WithMany(sm => sm.Refunded)
                .HasForeignKey(r => r.SaleIdReturned);

            modelBuilder.Entity<Refund>()
                .HasOne(r => r.Sale)
                .WithMany(sm => sm.Refunds)
                .HasForeignKey(r => r.SaleId);

            modelBuilder.Entity<Refund>()
                .HasOne(r => r.Authoriser)
                .WithMany(e => e.RefundsAuthorised)
                .HasForeignKey(r => r.AuthoriserId);

            modelBuilder.Entity<Role>()
                .HasOne(r => r.ParentRole)
                .WithMany()
                .HasForeignKey(r => r.ParentId)
                .IsRequired(false);

            modelBuilder.Entity<Role>()
                .HasMany(r => r.Employees)
                .WithOne(e => e.Role)
                .HasForeignKey(e => e.RoleId);

            modelBuilder.Entity<Sale>()
                .Property(b => b.DateOfSale)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Stock>()
                .HasOne(s => s.Store)
                .WithMany(st => st.Stocks)
                .HasForeignKey(s => s.IdThree);

            modelBuilder.Entity<Store>()
                .HasMany(s => s.Tills)
                .WithOne(t => t.Store)
                .HasForeignKey(t => t.StoreId);

            modelBuilder.Entity<Store>()
                .HasMany(s => s.Sales)
                .WithOne(sa => sa.Store)
                .HasForeignKey(sa => sa.StoreId);

            modelBuilder.Entity<Tax>()
                .HasOne(t => t.Business)
                .WithMany(b => b.Taxes)
                .HasForeignKey(t => t.IdTwo);

            modelBuilder.Entity<Till>()
                .HasMany(till => till.Transactions)
                .WithOne(trans => trans.Till)
                .HasForeignKey(trans => trans.TillId);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Item)
                .WithMany(i => i.Transactions)
                .HasForeignKey(t => new { t.ItemIdOne, t.ItemIdTwo });

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Sale)
                .WithMany(s => s.Transactions)
                .HasForeignKey(t => t.IdTwo);

            modelBuilder.Entity<Transaction_Discount>()
                .HasOne(tD => tD.Discount)
                .WithMany(d => d.Transaction_Discounts)
                .HasForeignKey(tD => tD.DiscountId);

            modelBuilder.Entity<Transaction_Discount>()
                .HasOne(tD => tD.Transaction)
                .WithMany(t => t.Transaction_Discounts)
                .HasForeignKey(tD => new { tD.TransactionId, tD.SaleId });
            #endregion

            /*modelBuilder.Entity<Stock>()
                .HasKey(k => new { k.ItemIdOne, k.ItemIdTwo, k.StoreId });*/


            // modelBuilder.Entity<Actor>()
            //.HasKey(nameof(Actor.FirstName), nameof(Actor.LastName));

            /*builder.Entity<OrderDetail>().HasKey(table => new {
                table.OrderID,
                table.ProductID
            });*/
            base.OnModelCreating(modelBuilder);
        }
        private void SaveMethods()
        {
            if (string.IsNullOrEmpty(CurrentUser))
                throw new ObjectIdMissingException("CurrentUser not defined!");

            if (!_syncState)
            {
                //Set Audit data if IAuditable Entity
                var auditableEntities = ChangeTracker
                    .Entries()
                    .Where(_ =>
                        _.Entity is IAuditable && (
                            _.State == EntityState.Added ||
                            _.State == EntityState.Modified)
                    );

                foreach (var entityEntry in auditableEntities)
                {
                    if (entityEntry.State == EntityState.Added)
                    {
                        ((IAuditable)entityEntry.Entity).CreatedAt = DateTime.UtcNow;
                        ((IAuditable)entityEntry.Entity).CreatedBy = CurrentUser ?? _systemName;
                    }
                    else
                    {
                        Entry((IAuditable)entityEntry.Entity).Property(p => p.CreatedAt).IsModified = false;
                        Entry((IAuditable)entityEntry.Entity).Property(p => p.CreatedBy).IsModified = false;
                    }

                    ((IAuditable)entityEntry.Entity).ModifiedAt = DateTime.UtcNow;
                    ((IAuditable)entityEntry.Entity).ModifiedBy = CurrentUser ?? _systemName;
                }
            }
            //Validate Entities
            var changedEntities = ChangeTracker
                .Entries()
                .Where(_ =>
                    _.State == EntityState.Added ||
                    _.State == EntityState.Modified
                );
            var errors = new List<ValidationResult>();
            foreach (var e in changedEntities)
            {
                var vc = new ValidationContext(e.Entity, null, null);
                Validator.TryValidateObject(e.Entity, vc, errors, validateAllProperties: true);
                if (errors.Any())
                    throw new DbUpdateException(errors.First().ErrorMessage ?? "Errored out, unknown error.");
            }
        }
    }
}
