using System;

namespace Plutus.SharedKernel;

/// <summary>
/// The VAT on a card surcharge.
///
/// ⚠ THE RULE IS SETTLED LAW, NOT A CHOICE. A fee the seller charges for paying a particular way is
/// FURTHER CONSIDERATION FOR THE MAIN SUPPLY — it takes the VAT treatment of the goods being paid
/// for (CJEU, <i>Bookit</i> C-607/14 and <i>NEC</i> C-130/15, 2016; HMRC applies them). It is NOT an
/// exempt financial service, and it is NOT automatically standard-rated: a surcharge on a basket of
/// zero-rated children's books is zero-rated, on standard-rated comics it carries 20%, and on a
/// mixed basket it is APPORTIONED by the value of the underlying supplies. A till that hardcodes
/// any one rate for the fee puts wrong numbers on a VAT return for every basket that differs.
///
/// ⚠ SELLING LEGALLY IS THE TENANT'S PROBLEM, BUT THE TILL MUST NOT MAKE IT WORSE. Surcharging
/// CONSUMER cards (and PayPal-style methods) has been banned in the UK since 13 January 2018
/// (Consumer Rights (Payment Surcharges) Regulations 2012, as amended — kept after Brexit).
/// Commercial/corporate cards may still be surcharged, at no more than the merchant's own cost.
/// The setting therefore exists for B2B tenants and other jurisdictions, and the helper text beside
/// it carries this warning — this class only makes sure that WHEN a surcharge is charged, its VAT
/// is right.
/// </summary>
public static class CardSurchargeVat
{
    /// <summary>The natural key of the provisioned catalogue row the fee is rung through. HERE, in
    /// the shared kernel, because the server provisions it and every till builds the line locally —
    /// two spellings of this string would be a fee that sells fine on one till and breaks the sale
    /// bridge on another.</summary>
    public const string ItemIdOne = "CARD-SURCHARGE";

    /// <summary>
    /// The fee itself, from the tenant's setting: <c>flat + gross × bp ÷ 10000</c>, rounded once,
    /// away from zero, MULTIPLY BEFORE DIVIDING — the same conventions as every shared money rule,
    /// because two tills disagreeing about the fee by a penny disagree about the sale.
    ///
    /// Percent-plus-flat because that is the shape of every acquirer's own pricing (1.69% + 20p),
    /// and lawful surcharging (where it is lawful at all) is capped at passing that cost through.
    /// </summary>
    /// <param name="basketGrossPence">Σ line gross of the SALE lines, after discounts — the same
    /// base <see cref="PairFor"/> apportions against.</param>
    public static long FeePence(int surchargeBp, long flatPence, long basketGrossPence)
    {
        if (surchargeBp < 0 || flatPence < 0)
            throw new ArgumentOutOfRangeException(nameof(surchargeBp),
                "A surcharge setting cannot be negative — money off is a discount, not a fee.");

        if (basketGrossPence <= 0) return 0;   // nothing to ride on → no fee, never a throw here

        return flatPence + (long)Math.Round(
            basketGrossPence * (decimal)surchargeBp / 10000m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The (inc, ex) price pair for a surcharge "line", apportioned from the basket it rides on.
    ///
    /// One blended ratio, rounded ONCE, away from zero like every shared money rule — HMRC asks for
    /// a fair and reasonable apportionment by the values of the underlying supplies, and
    /// <c>fee × basketEx ÷ basketGross</c> is exactly that. The line's declared rate then derives
    /// from this pair (C1 rule 2) and its VAT is gross − ex (C1 rule 3); nothing new is invented.
    /// </summary>
    /// <param name="surchargePence">The fee, in pence, as a POSITIVE number.</param>
    /// <param name="basketGrossPence">Σ line gross of the SALE lines the fee rides on — after
    /// discounts, excluding returns. ⚠ A refund attracts no surcharge, and a fee "apportioned"
    /// against a negative basket would produce a negative ex and a rate from nowhere.</param>
    /// <param name="basketExPence">Σ line ex-VAT of the same lines.</param>
    public static (long IncPence, long ExPence) PairFor(
        long surchargePence, long basketGrossPence, long basketExPence)
    {
        if (surchargePence < 0)
            throw new ArgumentOutOfRangeException(nameof(surchargePence),
                "A surcharge is a positive fee. Money off is a discount, and discounts follow "
                + "DiscountApportionment — the two must not blur, because a return drops one and not the other.");

        if (surchargePence == 0) return (0, 0);

        if (basketGrossPence <= 0)
            throw new ArgumentOutOfRangeException(nameof(basketGrossPence),
                "A surcharge takes the VAT treatment of the goods it is charged on, so a basket with "
                + "no positive sale value gives it nothing to follow. Do not surcharge a refund.");

        if (basketExPence < 0 || basketExPence > basketGrossPence)
            throw new ArgumentOutOfRangeException(nameof(basketExPence),
                "The basket's ex-VAT total must sit between zero and its gross — anything else means "
                + "the caller summed the wrong lines, and the fee would inherit the error.");

        // ⚠ ONE rounding, at the end — and MULTIPLY BEFORE DIVIDING. `fee × (ex ÷ gross)` puts a
        // non-terminating decimal in the middle (10000/12000 = 0.8333…), so a true midpoint like
        // 3 × 10000 ÷ 12000 = 2.5 arrives as 2.4999… and rounds the wrong way. Products first keeps
        // the value an exact rational until the single rounding. Both tills must produce THIS
        // number or the same basket carries two different VAT figures on two counters.
        var ex = (long)Math.Round(
            surchargePence * (decimal)basketExPence / basketGrossPence,
            MidpointRounding.AwayFromZero);

        return (surchargePence, ex);
    }
}
