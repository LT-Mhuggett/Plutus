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
    /// How far a line's DERIVED rate may sit from a published band and still belong to it, in basis
    /// points. Tills compute each line's rate from its price pair, so a genuine 20% band arrives at
    /// 1993–2004bp; 25bp (0.25 percentage points) absorbs that without ever reaching a neighbouring
    /// UK rate. Named because the band-snap, the tax-row mapping guard and the reports must all use
    /// the SAME number — two tolerances would disagree about which takings belong where.
    /// </summary>
    public const int BandSnapToleranceBp = 25;

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
    /// Which band do these takings belong to, judged only on the RATE? Sale lines carry a rate
    /// DERIVED from the price pair, so a single 20% band arrives as 1993…2004bp and would otherwise
    /// fragment a VAT return into a dozen buckets. Snap to the nearest published band; anything
    /// further away than <paramref name="toleranceBp"/> belongs to NO band and must be reported
    /// separately rather than quietly folded into a real one — that is off-band damage a human has
    /// to look at.
    ///
    /// ⚠ RETURNS NULL WHEN TWO BANDS TIE. A tenant with both a zero-rated and an exempt band has two
    /// candidates at 0bp, and picking "the first" would silently attribute takings to one of them —
    /// corrupting the partial-exemption figure this distinction exists to produce. Ambiguity is
    /// reported as unclassified so it is visible; the line's RECORDED band
    /// (<c>SaleLine.VatBand</c>) is what resolves it, and this is only the fallback for lines that
    /// carry none.
    /// </summary>
    public static VatBand? BandFor(IReadOnlyCollection<VatBand> bands, int derivedRateBp, int toleranceBp = BandSnapToleranceBp)
    {
        if (bands == null || bands.Count == 0) return null;
        var within = bands.Where(b => Math.Abs(b.RateBp - derivedRateBp) <= toleranceBp).ToList();
        if (within.Count == 0) return null;
        if (within.Count == 1) return within[0];

        // Several in range: only a single CLOSEST one is an answer. A genuine tie (two bands at the
        // same rate) is unresolvable from the rate alone, so say so rather than choose.
        var best = within.Min(b => Math.Abs(b.RateBp - derivedRateBp));
        var closest = within.Where(b => Math.Abs(b.RateBp - derivedRateBp) == best).ToList();
        return closest.Count == 1 ? closest[0] : null;
    }
}

/// <summary>
/// WP2c-exempt: which VAT band a legacy tax row means.
///
/// THE WHOLE REASON THIS TYPE EXISTS: items are priced against legacy <c>Taxes</c> rows that carry
/// only a name and a multiplier, so a band could only ever be inferred from the rate — and the rate
/// CANNOT distinguish zero-rated from exempt, because both are 0%. That distinction is real money
/// (exempt supplies block input-tax recovery; zero-rated don't), so it has to be stated, not
/// guessed.
///
/// Resolution is deliberately two-tier:
///   1. An EXPLICIT mapping the portal owns. Always wins.
///   2. Otherwise the rate, matched against the tenant's bands. Correct for every band that is
///      unambiguous at its rate — which is all of them until a tenant has two bands at 0%.
///
/// So nothing needs mapping until a shop actually sells exempt supplies, and once it does, the
/// mapping is a statement by its owner rather than a string-match on a 2019 seed row's name.
/// </summary>
public static class VatBandResolution
{
    /// <summary>
    /// The band for one legacy tax row. <paramref name="explicitBand"/> is the portal's mapping
    /// (null if none). <paramref name="rateBp"/> is the tax row's rate.
    ///
    /// ⚠ Returns null when the rate matches SEVERAL bands and nothing has been mapped — that is
    /// exactly the zero-vs-exempt case, and guessing would silently pick one. A null here means
    /// "a human has to say", and the portal surfaces it as an unmapped tax row.
    /// </summary>
    public static string? Resolve(
        IReadOnlyCollection<VatBand> bands, string? explicitBand, int rateBp, int toleranceBp = VatAccounting.BandSnapToleranceBp)
    {
        if (!string.IsNullOrWhiteSpace(explicitBand)) return explicitBand;
        if (bands == null || bands.Count == 0) return null;

        var candidates = bands.Where(b => Math.Abs(b.RateBp - rateBp) <= toleranceBp).ToList();
        return candidates.Count == 1 ? candidates[0].Key : null;
    }

    /// <summary>
    /// Does this tenant have an AMBIGUITY that mapping must resolve — two distinct bands close
    /// enough in rate that the rate cannot tell them apart? In practice this means "has a zero-rated
    /// AND an exempt band". Until they do, the rate is a complete answer and the portal need not
    /// nag anyone about mapping.
    /// </summary>
    public static bool NeedsExplicitMapping(IReadOnlyCollection<VatBand> bands, int toleranceBp = VatAccounting.BandSnapToleranceBp)
    {
        if (bands == null || bands.Count < 2) return false;
        var list = bands.ToList();
        // Pairwise, because "within tolerance of each other" is not a bucketing — 1990 and 2010 are
        // 20bp apart but land in different buckets under any fixed-width scheme. Band counts are
        // single digits, so the honest O(n²) is free.
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                if (!string.Equals(list[i].Key, list[j].Key, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(list[i].RateBp - list[j].RateBp) <= toleranceBp)
                    return true;
        return false;
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
