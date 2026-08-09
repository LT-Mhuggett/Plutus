using System;

namespace Plutus.SharedKernel;

/// <summary>
/// How a manual discount on one basket line becomes a number of pence.
///
/// ⚠ THIS SITS NEXT TO <see cref="VatLineMath"/> ON PURPOSE and is part of the same C2 twin: the
/// web till computes it in `till/basket.ts lineDiscountPence`, and the two must agree to the penny
/// or the same basket totals differently on two counters. <see cref="VatLineMath.ForLine"/> then
/// decides how the pence are split across net and VAT — that half was already shared; this half
/// was not, and MAUI was about to grow its own.
///
/// ⚠ RETURNS TAKE NO DISCOUNT, on either till. Refunding a discounted sale gives back what the
/// customer actually paid; re-applying the discount would refund less than they handed over.
/// </summary>
public static class LineDiscounts
{
    /// <summary>
    /// A fixed amount off EACH UNIT — "£1 off", entered in pence.
    ///
    /// ⚠ Multiplied by quantity, matching the web till (`round(amount*100) * quantity`): "£1 off"
    /// on three of something is £3, not £1.
    /// </summary>
    public static long FixedPerUnit(long amountPerUnitPence, int quantity, bool isReturn)
    {
        if (isReturn || amountPerUnitPence <= 0 || quantity <= 0) return 0;
        return amountPerUnitPence * quantity;
    }

    /// <summary>
    /// A percentage off the line, expressed as a FRACTION — 0.10 for 10%.
    ///
    /// ⚠ A FRACTION, NOT A PERCENT NUMBER, and this method REFUSES anything above 1 rather than
    /// quietly obeying it. That is not pedantry: the legacy MAUI till computed
    /// <c>item.Price * decimal.Parse(amount)</c> straight from an operator's input, so entering
    /// <c>10</c> for "10%" multiplied the price BY TEN and the basket cheerfully charged it. A
    /// discount greater than the line is not a discount, so it throws where the old code silently
    /// took the customer's money.
    ///
    /// ⚠ Rounded away from zero on the whole line (`round(unit × qty × fraction)`), like the web
    /// till — NOT per unit and summed, which drifts by a penny on odd quantities.
    /// </summary>
    public static long Percentage(long unitIncPence, int quantity, decimal fraction, bool isReturn)
    {
        if (fraction < 0m || fraction > 1m)
            throw new ArgumentOutOfRangeException(nameof(fraction),
                $"A line discount must be a fraction between 0 and 1 (0.10 = 10%); got {fraction}. " +
                "Passing a percent NUMBER here is how a 10% discount becomes a 1000% one.");

        if (isReturn || fraction == 0m || unitIncPence <= 0 || quantity <= 0) return 0;

        var discount = (long)Math.Round(unitIncPence * quantity * fraction, MidpointRounding.AwayFromZero);

        // ⚠ Never more than the line is worth. Rounding at the 100% boundary can otherwise produce
        // a discount a penny larger than the line, which makes a negative-gross "sale".
        var lineValue = unitIncPence * quantity;
        return discount > lineValue ? lineValue : discount;
    }
}
