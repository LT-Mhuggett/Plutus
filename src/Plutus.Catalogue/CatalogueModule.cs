using Microsoft.Extensions.DependencyInjection;

namespace Plutus.Catalogue
{
    /// <summary>
    /// Catalogue module composition entry point (T0.2 AddPlutusModule pattern) — items,
    /// barcodes, band validation, catalogue publish feed. Its controllers are registered
    /// with MVC as an application part by the host. Currently owns ItemController (incl.
    /// the VAT band-consistency guardrail); item CRUD services grow here in later phases.
    /// </summary>
    public static class CatalogueModule
    {
        public static IServiceCollection AddPlutusCatalogue(this IServiceCollection services) => services;
    }
}
