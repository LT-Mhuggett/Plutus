using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Catalogue
{
    /// <summary>
    /// Carrier bags — defined once in the portal, read by every till. Ruling 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"That creates the 5p and 20p bags at the back and that pushes down to the tills… This
    /// would be cleaner than creating a bag at each till."* It replaces a **local device preference** on
    /// both tills (`DefaultBagId` / `prefs.bagBarcode`), which meant a five-till shop configured its bag
    /// five times, the tills could disagree, and a new till sold no bags until somebody remembered. It is
    /// also how a till came to hold `"001"` — a barcode no item has.
    ///
    /// ⚠⚠ **A LIST, NOT A PAIR.** A shop normally sells BOTH a statutory-minimum single-use bag AND a
    /// dearer bag for life, and may add a paper or jute one later — see `SharedKernel.CarrierBags` for
    /// why, and for why no statutory price is hardcoded anywhere (England's minimum moved from 5p to 10p
    /// on 21 May 2021, and the four nations differ).
    ///
    /// ⚠ Bags are REAL catalogue items in their own category, so they sell, report and carry VAT like
    /// anything else — the same shape `GiftCardSaleItem` uses, and no new entity or column. The category
    /// is the marker both tills filter on to keep bags out of the Inventory list.
    /// </summary>
    [Route("api/v1/carrier-bags")]
    [ApiController]
    public sealed class CarrierBagsController : ControllerBase
    {
        private readonly MySqlDbContext _db;
        private readonly ITenantContext _tenant;

        public CarrierBagsController(MySqlDbContext db, ITenantContext tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        private Guid Actor =>
            Guid.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

        /// <summary>
        /// The bags this shop sells, cheapest first — what a till renders as its Bag buttons.
        ///
        /// ⚠ Any authenticated caller, like `payments/gateway/active` and `reports/published`: it is a
        /// short list of item names and prices that every till needs, and gating it behind a portal
        /// permission would stop a till drawing its own buttons.
        ///
        /// ⚠ CHEAPEST FIRST, deliberately. The single-use bag is the one asked for many times a day and
        /// the bag for life occasionally, so the common case sits under the operator's thumb.
        /// </summary>
        [HttpGet("")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            var bags = await BagsAsync();

            return Ok(bags
                .OrderBy(b => b.PricePence)
                .Select(b => new { idOne = b.IdOne, name = b.Name, pricePence = b.PricePence }));
        }

        /// <summary>
        /// Add a bag at this price, or update the name of the one already at it.
        ///
        /// ⚠⚠ PRICE IS THE IDENTITY (`CarrierBags.IdFor`), so this is an UPSERT and a shop cannot end up
        /// with three 10p bags that look identical on a receipt and split one line across three report
        /// rows. Changing a bag's price therefore means adding the new one and withdrawing the old —
        /// which is correct: they are different products, and the old one is on historical receipts.
        ///
        /// ⚠ Audited. "Who put the bag charge up, and when" is the first question after a customer
        /// complains, and the second is what it was before.
        /// </summary>
        [HttpPut("")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Upsert([FromBody] BagBody body)
        {
            if (body is null || body.PricePence <= 0)
                return BadRequest(new { detail = "A carrier bag needs a price above zero." });

            // ⚠ A TYPO CAP, not a policy. No shop charges £5 for a carrier bag, and a slipped digit
            // would charge every customer it until somebody noticed — the same reasoning behind the card
            // surcharge guards.
            if (body.PricePence > 500)
                return BadRequest(new { detail = "That is more than £5.00 for a bag — check the price." });

            var idOne = CarrierBags.IdFor(body.PricePence);
            var name = string.IsNullOrWhiteSpace(body.Name)
                ? CarrierBags.DefaultNameFor(body.PricePence)
                : body.Name.Trim();

            var businesses = await BusinessesAsync();
            if (businesses.Count == 0) return BadRequest(new { detail = "This tenant has no business yet." });

            _db.CurrentUser = Actor.ToString();

            foreach (var business in businesses)
            {
                var existing = await _db.Items
                    .FirstOrDefaultAsync(i => i.IdOne == idOne && i.IdTwo == business.Id);

                if (existing is not null)
                {
                    existing.Name = name;

                    // ⚠ The PRICE is re-asserted from the id, not from the body. They cannot disagree
                    // here, but an item edited directly elsewhere could have drifted — and a bag selling
                    // at 25p under the id `BAG-10` is a receipt nobody can explain.
                    existing.Price = body.PricePence / 100m;
                    existing.ExPrice = ExPriceFor(business, existing.TaxId, body.PricePence);

                    // ⚠ Re-adding a bag the shop previously withdrew must bring it back, not leave it
                    // binned while the portal lists it as on sale.
                    existing.BinnedAtUtc = null;
                    continue;
                }

                var taxId = CarrierBags.StandardRatePreferred(await BandsAsync(business.Id));
                if (taxId is null)
                    return Conflict(new { detail = "This business has no VAT bands set up yet, so a bag cannot be priced." });

                var catId = await EnsureCategoryAsync(business);

                _db.Items.Add(Bag(business, idOne, name, body.PricePence, taxId.Value, catId));
            }

            _db.Audit(_tenant.TenantId, Actor, "catalogue.carrier-bag", nameof(Item), idOne,
                $"name={name} pricePence={body.PricePence}");

            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Stop selling a bag at this price.
        ///
        /// ⚠⚠ IT IS **BINNED**, NOT DELETED. The bag is on historical receipts and in past VAT returns,
        /// and `Item` is the row those lines resolve against — deleting it would orphan them. Binning is
        /// what the rest of the catalogue does to withdraw something from sale, and both tills filter
        /// binned items out already.
        /// </summary>
        [HttpDelete("{pricePence:long}")]
        [Authorize(Policy = "perm:" + PermissionCatalogue.PortalCompanyManage)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Withdraw(long pricePence)
        {
            if (pricePence <= 0) return NoContent();   // ⚠ idempotent: never a bag in the first place

            var idOne = CarrierBags.IdFor(pricePence);
            var rows = await _db.Items.Where(i => i.IdOne == idOne).ToListAsync();
            if (rows.Count == 0) return NoContent();

            _db.CurrentUser = Actor.ToString();
            foreach (var row in rows) row.BinnedAtUtc = DateTime.UtcNow;

            _db.Audit(_tenant.TenantId, Actor, "catalogue.carrier-bag.withdraw", nameof(Item), idOne,
                $"pricePence={pricePence}");

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private sealed record BagRow(string IdOne, string Name, long PricePence);

        /// <summary>⚠ Binned bags are excluded: withdrawn from sale, kept for the receipts they are on.</summary>
        private async Task<List<BagRow>> BagsAsync()
        {
            var prefix = CarrierBags.IdPrefix;

            var rows = await _db.Items.AsNoTracking()
                .Where(i => i.IdOne.StartsWith(prefix) && i.BinnedAtUtc == null)
                .Select(i => new { i.IdOne, i.Name, i.Price })
                .ToListAsync();

            // ⚠ Filtered in memory through the shared rule rather than by a second SQL LIKE: the two
            // would then disagree about what a bag id is the moment either changed.
            return rows
                .Where(r => CarrierBags.IsBagId(r.IdOne))
                .GroupBy(r => r.IdOne)
                .Select(g => new BagRow(g.Key, g.First().Name, CarrierBags.PriceFromId(g.Key)!.Value))
                .ToList();
        }

        private sealed record BusinessRef(Guid Id, Guid TenantId);

        private async Task<List<BusinessRef>> BusinessesAsync() =>
            await _db.Business.AsNoTracking()
                .Select(b => new BusinessRef(b.Id, EF.Property<Guid>(b, "TenantId")))
                .ToListAsync();

        /// <summary>⚠ `Tax.Rate` is a `double`; money is `decimal`. The cast happens once, here.</summary>
        private async Task<List<(int IdOne, decimal Rate)>> BandsAsync(Guid businessId)
        {
            var bands = await _db.Taxes.AsNoTracking()
                .Where(t => t.IdTwo == businessId)
                .Select(t => new { t.IdOne, t.Rate })
                .ToListAsync();

            return bands.Select(b => (b.IdOne, (decimal)b.Rate)).ToList();
        }

        /// <summary>
        /// ⚠ THE EX PRICE IS DERIVED, NEVER TYPED. The server guards
        /// <c>|price − exPrice × rate| ≤ 2p</c> because free-typed ex-prices corrupted 47 live items, and
        /// with them every downstream VAT figure. The rate comes from the band the item carries.
        ///
        /// ⚠⚠ `Tax.Rate` IS A `double` and money is `decimal`, so the cast is explicit and happens ONCE —
        /// the same shape `ItemController` and `ReportsController` use. Dividing money by a double would
        /// silently widen every bag price into binary floating point.
        /// </summary>
        private decimal ExPriceFor(BusinessRef business, int taxId, long pricePence)
        {
            var rate = _db.Taxes.AsNoTracking()
                .Where(t => t.IdTwo == business.Id && t.IdOne == taxId)
                .Select(t => t.Rate)
                .FirstOrDefault();

            var multiplier = rate <= 0 ? 1m : (decimal)rate;
            return Math.Round(pricePence / 100m / multiplier, 2, MidpointRounding.AwayFromZero);
        }

        private async Task<Guid> EnsureCategoryAsync(BusinessRef business)
        {
            var existing = await _db.Category
                .Where(c => c.IdTwo == business.Id && c.Name == CarrierBags.CategoryName)
                .Select(c => c.IdOne)
                .FirstOrDefaultAsync();

            if (existing != Guid.Empty) return existing;

            var cat = new Category
            {
                IdOne = Uuid7.New(),
                IdTwo = business.Id,
                Name = CarrierBags.CategoryName,
                // ⚠ `Description` is [Required] and an empty string fails validation on save — the same
                // trap `GiftCardSaleItem` records. Say something a person would want to read.
                Description = "Carrier bags sold at the counter. Kept in their own category so bag "
                    + "charges never inflate a product category's sales, and so the tills can keep them "
                    + "out of the Inventory list.",
            };

            _db.Category.Add(cat);
            _db.Entry(cat).Property("TenantId").CurrentValue = business.TenantId;
            return cat.IdOne;
        }

        private Item Bag(BusinessRef business, string idOne, string name, long pricePence, int taxId, Guid catId)
        {
            var item = new Item
            {
                IdOne = idOne,
                IdTwo = business.Id,
                Name = name,
                Brand = "-",
                Desc = "Carrier bag sold at the counter. Standard-rated: an ordinary retail supply, "
                    + "unlike a gift-card activation. The price is set in the portal because the "
                    + "statutory minimum differs by nation and changes over time.",
                Cost = 0m,
                Price = pricePence / 100m,
                ExPrice = ExPriceFor(business, taxId, pricePence),
                // ⚠ A bag is not inventory anybody counts, and a stock-tracked bag would go negative on
                // the first busy Saturday and sit in the negative-stock report for ever.
                StockUntracked = true,
                TaxId = taxId,
                CatId = catId,
            };

            _db.Entry(item).Property("TenantId").CurrentValue = business.TenantId;
            return item;
        }

        public sealed class BagBody
        {
            public long PricePence { get; set; }

            /// <summary>Optional — a sensible default is derived from the price when blank.</summary>
            public string Name { get; set; }
        }
    }
}
