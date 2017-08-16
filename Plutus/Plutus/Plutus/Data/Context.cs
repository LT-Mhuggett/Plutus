using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plutus.Models;

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
        public DbSet<Refund_SaleModel> RefundSales { get; set; }
        public DbSet<SaleModel> Sales { get; set; }
        public DbSet<TransactionModel> Trans { get; set; }
        public DbSet<CategoryModel> Category { get; set; }
        public DbSet<StockModel> Stocks { get; set; }

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

            modelBuilder.Entity<SaleModel>()
                .HasOne(sm => sm.Refund)
                .WithOne(r => r.Sale)
                .HasForeignKey<RefundModel>(k => k.SaleId);

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

            modelBuilder.Entity<Refund_SaleModel>()
                .HasKey(k => new { k.RId, k.SaleId });

            modelBuilder.Entity<Refund_SaleModel>()
                .HasOne(rs => rs.Refund)
                .WithMany(r => r.RefundSales)
                .HasForeignKey(rs => rs.RId);

            modelBuilder.Entity<Refund_SaleModel>()
                .HasOne(rs => rs.Sale)
                .WithMany(s => s.RefundSales)
                .HasForeignKey(rs => rs.SaleId);
        }
    }
}
