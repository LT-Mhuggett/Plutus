using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>
    /// WP6.2 review-queue store over the shared DB. Parks each unknown SKU as one row per
    /// (tenant, webstore, SKU); a re-seen SKU bumps <c>SeenCount</c> rather than duplicating.
    /// Already-resolved rows (Bound/Ignored) are left untouched — a resolved SKU that reappears
    /// isn't re-opened. Writes go through the tenant stamp/guard on SaveChanges.
    /// </summary>
    public sealed class WebstoreSkuMapQueue : IWebstoreSkuMapQueue
    {
        private readonly MySqlDbContext _db;
        public WebstoreSkuMapQueue(MySqlDbContext db) => _db = db;

        public async Task EnqueueAsync(
            WebstoreConnectionContext ctx, long wooOrderId, IReadOnlyList<string> unmatchedSkus, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            foreach (var raw in unmatchedSkus)
            {
                var sku = (raw ?? string.Empty).Trim();
                if (sku.Length == 0) continue;
                if (sku.Length > 64) sku = sku.Substring(0, 64);

                var existing = await _db.WebstoreSkuMaps
                    .FirstOrDefaultAsync(m => m.WebStoreId == ctx.WebStoreId && m.Sku == sku, ct);

                if (existing is null)
                {
                    _db.WebstoreSkuMaps.Add(new WebstoreSkuMap
                    {
                        Id = Uuid7.New(), TenantId = ctx.TenantId, WebStoreId = ctx.WebStoreId, Sku = sku,
                        Status = "Pending", SeenCount = 1, FirstSeenWooOrderId = wooOrderId,
                        FirstSeenUtc = now, UpdatedAtUtc = now,
                    });
                }
                else if (existing.Status == "Pending")
                {
                    existing.SeenCount += 1;
                    existing.UpdatedAtUtc = now;
                }
                // Bound / Ignored rows are intentionally left as-is.
            }
            await _db.SaveChangesAsync(ct);
        }
    }
}
