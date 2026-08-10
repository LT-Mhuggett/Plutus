using System.Globalization;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// What a tax band says to the person setting a price.
///
/// ⚠ WHY THIS IS A TESTED RULE AND NOT STRING FORMATTING. Matt, 2026-08-10: *"Maui edit items is
/// missing category and tax e.g. 20%."* The item editor now shows the band beside the price, and
/// the only reason to show it is so somebody can CHECK it — a zero-rated book sitting in the
/// standard band looks perfectly normal on a price label and is wrong on every VAT return from
/// then on. A label that misstates the rate is therefore worse than no label: it converts a
/// checkable field into a confident wrong answer.
///
/// The conversion is the part that can be got wrong: `Rate` is a MULTIPLIER, not a percentage.
/// </summary>
public class TaxBandLabelTests
{
    private static TaxBandDto Band(string name, decimal rate, int id = 1)
        => new() { IdOne = id, Name = name, Rate = rate };

    [Fact]
    public void A_multiplier_becomes_the_percentage_an_operator_recognises()
    {
        // ⚠ 1.2 IS 20%, NOT 1.2%. Printing the multiplier is the mistake this pins.
        Assert.Equal("Standard — 20%", TaxBandLabel.For(Band("Standard", 1.2m)));
        Assert.Equal("Reduced — 5%", TaxBandLabel.For(Band("Reduced", 1.05m)));
    }

    [Fact]
    public void Zero_rated_and_Exempt_both_read_zero_percent_and_stay_TELLABLE_APART()
    {
        // ⚠ Both are rate 1.0 and they are NOT the same thing — they land in different boxes on a
        // VAT return. The NAME is the only thing that distinguishes them, so the name must survive
        // into the label. Replacing the label with the rate would merge two bands on screen.
        var zero = TaxBandLabel.For(Band("Zero rated", 1.0m, 2));
        var exempt = TaxBandLabel.For(Band("Exempt", 1.0m, 3));

        Assert.Equal("Zero rated — 0%", zero);
        Assert.Equal("Exempt — 0%", exempt);
        Assert.NotEqual(zero, exempt);
    }

    [Fact]
    public void A_fractional_rate_keeps_its_decimals_and_a_whole_one_does_not_gain_any()
    {
        Assert.Equal("Odd — 17.5%", TaxBandLabel.For(Band("Odd", 1.175m)));
        Assert.Equal("Standard — 20%", TaxBandLabel.For(Band("Standard", 1.2m)));   // not "20.00%"
    }

    [Fact]
    public void A_rate_below_one_shows_the_raw_multiplier_rather_than_a_negative_percentage()
    {
        // ⚠ A multiplier under 1 is a BROKEN ROW, not a discount. "−20%" is a lie an operator
        // would act on; the raw number at least reads as wrong.
        Assert.Equal("Broken — rate 0.8", TaxBandLabel.For(Band("Broken", 0.8m)));
    }

    [Fact]
    public void A_band_the_catalogue_names_but_the_list_does_not_still_shows_something()
    {
        // ⚠ An item can carry a taxId that `/api/Tax/Index` did not return. Showing a blank where
        // a rate belongs reads as "no tax", which is a different and much worse claim.
        Assert.Equal("band 7", TaxBandLabel.For(null, 7));
        Assert.Equal("none", TaxBandLabel.For(null, 0));
    }

    [Fact]
    public void A_nameless_band_is_identified_by_its_id_not_by_an_empty_string()
    {
        Assert.Equal("band 4 — 20%", TaxBandLabel.For(Band("   ", 1.2m, 4)));
    }

    [Fact]
    public void The_percentage_is_invariant_whatever_windows_thinks()
    {
        // ⚠ A till whose Windows is set to de-DE must not print "17,5%" on a UK VAT band — the
        // same reason receipt money is pinned to invariant £.
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Odd — 17.5%", TaxBandLabel.For(Band("Odd", 1.175m)));
        }
        finally { CultureInfo.CurrentCulture = was; }
    }
}
