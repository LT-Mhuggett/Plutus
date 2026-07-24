using Microsoft.Extensions.DependencyInjection;

namespace Plutus.Sales
{
    /// <summary>
    /// Sales module composition entry point (T0.2 AddPlutusModule pattern). Owns the sale
    /// store and (currently) the sale read/detail endpoints; the idempotent ingest API and
    /// outbox relay land here in Phase 1. The Summary/VatIntegrity/SaleReport actions on
    /// SaleController are transitional and migrate to Plutus.Reporting in Phase 3.
    /// </summary>
    public static class SalesModule
    {
        public static IServiceCollection AddPlutusSales(this IServiceCollection services) => services;
    }
}
