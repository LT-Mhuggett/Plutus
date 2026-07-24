using Microsoft.Extensions.DependencyInjection;

namespace Plutus.Reporting
{
    /// <summary>
    /// Reporting module (architecture §7.1, build order Phase 3). Will own the SaleRecorded
    /// projection consumer, rollup tables (per till/store/company/day), VAT rollups and the
    /// Company-view report endpoints. Scaffolded now so the Phase 0 module layout is complete;
    /// the transitional Summary/VatIntegrity endpoints on Plutus.Sales migrate here then.
    /// </summary>
    public static class ReportingModule
    {
        public static IServiceCollection AddPlutusReporting(this IServiceCollection services) => services;
    }
}
