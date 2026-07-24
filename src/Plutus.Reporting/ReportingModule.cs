using System;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Reporting
{
    /// <summary>
    /// Reporting module (architecture §7.1, build order Phase 3). Will own the SaleRecorded
    /// projection consumer, rollup tables (per till/store/company/day), VAT rollups and the
    /// Company-view report endpoints. Until then it hosts the TRANSITIONAL Phase-2 legacy
    /// bridge (<see cref="LegacySaleBridgeConsumer"/>) that keeps the legacy read model fed
    /// from the v1 pipeline; the transitional Summary/VatIntegrity endpoints on Plutus.Sales
    /// migrate here in Phase 3 and the bridge is deleted when WP3.3 rollups land.
    /// </summary>
    public static class ReportingModule
    {
        public static IServiceCollection AddPlutusReporting(this IServiceCollection services)
        {
            // Resolves the SAME scoped context the OutboxDrainer saves — the bridge's writes
            // commit atomically with the consumer offset (see the consumer's class comment).
            // Inert on a DEBUG/SQLite host: no consumer is registered there.
            services.AddScoped<IEventConsumer>(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "The legacy sale bridge requires the MySqlDbContext (server build).");
                return new LegacySaleBridgeConsumer(ctx);
            });
            return services;
        }
    }
}
