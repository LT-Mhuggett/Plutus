using System;

namespace Plutus.SharedKernel;

/// <summary>What one basket line contributes to a sale, as the wire wants it.</summary>
/// <param name="Qty">Signed: negative for a return.</param>
/// <param name="LineGrossPence">Inc-VAT, after discount, signed.</param>
/// <param name="LineExPence">Ex-VAT, after discount, signed.</param>
/// <param name="VatRateBp">Derived from the PRICE PAIR, so legitimately "wobbled" (2002bp for a
/// £14.99/£12.49 line). This is not a bug and must not be "cleaned up" to the band's rate.</param>
/// <param name="VatAmountPence">Gross − ex. Never rate arithmetic.</param>
/// <param name="DiscountPence">What goes on the wire's DiscountPence (zero on a return).</param>
public readonly record struct VatLine(
    int Qty, long LineGrossPence, long LineExPence, int VatRateBp, long VatAmountPence, long DiscountPence);

/// <summary>
/// THE ONE IMPLEMENTATION of how a till turns a basket line into the VAT figures on the wire.
///
/// WHY THIS EXISTS. Until now this arithmetic lived only in the web till's TypeScript, and the
/// retrofit plan calls that "the reference implementation" — meaning every other till was expected
/// to re-derive it by reading the code and getting it right. There are already three copies of
/// pieces of it in this repo, and Plutus expects tills on Windows, macOS and Linux. "Consistent
/// because each client was written carefully" is not a property a VAT return can rely on: two
/// implementations that disagree by a penny on the same basket disagree on every return forever, and
/// nothing would flag it.
///
/// So: this lives in <c>Plutus.SharedKernel</c>, which the backend and <c>Plutus.Client.Core</c> both
/// reference. Those client libraries are plain <c>net10.0</c> with no MAUI and no third-party
/// packages (pinned by an architecture test), so they run unchanged on Windows, macOS and Linux. Any
/// .NET till on any OS therefore gets this arithmetic by construction rather than by discipline.
/// The web till stays a deliberate second implementation in TypeScript, and
/// <c>VatLineMathTests</c> pins it to the same documented numbers.
///
/// THE RULES IT ENCODES (all four are decisions, not accidents — see the retrofit plan §2a):
///   1. Prices are VAT-INCLUSIVE. The ex-VAT figure is derived and rounded to the penny; the
///      customer-facing price is never recomputed from a rate.
///   2. The line's rate comes from the PRICE PAIR, not from the catalogue's band. A real 20% line
///      ships as 1993–2004bp and that is correct.
///   3. VAT is <c>gross − ex</c>. Rate arithmetic (<c>gross × bp/(10000+bp)</c>) disagrees with the
///      receipt by a penny on about a third of standard-rated lines, and the receipt is what the
///      customer holds.
///   4. A discount is applied to the ex-VAT figure SCALED by the ex/inc ratio, so the net and VAT
///      split of a discounted line stays consistent with its gross.
/// </summary>
public static class VatLineMath
{
    /// <summary>
    /// ⚠ JavaScript's <c>Math.round</c> rounds a half AWAY FROM ZERO for positive numbers, while
    /// .NET's default is banker's rounding — so <c>Math.Round(0.5)</c> is 1 in the web till and 0
    /// here unless this is stated. Every rounded quantity below is non-negative, so away-from-zero
    /// reproduces the web till exactly. This one line is the difference between two tills agreeing
    /// and disagreeing by a penny.
    /// </summary>
    private static long RoundLikeTheWebTill(decimal value) =>
        (long)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The rate a line declares, from its price pair: <c>round((inc/ex − 1) × 10000)</c>.
    /// Zero when there is no pair to reason about (a free line, or missing metadata) — guessing a
    /// rate there is worse than declaring none.
    /// </summary>
    public static int RateBpFromPair(long unitIncPence, long unitExPence) =>
        unitExPence <= 0 || unitIncPence <= 0
            ? 0
            : (int)RoundLikeTheWebTill((unitIncPence / (decimal)unitExPence - 1m) * 10000m);

    /// <summary>
    /// Build one line's wire figures.
    ///
    /// <paramref name="discountPence"/> is the line's total discount, inc-VAT, as a POSITIVE number.
    /// ⚠ A RETURN DROPS THE DISCOUNT — refunding a discounted sale returns what was actually paid,
    /// and re-applying the discount would refund less than the customer handed over. Its
    /// <see cref="VatLine.DiscountPence"/> is therefore zero and every money figure is negated.
    /// </summary>
    public static VatLine ForLine(
        long unitIncPence, long unitExPence, int quantity, long discountPence, bool isReturn)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(
            nameof(quantity), "Pass a positive quantity and isReturn:true — a negative quantity would "
            + "double-negate and silently produce a positive refund.");

        var discount = isReturn ? 0 : Math.Max(0, discountPence);
        var qty = isReturn ? -quantity : quantity;

        var lineGross = unitIncPence * qty - discount;

        // The discount is scaled into the ex-VAT figure by the ex/inc ratio, so a discounted line's
        // net and VAT split stays consistent with the gross the customer paid.
        var ratio = unitIncPence > 0 ? unitExPence / (decimal)unitIncPence : 1m;
        var lineEx = (unitExPence * quantity - RoundLikeTheWebTill(discount * ratio)) * (isReturn ? -1 : 1);

        return new VatLine(
            Qty: qty,
            LineGrossPence: lineGross,
            LineExPence: lineEx,
            VatRateBp: RateBpFromPair(unitIncPence, unitExPence),
            // Rule 3: gross − ex, never rate arithmetic.
            VatAmountPence: lineGross - lineEx,
            DiscountPence: discount);
    }
}
