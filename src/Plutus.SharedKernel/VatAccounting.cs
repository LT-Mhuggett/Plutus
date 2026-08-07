using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>
/// How a supply is classified for UK VAT. ⚠ <see cref="Zero"/> and <see cref="Exempt"/> are NOT
/// the same thing even though both charge the customer nothing:
///   • Zero-rated is a TAXABLE supply at 0% — input tax on related costs IS recoverable.
///   • Exempt is NOT a taxable supply — input tax attributable to it is NOT recoverable
///     (partial exemption).
/// Collapsing them to "0%" loses the distinction permanently, which is why the class travels
/// alongside the rate rather than being inferred from it.
/// </summary>
public enum VatClass
{
    Standard = 0,
    Reduced = 1,
    Zero = 2,
    Exempt = 3,
    OutsideScope = 4,
}

/// <summary>A VAT band as the portal publishes it: identity, class, rate, and when it took effect.</summary>
public readonly record struct VatBand(string Key, string DisplayName, VatClass Class, int RateBp, DateTime EffectiveFromUtc);

/// <summary>
/// UK VAT accounting arithmetic for a retailer, per HMRC Notice 727 (retail schemes).
///
/// THE RULE THIS ENCODES — Notice 727 §3.4.1, Point of Sale scheme:
///   "Once your system has produced the total value of sales at each rate, you calculate your
///    output tax by applying the appropriate VAT fraction to the relevant portion of your DGT."
///
/// So output tax is the VAT fraction applied to the PERIOD'S TAKINGS AT EACH RATE — not the sum of
/// per-line VAT figures. Those differ: each line's VAT is rounded to the penny, and thousands of
/// roundings do not add up to the rounding of the total. Summing them understates the liability.
///
/// The VAT fraction for a rate r is r/(100+r) — one sixth at 20% (Notice 700 §17).
/// Rounding is to the NEAREST penny: HMRC's round-down concession is explicitly not available to
/// retailers (VATREC12020), which lists "round up and down to the nearest 1p" among the permitted
/// methods.
/// </summary>
public static class VatAccounting
{
    /// <summary>
    /// Output tax due on VAT-inclusive takings at one rate — the VAT fraction, to the nearest penny.
    /// This is the figure that belongs on a VAT return; it is NOT the sum of the lines' VAT.
    /// </summary>
    public static long OutputTaxOn(long grossPence, int rateBp)
    {
        if (rateBp <= 0) return 0;      // zero-rated and exempt both yield no output tax
        return (long)Math.Round(grossPence * (decimal)rateBp / (10000m + rateBp), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Which band do these takings belong to? Sale lines carry a rate DERIVED from the price pair,
    /// so a single 20% band arrives as 1993…2004bp and would otherwise fragment a VAT return into
    /// a dozen buckets. Snap to the nearest published band; anything further away than
    /// <paramref name="toleranceBp"/> belongs to NO band and must be reported separately rather
    /// than quietly folded into a real one — that is off-band damage a human has to look at.
    /// </summary>
    public static VatBand? BandFor(IReadOnlyCollection<VatBand> bands, int derivedRateBp, int toleranceBp = 25)
    {
        if (bands == null || bands.Count == 0) return null;
        var nearest = bands.OrderBy(b => Math.Abs(b.RateBp - derivedRateBp)).First();
        return Math.Abs(nearest.RateBp - derivedRateBp) <= toleranceBp ? nearest : null;
    }
}

/// <summary>One line of a VAT return: the takings at a band, and the tax actually due on them.</summary>
public sealed record VatReturnLine(
    string BandKey,
    string DisplayName,
    VatClass Class,
    int RateBp,
    long GrossPence,
    /// <summary>The legal figure: VAT fraction × takings (Notice 727 §3.4.1).</summary>
    long OutputTaxPence,
    /// <summary>What the tills actually charged, summed. Kept for reconciliation — a persistent
    /// gap between this and <see cref="OutputTaxPence"/> is expected (penny rounding per line),
    /// but a LARGE one means something is wrong with pricing, not with rounding.</summary>
    long ChargedPence)
{
    public long NetPence => GrossPence - OutputTaxPence;
    /// <summary>Positive = the tills charged less than is due. Always report it; never hide it.</summary>
    public long RoundingDifferencePence => OutputTaxPence - ChargedPence;
}
