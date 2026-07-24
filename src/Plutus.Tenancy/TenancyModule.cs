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
            return services;
        }
    }
}
