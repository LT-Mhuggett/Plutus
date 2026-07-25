#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;

namespace Plutus.Catalogue
{
    public sealed record EffectivePrice(
        string ItemIdOne, long PricePence, long ExPricePence, string Source, PricePolicy Policy);

    /// <summary>
    /// WP5.4 effective-price resolution (architecture §7.4):
    ///   store override (when the policy permits) → company price list → legacy Items price
    /// (the evolve-in-place baseline until a first price-list entry exists). Entries are
    /// effective-dated: the latest entry whose EffectiveFromUtc ≤ <paramref name="at"/> wins —
    /// which is exactly what makes a scheduled Sunday-night reprice activate at the boundary.
    /// </summary>
    public sealed class PricingService
    {
        private readonly MySqlDbContext _db;
        public PricingService(MySqlDbContext db) => _db = db;

        public async Task<PricePolicy> PolicyForAsync(string itemIdOne)
        {
            var row = await _db.ItemPricePolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ItemIdOne == itemIdOne);
            return row?.Policy ?? PricePolicy.Central;
        }

        public async Task<EffectivePrice> ResolveAsync(string itemIdOne, int storeId, DateTime at)
        {
            var policy = await PolicyForAsync(itemIdOne);

            var central = await _db.PriceListEntries.AsNoTracking()
                .Where(p => p.ItemIdOne == itemIdOne && p.EffectiveFromUtc <= at)
                .OrderByDescending(p => p.EffectiveFromUtc).ThenByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync();

            PriceOverride storePrice = null;
            if (policy != PricePolicy.Central)
                storePrice = await _db.PriceOverrides.AsNoTracking()
                    .Where(o => o.ItemIdOne == itemIdOne && o.StoreId == storeId &&
                                o.RevokedAtUtc == null && o.EffectiveFromUtc <= at)
                    .OrderByDescending(o => o.EffectiveFromUtc).ThenByDescending(o => o.CreatedAtUtc)
                    .FirstOrDefaultAsync();

            if (storePrice != null)
                return new EffectivePrice(itemIdOne, storePrice.PricePence, storePrice.ExPricePence,
                    policy == PricePolicy.Local ? "local" : "override", policy);
            if (central != null)
                return new EffectivePrice(itemIdOne, central.PricePence, central.ExPricePence, "central", policy);

            // evolve-in-place baseline: the legacy catalogue price
            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == itemIdOne);
            if (item == null) return null;
            return new EffectivePrice(itemIdOne,
                (long)Math.Round(item.Price * 100), (long)Math.Round(item.ExPrice * 100), "legacy", policy);
        }

        public async Task<List<EffectivePrice>> ResolveManyAsync(IReadOnlyList<string> itemIds, int storeId, DateTime at)
        {
            var results = new List<EffectivePrice>();
            foreach (var id in itemIds.Distinct())
            {
                var price = await ResolveAsync(id, storeId, at);
                if (price != null) results.Add(price);
            }
            return results;
        }
    }
}
