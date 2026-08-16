using System;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Inventory
{
    /// <summary>
    /// The price pair an item is saved with, and whether the catalogue will accept it (WP10).
    ///
    /// ⚠⚠ THE EX PRICE IS DERIVED, NEVER TYPED TWICE. Free-typed ex prices corrupted **47 live
    /// items** — a £7.99 item carrying a £799.00 ex price — and with them every downstream VAT
    /// figure. The operator gives the INC price; the ex price comes from the band.
    ///
    /// ⚠ IT LIVES HERE, NOT IN THE VIEWMODEL, so the arithmetic and the guard can be tested without
    /// a device. `ViewAllViewModel` is 1,300 lines of screen; this is the part that decides money.
    /// </summary>
    public static class ItemPricing
    {
        /// <summary>
        /// The ex-VAT price for an inc-VAT price under a band.
        ///
        /// ⚠ <paramref name="bandMultiplier"/> IS A MULTIPLIER (1.2 = 20%), not a rate and not basis
        /// points — that is what `/api/Tax/Index` returns and what the server's guard compares
        /// against. So the ex price **divides** by it; multiplying makes a £10 item's ex price £12
        /// and the server rejects the write with a message that never mentions units.
        ///
        /// ⚠ AN UNKNOWN BAND KEEPS THE ITEM'S EXISTING RATIO rather than refusing. An item whose tax
        /// row `/api/Tax/Index` does not know must stay EDITABLE — the alternative is an item nobody
        /// can fix, and the operator is usually editing it precisely because something is wrong.
        ///
        /// ⚠⚠ BUT ONLY IF THAT RATIO IS PLAUSIBLE — see <see cref="CarriedRatioIsUsable"/>. Carrying
        /// it blindly is how the 47-item corruption SPREADS: the £7.99/£799.00 item has a ratio of
        /// 100, so repricing it to £9.99 would write a £999.00 ex price. An implausible ratio is
        /// dropped for ex == inc, which is wrong in the safe direction — visibly VAT-free, and
        /// fixable by giving the item a band — instead of wrong by a factor of a hundred.
        /// </summary>
        public static decimal ExPriceFor(
            decimal incPrice, decimal? bandMultiplier, decimal currentInc, decimal currentEx)
        {
            if (bandMultiplier is decimal m && m > 0)
                return Math.Round(incPrice / m, 2, MidpointRounding.AwayFromZero);

            if (!CarriedRatioIsUsable(currentInc, currentEx)) return incPrice;

            return Math.Round(incPrice * (currentEx / currentInc), 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Can this item's existing price pair stand in for a band we don't know?
        ///
        /// ⚠ EX ABOVE INC IS NEVER VALID — VAT is never negative, so a ratio over 1 means the stored
        /// pair is corrupt, not that the item is unusual. That single test is what stops a bad pair
        /// surviving a reprice.
        /// </summary>
        public static bool CarriedRatioIsUsable(decimal currentInc, decimal currentEx)
            => currentInc > 0 && currentEx > 0 && currentEx <= currentInc;

        /// <summary>
        /// Will the catalogue accept this pair under this band?
        ///
        /// ⚠⚠ THE SAME RULE THE SERVER APPLIES — `SharedKernel.VatRateHistory.Explains`, whose
        /// tolerance is a shared constant "kept identical on purpose so an item the portal accepts
        /// can never be a sale ingest refuses".
        ///
        /// ⚠⚠ IT CANNOT FIRE TODAY, AND THAT IS THE POINT. While the ex price comes from
        /// <see cref="ExPriceFor"/> the pair is consistent by construction, so this always passes —
        /// `A_derived_pair_always_satisfies_the_guard` pins exactly that. It is here as a BACKSTOP
        /// for the day someone re-adds a typed ex-price field, which is precisely how the 47 items
        /// were corrupted in the first place. Do not delete it as dead code; the day it stops being
        /// dead is the day it earns its keep.
        ///
        /// ⚠ AN UNKNOWN BAND IS NOT A FAILURE. There is no rate to reason about, so there is nothing
        /// to be inconsistent with — refusing would make an unclassified item permanently unsaveable.
        /// The server has the last word either way.
        /// </summary>
        public static bool IsBandConsistent(decimal incPrice, decimal exPrice, decimal? bandMultiplier)
        {
            if (bandMultiplier is not decimal m || m <= 0) return true;

            // ⚠ Multiplier → basis points, which is what the shared rule speaks: 1.2 → 2000.
            var rateBp = (int)Math.Round((m - 1m) * 10000m, MidpointRounding.AwayFromZero);

            return VatRateHistory.Explains(
                Pence.FromDecimal(incPrice), Pence.FromDecimal(exPrice), rateBp);
        }

        /// <summary>
        /// What to tell an operator whose pair the catalogue would refuse.
        ///
        /// ⚠ It names the number the server EXPECTED. "That price is inconsistent" sends somebody
        /// back to a form with no idea which of two fields to change.
        /// </summary>
        public static string InconsistencyMessage(decimal incPrice, decimal? bandMultiplier)
        {
            if (bandMultiplier is not decimal m || m <= 0)
                return "That price doesn't match the item's VAT band.";

            var expectedEx = Math.Round(incPrice / m, 2, MidpointRounding.AwayFromZero);

            return $"That price doesn't match the VAT band. For {incPrice:C2} including VAT the "
                 + $"ex-VAT price should be about {expectedEx:C2}. Check the band is right.";
        }
    }
}
