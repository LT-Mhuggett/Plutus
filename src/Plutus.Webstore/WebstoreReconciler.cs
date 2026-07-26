using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>Poll tuning (plan rule 3 — gentle on the VPS). Registered by the module with
    /// these defaults; the host may replace the instance to override.</summary>
    public sealed class WebstoreOptions
    {
        public bool PollEnabled { get; init; } = true;
        public int PollMinutes { get; init; } = 20;
        public int PerPage { get; init; } = 25;
        /// <summary>Hard page cap per webstore per cycle — a huge backlog drains over several
        /// cycles rather than hammering the site in one.</summary>
        public int MaxPagesPerCycle { get; init; } = 4;
        /// <summary>Re-read window behind the cursor; the deterministic saleId dedupes it.</summary>
        public int OverlapMinutes { get; init; } = 5;
        /// <summary>First-ever poll looks back this far (webhooks carry the live load; the poll
        /// only heals gaps, so a bounded backfill is right).</summary>
        public int InitialLookbackHours { get; init; } = 24;
        /// <summary>Nightly FULL product sweep runs in the 4-hour window starting at this UTC hour
        /// (off-peak for the shop). The first-ever sweep runs immediately regardless.</summary>
        public int FullSweepHourUtc { get; init; } = 2;
        /// <summary>Page cap for a FULL product sweep (nightly; ~75 pages on the Kapow store).</summary>
        public int MaxFullSweepPages { get; init; } = 200;
    }

    /// <summary>Summary of one reconciliation pass (logged; asserted in tests).</summary>
    public sealed class ReconcileSummary
    {
        public int Webstores, Requests, Orders, Recorded, Duplicates, Skipped, NeedsMapping, Quarantined, ProductsSeen;
        public override string ToString() =>
            $"webstores={Webstores} requests={Requests} orders={Orders} recorded={Recorded} " +
            $"dup={Duplicates} skipped={Skipped} needs-mapping={NeedsMapping} quarantined={Quarantined} products={ProductsSeen}";
    }

    /// <summary>
    /// WP6.2 reconciliation poll core: for each enabled connection, pull orders with
    /// `modified_after ≥ cursor − overlap` from the Woo REST API (read credentials) and run each
    /// through the SAME routing as webhooks (status gate, SKU queue, quarantine parking, idempotent
    /// ingest) on the same tenant-fixed pipeline. Heals webhook outages: anything missed is picked
    /// up here; anything double-seen dedupes on the deterministic saleId. Separated from the timer
    /// (WebstoreReconciliationService) so this core is unit-testable with a stubbed HttpClient.
    /// </summary>
    public sealed class WebstoreReconciler
    {
        private readonly DbContextOptions<MySqlDbContext> _dbOptions;
        private readonly WebstoreWebhookPipelineFactory _pipelines;
        private readonly IWebstoreSecretProvider _secrets;
        private readonly HttpClient _http;
        private readonly WebstoreOptions _options;
        private readonly ILogger? _log;

        public WebstoreReconciler(
            DbContextOptions<MySqlDbContext> dbOptions, WebstoreWebhookPipelineFactory pipelines,
            IWebstoreSecretProvider secrets, HttpClient http, WebstoreOptions options, ILogger? log = null)
        {
            _dbOptions = dbOptions;
            _pipelines = pipelines;
            _secrets = secrets;
            _http = http;
            _options = options;
            _log = log;
        }

        public async Task<ReconcileSummary> RunOnceAsync(CancellationToken ct = default)
        {
            var summary = new ReconcileSummary();

            // Unscoped context (Guid.Empty = platform) purely to enumerate connections.
            Entities.Models.WebStoreDetails[] stores;
            await using (var root = new MySqlDbContext(_dbOptions, new FixedTenantContext(Guid.Empty)) { CurrentUser = "webstore-poll" })
                stores = await root.WebStores.AsNoTracking().Where(w => w.Enabled).ToArrayAsync(ct);

            foreach (var ws in stores)
            {
                if (string.IsNullOrWhiteSpace(ws.Url)) continue;
                var creds = _secrets.GetRestCredentials(ws.Id);
                if (creds is null)
                {
                    _log?.LogWarning("webstore {Id}: no REST credentials configured — poll skipped.", ws.Id);
                    continue;
                }
                summary.Webstores++;

                var ctx = new WebstoreConnectionContext
                {
                    WebStoreId = ws.Id, TenantId = ws.TenantId, TillId = ws.TillId, DeviceId = ws.DeviceId,
                };
                var cursor = ws.OrdersCursorUtc ?? DateTime.UtcNow.AddHours(-_options.InitialLookbackHours);
                var since = cursor.AddMinutes(-_options.OverlapMinutes);
                var maxSeen = cursor;
                var client = new WooRestClient(_http, ws.Url, creds);

                using var pipeline = _pipelines.Create(ctx);
                for (var page = 1; page <= _options.MaxPagesPerCycle; page++)
                {
                    var (orders, totalPages) = await client.GetOrdersModifiedSinceAsync(since, page, _options.PerPage, ct);
                    foreach (var order in orders)
                    {
                        summary.Orders++;
                        var r = await pipeline.Processor.RouteOrderAsync(order, ctx, pipeline.Resolver, ct);
                        switch (r.Status)
                        {
                            case WebstoreInboundStatus.Recorded:
                                summary.Recorded++;
                                await WebstoreNotifications.CreateForRecordedAsync(pipeline.Db, ctx, order, ws.StoreId, ct);
                                break;
                            case WebstoreInboundStatus.Duplicate: summary.Duplicates++; break;
                            case WebstoreInboundStatus.Skipped: summary.Skipped++; break;
                            case WebstoreInboundStatus.NeedsMapping:
                                summary.NeedsMapping++;
                                // Park the payload so bind→retry can heal it (same as the webhook path).
                                await WebstoreQuarantine.ParkAsync(
                                    pipeline.Db, ctx, r, JsonSerializer.Serialize(order, WooJson.Options), ct);
                                break;
                            case WebstoreInboundStatus.Quarantined:
                                summary.Quarantined++;
                                await WebstoreQuarantine.ParkAsync(
                                    pipeline.Db, ctx, r, JsonSerializer.Serialize(order, WooJson.Options), ct);
                                break;
                        }
                        if (order.DateModifiedGmt is { } m)
                        {
                            var modified = WebstoreMoney.ParseGmt(m);
                            if (modified > maxSeen) maxSeen = modified;
                        }
                    }
                    if (page >= totalPages || orders.Count == 0) break;
                }

                // WP6.4 product sweep — incremental every cycle; FULL when never swept or the
                // nightly window comes round (the only pass that detects deletions).
                var fullDue = ws.LastFullProductSweepUtc is null ||
                              (DateTime.UtcNow - ws.LastFullProductSweepUtc.Value > TimeSpan.FromHours(20)
                               && DateTime.UtcNow.Hour >= _options.FullSweepHourUtc
                               && DateTime.UtcNow.Hour < _options.FullSweepHourUtc + 4);
                var swept = await SweepProductsAsync(pipeline.Db, client, ctx, ws, fullDue, ct);
                summary.ProductsSeen += swept;

                summary.Requests += client.RequestCount;

                if (maxSeen > (ws.OrdersCursorUtc ?? DateTime.MinValue))
                {
                    var row = await pipeline.Db.WebStores.FirstAsync(w => w.Id == ws.Id, ct);
                    row.OrdersCursorUtc = maxSeen;
                    await pipeline.Db.SaveChangesAsync(ct);
                }
            }

            _log?.LogInformation("webstore reconciliation: {Summary}", summary);
            return summary;
        }

        /// <summary>Upsert the product cache from an incremental (`modified_after` cursor) or FULL
        /// pull. A full pass also stamps deletions: previously-cached rows not seen in this pass →
        /// Status="deleted" (kept, so the alignment report can show what vanished).</summary>
        private async Task<int> SweepProductsAsync(
            MySqlDbContext db, WooRestClient client, WebstoreConnectionContext ctx,
            Entities.Models.WebStoreDetails ws, bool full, CancellationToken ct)
        {
            var sweepStart = DateTime.UtcNow;
            DateTime? since = full ? null : (ws.ProductsCursorUtc ?? sweepStart.AddHours(-_options.InitialLookbackHours));
            var maxPages = full ? _options.MaxFullSweepPages : _options.MaxPagesPerCycle;
            var seen = 0;
            var maxModified = ws.ProductsCursorUtc ?? DateTime.MinValue;

            for (var page = 1; page <= maxPages; page++)
            {
                var (products, totalPages) = await client.GetProductsAsync(since, page, _options.PerPage, ct);
                foreach (var p in products)
                {
                    seen++;
                    var row = await db.WebstoreProducts
                        .FirstOrDefaultAsync(x => x.WebStoreId == ctx.WebStoreId && x.WooProductId == p.Id, ct);
                    if (row is null)
                    {
                        row = new Entities.Models.WebstoreProduct
                        {
                            Id = Uuid7.New(), TenantId = ctx.TenantId, WebStoreId = ctx.WebStoreId, WooProductId = p.Id,
                        };
                        db.WebstoreProducts.Add(row);
                    }
                    row.Sku = string.IsNullOrWhiteSpace(p.Sku) ? null : p.Sku.Trim();
                    row.Name = p.Name ?? $"product {p.Id}";
                    row.PricePence = WebstoreMoney.ParsePence(p.Price);
                    row.RegularPricePence = string.IsNullOrWhiteSpace(p.RegularPrice) ? null : WebstoreMoney.ParsePence(p.RegularPrice);
                    row.StockQuantity = p.StockQuantity;
                    row.StockStatus = p.StockStatus;
                    row.Status = p.Status ?? "publish";
                    row.Permalink = p.Permalink;
                    if (p.DateModifiedGmt is { } m)
                    {
                        row.WooModifiedUtc = WebstoreMoney.ParseGmt(m);
                        if (row.WooModifiedUtc > maxModified) maxModified = row.WooModifiedUtc.Value;
                    }
                    row.LastSeenUtc = sweepStart;
                }
                await db.SaveChangesAsync(ct);
                if (page >= totalPages || products.Count == 0) break;
            }

            var wsRow = await db.WebStores.FirstAsync(w => w.Id == ws.Id, ct);
            if (maxModified > DateTime.MinValue) wsRow.ProductsCursorUtc = maxModified;
            if (full)
            {
                wsRow.LastFullProductSweepUtc = sweepStart;
                // Anything cached but not seen by the full pass no longer exists on the site.
                await foreach (var gone in db.WebstoreProducts
                    .Where(x => x.WebStoreId == ctx.WebStoreId && x.LastSeenUtc < sweepStart && x.Status != "deleted")
                    .AsAsyncEnumerable().WithCancellation(ct))
                    gone.Status = "deleted";
            }
            await db.SaveChangesAsync(ct);
            return seen;
        }
    }

    /// <summary>The timer shell: runs the reconciler every PollMinutes (initial settle delay,
    /// per-cycle scope, failures logged and retried next cycle — never crash the host).</summary>
    public sealed class WebstoreReconciliationService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly WebstoreOptions _options;
        private readonly ILogger<WebstoreReconciliationService> _log;

        public WebstoreReconciliationService(
            IServiceScopeFactory scopes, WebstoreOptions options, ILogger<WebstoreReconciliationService> log)
        {
            _scopes = scopes;
            _options = options;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.PollEnabled)
            {
                _log.LogInformation("webstore reconciliation poll disabled by options.");
                return;
            }
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);   // let the host settle
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<WebstoreReconciler>().RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "webstore reconciliation cycle failed; retrying next cycle.");
                }
                await Task.Delay(TimeSpan.FromMinutes(_options.PollMinutes), stoppingToken);
            }
        }
    }
}
