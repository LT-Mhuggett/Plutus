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
        public DbSet<TillDetails> TillDetails { get; set; }
        public DbSet<VatRatePoint> VatRatePoints { get; set; }
        // WP2c-exempt: legacy tax row → VAT band, so zero-rated and exempt (both 0%) stay apart.
        public DbSet<VatBandTaxMap> VatBandTaxMaps { get; set; }
        public DbSet<TillTheme> TillThemes { get; set; }
        public DbSet<TillGroup> TillGroups { get; set; }
        public DbSet<TillGroupMember> TillGroupMembers { get; set; }
        public DbSet<TillThemeAssignment> TillThemeAssignments { get; set; }
        public DbSet<DeletionSchedule> DeletionSchedules { get; set; }

        // Phase 6 WooCommerce connector (config + SKU review queue + product cache + notifications).
        public DbSet<WebStoreDetails> WebStores { get; set; }
        public DbSet<WebstoreSkuMap> WebstoreSkuMaps { get; set; }
        public DbSet<WebstoreProduct> WebstoreProducts { get; set; }
        public DbSet<WebstoreNotification> WebstoreNotifications { get; set; }
        public DbSet<WebstoreOutboundLog> WebstoreOutboundLogs { get; set; }
        // Reporting projections (WP3.3): rebuildable rollups the dashboards read.
        public DbSet<SalesRollup> SalesRollups { get; set; }
        public DbSet<VatRollup> VatRollups { get; set; }
        // WP13.1 operator usage metering: (TenantId, BusinessDay, Metric) → Value.
        public DbSet<TenantUsageRollup> TenantUsageRollups { get; set; }
        // WP13.2 per-tenant request health: (TenantId, MinuteUtc, RouteGroup) → counts + latency.
        public DbSet<TenantRequestStats> TenantRequestStats { get; set; }
        // WP13.3 job heartbeats + operator alerts (GLOBAL — TenantId is plain data).
        public DbSet<JobRun> JobRuns { get; set; }
        public DbSet<OperatorAlert> OperatorAlerts { get; set; }
        // WP14.2 feature flags + entitlement overrides (GLOBAL — platform-admin managed).
        public DbSet<TenantEntitlementOverride> TenantEntitlementOverrides { get; set; }
        public DbSet<PlatformFlag> PlatformFlags { get; set; }
        // WP15.1 in-app announcements (GLOBAL — TenantIds is data).
        public DbSet<PlatformAnnouncement> PlatformAnnouncements { get; set; }
        public DbSet<TenantSignal> TenantSignals { get; set; }
        public DbSet<TenantContract> TenantContracts { get; set; }
        public DbSet<ConnectorRun> ConnectorRuns { get; set; }
        public DbSet<TenantSendingIdentity> TenantSendingIdentities { get; set; }
        public DbSet<MessageEvent> MessageEvents { get; set; }
        public DbSet<NotificationSettings> NotificationSettings { get; set; }
        public DbSet<BillingSettings> BillingSettings { get; set; }

        /// <summary>Which till build the platform expects — GLOBAL single row. ⚠ Deliberately NOT
        /// in <c>TenantOwned</c>: a release decision belongs to the operator, not to a shop.</summary>
        public DbSet<TillReleaseSettings> TillReleaseSettings { get; set; }
        public DbSet<PaymentGatewaySettings> PaymentGatewaySettings { get; set; }
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<SupportTicket> SupportTickets { get; set; }
        public DbSet<SupportMessage> SupportMessages { get; set; }
        // Financial periods (WP3.4): close/lock + snapshot.
        public DbSet<FinancialPeriod> FinancialPeriods { get; set; }
        // Stock ledger (WP5.1): append-only movements + materialised levels.
        public DbSet<StockLocation> StockLocations { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<StockLevel> StockLevels { get; set; }
        // Transfers (WP5.2): paired movements with an in-transit state.
        public DbSet<StockTransfer> StockTransfers { get; set; }
        // Goods-in (WP5.3): suppliers + purchase orders feeding RECEIPT movements.
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<POLine> POLines { get; set; }
        // Pricing (WP5.4): policy + effective-dated price list + store overrides.
        public DbSet<ItemPricePolicy> ItemPricePolicies { get; set; }
        public DbSet<PriceListEntry> PriceListEntries { get; set; }
        public DbSet<PriceOverride> PriceOverrides { get; set; }
        // Cash sessions (WP7.2) + payment capture events (WP7.1).
        public DbSet<CashEvent> CashEvents { get; set; }
        public DbSet<PaymentEvent> PaymentEvents { get; set; }
        // Customers, store credit, loyalty (Phase 8).
        public DbSet<Customer> Customers { get; set; }
        public DbSet<CreditAccount> CreditAccounts { get; set; }
        public DbSet<CreditEntry> CreditEntries { get; set; }
        public DbSet<Membership> Memberships { get; set; }
        // FE1: pre-defined loyalty levels a membership is assigned (instead of free-text tiers).
        public DbSet<LoyaltyTier> LoyaltyTiers { get; set; }
        // FE2: per-tenant membership-number sequence (printed on loyalty cards).
        public DbSet<MemberNoCounter> MemberNoCounters { get; set; }
        // FE7: gift cards + their append-only balance ledger (a liability, like store credit).
        public DbSet<GiftCard> GiftCards { get; set; }
        public DbSet<GiftCardEntry> GiftCardEntries { get; set; }
        // FE7: the per-tenant VAT-treatment decision — its absence disables gift cards entirely.
        public DbSet<GiftCardSettings> GiftCardSettings { get; set; }
        // FE9: hashed, single-use password-reset / invite tokens.
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        // WP5.3 cross-channel identity (webstore ⇄ loyalty link by email).
        public DbSet<CustomerExternalRef> CustomerExternalRefs { get; set; }
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
            // Admin surface (WP3.2, WP11.1).
            typeof(AuditLog), typeof(StoreDetails), typeof(TillDetails),
            typeof(TillTheme), typeof(TillGroup), typeof(TillGroupMember), typeof(TillThemeAssignment),
            typeof(VatRatePoint), typeof(VatBandTaxMap),
            // Reporting projections (WP3.3).
            typeof(SalesRollup), typeof(VatRollup),
            // Operator usage metering (WP13.1) + request health (WP13.2) — per-tenant rows,
            // platform-admin reads cross-tenant.
            typeof(TenantUsageRollup), typeof(TenantRequestStats),
            // Financial periods (WP3.4).
            typeof(FinancialPeriod),
            // Stock ledger (WP5.1) + transfers (WP5.2) + goods-in (WP5.3).
            typeof(StockLocation), typeof(StockMovement), typeof(StockLevel), typeof(StockTransfer),
            typeof(Supplier), typeof(PurchaseOrder), typeof(POLine),
            // Pricing (WP5.4).
            typeof(ItemPricePolicy), typeof(PriceListEntry), typeof(PriceOverride),
            // Cash + payments (WP7).
            typeof(CashEvent), typeof(PaymentEvent),
            // Customers, credit, loyalty (Phase 8) + cross-channel identity (WP5.3).
            typeof(Customer), typeof(CreditAccount), typeof(CreditEntry), typeof(Membership),
            typeof(CustomerExternalRef),
            // FE7 gift cards (tenant-scoped: a code is only ever valid in the tenant that sold it).
            typeof(GiftCard), typeof(GiftCardEntry), typeof(GiftCardSettings),
            // WooCommerce connector config + SKU review queue + product cache + notifications (Phase 6).
            typeof(WebStoreDetails), typeof(WebstoreSkuMap), typeof(WebstoreProduct), typeof(WebstoreNotification),
            typeof(WebstoreOutboundLog),
            // 17.2 per-tenant payment gateway selection (client-managed).
            typeof(PaymentGatewaySettings),
            // OP4 support tickets (client-raised, operator-answered).
            typeof(SupportTicket), typeof(SupportMessage),
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
                // WP18.2 residency & DPA registry.
                e.Property(t => t.DataRegion).HasMaxLength(16).HasDefaultValue("UK");
                e.Property(t => t.DpaRef).HasMaxLength(128).IsRequired(false);
                // Email-first login: per-tenant MFA/SSO requirement.
                e.Property(t => t.MfaRequired).HasDefaultValue(false);
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
                // FE3.0 agent telemetry
                e.Property(x => x.AgentVersion).HasMaxLength(32);
                e.Property(x => x.AgentPrinterName).HasMaxLength(128);
                // WP5 pull signals, collected on the heartbeat.
                e.Property(x => x.LockReason).HasMaxLength(256);
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
                // WP2c-exempt: the band the line was rung up under — the rate alone cannot tell
                // zero-rated from exempt.
                e.Property(x => x.VatBand).HasMaxLength(40);
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
            modelBuilder.Entity<TillDetails>(e =>
            {
                e.ToTable("TillDetails");
                e.HasKey(x => x.TillId);
                e.Property(x => x.TillId).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(80).IsRequired();
                // Tenant-unique names. The MySQL default collation is case-insensitive, so this
                // index rejects "Front" vs "front" too (the app also guards explicitly for a clean 409).
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            });
            // MAUI retrofit WP2b: effective-dated VAT bands.
            modelBuilder.Entity<VatRatePoint>(e =>
            {
                e.ToTable("VatRatePoints");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Band).HasMaxLength(40).IsRequired();
                // One rate per band per instant; re-stating the same change is a no-op, not a dupe.
                e.HasIndex(x => new { x.TenantId, x.Band, x.EffectiveFromUtc }).IsUnique();
            });
            // WP2c-exempt: legacy tax row → band. One band per tax row, so the mapping can never
            // be ambiguous at the point a till resolves it.
            modelBuilder.Entity<VatBandTaxMap>(e =>
            {
                e.ToTable("VatBandTaxMaps");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Band).HasMaxLength(40).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.LegacyTaxId }).IsUnique();
            });
            // FE10 till theming.
            modelBuilder.Entity<TillTheme>(e =>
            {
                e.ToTable("TillThemes");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(60).IsRequired();
                e.Property(x => x.BaseMode).HasMaxLength(10).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            });
            modelBuilder.Entity<TillGroup>(e =>
            {
                e.ToTable("TillGroups");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(60).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            });
            modelBuilder.Entity<TillGroupMember>(e =>
            {
                e.ToTable("TillGroupMembers");
                e.HasKey(x => new { x.GroupId, x.TillId });
                e.HasIndex(x => new { x.TenantId, x.TillId }); // resolve-time lookup by till
            });
            modelBuilder.Entity<TillThemeAssignment>(e =>
            {
                e.ToTable("TillThemeAssignments");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ScopeKey).HasMaxLength(40).IsRequired();
                e.Property(x => x.ThemeKey).HasMaxLength(50).IsRequired();
                // One assignment per target — assigning again replaces, never stacks.
                e.HasIndex(x => new { x.TenantId, x.Scope, x.ScopeKey }).IsUnique();
            });
            // WP10.4 tenant deletion schedule — GLOBAL (platform-admin), carries TenantId as data.
            modelBuilder.Entity<DeletionSchedule>(e =>
            {
                e.ToTable("DeletionSchedules");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasIndex(x => new { x.TenantId, x.Status });
            });

            // Phase 6 WooCommerce connector.
            modelBuilder.Entity<WebStoreDetails>(e =>
            {
                e.ToTable("WebStores");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.Url).HasMaxLength(255);
                e.Property(x => x.Provider).HasMaxLength(50);
                // Tenant-unique name (case-insensitive via the default collation + app-side guard).
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            });
            modelBuilder.Entity<WebstoreSkuMap>(e =>
            {
                e.ToTable("WebstoreSkuMaps");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Sku).HasMaxLength(64).IsRequired();
                e.Property(x => x.Status).HasMaxLength(20).IsRequired();
                e.Property(x => x.BoundItemIdOne).HasMaxLength(20);
                // One row per (tenant, webstore, SKU) — re-seeing bumps SeenCount, not a new row.
                e.HasIndex(x => new { x.TenantId, x.WebStoreId, x.Sku }).IsUnique();
            });
            modelBuilder.Entity<WebstoreProduct>(e =>
            {
                e.ToTable("WebstoreProducts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Sku).HasMaxLength(64);
                e.Property(x => x.Name).HasMaxLength(300).IsRequired();
                e.Property(x => x.StockStatus).HasMaxLength(20);
                e.Property(x => x.Status).HasMaxLength(20).IsRequired();
                e.Property(x => x.Permalink).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.WebStoreId, x.WooProductId }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.WebStoreId, x.Sku });
            });
            modelBuilder.Entity<WebstoreOutboundLog>(e =>
            {
                e.ToTable("WebstoreOutboundLogs");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.Property(x => x.FromValue).HasMaxLength(300);
                e.Property(x => x.ToValue).HasMaxLength(300);
                e.Property(x => x.Mode).HasMaxLength(10).IsRequired();
                e.Property(x => x.Result).HasMaxLength(300).IsRequired();
                e.Property(x => x.Lane).HasMaxLength(12).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.WebStoreId, x.CreatedAtUtc });
                e.HasIndex(x => new { x.TenantId, x.WebStoreId, x.Kind, x.ItemIdOne });
            });
            modelBuilder.Entity<WebstoreNotification>(e =>
            {
                e.ToTable("WebstoreNotifications");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
                e.Property(x => x.AckedBy).HasMaxLength(100);
                e.HasIndex(x => new { x.TenantId, x.AckedAtUtc });
                e.HasIndex(x => new { x.TenantId, x.WebStoreId, x.WooOrderId }).IsUnique();  // one per order
            });

            // WP3.3 reporting rollups.
            modelBuilder.Entity<SalesRollup>(e =>
            {
                e.ToTable("SalesRollups");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.HasIndex(x => new { x.TenantId, x.TillId, x.BusinessDay }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.StoreId, x.BusinessDay });
                e.HasIndex(x => new { x.TenantId, x.CompanyId, x.BusinessDay });
            });
            modelBuilder.Entity<VatRollup>(e =>
            {
                e.ToTable("VatRollups");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.VatBand).HasMaxLength(40);
                // ⚠ The BAND is part of the unique grain. Without it, a zero-rated row and an
                // exempt row for the same store and day collide on (…, VatRateBp = 0) — the
                // projection would either throw or silently merge the two, and merging destroys
                // the partial-exemption figure. MySQL treats NULLs as distinct in a unique index,
                // which is what pre-band rows need.
                e.HasIndex(x => new { x.TenantId, x.StoreId, x.BusinessDay, x.VatRateBp, x.VatBand }).IsUnique();
            });
            // WP13.1 operator usage metering — composite key on the natural grain.
            modelBuilder.Entity<TenantUsageRollup>(e =>
            {
                e.ToTable("TenantUsageRollups");
                e.HasKey(x => new { x.TenantId, x.BusinessDay, x.Metric });
                e.Property(x => x.Metric).HasMaxLength(64);
            });
            // WP13.2 per-tenant request health — composite key per minute per route group.
            modelBuilder.Entity<TenantRequestStats>(e =>
            {
                e.ToTable("TenantRequestStats");
                e.HasKey(x => new { x.TenantId, x.MinuteUtc, x.RouteGroup });
                e.Property(x => x.RouteGroup).HasMaxLength(64);
                e.HasIndex(x => x.MinuteUtc); // retention purge scans by minute
            });
            // WP13.3 job heartbeats + operator alerts — GLOBAL tables (TenantId as data).
            modelBuilder.Entity<JobRun>(e =>
            {
                e.ToTable("JobRuns");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.JobName).HasMaxLength(100).IsRequired();
                e.Property(x => x.Detail).IsRequired(false); // a successful run carries no detail
                e.HasIndex(x => new { x.JobName, x.TenantId, x.StartedAtUtc });
            });
            modelBuilder.Entity<OperatorAlert>(e =>
            {
                e.ToTable("OperatorAlerts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.AlertKey).HasMaxLength(200).IsRequired();
                e.Property(x => x.JobName).HasMaxLength(100);
                e.Property(x => x.Kind).HasMaxLength(32);
                e.HasIndex(x => x.AlertKey).IsUnique();
            });
            // WP14.2 feature flags + entitlement overrides — GLOBAL tables.
            modelBuilder.Entity<TenantEntitlementOverride>(e =>
            {
                e.ToTable("TenantEntitlementOverrides");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Entitlement).HasMaxLength(128).IsRequired();
                e.Property(x => x.Reason).IsRequired(false);
                e.HasIndex(x => x.TenantId);
            });
            modelBuilder.Entity<PlatformFlag>(e =>
            {
                e.ToTable("PlatformFlags");
                e.HasKey(x => x.FlagName);
                e.Property(x => x.FlagName).HasMaxLength(64);
                e.Property(x => x.Reason).IsRequired(false);
            });
            modelBuilder.Entity<PlatformAnnouncement>(e =>
            {
                e.ToTable("PlatformAnnouncements");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.TenantIds).IsRequired(false);
                e.HasIndex(x => new { x.StartsAtUtc, x.EndsAtUtc });
            });
            // WP16.1/16.2 commercial ops — GLOBAL tables (operator-managed, TenantId as data).
            modelBuilder.Entity<TenantSignal>(e =>
            {
                e.ToTable("TenantSignals");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Signal).HasMaxLength(64).IsRequired();
                e.Property(x => x.Detail).IsRequired(false);
                e.HasIndex(x => new { x.TenantId, x.Signal });
            });
            modelBuilder.Entity<TenantContract>(e =>
            {
                e.ToTable("TenantContracts");
                e.HasKey(x => x.TenantId);
                e.Property(x => x.TenantId).ValueGeneratedNever();
                e.Property(x => x.Notes).IsRequired(false);
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
            });
            // WP17.1 connector health — GLOBAL table (TenantId as data).
            modelBuilder.Entity<ConnectorRun>(e =>
            {
                e.ToTable("ConnectorRuns");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Connector).HasMaxLength(64).IsRequired();
                e.Property(x => x.LastError).IsRequired(false);
                e.Ignore(x => x.LastActivityAtUtc); // computed
                e.HasIndex(x => new { x.Connector, x.TenantId }).IsUnique();
            });
            // WP17.3 messaging seam — GLOBAL tables (operator/provider-managed, tenant-attributed).
            modelBuilder.Entity<TenantSendingIdentity>(e =>
            {
                e.ToTable("TenantSendingIdentities");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.FromAddress).HasMaxLength(256).IsRequired();
                e.Property(x => x.Domain).HasMaxLength(256);
                e.HasIndex(x => new { x.TenantId, x.Channel }).IsUnique();
            });
            modelBuilder.Entity<MessageEvent>(e =>
            {
                e.ToTable("MessageEvents");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ToAddress).HasMaxLength(256).IsRequired();
                e.Property(x => x.FromAddress).HasMaxLength(256);
                e.Property(x => x.ProviderMessageId).HasMaxLength(200);
                e.Property(x => x.Detail).IsRequired(false);
                e.HasIndex(x => x.ProviderMessageId);
                e.HasIndex(x => new { x.TenantId, x.Status });
            });
            modelBuilder.Entity<NotificationSettings>(e =>
            {
                e.ToTable("NotificationSettings");
                e.HasKey(x => x.Channel);
                e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
                e.Property(x => x.ConfigJson).IsRequired(false);
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
            });
            // 16.4 billing provider (GLOBAL single row) + 17.2 per-tenant payment gateway.
            // The expected till build (GLOBAL single row) — see TillReleaseSettings.
            modelBuilder.Entity<TillReleaseSettings>(e =>
            {
                e.ToTable("TillReleaseSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                // ⚠ Nullable on purpose: blank means "say nothing", which is the safe default.
                e.Property(x => x.ExpectedMauiVersion).HasMaxLength(32).IsRequired(false);
                e.Property(x => x.ExpectedWebVersion).HasMaxLength(32).IsRequired(false);
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
            });
            modelBuilder.Entity<BillingSettings>(e =>
            {
                e.ToTable("BillingSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
                e.Property(x => x.ConfigJson).IsRequired(false);
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
            });
            modelBuilder.Entity<PaymentGatewaySettings>(e =>
            {
                e.ToTable("PaymentGatewaySettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
                e.Property(x => x.ConfigJson).IsRequired(false);
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
                e.HasIndex(x => x.TenantId).IsUnique();
            });
            modelBuilder.Entity<SubscriptionPlan>(e =>   // OP2 — GLOBAL price list
            {
                e.ToTable("SubscriptionPlans");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.Property(x => x.EntitlementsJson).IsRequired(false);
                e.Property(x => x.UpdatedBy).HasMaxLength(128);
                e.HasIndex(x => x.Name).IsUnique();
            });
            modelBuilder.Entity<SupportTicket>(e =>   // OP4 — tenant-owned (shadow TenantId via the loop)
            {
                e.ToTable("SupportTickets");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Subject).HasMaxLength(200).IsRequired();
                e.Property(x => x.RaisedByName).HasMaxLength(100);
                e.Property(x => x.AssignedTo).HasMaxLength(100).IsRequired(false);
                e.HasIndex(x => new { x.TenantId, x.Status });
            });
            modelBuilder.Entity<SupportMessage>(e =>
            {
                e.ToTable("SupportMessages");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.AuthorName).HasMaxLength(100);
                e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
                e.HasIndex(x => x.TicketId);
            });

            // WP3.4 financial periods.
            modelBuilder.Entity<FinancialPeriod>(e =>
            {
                e.ToTable("FinancialPeriods");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.CompanyId, x.StartDay });
            });

            // WP5.1 stock ledger.
            modelBuilder.Entity<StockLocation>(e =>
            {
                e.ToTable("StockLocations");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(100).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.StoreId });
            });
            modelBuilder.Entity<StockMovement>(e =>
            {
                e.ToTable("StockMovements");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.StockLocationId, x.ItemIdOne });
                e.HasIndex(x => new { x.TenantId, x.RefId });
            });
            modelBuilder.Entity<StockLevel>(e =>
            {
                e.ToTable("StockLevels");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.StockLocationId, x.ItemIdOne }).IsUnique();
            });
            modelBuilder.Entity<StockTransfer>(e =>
            {
                e.ToTable("StockTransfers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.Status });
            });
            // WP5.3 goods-in.
            modelBuilder.Entity<Supplier>(e =>
            {
                e.ToTable("Suppliers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.Email).HasMaxLength(255);
                e.Property(x => x.Phone).HasMaxLength(50);
            });
            modelBuilder.Entity<PurchaseOrder>(e =>
            {
                e.ToTable("PurchaseOrders");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Reference).HasMaxLength(100);
                e.Property(x => x.Notes).HasMaxLength(1000);
                e.HasIndex(x => new { x.TenantId, x.Status });
                e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId);
            });
            modelBuilder.Entity<POLine>(e =>
            {
                e.ToTable("POLines");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
            });
            // WP5.4 pricing.
            modelBuilder.Entity<ItemPricePolicy>(e =>
            {
                e.ToTable("ItemPricePolicies");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.ItemIdOne }).IsUnique();
            });
            modelBuilder.Entity<PriceListEntry>(e =>
            {
                e.ToTable("PriceListEntries");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.ItemIdOne, x.EffectiveFromUtc });
            });
            modelBuilder.Entity<PriceOverride>(e =>
            {
                e.ToTable("PriceOverrides");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.ItemIdOne).HasMaxLength(20).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.StoreId, x.ItemIdOne, x.EffectiveFromUtc });
            });

            // WP7 cash + payments.
            modelBuilder.Entity<CashEvent>(e =>
            {
                e.ToTable("CashEvents");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.TillId, x.BusinessDay });
            });
            modelBuilder.Entity<PaymentEvent>(e =>
            {
                e.ToTable("PaymentEvents");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
                e.Property(x => x.ProviderRef).HasMaxLength(200).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.ProviderRef });
                e.HasIndex(x => new { x.TenantId, x.ResolvedAtUtc });
            });

            // Phase 8 customers / credit / loyalty.
            modelBuilder.Entity<Customer>(e =>
            {
                e.ToTable("Customers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.Email).HasMaxLength(255);
                e.Property(x => x.Phone).HasMaxLength(50);
                e.HasIndex(x => new { x.TenantId, x.Email });
                // FE2: the membership number is tenant-unique. MySQL (and SQLite) allow repeated
                // NULLs in a unique index, so pre-backfill rows coexist happily.
                e.Property(x => x.MemberNo).HasMaxLength(16);
                e.HasIndex(x => new { x.TenantId, x.MemberNo }).IsUnique();
            });
            modelBuilder.Entity<GiftCard>(e =>
            {
                e.ToTable("GiftCards");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Code).HasMaxLength(24).IsRequired();
                e.Property(x => x.Batch).HasMaxLength(60);
                // FE7: a code is unique WITHIN a tenant — two shops may legitimately print the same
                // string, and a card is only ever spendable where it was sold.
                e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.CustomerId });
            });
            modelBuilder.Entity<GiftCardEntry>(e =>
            {
                e.ToTable("GiftCardEntries");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.GiftCardId });
            });
            modelBuilder.Entity<GiftCardSettings>(e =>
            {
                // one decision per tenant (same shape as PaymentGatewaySettings)
                e.ToTable("GiftCardSettings");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasIndex(x => x.TenantId).IsUnique();
            });
            modelBuilder.Entity<PasswordResetToken>(e =>
            {
                e.ToTable("PasswordResetTokens");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Email).HasMaxLength(255).IsRequired();
                e.Property(x => x.TokenHash).HasMaxLength(32).IsRequired();
                // completion looks the token up by hash alone (the user isn't signed in yet)
                e.HasIndex(x => x.TokenHash).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.UserId });
            });
            modelBuilder.Entity<MemberNoCounter>(e =>
            {
                e.ToTable("MemberNoCounters");
                e.HasKey(x => x.TenantId);
                e.Property(x => x.TenantId).ValueGeneratedNever();
                // the sequence value IS the concurrency token — a racing allocation gets 0 rows
                // updated and throws, and the allocator retries with the fresh value.
                e.Property(x => x.Next).IsConcurrencyToken();
            });
            modelBuilder.Entity<CreditAccount>(e =>
            {
                e.ToTable("CreditAccounts");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique();
            });
            modelBuilder.Entity<CreditEntry>(e =>
            {
                e.ToTable("CreditEntries");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Reason).HasMaxLength(500);
                e.HasIndex(x => new { x.TenantId, x.CreditAccountId });
            });
            modelBuilder.Entity<Membership>(e =>
            {
                e.ToTable("Memberships");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Tier).HasMaxLength(50).IsRequired();
                e.HasIndex(x => new { x.TenantId, x.CustomerId });
                // FE1: no FK constraint — tiers are never hard-deleted (deactivated instead), and
                // a constraint would block the legacy/null path. Resolution is an explicit join.
                e.HasIndex(x => new { x.TenantId, x.TierId });
            });
            modelBuilder.Entity<LoyaltyTier>(e =>
            {
                e.ToTable("LoyaltyTiers");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Name).HasMaxLength(50).IsRequired();
                // one tier per name per tenant; MySQL's default collation makes this
                // case-insensitive, which is the intent ("gold" must not shadow "Gold").
                e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            });
            modelBuilder.Entity<CustomerExternalRef>(e =>
            {
                e.ToTable("CustomerExternalRefs");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Provider).HasMaxLength(20).IsRequired();
                e.Property(x => x.ExternalId).HasMaxLength(64).IsRequired();
                e.Property(x => x.Email).HasMaxLength(255);
                // one link per (tenant, provider, external account); look-ups by customer for the dialog.
                e.HasIndex(x => new { x.TenantId, x.Provider, x.ExternalId }).IsUnique();
                e.HasIndex(x => new { x.TenantId, x.CustomerId });
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

            // WP5: the catalogue changes feed seeks by (tenant, ModifiedAt, IdOne) — that is exactly
            // its keyset order, so this index serves both the WHERE and the ORDER BY.
            // ⚠ Not optional. Items has ~20k rows for one tenant today and the feed runs on every
            // till's sync cadence; without this it is a full scan per till per 15 minutes, and it
            // would degrade quietly as the catalogue grows rather than failing anywhere visible.
            modelBuilder.Entity<Item>()
                .HasIndex("TenantId", nameof(Item.ModifiedAt), nameof(Item.IdOne))
                .HasDatabaseName("IX_Items_Tenant_Modified_IdOne");

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
