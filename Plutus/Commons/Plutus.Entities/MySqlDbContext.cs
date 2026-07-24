using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Entities
{
    public class MySqlDbContext : RepositoryContext
    {
        #region Fields
        private readonly string _connString = @"Server=127.0.0.1;User=root;Password=root;Database=plutus;Port=3306;Persist Security Info=false;Connect Timeout=300";
        // Tenancy (T1.1, evolve-in-place). Never null: defaults to the founding Kapow tenant so
        // every non-DI call site (tests, SeedMigrator, design-time factory, legacy code) keeps
        // working as single-tenant. The request-scoped JWT-backed context is injected via DI.
        private readonly ITenantContext _tenantContext;
        #endregion

        #region DbSets for MySql DB only (server-side; NOT on the MAUI Sqlite context)
        // Platform tenancy (T1.1, evolve-in-place). Kept on MySqlDbContext so the shared
        // model + the MAUI SqliteDbContext are unaffected.
        public DbSet<Tenant> Tenants { get; set; }
        // Enrolment (T1.2). Global/unscoped: the anon enrol + device-token endpoints look these
        // up (by hash / by Id) before a tenant is known. TenantId is carried as data.
        public DbSet<EnrolmentCode> EnrolmentCodes { get; set; }
        public DbSet<Device> Devices { get; set; }
        // Web login credentials (existing table). Global/unscoped — login is by email.
        public DbSet<WebCredential> WebCredentials { get; set; }
        // Sales schema v2 (T1.3). New tables alongside the legacy Sale/Transaction tables.
        public DbSet<SaleV2> SalesV2 { get; set; }
        public DbSet<SaleLine> SaleLines { get; set; }
        public DbSet<SaleTender> SaleTenders { get; set; }
        public DbSet<SaleAdjustment> SaleAdjustments { get; set; }
        public DbSet<SaleQuarantine> SaleQuarantine { get; set; }
        public DbSet<OutboxEvent> OutboxEvents { get; set; }
        public DbSet<ConsumerOffset> ConsumerOffsets { get; set; }
        // Outbox dispatch bookkeeping (T1.5). Global/infra.
        public DbSet<ProcessedEvent> ProcessedEvents { get; set; }
        public DbSet<ConsumerDeadLetter> ConsumerDeadLetters { get; set; }
        // RBAC (WP3.1). "Rbac" prefix because the legacy Role table survives evolve-in-place.
        public DbSet<RbacRole> RbacRoles { get; set; }
        public DbSet<RbacRoleGrant> RbacRoleGrants { get; set; }
        public DbSet<RbacRoleAssignment> RbacRoleAssignments { get; set; }
        // Admin surface (WP3.2): audit trail + portal-only store fields.
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<StoreDetails> StoreDetails { get; set; }
        #endregion

        /// <summary>The tenant scoping every query and write is bound to. Referenced by the
        /// global query filters (EF parameterises it per executing context) and by the
        /// SaveChanges stamp/guard. <see cref="Guid.Empty"/> means unscoped (platform admin).</summary>
        public Guid CurrentTenantId => _tenantContext.TenantId;

        public MySqlDbContext() : base()
        {
            _systemName = "Plutus.DBService";
            _tenantContext = FixedTenantContext.KapowDefault;
        }

        public MySqlDbContext(DbContextOptions options) : base(options)
        {
            _systemName = "Plutus.DBService";
            _tenantContext = FixedTenantContext.KapowDefault;
        }

        // DI-preferred ctor: EF picks this when an ITenantContext is registered (scoped, JWT-backed).
        public MySqlDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
        {
            _systemName = "Plutus.DBService";
            _tenantContext = tenantContext ?? FixedTenantContext.KapowDefault;
        }

        public MySqlDbContext(string connString) : base()
        {
            _systemName = "Plutus.DBService";
            _connString = connString;
            _tenantContext = FixedTenantContext.KapowDefault;
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

        // Tenant-owned entities (architecture §3, confirmed 2026-07-24). Role, PaymentMethod,
        // Person, AuthActions* and the pure mapping tables are GLOBAL/shared and stay unscoped.
        private static readonly Type[] TenantOwned =
        {
            typeof(Business), typeof(Store), typeof(Till),
            typeof(Item), typeof(Category), typeof(Tax),
            typeof(Discount), typeof(Discount_Category), typeof(Discount_Item), typeof(Transaction_Discount),
            typeof(Sale), typeof(Transaction), typeof(PaymentMethod_Sale), typeof(Refund),
            typeof(Note), typeof(SavedTransaction), typeof(Stock),
            // Person is the TPT root of Employee (People table holds only the employee base
            // rows). EF only allows a query filter on the hierarchy root, so scope Person —
            // it cascades to Employee. Person itself carries no shared/global rows.
            typeof(Person), typeof(CheckoutItemChange),
            // Sales v2 (T1.3) — real TenantId columns; the loop reuses them (no shadow added).
            typeof(SaleV2), typeof(SaleLine), typeof(SaleTender), typeof(SaleAdjustment),
            // RBAC (WP3.1) — real TenantId columns, per-tenant roles/assignments.
            typeof(RbacRole), typeof(RbacRoleGrant), typeof(RbacRoleAssignment),
            // Admin surface (WP3.2).
            typeof(AuditLog), typeof(StoreDetails),
        };

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // T1.1 tenancy (server-side only).
            modelBuilder.Entity<Tenant>(e =>
            {
                e.ToTable("Tenants");
                e.HasKey(t => t.Id);
                e.Property(t => t.Name).HasMaxLength(200);
                e.Property(t => t.Plan).HasMaxLength(50);
                e.Property(t => t.ConnectionRef).HasMaxLength(100);
            });

            // T1.2 enrolment (server-side, global/unscoped — see entity docs).
            modelBuilder.Entity<EnrolmentCode>(e =>
            {
                e.ToTable("EnrolmentCodes");
                e.HasKey(x => x.Id);
                e.Property(x => x.CodeHash).HasMaxLength(32).IsRequired();
                e.HasIndex(x => x.CodeHash).IsUnique();
                e.HasIndex(x => x.TillId);
            });
            modelBuilder.Entity<Device>(e =>
            {
                e.ToTable("Devices");
                e.HasKey(x => x.Id);
                e.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
                e.Property(x => x.SecretSalt).HasMaxLength(32).IsRequired();
                e.HasIndex(x => x.TillId);
                e.HasIndex(x => x.TenantId);
            });
            modelBuilder.Entity<WebCredential>(e =>
            {
                e.ToTable("WebCredentials");
                e.HasKey(x => x.Email);
                e.Property(x => x.Email).HasMaxLength(255);
            });

            // T1.3 sales v2 (server-side). Ids are client-minted UUIDv7 (not store-generated).
            modelBuilder.Entity<SaleV2>(e =>
            {
                e.ToTable("SalesV2");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.LegacyRef).HasMaxLength(32);
                e.Property(x => x.Note).HasMaxLength(1000);
                e.HasIndex(x => new { x.TenantId, x.DeviceId, x.DeviceSeq }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.BusinessDay });
                e.HasIndex(x => new { x.TenantId, x.TillId, x.BusinessDay });
                e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SaleId);
                e.HasMany(x => x.Tenders).WithOne().HasForeignKey(t => t.SaleId);
            });
            modelBuilder.Entity<SaleLine>(e =>
            {
                e.ToTable("SaleLines");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasIndex(x => new { x.TenantId, x.SaleId });
                e.HasIndex(x => new { x.TenantId, x.ItemId });
            });
            modelBuilder.Entity<SaleTender>(e =>
            {
                e.ToTable("SaleTenders");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasIndex(x => new { x.TenantId, x.SaleId });
            });
            modelBuilder.Entity<SaleAdjustment>(e =>
            {
                e.ToTable("SaleAdjustments");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.OriginalSaleId });
            });
            modelBuilder.Entity<SaleQuarantine>(e =>
            {
                e.ToTable("SaleQuarantine");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.SaleId }).IsUnique();
            });
            modelBuilder.Entity<OutboxEvent>(e =>
            {
                e.ToTable("OutboxEvents");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.EventType).HasMaxLength(100);
                e.HasIndex(x => x.EventId).IsUnique();
            });
            modelBuilder.Entity<ConsumerOffset>(e =>
            {
                e.ToTable("ConsumerOffsets");
                e.HasKey(x => x.ConsumerName);
                e.Property(x => x.ConsumerName).HasMaxLength(100);
            });
            modelBuilder.Entity<ProcessedEvent>(e =>
            {
                e.ToTable("ProcessedEvents");
                e.HasKey(x => new { x.ConsumerName, x.EventId });
                e.Property(x => x.ConsumerName).HasMaxLength(100);
            });
            modelBuilder.Entity<ConsumerDeadLetter>(e =>
            {
                e.ToTable("ConsumerDeadLetters");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ConsumerName).HasMaxLength(100);
                e.Property(x => x.EventType).HasMaxLength(100);
                e.HasIndex(x => x.ConsumerName);
            });

            // WP3.1 RBAC (server-side, tenant-owned). Ids are client-minted UUIDv7.
            modelBuilder.Entity<RbacRole>(e =>
            {
                e.ToTable("RbacRoles");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
                e.HasMany(x => x.Grants).WithOne().HasForeignKey(g => g.RoleId);
            });
            modelBuilder.Entity<RbacRoleGrant>(e =>
            {
                e.ToTable("RbacRoleGrants");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.PermissionCode).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.RoleId, x.PermissionCode }).IsUnique();
            });
            modelBuilder.Entity<RbacRoleAssignment>(e =>
            {
                e.ToTable("RbacRoleAssignments");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ScopeId).HasMaxLength(64).IsRequired();
                e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
                e.HasIndex(x => new { x.TenantId, x.UserId });
                e.HasIndex(x => new { x.TenantId, x.ScopeType, x.ScopeId });
            });

            // WP3.2 admin surface.
            modelBuilder.Entity<AuditLog>(e =>
            {
                e.ToTable("AuditLogs");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.Action).HasMaxLength(100).IsRequired();
                e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
                e.Property(x => x.EntityId).HasMaxLength(64).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.AtUtc });
                e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            });
            modelBuilder.Entity<StoreDetails>(e =>
            {
                e.ToTable("StoreDetails");
                e.HasKey(x => x.StoreId);
                e.Property(x => x.StoreId).ValueGeneratedNever();
            });

            // Test/dev harness only (WP2.1): on MySQL, Trans.IdOne is AUTO_INCREMENT within a
            // composite key — SQLite cannot generate values for that shape, so when this
            // context runs on SQLite (unit/integration tests) an in-memory generator supplies
            // them. Uniqueness only matters within a sale (PK is IdOne+IdTwo=SaleId); a
            // process-wide counter is more than enough. No effect on the MySQL model/migrations.
            if (Database.IsSqlite())
                modelBuilder.Entity<Transaction>().Property(t => t.IdOne)
                    .HasValueGenerator<SqliteTransIdGenerator>();

            // Shadow TenantId + index on every tenant-owned entity (by convention, never by
            // hand per entity). Shadow => the shared POCOs and the MAUI SqliteDbContext stay
            // untouched. NOT NULL: backfilled to Kapow in the migration.
            foreach (var clr in TenantOwned)
            {
                modelBuilder.Entity(clr).Property<Guid>("TenantId");
                modelBuilder.Entity(clr).HasIndex("TenantId");
            }
            ApplyTenantQueryFilters(modelBuilder);
        }

        // Applies the global query filter to each tenant-owned entity via a strongly-typed
        // generic helper so the lambda closes over `this` (CurrentTenantId) — EF then
        // re-parameterises it per executing context (the documented dynamic-filter pattern).
        private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
        {
            var setter = typeof(MySqlDbContext)
                .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var clr in TenantOwned)
                setter.MakeGenericMethod(clr).Invoke(this, new object[] { modelBuilder });
        }

        // A CLR type is tenant-owned if it (or a base type — e.g. Employee : Person) is in the
        // scoped set. The base-type walk keeps TPT-derived entities auto-stamped via their root.
        private static bool IsTenantOwned(Type clr)
        {
            for (var t = clr; t != null && t != typeof(object); t = t.BaseType)
                if (Array.IndexOf(TenantOwned, t) >= 0) return true;
            return false;
        }

        private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class
        {
            // Guid.Empty context => unscoped (platform admin) sees all rows.
            modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
                CurrentTenantId == Guid.Empty || EF.Property<Guid>(e, "TenantId") == CurrentTenantId);
        }

        #region Tenant stamping / defence-in-depth guard (architecture §3)
        public override int SaveChanges()
        {
            StampAndGuardTenant();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampAndGuardTenant();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            StampAndGuardTenant();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampAndGuardTenant();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>Stamps TenantId from context on added tenant-owned rows, and throws if a
        /// row's TenantId disagrees with the context (cross-tenant write). Unscoped contexts
        /// (Guid.Empty / platform admin) neither stamp nor block.</summary>
        private void StampAndGuardTenant()
        {
            var tid = _tenantContext.TenantId;
            if (tid == Guid.Empty) return;

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified) continue;
                // Only the query-filtered, tenant-owned entities are auto-stamped/guarded.
                // Global tables that carry a TenantId as plain data (EnrolmentCode, Device) are
                // set explicitly by the caller and must NOT be forced to the ambient tenant.
                if (!IsTenantOwned(entry.Metadata.ClrType)) continue;

                var prop = entry.Property("TenantId");
                var current = prop.CurrentValue is Guid g ? g : Guid.Empty;

                if (entry.State == EntityState.Added)
                {
                    if (current == Guid.Empty) prop.CurrentValue = tid;
                    else if (current != tid)
                        throw new InvalidOperationException(
                            $"Cross-tenant write blocked: {entry.Metadata.ClrType.Name}.TenantId {current} != context {tid}.");
                }
                else if (current != Guid.Empty && current != tid)
                {
                    throw new InvalidOperationException(
                        $"Cross-tenant modify blocked: {entry.Metadata.ClrType.Name}.TenantId {current} != context {tid}.");
                }
            }
        }
        #endregion
    }

    /// <summary>See the OnModelCreating note: supplies Trans.IdOne on SQLite test/dev hosts,
    /// where the composite-key AUTO_INCREMENT shape has no provider-side generator.</summary>
    internal sealed class SqliteTransIdGenerator : Microsoft.EntityFrameworkCore.ValueGeneration.ValueGenerator<int>
    {
        private static int _next = 1_000_000; // clear of any hand-seeded test ids
        public override bool GeneratesTemporaryValues => false;
        public override int Next(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
            => Interlocked.Increment(ref _next);
    }
}
