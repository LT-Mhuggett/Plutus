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
                return new ProvisioningService(ctx);
            });

            // Phase 10: entitlements, billing seam, tenant lifecycle, retention sweeper.
            services.AddScoped<TenantLifecycleService>(sp => new TenantLifecycleService(sp.GetRequiredService<MySqlDbContext>()));
            services.AddScoped<IEntitlementService>(sp => new EntitlementService(sp.GetRequiredService<MySqlDbContext>()));
            // Provider seam — NullBillingProvider stands in until the commercial choice; verifies
            // BILLING_WEBHOOK_SECRET so the entitlement-write path is testable now.
            services.AddSingleton<IBillingProvider>(new NullBillingProvider(configuration["BILLING_WEBHOOK_SECRET"]));
            services.AddSingleton(new RetentionOptions());
            services.AddHostedService<RetentionSweeper>();

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
