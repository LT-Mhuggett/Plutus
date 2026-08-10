using System.Globalization;
using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

/// <summary>
/// How a tax band reads to an operator: <c>"Standard — 20%"</c>.
///
/// ⚠ THIS EXISTS BECAUSE A BAND NAME ALONE TELLS NOBODY WHAT IT CHARGES. Matt, 2026-08-10, asked
/// for tax *"e.g. 20%"* on the item editor. A band called "Standard", or "T1", is not something an
/// operator can check a price against — and checking is the entire point of showing it: a
/// zero-rated book that has been put in the standard band looks completely normal on a price label
/// and is wrong on every VAT return from then on.
///
/// ⚠ IT IS A SHARED RULE, NOT A SCREEN'S FORMATTING, because the conversion is the part that can be
/// got wrong: <see cref="TaxBandDto.Rate"/> is a MULTIPLIER (1.2 = 20%), so the percentage is
/// <c>(rate − 1) × 100</c>. Reading it as a percentage directly would print "1.2%" next to a 20%
/// band, and a screen that quietly misstates the VAT rate is worse than one that omits it.
///
/// ⚠ ZERO-RATED AND EXEMPT BOTH SHOW 0% AND ARE DIFFERENT BANDS. The NAME is what distinguishes
/// them, and they land in different boxes on a VAT return — which is why the name is always kept
/// and never replaced by the rate. See `vat-exempt-must-stay-supported`.
/// </summary>
public static class TaxBandLabel
{
    /// <summary>The label for a band that is known.</summary>
    public static string For(TaxBandDto? band, int fallbackId = 0)
    {
        // ⚠ An id with no matching band is NOT an error to hide. It happens when the catalogue
        // holds a tax row `/api/Tax/Index` did not return, and the operator needs to see that
        // something is there rather than a blank where a rate should be.
        if (band is null) return fallbackId == 0 ? "none" : $"band {fallbackId}";

        var name = string.IsNullOrWhiteSpace(band.Name) ? $"band {band.IdOne}" : band.Name.Trim();

        // ⚠ A multiplier below 1 would be a NEGATIVE VAT rate. That is a broken row, not a
        // discount, so show the raw multiplier — a confident "−20%" is a lie an operator would act
        // on, and there is no honest percentage to print.
        if (band.Rate < 1m) return $"{name} — rate {band.Rate.ToString("0.###", CultureInfo.InvariantCulture)}";

        var percent = (band.Rate - 1m) * 100m;
        var format = percent == decimal.Truncate(percent) ? "0" : "0.##";

        return $"{name} — {percent.ToString(format, CultureInfo.InvariantCulture)}%";
    }
}
