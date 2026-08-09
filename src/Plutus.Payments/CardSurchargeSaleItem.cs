using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Payments
{
    /// <summary>
    /// The catalogue row a card surcharge is rung through — same pattern as
    /// <c>GiftCardSaleItem</c>, for the same reason: money at the till must be a real sale line,
    /// not a side-channel. The sale model has no fee field, <c>GrossPence</c> must equal the sum of
    /// the line grosses, and the legacy projection's <c>Transaction</c> rows foreign-key to Items —
    /// so a surcharge with no item behind it either fails the reconcile invariant or breaks the
    /// bridge on the first card payment.
    ///
    /// ⚠ THE ITEM CARRIES NO VAT DECISION. Its catalogue band is the zero band, and that band is
    /// NEVER what taxes the fee: a surcharge is further consideration for the main supply
    /// (<i>Bookit</i> C-607/14 / <i>NEC</i> C-130/15), so its VAT follows the basket it rides on —
    /// the till prices the LINE with <c>SharedKernel.CardSurchargeVat.PairFor</c>, and reporting
    /// reads the line's declared figures, exactly as gift-card activation lines already work. A
    /// catalogue band here that said 20% would be wrong on every zero-rated basket, and one that
    /// meant anything at all would tempt someone to read it.
    ///
    /// Stock-untracked (a fee is not inventory), own category so fees never inflate a product
    /// category's sales. Idempotent, runs at startup.
    /// </summary>
    public static class CardSurchargeSaleItem
    {
        /// <summary>The natural key — owned by <see cref="CardSurchargeVat.ItemIdOne"/> in the
        /// shared kernel, because the tills build the fee line locally against the same string
        /// (the item id is deterministic from it via <c>DeterministicGuid.ForItem</c>).</summary>
        public const string ItemIdOne = CardSurchargeVat.ItemIdOne;
        public const string ItemName = "Card surcharge";
        public const string CategoryName = "Payment fees";

        /// <summary>
        /// Ensures the item (and its category) exist for every business. Idempotent; returns the
        /// number created. ⚠ Sets <c>db.CurrentUser</c> BEFORE any early return — the context throws
        /// "CurrentUser not defined!" on save otherwise (the FE1 lesson).
        /// </summary>
        public static async Task<int> EnsureAsync(MySqlDbContext db, CancellationToken ct = default)
        {
            db.CurrentUser = "surcharge-provision";

            // Cross-tenant startup pass: no ambient tenant, TenantId is a shadow property.
            var businesses = await db.Business.IgnoreQueryFilters().AsNoTracking()
                .Select(b => new { b.Id, TenantId = EF.Property<Guid>(b, "TenantId") }).ToListAsync(ct);
            if (businesses.Count == 0) return 0;

            var created = 0;
            foreach (var business in businesses)
            {
                var exists = await db.Items.IgnoreQueryFilters()
                    .AnyAsync(i => i.IdOne == ItemIdOne && i.IdTwo == business.Id, ct);
                if (exists) continue;

                var taxId = await ZeroRateTaxIdAsync(db, business.Id, ct);
                if (taxId == null) continue;   // half-seeded business; try next start

                var catId = await EnsureCategoryAsync(db, business.Id, business.TenantId, ct);

                var item = new Item
                {
                    IdOne = ItemIdOne,
                    IdTwo = business.Id,
                    Name = ItemName,
                    Brand = "-",
                    // Desc is [Required] — empty fails validation on save.
                    Desc = "Card payment surcharge. The till sets the line price to the fee charged; "
                         + "its VAT follows the goods in the basket, never this item's band.",
                    Cost = 0m,
                    // Price 0: the fee has no list price — the till prices the line from the
                    // tenant's gateway setting and the basket it rides on.
                    ExPrice = 0m,
                    Price = 0m,
                    StockUntracked = true,   // a fee is not inventory
                    TaxId = taxId.Value,
                    CatId = catId,
                };
                db.Items.Add(item);
                db.Entry(item).Property("TenantId").CurrentValue = business.TenantId;
                created++;
            }

            if (created > 0) await db.SaveChangesAsync(ct);
            return created;
        }

        /// <summary>Zero band preferred (Tax.Rate is a MULTIPLIER, 1.0 = no VAT); lowest otherwise.
        /// The band is descriptive here — see the class remarks — so least-wrong beats refusing.</summary>
        private static async Task<int?> ZeroRateTaxIdAsync(MySqlDbContext db, Guid businessId, CancellationToken ct)
        {
            var bands = await db.Taxes.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.IdTwo == businessId)
                .Select(t => new { t.IdOne, t.Rate }).ToListAsync(ct);
            if (bands.Count == 0) return null;
            var zero = bands.Where(b => b.Rate <= 1.0001).OrderBy(b => b.Rate).FirstOrDefault()
                ?? bands.OrderBy(b => b.Rate).First();
            return zero.IdOne;
        }

        private static async Task<Guid> EnsureCategoryAsync(
            MySqlDbContext db, Guid businessId, Guid tenantId, CancellationToken ct)
        {
            var existing = await db.Category.IgnoreQueryFilters()
                .Where(c => c.IdTwo == businessId && c.Name == CategoryName)
                .Select(c => c.IdOne).FirstOrDefaultAsync(ct);
            if (existing != Guid.Empty) return existing;

            var cat = new Category
            {
                IdOne = Uuid7.New(),
                IdTwo = businessId,
                Name = CategoryName,
                Description = "Card surcharges and payment fees — not product sales.",
            };
            db.Category.Add(cat);
            db.Entry(cat).Property("TenantId").CurrentValue = tenantId;
            return cat.IdOne;
        }
    }
}
