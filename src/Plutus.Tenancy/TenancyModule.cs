using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        public static IServiceCollection AddPlutusTenancy(this IServiceCollection services)
        {
            // Request-scoped tenant identity from the principal's claims (null-safe → Kapow).
            // Registered as ITenantContext so the DbContext's (options, ITenantContext) ctor is
            // chosen by DI and every query/write is tenant-scoped. IHttpContextAccessor is
            // registered by the host (ConfigureHttpAccessor); ensure it here too for safety.
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContext, HttpTenantContext>();
            return services;
        }
    }
}
