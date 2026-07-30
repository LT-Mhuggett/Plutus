using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Customers
{
    /// <summary>
    /// FE7: the catalogue row an activation is rung through.
    ///
    /// ⚠ WHY there has to be one. Selling a gift card must take money at the till, in the drawer, on
    /// the receipt, in the cash-up — i.e. it must be a real sale, not a side-channel. The sales
    /// pipeline requires every line to reference an item: the legacy projection writes a
    /// <c>Transaction</c> row whose (ItemIdOne, ItemIdTwo) is a FOREIGN KEY to Items, so a synthetic
    /// item id would break the bridge on the first card sold.
    ///
    /// ⚠ WHY its tax band is the zero/exempt one. For a MULTI-purpose voucher tenant, activation is
    /// not a VAT-able supply — VAT falls out of the goods at their own bands when the card is
    /// redeemed — so zero is the correct band. For a SINGLE-purpose tenant (VAT due at the card
    /// sale), the till prices the LINE at the standard rate (per the tenant's declared
    /// GiftCardVatTreatment, carried on the lookup response); v1 reporting reads the line's
    /// VatRateBp, not this catalogue band, so the item itself stays on zero either way and the
    /// declared treatment is what decides the money.
    ///
    /// The item is stock-untracked (a card is not inventory) and lives in its own category so gift
    /// cards never inflate a product category's sales. Provisioning is idempotent and runs at startup.
    /// </summary>
    public static class GiftCardSaleItem
    {
        /// <summary>The catalogue barcode/natural key of the activation item. Also known to the till,
        /// which builds the activation line locally (the id is deterministic from it).</summary>
        public const string ItemIdOne = "GIFT-CARD";
        public const string ItemName = "Gift card";
        public const string CategoryName = "Gift cards";

        /// <summary>
        /// Ensures the item (and its category) exist for every business in the database. Idempotent.
        /// Returns the number of items created.
        ///
        /// ⚠ Sets <c>db.CurrentUser</c> BEFORE any early return — the context throws
        /// "CurrentUser not defined!" on save otherwise, and a startup pass has no signed-in user.
        /// (Learned the hard way on the FE1 deploy.)
        /// </summary>
        public static async Task<int> EnsureAsync(MySqlDbContext db, CancellationToken ct = default)
        {
            db.CurrentUser = "giftcard-provision";

            // Cross-tenant: this runs before any request, so there is no ambient tenant to filter by.
            // TenantId is a SHADOW property on the legacy entities, so it is read via EF.Property.
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
                if (taxId == null) continue;   // no tax bands yet — a half-seeded business; try next start

                var catId = await EnsureCategoryAsync(db, business.Id, business.TenantId, ct);

                var item = new Item
                {
                    IdOne = ItemIdOne,
                    IdTwo = business.Id,
                    Name = ItemName,
                    Brand = "-",
                    // Desc is [Required] and an empty string fails validation on save (same trap as
                    // Category.Description) — say something useful instead.
                    Desc = "Gift card / voucher. Sold at the value loaded onto the card; no VAT on the "
                         + "sale (VAT applies to the goods it is later spent on).",
                    Cost = 0m,
                    // Price 0: a card has no list price — the till sets the line price to the amount
                    // being loaded, which is exactly the money taken.
                    ExPrice = 0m,
                    Price = 0m,
                    StockUntracked = true,   // a card is not inventory
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

        /// <summary>The business's zero-VAT band (Tax.Rate is a MULTIPLIER: 1.0 = no VAT, 1.2 = 20%).
        /// Prefers an exact 1.0; falls back to the lowest band so a tenant with an odd setup still
        /// gets the least-wrong answer rather than a 20% card.</summary>
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
                Description = "Gift cards and vouchers — money taken as a liability, not product sales.",
            };
            db.Category.Add(cat);
            db.Entry(cat).Property("TenantId").CurrentValue = tenantId;
            return cat.IdOne;
        }
    }
}
