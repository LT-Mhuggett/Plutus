using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities.Models;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Plutus.Entities.Models.Interface;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plutus.Entities
{
    public class RepositoryContext : DbContext
    {
        #region Fields
        protected string _systemName;
        protected bool _syncState = false;
        #endregion

        #region Properties
        public string CurrentUser { get; set; }
        #endregion

        #region DbSets
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Store> Stores { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<PaymentMethod> PayMethods { get; set; }
        public DbSet<PaymentMethod_Sale> PaySales { get; set; }
        public DbSet<Refund> Refunds { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<Transaction> Trans { get; set; }
        public DbSet<Category> Category { get; set; }
        public DbSet<Stock> Stocks { get; set; }
        public DbSet<AuthActions> AuthActions { get; set; }
        public DbSet<Emp_AuthActions> EmpAuthActions { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<Notes_Sale> NotesSales { get; set; }
        public DbSet<Discount> Discounts { get; set; }
        public DbSet<Discount_Item> DiscountItems { get; set; }
        public DbSet<Discount_Category> DiscountCats { get; set; }
        public DbSet<SavedTransaction> SavedTransactions { get; set; }
        public DbSet<Transaction_Discount> Transaction_Discounts { get; set; }
        public DbSet<Bussiness> Bussiness { get; set; }
        #endregion

        public RepositoryContext()
        {
        }

        public RepositoryContext(DbContextOptions options) : base(options)
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //Filters
            /*if(!string.IsNullOrEmpty(_storeId))
                modelBuilder.Entity<StockModel>().HasQueryFilter(s => s.StoreId == _storeId);
            */
            //Relationships

            modelBuilder.Entity<Tax>()
                .HasKey(t => new { t.IdOne, t.IdTwo });

            modelBuilder.Entity<AuthActionAPIMapping>()
                .HasKey(t => new { t.IdOne, t.IdTwo });

            modelBuilder.Entity<AuthActionAPIMapping>()
                .HasOne(am => am.Role)
                .WithMany(r => r.AuthActionAPIMappings)
                .HasForeignKey(am => am.IdOne);

            modelBuilder.Entity<AuthActionAPIMapping>()
                .HasOne(am => am.AuthAction)
                .WithMany(a => a.AuthActionAPIMappings)
                .HasForeignKey(am => am.IdTwo);

            modelBuilder.Entity<Employee>()
               .HasOne(e => e.ParentUser);

            modelBuilder.Entity<Role>()
               .HasOne(r => r.ParentRole);

            modelBuilder.Entity<Tax>()
                .Property(t => t.IdOne)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Item>()
                .HasOne(i => i.Tax)
                .WithMany(t => t.Items)
                .HasForeignKey(i => new { i.TaxId, i.IdTwo });

            modelBuilder.Entity<Tax>() 
                .HasOne(t => t.Bussiness)
                .WithMany(b => b.Taxes)
                .HasForeignKey(t => t.IdTwo);

            modelBuilder.Entity<Bussiness>()
                .HasMany(b => b.Discounts)
                .WithOne(d => d.Bussiness)
                .HasForeignKey(d => d.BussinessId);

            modelBuilder.Entity<Role>()
                .HasMany(r => r.Employees)
                .WithOne(e => e.Role)
                .HasForeignKey(e => e.RoleId);

            modelBuilder.Entity<Role>()
                .HasMany(r => r.Employees)
                .WithOne(e => e.Role)
                .HasForeignKey(e => e.RoleId);

            modelBuilder.Entity<Bussiness>()
                .HasMany(b => b.Stores)
                .WithOne(s => s.Bussiness)
                .HasForeignKey(s => s.BussinessId);
            
            modelBuilder.Entity<Bussiness>()
                .HasMany(b => b.Items)
                .WithOne(i => i.Bussiness)
                .HasForeignKey(i => i.IdTwo);
             
            modelBuilder.Entity<Bussiness>()
                .HasMany(b => b.Employees)
                .WithOne(e => e.Bussiness)
                .HasForeignKey(e => e.BussinessId);

            modelBuilder.Entity<Item>()
                .HasKey(i => new { i.IdOne, i.IdTwo });

            modelBuilder.Entity<Stock>()
                .HasKey(s => new { s.IdOne, s.IdTwo, s.IdThree });

            modelBuilder.Entity<Store>()
                .HasMany(s => s.Sales)
                .WithOne(sa => sa.Store)
                .HasForeignKey(sa => sa.StoreId);
                
            modelBuilder.Entity<Discount_Item>()
                .HasOne(di => di.Item)
                .WithMany(i => i.DisItems);

            modelBuilder.Entity<Discount_Category>()
                .HasOne(dc => dc.Cat)
                .WithMany(c => c.DisCats);

            modelBuilder.Entity<Discount>()
                .HasMany(d => d.DisCategoryList)
                .WithOne(dc => dc.Discount);

            modelBuilder.Entity<Discount>()
                .HasMany(d => d.DisItemList)
                .WithOne(di => di.Discount);

            modelBuilder.Entity<Transaction_Discount>()
                .HasKey(tD => new { tD.TransactionId, tD.DiscountId });

            modelBuilder.Entity<Transaction_Discount>()
                .HasOne(tD => tD.Discount)
                .WithMany(d => d.Transaction_Discounts)
                .HasForeignKey(tD => tD.DiscountId);

            modelBuilder.Entity<Transaction_Discount>()
                .HasOne(tD => tD.Transaction)
                .WithMany(t => t.Transaction_Discounts)
                .HasForeignKey(tD => tD.TransactionId);

            modelBuilder.Entity<Notes_Sale>()
                .HasKey(ns => new { ns.NoteId, ns.SaleId });

            modelBuilder.Entity<Notes_Sale>()
                .HasOne(ns => ns.Sale)
                .WithMany(s => s.Notes)
                .HasForeignKey(ns => ns.SaleId);

            modelBuilder.Entity<Notes_Sale>()
                .HasOne(ns => ns.Note)
                .WithMany(n => n.NoteSales)
                .HasForeignKey(ns => ns.NoteId);

            modelBuilder.Entity<Employee>()
                .HasIndex(u => u.Email)
                .IsUnique();

            /*modelBuilder.Entity<Stock>()
                .HasKey(k => new { k.ItemIdOne, k.ItemIdTwo, k.StoreId });*/

            modelBuilder.Entity<Item>()
                .HasOne(i => i.Stock)
                .WithOne(s => s.Item)
                .HasForeignKey<Stock>(s => new { s.IdOne, s.IdTwo });

            modelBuilder.Entity<Stock>()
                .HasOne(s => s.Store)
                .WithMany(st => st.Stocks)
                .HasForeignKey(s => s.IdThree);

            modelBuilder.Entity<Emp_AuthActions>()
                .HasKey(k => new { k.AuthAId, k.EmpId });

            modelBuilder.Entity<Emp_AuthActions>()
                .HasOne(ea => ea.Emp)
                .WithMany(e => e.EmpAuths)
                .HasForeignKey(ea => ea.EmpId);

            modelBuilder.Entity<Emp_AuthActions>()
                .HasOne(ea => ea.AuthA)
                .WithMany(a => a.EmpAuths)
                .HasForeignKey(ea => ea.AuthAId);

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Item)
                .WithMany(i => i.Transactions)
                .HasForeignKey(t => new { t.ItemIdOne, t.ItemIdTwo });

            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.Sale)
                .WithMany(s => s.Transactions)
                .HasForeignKey(t => t.SaleId);

            modelBuilder.Entity<Employee>()
                .HasMany(e => e.Sale)
                .WithOne(sm => sm.Employee)
                .HasForeignKey(k => k.EmployeeId);

            modelBuilder.Entity<Sale>()
                .Property(b => b.DateOfSale)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<Refund>()
                .HasOne(r => r.Item)
                .WithMany(i => i.Refunds)
                .HasForeignKey(r => new { r.ItemIdOne, r.ItemIdTwo } );

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

            modelBuilder.Entity<PaymentMethod_Sale>()
                .HasKey(k => new { k.PayId, k.SaleId });

            modelBuilder.Entity<PaymentMethod_Sale>()
                .HasOne(ps => ps.PayMethod)
                .WithMany(pm => pm.PaySales)
                .HasForeignKey(ps => ps.PayId);

            modelBuilder.Entity<PaymentMethod_Sale>()
                .HasOne(ps => ps.Sale)
                .WithMany(s => s.PaySales)
                .HasForeignKey(ps => ps.SaleId);

            modelBuilder.Entity<CheckoutItemChange>()
                .HasOne(cIC => cIC.Item)
                .WithMany(i => i.CheckoutItemChanges)
                .HasForeignKey(cIC =>  new { cIC.ItemIdOne, cIC.ItemIdTwo });

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

            // modelBuilder.Entity<Actor>()
            //.HasKey(nameof(Actor.FirstName), nameof(Actor.LastName));

            /*builder.Entity<OrderDetail>().HasKey(table => new {
                table.OrderID,
                table.ProductID
            });*/
            base.OnModelCreating(modelBuilder);
        }

        public void SetSyncState(bool syncState) => _syncState = syncState;

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

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            SaveMethods();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void SaveMethods()
        {
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
                    throw new DbUpdateException(errors.First().ErrorMessage);
            }
        }
    }
}
