using System;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Catalogue
{
    /// <summary>
    /// Catalogue module composition entry point (T0.2 AddPlutusModule pattern) — items,
    /// barcodes, band validation, catalogue publish feed, and (WP5.1) the stock ledger.
    /// Its controllers are registered with MVC as an application part by the host.
    /// </summary>
    public static class CatalogueModule
    {
        public static IServiceCollection AddPlutusCatalogue(this IServiceCollection services)
        {
            // WP5.1: the stock projection consumer — same drainer contract as the Reporting
            // consumers (shared scoped context; commits ride the drainer's SaveChanges).
            services.AddScoped<IEventConsumer>(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "The stock ledger requires the MySqlDbContext (server build).");
                return new StockProjectionConsumer(ctx);
            });
            return services;
        }
    }
}
