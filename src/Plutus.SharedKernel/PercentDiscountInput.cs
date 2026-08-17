using System;
using System.Globalization;

namespace Plutus.SharedKernel
{
    /// <summary>
    /// What an operator typed into a box labelled **Percent**, turned into the fraction the money
    /// rules take.
    ///
    /// ⚠⚠ MATT'S RULING, 2026-08-17: *"make it %"*. The box is a PERCENT NUMBER — somebody types
    /// <c>10</c> for 10% — and this is the one place that converts it. Before this, the MAUI till did
    /// <c>item.Price * decimal.Parse(typed)</c> straight from the box, so typing <c>10</c> multiplied
    /// the price **by ten**: a £20 item took £200 off. It was contained only because
    /// `DiscountDecision` refuses a discount larger than the basket — so nobody was overcharged, but
    /// every percentage discount above 100% was simply impossible and the operator was told the
    /// basket was too small rather than that they had typed the wrong thing.
    ///
    /// ⚠ IT RETURNS null RATHER THAN THROWING. <see cref="LineDiscounts.Percentage"/> throws on a
    /// fraction above 1 — correctly, because that is a programming error by the time it is reached —
    /// but an operator mistyping into a till is not a programming error and must get a sentence, not
    /// a crash. So parsing and refusing happens here, and the money rule stays strict behind it.
    ///
    /// ⚠ In `SharedKernel` because the conversion is the same wherever a percentage is typed, and a
    /// till with its own copy is a till that can disagree about what "10" means. See C1.
    /// </summary>
    public static class PercentDiscountInput
    {
        /// <summary>The most a percentage discount can be. 100% is legitimate — Matt, 2026-08-13:
        /// a discount equal to the basket is allowed.</summary>
        public const decimal MaxPercent = 100m;

        /// <summary>
        /// The fraction for a typed percent, or null when it is not a usable percentage.
        ///
        /// ⚠ A trailing <c>%</c> is accepted. Operators type it, and refusing the character they just
        /// read off the label is the kind of pedantry that gets a till called broken.
        /// </summary>
        public static decimal? FractionFromTyped(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed)) return null;

            var cleaned = typed.Trim().TrimEnd('%').Trim();

            // ⚠ INVARIANT **AND** CURRENT CULTURE. A till in a comma-decimal locale gets "12,5" from
            // its own numeric keypad, and a till whose locale is English gets "12.5" — accepting only
            // one of them makes a discount silently unavailable on some machines.
            if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out var percent)
                && !decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out percent))
                return null;

            // ⚠ NEGATIVE IS NOT A DISCOUNT. The old code took `Math.Abs`, which turned "-10" into a
            // 10% discount — quietly doing something the operator did not ask for.
            if (percent < 0m || percent > MaxPercent) return null;

            // ⚠ Rounded to 4 dp so a fraction is exact for the pence arithmetic that follows. 12.5%
            // is 0.125; without this, repeating decimals reach `Percentage` and the rounding it does
            // becomes dependent on how many digits somebody typed.
            return Math.Round(percent / 100m, 4, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The percent number to SHOW for a stored fraction — the inverse, for pre-filling the box.
        ///
        /// ⚠⚠ LEGACY ROWS HOLD A FRACTION. `DiscountModel.Amount` from the old NatApp table is 0.1
        /// for 10% (which is why the original multiply looked plausible), so a box that now means
        /// "percent" must show **10**, not 0.1. Getting this wrong in the other direction would make
        /// every migrated discount read as 0.1% and apply a hundredth of itself.
        /// </summary>
        public static decimal PercentFromFraction(decimal fraction) =>
            Math.Round(fraction * 100m, 2, MidpointRounding.AwayFromZero);

        /// <summary>
        /// What to tell an operator whose entry was refused.
        ///
        /// ⚠ It names the bound and gives an example, because "invalid" leaves somebody guessing
        /// whether the problem is the number, the format, or the till.
        /// </summary>
        public static string RefusalMessage(string typed) =>
            $"\"{typed?.Trim()}\" isn't a percentage between 0 and {MaxPercent:0}. "
            + "Type the percent itself — 10 for 10% off.";
    }
}
