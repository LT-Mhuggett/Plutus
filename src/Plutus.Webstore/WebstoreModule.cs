using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plutus.Entities;

namespace Plutus.Webstore
{
    /// <summary>
    /// Phase 6 WooCommerce connector wiring. Registers the connector-side services: the DB-backed
    /// SKU resolver + review queue, the inbound webhook processor, and the WP6.2a webhook handler
    /// + per-delivery tenant-scoped pipeline factory. The host MUST also register:
    ///  - <c>Func&lt;MySqlDbContext, WebstoreConnectionContext, IWebstoreSaleSink&gt;</c> — the
    ///    sink factory (adapter over SalesIngestService; host-side so this module never
    ///    references the Sales module);
    ///  - <see cref="IWebstoreSecretProvider"/> — per-connection webhook secrets from config.
    /// </summary>
    public static class WebstoreModule
    {
        public static IServiceCollection AddPlutusWebstore(this IServiceCollection services)
        {
            services.AddScoped<IWebstoreSkuResolver, CatalogueSkuResolver>();
            services.AddScoped<IWebstoreSkuMapQueue, WebstoreSkuMapQueue>();
            // NOTE: WebstoreWebhookProcessor is deliberately NOT registered — it is constructed
            // per delivery by the pipeline factory with a tenant-fixed context + per-delivery
            // sink. A scoped registration here fails dev-mode ValidateOnBuild (IWebstoreSaleSink
            // has no direct registration, only the per-delivery factory func) — it crashed the
            // 2026-07-26 deploy until removed.

            // WP6.2a: per-delivery tenant-scoped pipeline + the anonymous-webhook handler.
            // DbContextOptions<MySqlDbContext> is registered by the host's
            // AddDbContext<RepositoryContext, MySqlDbContext> — as a SCOPED service, so the
            // factory must be scoped too (a singleton here 500s at controller activation:
            // "Cannot resolve scoped service … from root provider", found live 2026-07-26).
            services.AddScoped(sp => new WebstoreWebhookPipelineFactory(
                sp.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
                sp.GetRequiredService<Func<MySqlDbContext, WebstoreConnectionContext, IWebstoreSaleSink>>()));
            services.AddScoped<WebstoreWebhookHandler>();

            // WP6.2 reconciliation poll: heals webhook outages via the same routing core.
            // Options default to the plan's gentle cadence; the host may pre-register its own
            // WebstoreOptions instance to override (TryAdd keeps it).
            services.TryAddSingleton(new WebstoreOptions());
            services.AddHttpClient(nameof(WebstoreReconciler))
                .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddScoped(sp => new WebstoreReconciler(
                sp.GetRequiredService<DbContextOptions<MySqlDbContext>>(),
                sp.GetRequiredService<WebstoreWebhookPipelineFactory>(),
                sp.GetRequiredService<IWebstoreSecretProvider>(),
                sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(nameof(WebstoreReconciler)),
                sp.GetRequiredService<WebstoreOptions>(),
                sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger(nameof(WebstoreReconciler))));
            services.AddHostedService<WebstoreReconciliationService>();
            return services;
        }
    }
}
