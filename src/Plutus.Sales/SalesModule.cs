using System;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;

namespace Plutus.Sales
{
    /// <summary>
    /// Sales module composition entry point (T0.2 AddPlutusModule pattern). Owns the sale
    /// store, the sale read/detail endpoints, and (T1.4) the idempotent ingest API. The
    /// Summary/VatIntegrity/SaleReport actions on SaleController are transitional and migrate
    /// to Plutus.Reporting in Phase 3.
    /// </summary>
    public static class SalesModule
    {
        public static IServiceCollection AddPlutusSales(this IServiceCollection services)
        {
            // Ingest writes the server-only sales-v2 tables on MySqlDbContext (resolved from the
            // registered RepositoryContext; inert on a DEBUG/SQLite host).
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Sale ingest requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new SalesIngestService(ctx);
            });
            return services;
        }
    }
}
