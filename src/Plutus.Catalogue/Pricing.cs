#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

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

            // evolve-in-place baseline: the legacy catalogue price
            var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.IdOne == itemIdOne);
            if (item == null && central == null && storePrice == null) return null;

            // ⚠ The PRECEDENCE now lives in SharedKernel.PriceResolution, because a till trading
            // offline has to answer this same question at the sale's instant and must get the same
            // answer. Two implementations would be two tills quoting different prices for the same
            // barcode on the same day — which a customer notices before anybody else does.
            var resolved = PriceResolution.Resolve(
                (PriceOwner)(byte)policy,
                central == null ? null : new[] { ToPoint(central) },
                storePrice == null ? null : new[] { ToPoint(storePrice) },
                item == null ? null : Pence.FromDecimal(item.Price),
                item == null ? null : Pence.FromDecimal(item.ExPrice),
                at);

            return resolved == null
                ? null
                : new EffectivePrice(itemIdOne, resolved.PricePence, resolved.ExPricePence, resolved.Source, policy);
        }

        /// <summary>
        /// Move the item's sync cursor so a price change REACHES A TILL.
        ///
        /// ⚠ WITHOUT THIS, PRICING IS INVISIBLE TO EVERY TILL. The catalogue feed pages by
        /// <c>Item.ModifiedAt</c>, and prices live in their own tables — so writing a price changes
        /// nothing the feed can see, and tills go on charging the old price for ever with nothing
        /// anywhere reporting it. The failure is silent, it is about money, and it would be found by
        /// a customer.
        ///
        /// ⚠ Every price write path must call this. There are exactly three (central, override,
        /// force-reset) and they are all in <c>PricesController</c>;
        /// <c>PriceWritesReachTheTillFeed</c> asserts each one moves the cursor, so a fourth added
        /// later fails a test rather than a shop.
        ///
        /// Deliberately does NOT call SaveChanges — the caller batches it with the price row, so the
        /// two either land together or not at all.
        /// </summary>
        public async Task TouchItemForSyncAsync(string itemIdOne)
        {
            var item = await _db.Items.FirstOrDefaultAsync(i => i.IdOne == itemIdOne);
            if (item == null) return;

            // RepositoryContext.SaveMethods stamps ModifiedAt for any IAuditable it sees as
            // Modified. Marking the entity is enough; the value comes from the one choke point
            // every write already passes through.
            _db.Entry(item).State = EntityState.Modified;
        }

        private static PricePoint ToPoint(PriceListEntry e) =>
            new(e.PricePence, e.ExPricePence, e.EffectiveFromUtc, e.CreatedAtUtc);

        private static PricePoint ToPoint(PriceOverride o) =>
            new(o.PricePence, o.ExPricePence, o.EffectiveFromUtc, o.CreatedAtUtc);

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
