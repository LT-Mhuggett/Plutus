using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Plutus.Models;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Plutus.Data
{
    public sealed class Context : DbContext
    {
        public DbSet<EmployeeModel> Employees { get; set; }
        public DbSet<StoreModel> Stores { get; set; }
        public DbSet<VatModel> Vats { get; set; }
        public DbSet<ItemModel> Items { get; set; }
        public DbSet<PaymentMethodModel> PayMethods { get; set; }
        public DbSet<PaymentMethod_SaleModel> PaySales { get; set; }
        public DbSet<RefundModel> Refunds { get; set; }
        public DbSet<SaleModel> Sales { get; set; }
        public DbSet<TransactionModel> Trans { get; set; }
        public DbSet<CategoryModel> Category { get; set; }
        public DbSet<StockModel> Stocks { get; set; }
        public DbSet<AuthActions> AuthActions { get; set; }
        public DbSet<Emp_AuthActions> EmpAuthActions { get; set; }
        public DbSet<NoteModel> Notes { get; set; }
        public DbSet<Notes_SaleModel> NotesSales { get; set; }
        public DbSet<DiscountModel> Discounts { get; set; }
        public DbSet<Discount_Item> DiscountItems { get; set; }
        public DbSet<Discount_Category> DiscountCats { get; set; }
        
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
            //Audit
            foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                .Where(e => typeof(IAuditable).IsAssignableFrom(e.ClrType)))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<DateTime>("Created");
                modelBuilder.Entity(entityType.ClrType)
                    .Property<DateTime>("Modified");
                modelBuilder.Entity(entityType.ClrType)
                    .Property<string>("CreatedBy");
                modelBuilder.Entity(entityType.ClrType)
                    .Property<string>("ModifiedBy");
            }

            //Relationships

            modelBuilder.Entity<Discount_Item>()
                .HasOne(di => di.Item)
                .WithMany(i => i.DisItems);

            modelBuilder.Entity<Discount_Category>()
                .HasOne(dc => dc.Cat)
                .WithMany(c => c.DisCats);

            modelBuilder.Entity<DiscountModel>()
                .HasMany(d => d.DisCategoryList)
                .WithOne(dc => dc.Discount);

            modelBuilder.Entity<DiscountModel>()
                .HasMany(d => d.DisItemList)
                .WithOne(di => di.Discount);

            modelBuilder.Entity<Notes_SaleModel>()
                .HasKey(ns => new { ns.NoteId, ns.SaleId });

            modelBuilder.Entity<Notes_SaleModel>()
                .HasOne(ns => ns.Sale)
                .WithMany(s => s.Notes)
                .HasForeignKey(ns => ns.SaleId);

            modelBuilder.Entity<Notes_SaleModel>()
                .HasOne(ns => ns.Note)
                .WithMany(n => n.NoteSales)
                .HasForeignKey(ns => ns.NoteId);

            modelBuilder.Entity<EmployeeModel>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<StockModel>()
                .HasKey(k => new { k.ItemId, k.StoreId });

            modelBuilder.Entity<ItemModel>()
                .HasOne(i => i.Stock)
                .WithOne(s => s.Item)
                .HasForeignKey<StockModel>(s => s.ItemId);

            modelBuilder.Entity<StockModel>()
                .HasOne(s => s.Store)
                .WithMany(st => st.Stocks)
                .HasForeignKey(s => s.StoreId);

            modelBuilder.Entity<Emp_AuthActions>()
                .HasKey(k => new { k.AuthAId, k.EmpId });

            modelBuilder.Entity<Emp_AuthActions>()
                .HasOne(ea => ea.Emp)
                .WithMany(e => e.EmpAuths)
                .HasForeignKey(ea => ea.EmpId);

            modelBuilder.Entity<Emp_AuthActions>()
                .HasOne(ea => ea.Auth)
                .WithMany(a => a.EmpAuths)
                .HasForeignKey(ea => ea.AuthAId);

            modelBuilder.Entity<TransactionModel>()
                .HasKey(k => new { k.SaleId, k.ItemId });

            modelBuilder.Entity<TransactionModel>()
                .HasOne(t => t.Item)
                .WithMany(i => i.Transactions)
                .HasForeignKey(t => t.ItemId);

            modelBuilder.Entity<TransactionModel>()
                .HasOne(t => t.Sale)
                .WithMany(s => s.Transactions)
                .HasForeignKey(t => t.SaleId);

            modelBuilder.Entity<EmployeeModel>()
                .HasMany(e => e.Sale)
                .WithOne(sm => sm.Employee)
                .HasForeignKey(k => k.EmployeeId);

            modelBuilder.Entity<SaleModel>()
                .Property(b => b.DateOfSale)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<RefundModel>()
                .HasOne(r => r.Item)
                .WithMany(i => i.Refunds)
                .HasForeignKey(r => r.ItemId);

            modelBuilder.Entity<RefundModel>()
                .HasOne(r => r.SaleReturned)
                .WithMany(sm => sm.Refunded)
                .HasForeignKey(r => r.SaleIdReturned);

            modelBuilder.Entity<RefundModel>()
                .HasOne(r => r.Sale)
                .WithMany(sm => sm.Refunds)
                .HasForeignKey(r => r.SaleId);

            modelBuilder.Entity<PaymentMethod_SaleModel>()
                .HasKey(k => new { k.PayId, k.SaleId });

            modelBuilder.Entity<PaymentMethod_SaleModel>()
                .HasOne(ps => ps.PayMethod)
                .WithMany(pm => pm.PaySales)
                .HasForeignKey(ps => ps.PayId);

            modelBuilder.Entity<PaymentMethod_SaleModel>()
                .HasOne(ps => ps.Sale)
                .WithMany(s => s.PaySales)
                .HasForeignKey(ps => ps.SaleId);

            base.OnModelCreating(modelBuilder);
        }

        public override int SaveChanges()
        {
            ApplyAuditData();
            return base.SaveChanges();
        }

        private void ApplyAuditData()
        {
            var modifiedEntries = ChangeTracker.Entries()
                .Where(e => e.State.Equals(EntityState.Added)
                    || e.State.Equals(EntityState.Modified));
            foreach(EntityEntry entry in modifiedEntries)
            {
                var entityType = entry.Context.Model.FindEntityType(entry.Entity.GetType());

                var modifiedProperty = entityType.FindProperty("Modified");
                var modifiedByProperty = entityType.FindProperty("ModifiedBy");
                var createdProperty = entityType.FindProperty("Created");
                var createdByProperty = entityType.FindProperty("CreatedBy");

                if (entry.State != EntityState.Modified && entry.State != EntityState.Added) continue;
                switch (entry.State)
                {
                    case EntityState.Modified when (modifiedProperty != null || modifiedByProperty != null):
                        entry.Property("Modified").CurrentValue = DateTime.Now;
                        entry.Property("ModifiedBy").CurrentValue = App.LastAuthUser == null ? "System" : App.LastAuthUser.FName + " " + App.LastAuthUser.LName;
                        break;
                    case EntityState.Added when (createdProperty != null || createdByProperty != null):
                        entry.Property("Created").CurrentValue = DateTime.Now;
                        entry.Property("CreatedBy").CurrentValue = App.LastAuthUser.FName == null ? "System" : App.LastAuthUser.FName + " " + App.LastAuthUser.LName;
                        break;
                }
            }
        }
    }
}
