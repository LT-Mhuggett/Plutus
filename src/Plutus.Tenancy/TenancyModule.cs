using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// Tenancy module (architecture §3, build order Phase 1). Owns Tenants/Companies/
    /// Stores/Tills, provisioning + device enrolment, and the ITenantContext resolution from
    /// the JWT `tid`/`did` claims.
    /// </summary>
    public static class TenancyModule
    {
        public static IServiceCollection AddPlutusTenancy(this IServiceCollection services, IConfiguration configuration)
        {
            // Request-scoped tenant identity from the principal's claims (null-safe → Kapow).
            // Registered as ITenantContext so the DbContext's (options, ITenantContext) ctor is
            // chosen by DI and every query/write is tenant-scoped. IHttpContextAccessor is
            // registered by the host (ConfigureHttpAccessor); ensure it here too for safety.
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContext, HttpTenantContext>();

            // Device tokens are signed with the SAME secret the auth handler validates against.
            services.AddSingleton(new EnrolmentOptions { DeviceTokenSecret = configuration["TEST_TOKEN_SECRET"] ?? "" });

            // WP5 till presence. SINGLETON and in-memory on purpose: a fleet beating every 60s is a
            // write per till per minute of data that expires in five, which would be the busiest
            // write path in the system and the least useful row in the database. See TillPresence.
            services.AddSingleton<TillPresence>();

            // The tenancy tables live only on MySqlDbContext (server). Resolve it from the
            // registered RepositoryContext; in a DEBUG/SQLite host these endpoints are inert.
            // WP3.2: MySqlDbContext itself is registered so admin controllers can take it
            // directly (same scoped instance as RepositoryContext).
            services.AddScoped(sp =>
                sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "This endpoint requires the MySqlDbContext (server build), not the SQLite dev context."));
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Tenancy endpoints require the MySqlDbContext (server build), not the SQLite dev context.");
                return new EnrolmentService(ctx, sp.GetRequiredService<EnrolmentOptions>());
            });
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Provisioning requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new ProvisioningService(ctx, sp.GetRequiredService<ITenantRoleProvisioner>());
            });

            // ── WP-SIGNUP: self-serve tenancy and the DPA that gates it ──────────────────────────
            // ⚠ The disposable-domain list is a SINGLETON built from config, so it can be replaced
            // by setting SIGNUP_DISPOSABLE_DOMAINS without a code change — the plan requires the
            // list be config rather than code, because it is wrong the day after it ships.
            services.AddSingleton<IDisposableEmailDomains>(
                _ => new DisposableEmailDomains(configuration["SIGNUP_DISPOSABLE_DOMAINS"]));

            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Signup requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new TenantApplicationService(ctx, sp.GetRequiredService<IDisposableEmailDomains>());
            });

            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "The DPA service requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new DpaService(ctx);
            });
            // ⚠⚠ The front door's on/off switch, and it DEFAULTS TO CLOSED — unlike every other
            // PlatformFlag, which is a kill switch that defaults to on. See SignupGate.
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "The signup gate requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new SignupGate(ctx);
            });

            // Seeds Matt's DPA as an UNPUBLISHED draft on first boot, once. Never fatal — a
            // seeder that can stop the backend booting takes every till offline over a document
            // nobody can accept yet.
            services.AddHostedService<DpaSeedHostedService>();

            // Phase 10: entitlements, billing seam, tenant lifecycle, retention sweeper.
            services.AddScoped<TenantLifecycleService>(sp => new TenantLifecycleService(sp.GetRequiredService<MySqlDbContext>()));
            services.AddScoped<IEntitlementService>(sp => new EntitlementService(sp.GetRequiredService<MySqlDbContext>()));
            services.AddScoped<IQuotaGuard>(sp => new QuotaGuard(sp.GetRequiredService<IEntitlementService>())); // WP13.5
            // Provider seam — NullBillingProvider stands in until the commercial choice; verifies
            // BILLING_WEBHOOK_SECRET so the entitlement-write path is testable now.
            services.AddSingleton<IBillingProvider>(new NullBillingProvider(configuration["BILLING_WEBHOOK_SECRET"]));
            services.AddSingleton(new RetentionOptions());
            services.AddHostedService<RetentionSweeper>();

            // WP15.2 status-page feeder: writes status.json to the status vhost's docroot every
            // 30s (inert unless STATUS_JSON_PATH is set, so dev/test never touch the disk).
            services.AddSingleton(new StatusPageOptions { OutputPath = configuration["STATUS_JSON_PATH"] });
            services.AddHostedService<StatusPageWriter>();

            // WP13.1 usage metering: the SaleRecorded → sales.* fold. Own consumer name → own
            // offset + dedupe, drained by the outbox dispatcher exactly like the rollup projection.
            services.AddScoped<IEventConsumer>(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Usage metering requires the MySqlDbContext (server build).");
                return new UsageMeteringConsumer(ctx);
            });
            return services;
        }
    }
}
