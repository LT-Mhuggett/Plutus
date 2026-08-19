using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Plutus.SharedKernel
{
    /// <summary>
    /// Carrier bags — the shop defines them once in the portal and every till gets them. Ruling
    /// 2026-08-19.
    ///
    /// ⚠⚠ MATT: *"Can you add the carrier bag decision to the portal? That creates the 5p and 20p bags
    /// at the back and that pushes down to the tills… This could just be a unique item that doesnt show
    /// in the Inventory. This would be cleaner than creating a bag at each till."*
    ///
    /// He is right that per-till was wrong: both tills stored ONE bag barcode as a **local device
    /// preference** (`DefaultBagId` / `prefs.bagBarcode`), so a five-till shop configured it five times,
    /// each till could disagree, and a new till sold no bags until somebody remembered. It is also how a
    /// till came to hold `"001"`, a barcode no item has.
    ///
    /// ⚠⚠ **"5p AND 20p" IS NOT A CHOICE BETWEEN TWO — A SHOP NORMALLY SELLS BOTH**, and the answer to
    /// Matt's question is why this is a LIST and not a pair of settings. A single-use carrier bag carries
    /// a **statutory minimum** charge; a heavier "bag for life" is priced however the shop likes. They
    /// are different products at different prices, sold side by side, and a shop may add a paper bag or
    /// a jute one later.
    ///
    /// ⚠ **AND THE STATUTORY FIGURE IS NOT 5p ANY MORE IN ENGLAND** — the minimum rose to **10p on
    /// 21 May 2021** and was extended from large retailers to all of them. The four UK nations differ and
    /// have moved at different times. **This code therefore hardcodes NO price**: the portal sets each
    /// bag's price, because a compliance figure baked into a build is one that is wrong the next time
    /// Parliament moves and right nowhere but England.
    ///
    /// ⚠⚠ **STANDARD-RATED, unlike the gift card.** A carrier bag is an ordinary retail supply at 20%,
    /// where `GiftCardSaleItem` deliberately provisions on the ZERO band because activation is not a
    /// VAT-able supply. Copying that file's band would under-declare VAT on every bag a shop sells, so
    /// the band is chosen explicitly here — see <see cref="StandardRatePreferred"/>.
    /// </summary>
    public static class CarrierBags
    {
        /// <summary>
        /// The category every carrier bag lives in, and the marker that makes it one.
        ///
        /// ⚠ THE CATEGORY IS THE FLAG, deliberately: no new column, no new entity, and the bags are real
        /// catalogue items so they sell, report and carry VAT like anything else. It follows
        /// `GiftCardSaleItem`'s reasoning — *"lives in its own category so gift cards never inflate a
        /// product category's sales"* — and it is what both tills filter on to keep bags out of the
        /// Inventory list (Matt: *"a unique item that doesnt show in the Inventory"*).
        /// </summary>
        public const string CategoryName = "Carrier bags";

        /// <summary>
        /// The prefix of a bag's catalogue barcode. ⚠ The rest is the PRICE IN PENCE, so `BAG-10` is the
        /// 10p bag — deterministic, so the portal and both tills derive the same key without storing a
        /// mapping, and legible in a report where a GUID would not be.
        /// </summary>
        public const string IdPrefix = "BAG-";

        /// <summary>The catalogue barcode for a bag at this price. ⚠ Price IS the identity: two bags at
        /// the same price are the same bag, which is what stops a shop accumulating three 10p rows.</summary>
        public static string IdFor(long pricePence)
        {
            if (pricePence <= 0) throw new ArgumentOutOfRangeException(nameof(pricePence),
                "A carrier bag must have a price — a free bag is not a line on a receipt.");

            return IdPrefix + pricePence.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Is this a carrier-bag barcode?</summary>
        public static bool IsBagId(string? idOne) =>
            idOne != null && idOne.StartsWith(IdPrefix, StringComparison.Ordinal)
            && long.TryParse(idOne.Substring(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var p)
            && p > 0;

        /// <summary>
        /// The price a bag id encodes, or null when it is not a bag id.
        ///
        /// ⚠ Used to cross-check the stored item price against its own key. They can only disagree if
        /// somebody edited the item directly, and a 10p bag selling at 25p under the id `BAG-10` would
        /// be a receipt nobody can explain.
        /// </summary>
        public static long? PriceFromId(string? idOne) =>
            IsBagId(idOne) ? long.Parse(idOne!.Substring(IdPrefix.Length), CultureInfo.InvariantCulture) : null;

        /// <summary>
        /// A sensible default NAME for a bag at this price — the portal may override it.
        ///
        /// ⚠ It does NOT try to say "single-use" or "bag for life" from the price alone: which is which
        /// depends on the shop and on the statutory minimum where it trades, and guessing puts a wrong
        /// word on a customer's receipt. The price is the only thing this can honestly state.
        /// </summary>
        public static string DefaultNameFor(long pricePence) =>
            $"Carrier bag ({(pricePence / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-GB"))})";

        /// <summary>
        /// Pick the STANDARD-rate band from a business's bands, or null when none can be identified.
        ///
        /// ⚠⚠ THE HIGHEST RATE, not the lowest — the exact opposite of `GiftCardSaleItem`'s zero-band
        /// pick, and the reason that logic is not shared. A carrier bag is an ordinary 20% supply; a
        /// gift-card activation is not a supply at all. Reaching for the same helper would silently
        /// under-declare VAT on every bag sold.
        ///
        /// ⚠ Bands here are MULTIPLIERS (1.2 = 20%), matching `Tax.Rate` elsewhere in this codebase.
        /// ⚠ Returns null rather than guessing when a business has no bands yet — a half-seeded tenant is
        /// tried again on the next pass, exactly as gift-card provisioning does.
        /// </summary>
        public static int? StandardRatePreferred(IEnumerable<(int IdOne, decimal Rate)> bands)
        {
            var list = bands?.ToList();
            if (list is null || list.Count == 0) return null;

            // ⚠ Prefer a band that IS 20%, and only then fall back to the highest available: a tenant
            // trading somewhere with a different standard rate must not have 20% invented for them.
            var twenty = list.Where(b => Math.Abs(b.Rate - 1.20m) < 0.0001m).ToList();
            if (twenty.Count > 0) return twenty[0].IdOne;

            return list.OrderByDescending(b => b.Rate).First().IdOne;
        }
    }
}
