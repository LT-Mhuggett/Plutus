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
        /// <summary>WP6.3 slow lane: max stock corrections journaled/sent per cycle (a big stock
        /// take drains over cycles instead of hammering the site).</summary>
        public int MaxOutboundPerCycle { get; init; } = 100;
        /// <summary>WP6.5 draft scan: how far back "recently created Plutus items" reaches.</summary>
        public int DraftScanLookbackHours { get; init; } = 48;
        /// <summary>WP6.1 onboarding: the PUBLIC https base of this API (wc-auth callback +
        /// webhook delivery URLs). Unset → onboarding endpoints refuse with a clear error.</summary>
        public string? PublicBaseUrl { get; init; }
    }

    /// <summary>Summary of one reconciliation pass (logged; asserted in tests).</summary>
    public sealed class ReconcileSummary
    {
        public int Webstores, Requests, Orders, Recorded, Duplicates, Skipped, NeedsMapping, Quarantined, ProductsSeen,
            OutboundStock, OutboundDrafts;
        public override string ToString() =>
            $"webstores={Webstores} requests={Requests} orders={Orders} recorded={Recorded} " +
            $"dup={Duplicates} skipped={Skipped} needs-mapping={NeedsMapping} quarantined={Quarantined} products={ProductsSeen} " +
            $"outbound-stock={OutboundStock} outbound-drafts={OutboundDrafts}";
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
        private readonly Plutus.SharedKernel.IJobHeartbeat? _heartbeat;

        public WebstoreReconciler(
            DbContextOptions<MySqlDbContext> dbOptions, WebstoreWebhookPipelineFactory pipelines,
            IWebstoreSecretProvider secrets, HttpClient http, WebstoreOptions options, ILogger? log = null,
            Plutus.SharedKernel.IJobHeartbeat? heartbeat = null)
        {
            _dbOptions = dbOptions;
            _pipelines = pipelines;
            _secrets = secrets;
            _http = http;
            _options = options;
            _log = log;
            _heartbeat = heartbeat;
        }

        public async Task<ReconcileSummary> RunOnceAsync(CancellationToken ct = default)
        {
            var summary = new ReconcileSummary();

            // Unscoped context (Guid.Empty = platform) purely to enumerate connections.
            Entities.Models.WebStoreDetails[] stores;
            await using (var root = new MySqlDbContext(_dbOptions, new FixedTenantContext(Guid.Empty)) { CurrentUser = "webstore-poll" })
                stores = await root.WebStores.AsNoTracking().Where(w => w.Enabled).ToArrayAsync(ct);

            // Each store's poll is tracked per tenant (WP13.3 "woo-poll") so a stalled or failing
            // per-tenant poll surfaces as an operator alert. Body unchanged — just wrapped.
            async Task PollOneAsync(Entities.Models.WebStoreDetails ws)
            {
                if (string.IsNullOrWhiteSpace(ws.Url)) return;
                var creds = _secrets.GetRestCredentials(ws.Id);
                if (creds is null)
                {
                    _log?.LogWarning("webstore {Id}: no REST credentials configured — poll skipped.", ws.Id);
                    return;
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
                        if (r.Status is WebstoreInboundStatus.Recorded or WebstoreInboundStatus.Duplicate or WebstoreInboundStatus.Skipped)
                            await WebstoreRefunds.ApplyAsync(pipeline.Db, ctx, order, ct);
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

                // WP6.3 SLOW lane: catch everything the fast lane can't see (stock takes,
                // transfers, goods-in, manual corrections) by diffing Plutus levels against the
                // freshly-swept web cache. WP6.5: newly-created Plutus items → draft products.
                // Both no-op instantly unless OutboundMode is dry-run/live.
                if (ws.OutboundMode is "dry-run" or "live")
                {
                    var diff = await StockDiffAsync(pipeline.Db, ws, _options.MaxOutboundPerCycle, ct);
                    summary.OutboundStock += await WebstoreOutbound.PushStockAsync(pipeline.Db, client, ws, diff, lane: "slow", ct);
                    summary.OutboundDrafts += await WebstoreOutbound.PushNewItemDraftsAsync(
                        pipeline.Db, client, ws, TimeSpan.FromHours(_options.DraftScanLookbackHours), ct);
                }

                summary.Requests += client.RequestCount;

                if (maxSeen > (ws.OrdersCursorUtc ?? DateTime.MinValue))
                {
                    var row = await pipeline.Db.WebStores.FirstAsync(w => w.Id == ws.Id, ct);
                    row.OrdersCursorUtc = maxSeen;
                    await pipeline.Db.SaveChangesAsync(ct);
                }
            }

            foreach (var ws in stores)
            {
                if (_heartbeat != null)
                    await _heartbeat.TrackAsync("woo-poll", ws.TenantId, _ => PollOneAsync(ws), ct);
                else
                    await PollOneAsync(ws);
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

        /// <summary>Items whose web-listed quantity disagrees with the Plutus level (post-buffer)
        /// — the slow lane's work list, capped per cycle.</summary>
        private static async Task<System.Collections.Generic.List<string>> StockDiffAsync(
            MySqlDbContext db, Entities.Models.WebStoreDetails ws, int cap, CancellationToken ct)
        {
            var locationId = await db.StockLocations.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.TenantId == ws.TenantId && l.StoreId == (ws.StoreId ?? 1) && l.Type == Entities.Models.StockLocationType.Store)
                .Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
            if (locationId is null) return new System.Collections.Generic.List<string>();

            var levels = db.StockLevels.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == ws.TenantId && s.StockLocationId == locationId);
            // ONLY products linked to a catalogue item — Plutus must never adjust stock on web
            // products it doesn't manage. (Caught by the first LIVE dry-run 2026-07-27: unlinked
            // products — incl. 26-digit composite SKUs — would have been zeroed on go-live.)
            var itemSkus = db.Items.IgnoreQueryFilters().AsNoTracking().Select(i => i.IdOne);
            var products = db.WebstoreProducts.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.WebStoreId == ws.Id && p.Sku != null && p.Status != "deleted"
                            && itemSkus.Contains(p.Sku));

            // Left-join products→levels: a listed product with NO level row is level 0.
            var diff = await (from p in products
                              join s in levels on p.Sku equals s.ItemIdOne into g
                              from s in g.DefaultIfEmpty()
                              let level = (int?)s.Quantity ?? 0
                              let target = Math.Max(0, level - ws.OversellBuffer)
                              where p.StockQuantity != target
                              select p.Sku!).Take(cap).ToListAsync(ct);
            return diff;
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
