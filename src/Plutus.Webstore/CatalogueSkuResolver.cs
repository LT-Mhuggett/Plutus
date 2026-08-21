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
                .Select(i => new { i.IdTwo, i.IdOne })
                .FirstOrDefault();

            // ⚠⚠ HASHED FROM THE ITEM'S OWN IdOne, NOT FROM THE SKU STRING WE WERE GIVEN.
            // Behaviour-neutral today — the query matched on `IdOne`, so `s == hit.IdOne` always —
            // and it was `ForItem(hit.IdTwo, s)` until 2026-08-20. Corrected as part of the
            // multi-barcode work (plan D11) because the day this resolver learns about aliases, the
            // old form would mint a DIFFERENT item GUID for the same physical item than every till
            // does, and a webstore sale would land under an id nothing else uses.
            //
            // ⚠ This resolver is still EXACT-IdOne-only: a Woo SKU matching an alias does not
            // resolve, and routes to the review queue as an unknown SKU. Making it alias-aware is
            // deliberately out of scope (plan §8) — `WooOrderMapper` writes the raw SKU onto the
            // sale line, and that would have to be canonicalised first.
            return hit is null ? (Guid?)null : DeterministicGuid.ForItem(hit.IdTwo, hit.IdOne);
        }
    }
}
