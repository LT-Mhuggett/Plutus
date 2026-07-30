using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>
    /// WP6.2 SKU⇔catalogue resolution over the live catalogue. A Woo SKU is the item's barcode
    /// (<c>Items.IdOne</c>); when it exists for the tenant, the ItemId is the SAME deterministic
    /// GUID the web POS derives (<see cref="DeterministicGuid.ForItem"/>), so an item has one
    /// identity across till, web-POS, and webstore channels. Unknown SKU → null → the order routes
    /// to the WP6.2 review queue. Reads honour the DbContext's tenant query filter.
    /// </summary>
    public sealed class CatalogueSkuResolver : IWebstoreSkuResolver
    {
        private readonly MySqlDbContext _db;
        public CatalogueSkuResolver(MySqlDbContext db) => _db = db;

        public Guid? Resolve(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return null;
            var s = sku.Trim();
            // Project only the business key — cheap, and the item's IdTwo is the businessId the
            // deterministic id is namespaced by.
            // FE5.4: a binned item no longer resolves, so a webstore order for it routes to the
            // review queue (unknown SKU) instead of silently selling something withdrawn.
            var hit = _db.Items.AsNoTracking()
                .Where(i => i.IdOne == s && i.BinnedAtUtc == null)
                .Select(i => new { i.IdTwo })
                .FirstOrDefault();
            return hit is null ? (Guid?)null : DeterministicGuid.ForItem(hit.IdTwo, s);
        }
    }
}
