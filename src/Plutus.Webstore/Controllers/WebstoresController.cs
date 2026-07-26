using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore.Controllers
{
    public sealed record BindBody(string ItemIdOne);
    public sealed record CreateItemBody(string? Name, long? PricePence);

    /// <summary>
    /// Phase 6 portal/till surface: connections list, the WP6.2 SKU review queue (bind / ignore /
    /// WP6.5 create-as-item) + parked-order retry, the WP6.4 product cache (catalogue view,
    /// manual refresh, alignment report), and the WP6.2 pick-from-floor notifications the till
    /// polls. All reads/writes run under the caller's ambient tenant (portal JWT / till token);
    /// the entitlement gate (`woo-connector`) is checked per action.
    /// </summary>
    [ApiController]
    [Route("api/v1/webstores")]
    public sealed class WebstoresController : ControllerBase
    {
        private static readonly Dictionary<Guid, DateTime> ManualRefreshAt = new();   // rate-limit memory

        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly IEntitlementService _entitlements;
        private readonly WebstoreWebhookPipelineFactory _pipelines;
        private readonly IWebstoreSecretProvider _secrets;
        private readonly IHttpClientFactory _httpFactory;
        private readonly WebstoreOptions _options;

        public WebstoresController(
            MySqlDbContext db, ITenantContext tenant, IEntitlementService entitlements,
            WebstoreWebhookPipelineFactory pipelines, IWebstoreSecretProvider secrets,
            IHttpClientFactory httpFactory, WebstoreOptions options)
        {
            _db = db; _tenant = tenant; _entitlements = entitlements;
            _pipelines = pipelines; _secrets = secrets; _httpFactory = httpFactory; _options = options;
        }

        private Guid Actor => Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
        private string ActorName => User?.Identity?.Name ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

        private async Task<bool> EntitledAsync(CancellationToken ct) =>
            await _entitlements.IsEnabledAsync(_tenant.TenantId, Entitlements.WooConnector, ct);

        // ---- connections ----

        [HttpGet]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            if (!await EntitledAsync(ct)) return StatusCode(403, new { detail = "woo-connector is not enabled." });
            var rows = await _db.WebStores.AsNoTracking().OrderBy(w => w.Name).Select(w => new
            {
                w.Id, w.Name, w.Url, w.Provider, w.StoreId, w.Enabled, w.OversellBuffer,
                w.OrdersCursorUtc, w.ProductsCursorUtc, w.LastFullProductSweepUtc,
                pendingSkus = _db.WebstoreSkuMaps.Count(m => m.WebStoreId == w.Id && m.Status == "Pending"),
            }).ToListAsync(ct);
            return Ok(rows);
        }

        // ---- WP6.2 SKU review queue ----

        [HttpGet("{id:guid}/skumap")]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> SkuMap(Guid id, [FromQuery] string? status, CancellationToken ct)
        {
            var q = _db.WebstoreSkuMaps.AsNoTracking().Where(m => m.WebStoreId == id);
            if (!string.IsNullOrEmpty(status)) q = q.Where(m => m.Status == status);
            var rows = await q.OrderByDescending(m => m.UpdatedAtUtc).Take(500).ToListAsync(ct);
            // Pair each SKU with what the webstore says it is (from the WP6.4 cache) to make
            // bind/create decisions one-glance.
            var skus = rows.Select(r => r.Sku).ToList();
            var cache = await _db.WebstoreProducts.AsNoTracking()
                .Where(p => p.WebStoreId == id && p.Sku != null && skus.Contains(p.Sku))
                .ToDictionaryAsync(p => p.Sku!, p => new { p.Name, p.PricePence, p.Status }, ct);
            return Ok(rows.Select(r => new
            {
                r.Id, r.Sku, r.Status, r.BoundItemIdOne, r.SeenCount, r.FirstSeenWooOrderId, r.FirstSeenUtc, r.UpdatedAtUtc,
                web = cache.TryGetValue(r.Sku, out var w) ? w : null,
            }));
        }

        [HttpPost("{id:guid}/skumap/{mapId:guid}/bind")]
        [Authorize(Policy = "perm:portal.stock.adjust")]
        public async Task<IActionResult> Bind(Guid id, Guid mapId, [FromBody] BindBody body, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(body?.ItemIdOne)) return BadRequest(new { detail = "itemIdOne is required." });
            var itemIdOne = body.ItemIdOne.Trim();
            if (!await _db.Items.AsNoTracking().AnyAsync(i => i.IdOne == itemIdOne, ct))
                return NotFound(new { detail = $"No catalogue item with barcode '{itemIdOne}'." });

            var row = await _db.WebstoreSkuMaps.FirstOrDefaultAsync(m => m.Id == mapId && m.WebStoreId == id, ct);
            if (row is null) return NotFound();
            row.Status = "Bound";
            row.BoundItemIdOne = itemIdOne;
            row.UpdatedAtUtc = DateTime.UtcNow;
            _db.Audit(_tenant.TenantId, Actor, "webstore.skumap.bind", nameof(WebstoreSkuMap), row.Id.ToString(),
                new { row.Sku, itemIdOne });
            await _db.SaveChangesAsync(ct);
            return Ok(new { status = "bound", row.Sku, itemIdOne });
        }

        [HttpPost("{id:guid}/skumap/{mapId:guid}/ignore")]
        [Authorize(Policy = "perm:portal.stock.adjust")]
        public async Task<IActionResult> Ignore(Guid id, Guid mapId, CancellationToken ct)
        {
            var row = await _db.WebstoreSkuMaps.FirstOrDefaultAsync(m => m.Id == mapId && m.WebStoreId == id, ct);
            if (row is null) return NotFound();
            row.Status = "Ignored";
            row.UpdatedAtUtc = DateTime.UtcNow;
            _db.Audit(_tenant.TenantId, Actor, "webstore.skumap.ignore", nameof(WebstoreSkuMap), row.Id.ToString(), new { row.Sku });
            await _db.SaveChangesAsync(ct);
            return Ok(new { status = "ignored", row.Sku });
        }

        /// <summary>WP6.5 (Woo→Plutus half): one click creates the till item from the webstore's
        /// own data (name/price from the product cache; overridable), then binds the SKU. Reviewed,
        /// never silent — a typo'd web SKU can't mint a phantom till item.</summary>
        [HttpPost("{id:guid}/skumap/{mapId:guid}/create-item")]
        [Authorize(Policy = "perm:portal.stock.adjust")]
        public async Task<IActionResult> CreateItem(Guid id, Guid mapId, [FromBody] CreateItemBody? body, CancellationToken ct)
        {
            var row = await _db.WebstoreSkuMaps.FirstOrDefaultAsync(m => m.Id == mapId && m.WebStoreId == id, ct);
            if (row is null) return NotFound();
            if (row.Sku.Length > 20) return BadRequest(new { detail = "SKU exceeds the catalogue barcode length (20) — bind it to an existing item instead." });
            if (await _db.Items.AsNoTracking().AnyAsync(i => i.IdOne == row.Sku, ct))
                return Conflict(new { detail = "An item with this barcode already exists — use bind." });

            var web = await _db.WebstoreProducts.AsNoTracking()
                .FirstOrDefaultAsync(p => p.WebStoreId == id && p.Sku == row.Sku, ct);
            var name = body?.Name ?? web?.Name;
            var pricePence = body?.PricePence ?? web?.PricePence ?? 0;
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { detail = "No name available (not in the product cache) — supply one." });

            // Tenant defaults for the legacy item's required relations.
            var businessId = await _db.Business.AsNoTracking().Select(b => b.Id).FirstAsync(ct);
            var taxId = await _db.Items.AsNoTracking().GroupBy(i => i.TaxId)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync(ct);
            var catId = await _db.Items.AsNoTracking().GroupBy(i => i.CatId)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync(ct);

            var price = pricePence / 100m;
            _db.Items.Add(new Item
            {
                IdOne = row.Sku, IdTwo = businessId, Name = name.Trim(), Brand = "-", Desc = "",
                Cost = 0, ExPrice = price, Price = price, TaxId = taxId, CatId = catId,
            });
            row.Status = "Bound";
            row.BoundItemIdOne = row.Sku;
            row.UpdatedAtUtc = DateTime.UtcNow;
            _db.Audit(_tenant.TenantId, Actor, "webstore.skumap.create-item", nameof(Item), row.Sku,
                new { name, pricePence });
            await _db.SaveChangesAsync(ct);
            return Ok(new { status = "created", barcode = row.Sku, name, pricePence });
        }

        /// <summary>Re-process this connection's parked orders (needs-mapping / quarantined) after
        /// SKUs were bound or items created. Resolved ones record + stamp ResolvedAtUtc; the rest stay.</summary>
        [HttpPost("{id:guid}/retry")]
        [Authorize(Policy = "perm:portal.stock.adjust")]
        public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
        {
            var ws = await _db.WebStores.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
            if (ws is null) return NotFound();
            var ctx = new WebstoreConnectionContext { WebStoreId = ws.Id, TenantId = ws.TenantId, TillId = ws.TillId, DeviceId = ws.DeviceId };

            var parked = await _db.SaleQuarantine
                .Where(q => q.ResolvedAtUtc == null).OrderBy(q => q.ReceivedAtUtc).Take(200).ToListAsync(ct);

            int recorded = 0, still = 0, notOurs = 0;
            using var pipeline = _pipelines.Create(ctx);
            foreach (var q in parked)
            {
                WooOrder? order;
                try { order = JsonSerializer.Deserialize<WooOrder>(q.PayloadJson, WooJson.Options); }
                catch (JsonException) { notOurs++; continue; }
                // Only rows that belong to THIS connection (their saleId derives from our device).
                if (order is null || WooOrderMapper.SaleIdFor(ctx.DeviceId, order.Id) != q.SaleId) { notOurs++; continue; }

                var r = await pipeline.Processor.RouteOrderAsync(order, ctx, pipeline.Resolver, ct);
                if (r.Status is WebstoreInboundStatus.Recorded or WebstoreInboundStatus.Duplicate)
                {
                    if (r.Status == WebstoreInboundStatus.Recorded && r.Order is not null)
                        await WebstoreNotifications.CreateForRecordedAsync(pipeline.Db, ctx, r.Order, ws.StoreId, ct);
                    var live = await pipeline.Db.SaleQuarantine.FirstAsync(x => x.Id == q.Id, ct);
                    live.ResolvedAtUtc = DateTime.UtcNow;
                    await pipeline.Db.SaveChangesAsync(ct);
                    recorded++;
                }
                else still++;
            }
            _db.Audit(_tenant.TenantId, Actor, "webstore.retry", nameof(WebStoreDetails), id.ToString(),
                new { recorded, still, notOurs });
            await _db.SaveChangesAsync(ct);
            return Ok(new { recorded, still, notOurs });
        }

        // ---- WP6.4 catalogue view + alignment ----

        [HttpGet("{id:guid}/products")]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> Products(
            Guid id, [FromQuery] string? status, [FromQuery] string? linked,
            [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            var q = _db.WebstoreProducts.AsNoTracking().Where(p => p.WebStoreId == id);
            if (!string.IsNullOrEmpty(status)) q = q.Where(p => p.Status == status);

            var plutusSkus = _db.Items.AsNoTracking().Select(i => i.IdOne);
            if (linked == "yes") q = q.Where(p => p.Sku != null && plutusSkus.Contains(p.Sku));
            if (linked == "no") q = q.Where(p => p.Sku == null || !plutusSkus.Contains(p.Sku));

            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(p => p.Name).Skip(skip).Take(take)
                .Select(p => new
                {
                    p.WooProductId, p.Sku, p.Name, p.PricePence, p.RegularPricePence,
                    p.StockQuantity, p.StockStatus, p.Status, p.Permalink, p.WooModifiedUtc,
                    linkedItem = p.Sku != null && plutusSkus.Contains(p.Sku),
                }).ToListAsync(ct);
            var lastRefreshed = await _db.WebstoreProducts.AsNoTracking()
                .Where(p => p.WebStoreId == id).MaxAsync(p => (DateTime?)p.LastSeenUtc, ct);
            return Ok(new { total, skip, take, lastRefreshed, rows });
        }

        /// <summary>Manual "Refresh now" — incremental sweep only, min 5 minutes apart per
        /// connection (the just-edited-it-in-wp-admin case; counted in the request budget).</summary>
        [HttpPost("{id:guid}/products/refresh")]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> RefreshProducts(Guid id, CancellationToken ct)
        {
            var ws = await _db.WebStores.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
            if (ws is null || string.IsNullOrWhiteSpace(ws.Url)) return NotFound();
            lock (ManualRefreshAt)
            {
                if (ManualRefreshAt.TryGetValue(id, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(5))
                    return StatusCode(429, new { detail = "Refresh ran less than 5 minutes ago — the cache is at most that stale." });
                ManualRefreshAt[id] = DateTime.UtcNow;
            }
            var creds = _secrets.GetRestCredentials(id);
            if (creds is null) return StatusCode(500, new { detail = "No REST credentials configured for this webstore." });

            var ctx = new WebstoreConnectionContext { WebStoreId = ws.Id, TenantId = ws.TenantId, TillId = ws.TillId, DeviceId = ws.DeviceId };
            using var pipeline = _pipelines.Create(ctx);
            var client = new WooRestClient(_httpFactory.CreateClient(nameof(WebstoreReconciler)), ws.Url!, creds);
            var since = ws.ProductsCursorUtc ?? DateTime.UtcNow.AddHours(-_options.InitialLookbackHours);
            var seen = 0;
            for (var page = 1; page <= _options.MaxPagesPerCycle; page++)
            {
                var (products, totalPages) = await client.GetProductsAsync(since.AddMinutes(-5), page, _options.PerPage, ct);
                foreach (var p in products)
                {
                    seen++;
                    var row = await pipeline.Db.WebstoreProducts
                        .FirstOrDefaultAsync(x => x.WebStoreId == id && x.WooProductId == p.Id, ct);
                    if (row is null)
                    {
                        row = new WebstoreProduct { Id = Uuid7.New(), TenantId = ws.TenantId, WebStoreId = id, WooProductId = p.Id };
                        pipeline.Db.WebstoreProducts.Add(row);
                    }
                    row.Sku = string.IsNullOrWhiteSpace(p.Sku) ? null : p.Sku!.Trim();
                    row.Name = p.Name ?? $"product {p.Id}";
                    row.PricePence = 0;
                    try { row.PricePence = WebstoreMoneyParse(p.Price); } catch (FormatException) { }
                    row.StockQuantity = p.StockQuantity;
                    row.StockStatus = p.StockStatus;
                    row.Status = p.Status ?? "publish";
                    row.Permalink = p.Permalink;
                    if (p.DateModifiedGmt is { } m) row.WooModifiedUtc = DateTime.SpecifyKind(DateTime.Parse(m), DateTimeKind.Utc);
                    row.LastSeenUtc = DateTime.UtcNow;
                }
                await pipeline.Db.SaveChangesAsync(ct);
                if (page >= totalPages || products.Count == 0) break;
            }
            return Ok(new { refreshed = seen, requests = client.RequestCount });
        }

        private static long WebstoreMoneyParse(string? s) =>
            string.IsNullOrWhiteSpace(s) ? 0 :
            (long)Math.Round(decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture) * 100m, MidpointRounding.AwayFromZero);

        /// <summary>WP6.4 alignment: webstore cache vs Plutus catalogue by SKU⇔barcode. Price
        /// differences are DISPLAY (web ≠ shelf is legitimate), not errors.</summary>
        [HttpGet("{id:guid}/alignment")]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> Alignment(Guid id, CancellationToken ct)
        {
            var web = await _db.WebstoreProducts.AsNoTracking()
                .Where(p => p.WebStoreId == id && p.Status != "deleted")
                .Select(p => new { p.Sku, p.Name, p.PricePence, p.Status, p.StockStatus })
                .ToListAsync(ct);
            var webSkus = web.Where(w => w.Sku != null).Select(w => w.Sku!).ToHashSet();
            var items = await _db.Items.AsNoTracking()
                .Where(i => webSkus.Contains(i.IdOne))
                .Select(i => new { i.IdOne, i.Name, i.Price })
                .ToDictionaryAsync(i => i.IdOne, ct);

            var matched = web.Where(w => w.Sku != null && items.ContainsKey(w.Sku!)).Select(w =>
            {
                var it = items[w.Sku!];
                var tillPence = (long)Math.Round(it.Price * 100m, MidpointRounding.AwayFromZero);
                return new
                {
                    sku = w.Sku, webName = w.Name, tillName = it.Name,
                    nameDrift = !string.Equals(w.Name?.Trim(), it.Name?.Trim(), StringComparison.OrdinalIgnoreCase),
                    webPricePence = w.PricePence, tillPricePence = tillPence,
                    priceDiffPence = w.PricePence - tillPence, w.Status, w.StockStatus,
                };
            }).ToList();

            var webOnly = web.Where(w => w.Sku == null || !items.ContainsKey(w.Sku!))
                .Select(w => new { sku = w.Sku, w.Name, w.PricePence, w.Status }).ToList();
            var tillOnlyCount = await _db.Items.AsNoTracking().CountAsync(ct) - items.Count;

            return Ok(new
            {
                matched = matched.Count,
                nameDrift = matched.Where(m => m.nameDrift).Take(200),
                priceDiffers = matched.Where(m => m.priceDiffPence != 0).OrderByDescending(m => Math.Abs(m.priceDiffPence)).Take(200),
                webOnly,
                tillOnlyCount,
            });
        }

        // ---- WP6.3 outbound: journal view + the mode switch (off | dry-run | live) ----

        [HttpGet("{id:guid}/outbound-log")]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> OutboundLog(Guid id, [FromQuery] int take = 100, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 500);
            var mode = await _db.WebStores.AsNoTracking().Where(w => w.Id == id).Select(w => w.OutboundMode).FirstOrDefaultAsync(ct);
            if (mode is null) return NotFound();
            var rows = await _db.WebstoreOutboundLogs.AsNoTracking()
                .Where(l => l.WebStoreId == id).OrderByDescending(l => l.Id).Take(take)
                .Select(l => new { l.Id, l.Kind, l.ItemIdOne, l.WooProductId, l.FromValue, l.ToValue, l.Mode, l.Result, l.Lane, l.CreatedAtUtc, l.SentAtUtc })
                .ToListAsync(ct);
            var pendingDry = await _db.WebstoreOutboundLogs.AsNoTracking()
                .CountAsync(l => l.WebStoreId == id && l.Mode == "dry-run", ct);
            return Ok(new { mode, pendingDry, rows });
        }

        public sealed record OutboundModeBody(string Mode);

        /// <summary>The WP6.3 gates live here: off (kill switch) ↔ dry-run freely; LIVE is refused
        /// unless a dry-run journal exists to have been reviewed (the plan's dry-run-first gate) —
        /// and going live is audited with the actor.</summary>
        [HttpPut("{id:guid}/outbound-mode")]
        [Authorize(Policy = "perm:portal.company.manage")]
        public async Task<IActionResult> SetOutboundMode(Guid id, [FromBody] OutboundModeBody body, CancellationToken ct)
        {
            var mode = body?.Mode?.Trim().ToLowerInvariant();
            if (mode is not ("off" or "dry-run" or "live"))
                return BadRequest(new { detail = "mode must be off | dry-run | live." });
            var ws = await _db.WebStores.FirstOrDefaultAsync(w => w.Id == id, ct);
            if (ws is null) return NotFound();

            if (mode == "live")
            {
                var dryRows = await _db.WebstoreOutboundLogs.AsNoTracking().CountAsync(l => l.WebStoreId == id && l.Mode == "dry-run", ct);
                if (dryRows == 0)
                    return UnprocessableEntity(new { detail = "Refusing to go live with no dry-run journal to review — run dry-run first (plan WP6.3 gate)." });
            }
            var from = ws.OutboundMode;
            ws.OutboundMode = mode;
            _db.Audit(_tenant.TenantId, Actor, "webstore.outbound-mode", nameof(WebStoreDetails), id.ToString(), new { from, to = mode });
            await _db.SaveChangesAsync(ct);
            return Ok(new { mode });
        }

        /// <summary>WP6.4 CSV export of the alignment report (matched rows + web-only).</summary>
        [HttpGet("{id:guid}/alignment.csv")]
        [Authorize(Policy = "perm:portal.reports.view")]
        public async Task<IActionResult> AlignmentCsv(Guid id, CancellationToken ct)
        {
            var web = await _db.WebstoreProducts.AsNoTracking()
                .Where(p => p.WebStoreId == id && p.Status != "deleted")
                .Select(p => new { p.Sku, p.Name, p.PricePence, p.Status })
                .ToListAsync(ct);
            var webSkus = web.Where(w => w.Sku != null).Select(w => w.Sku!).ToHashSet();
            var items = await _db.Items.AsNoTracking().Where(i => webSkus.Contains(i.IdOne))
                .Select(i => new { i.IdOne, i.Name, i.Price }).ToDictionaryAsync(i => i.IdOne, ct);

            var sb = new System.Text.StringBuilder("sku,web_name,till_name,web_price,till_price,price_diff,status\n");
            foreach (var w in web.OrderBy(x => x.Name))
            {
                var it = w.Sku != null && items.TryGetValue(w.Sku, out var v) ? v : null;
                var tillPence = it is null ? (long?)null : (long)Math.Round(it.Price * 100m, MidpointRounding.AwayFromZero);
                sb.Append(Csv(w.Sku ?? "")).Append(',').Append(Csv(w.Name)).Append(',').Append(Csv(it?.Name ?? ""))
                  .Append(',').Append((w.PricePence / 100m).ToString("0.00"))
                  .Append(',').Append(tillPence is { } t ? (t / 100m).ToString("0.00") : "")
                  .Append(',').Append(tillPence is { } t2 ? ((w.PricePence - t2) / 100m).ToString("0.00") : "")
                  .Append(',').Append(w.Status).Append('\n');
            }
            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "webstore-alignment.csv");

            static string Csv(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

        // ---- WP6.2 pick-from-floor notifications (till-facing) ----

        [HttpGet("/api/v1/notifications")]
        [Authorize(Policy = "perm:sales.ingest")]
        public async Task<IActionResult> Notifications([FromQuery] bool unackedOnly = true, CancellationToken ct = default)
        {
            var q = _db.WebstoreNotifications.AsNoTracking().OrderByDescending(n => n.CreatedAtUtc).AsQueryable();
            if (unackedOnly) q = q.Where(n => n.AckedAtUtc == null);
            return Ok(await q.Take(50).Select(n => new { n.Id, n.Message, n.WooOrderId, n.StoreId, n.CreatedAtUtc, n.AckedAtUtc }).ToListAsync(ct));
        }

        [HttpPost("/api/v1/notifications/{noteId:guid}/ack")]
        [Authorize(Policy = "perm:sales.ingest")]
        public async Task<IActionResult> Ack(Guid noteId, CancellationToken ct)
        {
            var n = await _db.WebstoreNotifications.FirstOrDefaultAsync(x => x.Id == noteId, ct);
            if (n is null) return NotFound();
            if (n.AckedAtUtc is null)
            {
                n.AckedAtUtc = DateTime.UtcNow;
                n.AckedBy = ActorName;
                _db.Audit(_tenant.TenantId, Actor, "webstore.notification.ack", nameof(WebstoreNotification), n.Id.ToString(),
                    new { n.WooOrderId });
                await _db.SaveChangesAsync(ct);
            }
            return Ok(new { status = "acked" });
        }
    }
}
