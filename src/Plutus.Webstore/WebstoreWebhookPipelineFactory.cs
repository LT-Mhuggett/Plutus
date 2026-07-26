using System;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Tenancy;

namespace Plutus.Webstore
{
    /// <summary>One webhook delivery's processing services, all bound to a TENANT-FIXED DbContext
    /// (WP6.2a step 4). Dispose after the delivery — the context is per-delivery, not per-request.</summary>
    public sealed class WebstoreWebhookPipeline : IDisposable
    {
        public MySqlDbContext Db { get; }
        public WebstoreWebhookProcessor Processor { get; }
        public IWebstoreSkuResolver Resolver { get; }

        internal WebstoreWebhookPipeline(MySqlDbContext db, WebstoreWebhookProcessor processor, IWebstoreSkuResolver resolver)
        {
            Db = db;
            Processor = processor;
            Resolver = resolver;
        }

        public void Dispose() => Db.Dispose();
    }

    /// <summary>
    /// WP6.2a step 4 — the per-delivery tenant scope. A webhook is anonymous, so the ambient
    /// (claims-derived) tenant context is WRONG for it; this factory builds the whole processing
    /// pipeline over <c>new MySqlDbContext(options, new FixedTenantContext(connection.TenantId))</c>
    /// so the resolver, queue, and ingest all run under the webstore's own tenant — query filters
    /// and the SaveChanges stamp/guard included. The sale sink is host-supplied (the adapter over
    /// SalesIngestService lives host-side so this module never references the Sales module).
    /// </summary>
    public sealed class WebstoreWebhookPipelineFactory
    {
        private readonly DbContextOptions<MySqlDbContext> _options;
        private readonly Func<MySqlDbContext, WebstoreConnectionContext, IWebstoreSaleSink> _sinkFactory;

        public WebstoreWebhookPipelineFactory(
            DbContextOptions<MySqlDbContext> options,
            Func<MySqlDbContext, WebstoreConnectionContext, IWebstoreSaleSink> sinkFactory)
        {
            _options = options;
            _sinkFactory = sinkFactory;
        }

        public WebstoreWebhookPipeline Create(WebstoreConnectionContext ctx)
        {
            var db = new MySqlDbContext(_options, new FixedTenantContext(ctx.TenantId))
            {
                CurrentUser = $"webstore:{ctx.WebStoreId:D}",
            };
            return new WebstoreWebhookPipeline(
                db,
                new WebstoreWebhookProcessor(_sinkFactory(db, ctx), new WebstoreSkuMapQueue(db)),
                new CatalogueSkuResolver(db));
        }
    }
}
