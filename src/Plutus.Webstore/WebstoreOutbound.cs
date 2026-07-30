using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>
    /// WP6.3 outbound engine — the ONLY code that would ever write to a webstore, and it writes
    /// nothing unless the connection's <c>OutboundMode</c> says so:
    ///   off     → every loop exits before doing anything (the kill switch);
    ///   dry-run → journals exactly what WOULD be sent (WebstoreOutboundLogs), sends nothing;
    ///   live    → sends, then journals sent/failed (a read-only key makes sends 401 → fails safe).
    /// Stock listed to the web = max(0, plutusLevel − OversellBuffer). Idempotent per state: a
    /// change is only journaled/sent when the target differs from the webstore's cached value,
    /// and the cache is updated on success so repeats are no-ops.
    /// </summary>
    public static class WebstoreOutbound
    {
        /// <summary>Push current stock for specific items (the FAST lane: called per sale/return
        /// with that sale's items, so an in-store sale reaches the web in seconds; also reused by
        /// the slow lane with a diff list). Returns rows journaled.</summary>
        public static async Task<int> PushStockAsync(
            MySqlDbContext db, WooRestClient client, WebStoreDetails ws,
            IReadOnlyCollection<string> itemIdOnes, string lane, CancellationToken ct = default)
        {
            if (ws.OutboundMode is not ("dry-run" or "live") || itemIdOnes.Count == 0) return 0;

            // The webstore's fulfilment stock: the Store-type location of its StoreId.
            var locationId = await db.StockLocations.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.TenantId == ws.TenantId && l.StoreId == (ws.StoreId ?? 1) && l.Type == StockLocationType.Store)
                .Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
            if (locationId is null) return 0;

            var pushed = 0;
            foreach (var itemIdOne in itemIdOnes.Distinct())
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(itemIdOne) || itemIdOne.Length > 20) continue;  // not a catalogue barcode
                // Kill switch honoured mid-batch: re-read the row's mode every item.
                var mode = await db.WebStores.IgnoreQueryFilters().AsNoTracking()
                    .Where(w => w.Id == ws.Id).Select(w => w.OutboundMode).FirstAsync(ct);
                if (mode is not ("dry-run" or "live")) break;

                var product = await db.WebstoreProducts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.WebStoreId == ws.Id && p.Sku == itemIdOne && p.Status != "deleted", ct);
                if (product is null) continue;   // not listed on the web — nothing to sync

                var level = await db.StockLevels.IgnoreQueryFilters().AsNoTracking()
                    .Where(s => s.TenantId == ws.TenantId && s.StockLocationId == locationId && s.ItemIdOne == itemIdOne)
                    .Select(s => (int?)s.Quantity).FirstOrDefaultAsync(ct) ?? 0;
                var target = Math.Max(0, level - ws.OversellBuffer);
                if (product.StockQuantity == target) continue;   // already in agreement

                var log = new WebstoreOutboundLog
                {
                    TenantId = ws.TenantId, WebStoreId = ws.Id, Kind = "stock", ItemIdOne = itemIdOne,
                    WooProductId = product.WooProductId, Lane = lane,
                    FromValue = product.StockQuantity?.ToString() ?? "?", ToValue = target.ToString(),
                    Mode = mode, Result = "logged", CreatedAtUtc = DateTime.UtcNow,
                };

                if (mode == "live")
                {
                    try
                    {
                        await client.UpdateProductStockAsync(product.WooProductId, target, ct);
                        log.Result = "sent";
                        log.SentAtUtc = DateTime.UtcNow;
                        product.StockQuantity = target;          // cache follows the write
                        product.StockStatus = target > 0 ? "instock" : "outofstock";
                    }
                    catch (HttpRequestException ex)
                    {
                        log.Result = Trunc($"failed: {ex.Message}", 300);
                    }
                }

                db.WebstoreOutboundLogs.Add(log);
                await db.SaveChangesAsync(ct);
                pushed++;
            }
            return pushed;
        }

        /// <summary>WP6.5 Plutus→Woo half: recently-created catalogue items that aren't on the
        /// webstore become DRAFT products (never published by Plutus). Journal-idempotent: an item
        /// already journaled as draft-product is never re-pushed.</summary>
        public static async Task<int> PushNewItemDraftsAsync(
            MySqlDbContext db, WooRestClient client, WebStoreDetails ws, TimeSpan lookback, CancellationToken ct = default)
        {
            if (ws.OutboundMode is not ("dry-run" or "live")) return 0;
            var cutoff = DateTime.UtcNow - lookback;

            var recent = await db.Items.IgnoreQueryFilters().AsNoTracking()
                // FE5.4: never push a binned item to the web — the Bin has to mean "gone from
                // every selling surface", not just the ones that share ItemParameters.
                .Where(i => i.CreatedAt >= cutoff && i.BinnedAtUtc == null)
                .Select(i => new { i.IdOne, i.Name, i.Price })
                .Take(100).ToListAsync(ct);
            if (recent.Count == 0) return 0;

            var pushed = 0;
            foreach (var item in recent)
            {
                if (ct.IsCancellationRequested) break;
                var mode = await db.WebStores.IgnoreQueryFilters().AsNoTracking()
                    .Where(w => w.Id == ws.Id).Select(w => w.OutboundMode).FirstAsync(ct);
                if (mode is not ("dry-run" or "live")) break;

                // Skip anything already on the web, or already journaled (either mode).
                if (await db.WebstoreProducts.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(p => p.WebStoreId == ws.Id && p.Sku == item.IdOne, ct)) continue;
                if (await db.WebstoreOutboundLogs.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(l => l.WebStoreId == ws.Id && l.Kind == "draft-product" && l.ItemIdOne == item.IdOne, ct)) continue;

                var pricePence = (long)Math.Round(item.Price * 100m, MidpointRounding.AwayFromZero);
                var log = new WebstoreOutboundLog
                {
                    TenantId = ws.TenantId, WebStoreId = ws.Id, Kind = "draft-product", ItemIdOne = item.IdOne,
                    Lane = "draft-scan", FromValue = null, ToValue = Trunc($"{item.Name} @ {pricePence}p (draft)", 300),
                    Mode = mode, Result = "logged", CreatedAtUtc = DateTime.UtcNow,
                };

                if (mode == "live")
                {
                    try
                    {
                        var wooId = await client.CreateDraftProductAsync(item.IdOne, item.Name, pricePence, ct);
                        log.WooProductId = wooId;
                        log.Result = "sent";
                        log.SentAtUtc = DateTime.UtcNow;
                        db.WebstoreProducts.Add(new WebstoreProduct
                        {
                            Id = Uuid7.New(), TenantId = ws.TenantId, WebStoreId = ws.Id, WooProductId = wooId,
                            Sku = item.IdOne, Name = item.Name, PricePence = pricePence, StockQuantity = 0,
                            StockStatus = "outofstock", Status = "draft", LastSeenUtc = DateTime.UtcNow,
                        });
                    }
                    catch (HttpRequestException ex)
                    {
                        log.Result = Trunc($"failed: {ex.Message}", 300);
                    }
                }

                db.WebstoreOutboundLogs.Add(log);
                await db.SaveChangesAsync(ct);
                pushed++;
            }
            return pushed;
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }

    /// <summary>
    /// WP6.3 FAST lane: consumes <see cref="SaleRecorded"/> from the outbox — the moment a sale
    /// lands (any channel: till, web-POS, webstore), the affected items' stock is pushed to every
    /// outbound-enabled webstore. Outbox dispatch latency is seconds, meeting the p95 ≤ 60 s
    /// target. Idempotent under redelivery: a repeat push sees cache == target and no-ops.
    /// </summary>
    public sealed class WebstoreStockOutboundConsumer : IEventConsumer
    {
        public const string ConsumerName = "webstore-stock-outbound";
        private readonly MySqlDbContext _db;
        private readonly IWebstoreSecretProvider _secrets;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConnectorHealth? _connectorHealth;

        public WebstoreStockOutboundConsumer(MySqlDbContext db, IWebstoreSecretProvider secrets, IHttpClientFactory httpFactory, IConnectorHealth? connectorHealth = null)
        {
            _db = db;
            _secrets = secrets;
            _httpFactory = httpFactory;
            _connectorHealth = connectorHealth;
        }

        public string Name => ConsumerName;

        public async Task HandleAsync(DomainEvent e, CancellationToken ct)
        {
            if (e is not SaleRecorded recorded) return;

            var stores = await _db.WebStores.IgnoreQueryFilters().AsNoTracking()
                .Where(w => w.TenantId == recorded.TenantId && w.Enabled && w.OutboundMode != "off")
                .ToListAsync(ct);
            if (stores.Count == 0) return;

            var itemIdOnes = await _db.SaleLines.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.SaleId == recorded.SaleId && l.ItemIdOne != null)
                .Select(l => l.ItemIdOne!).Distinct().ToListAsync(ct);
            if (itemIdOnes.Count == 0) return;

            foreach (var ws in stores)
            {
                var creds = _secrets.GetRestCredentials(ws.Id);
                if (creds is null || string.IsNullOrWhiteSpace(ws.Url)) continue;
                var client = new WooRestClient(_httpFactory.CreateClient(nameof(WebstoreReconciler)), ws.Url!, creds);
                await WebstoreOutbound.PushStockAsync(_db, client, ws, itemIdOnes, lane: "fast", ct);
                if (_connectorHealth != null) // WP17.1 record outbound activity for connector health
                    await _connectorHealth.RecordAsync(ConnectorRegistry.Woo, ws.TenantId, ConnectorActivity.Outbound, true, null, ct);
            }
        }
    }
}
