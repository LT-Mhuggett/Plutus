using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// UK VAT accounting arithmetic for a retailer — HMRC Notice 727 §3.4.1 (Point of Sale scheme):
/// output tax is the VAT fraction applied to the period's takings at each rate, NOT the sum of
/// per-line VAT.
///
/// Plutus reported the summed figure, and it is systematically low: each line's VAT is rounded to
/// the penny, and thousands of roundings do not equal the rounding of the total. Measured on the
/// live Kapow rollups, the gap was £10.78 under-declared.
/// </summary>
public class VatAccountingTests
{
    private static readonly VatBand[] UkBands =
    {
        new("standard", "20%", VatClass.Standard, 2000, DateTime.UnixEpoch),
        new("reduced", "5%", VatClass.Reduced, 500, DateTime.UnixEpoch),
        new("zero", "Zero rated", VatClass.Zero, 0, DateTime.UnixEpoch),
    };

    [Fact]
    public void Output_tax_is_the_VAT_fraction_one_sixth_at_twenty_percent()
    {
        // Notice 700 §17: the VAT fraction for 20% is 20/120 = 1/6.
        Assert.Equal(100, VatAccounting.OutputTaxOn(600, 2000));
        Assert.Equal(250, VatAccounting.OutputTaxOn(1499, 2000));      // £14.99 → £2.50
        Assert.Equal(1_000_000, VatAccounting.OutputTaxOn(6_000_000, 2000));
        // 5% → 5/105
        Assert.Equal(25, VatAccounting.OutputTaxOn(525, 500));
        // zero-rated AND exempt both yield no output tax
        Assert.Equal(0, VatAccounting.OutputTaxOn(40_364_972, 0));
    }

    [Fact]
    public void Rounding_is_to_the_NEAREST_penny_never_down()
    {
        // HMRC's round-down concession is explicitly NOT available to retailers (VATREC12020);
        // "round up and down to the nearest 1p" is. 105p at 20% is exactly 17.5p of VAT.
        Assert.Equal(18, VatAccounting.OutputTaxOn(105, 2000));
        Assert.Equal(17, VatAccounting.OutputTaxOn(104, 2000));
    }

    [Fact]
    public void Summing_per_line_VAT_understates_the_return_which_is_why_the_fraction_is_used()
    {
        // The defect, reproduced. 1,000 sales of a £1.05 standard-rated item: each line's VAT
        // rounds to 18p… but the till derives VAT as gross − net, and the net rounds UP, so each
        // line charges 17p. Summed, that is £10 short of the liability on the same takings.
        const int lines = 1_000;
        const long unitInc = 105;
        var perLineCharged = unitInc - (long)Math.Round(unitInc / 1.2m, MidpointRounding.AwayFromZero); // 105 − 88 = 17
        Assert.Equal(17, perLineCharged);

        var summed = perLineCharged * lines;                                   // 17,000p
        var due = VatAccounting.OutputTaxOn(unitInc * lines, 2000);            // fraction on takings

        Assert.Equal(17_500, due);
        Assert.Equal(17_000, summed);
        Assert.Equal(500, due - summed);   // £5.00 under-declared on this basket alone
    }

    [Fact]
    public void Takings_are_grouped_by_BAND_so_a_derived_rate_cannot_fragment_a_return()
    {
        // A till derives a line's rate from its price pair, so ONE 20% band arrives as a spread.
        // Kapow's live return was split across six standard-rate buckets before this.
        foreach (var derived in new[] { 1993, 1995, 1997, 1999, 2000, 2002, 2004 })
        {
            var band = VatAccounting.BandFor(UkBands, derived);
            Assert.Equal("standard", band!.Value.Key);
            Assert.Equal(2000, band.Value.RateBp);
        }
        Assert.Equal("zero", VatAccounting.BandFor(UkBands, 0)!.Value.Key);
        Assert.Equal("reduced", VatAccounting.BandFor(UkBands, 500)!.Value.Key);
    }

    [Fact]
    public void Genuinely_off_band_takings_belong_to_NO_band_and_must_not_be_folded_into_one()
    {
        // Kapow has a real 2500bp line. Snapping it to 20% would silently invent £-figures for a
        // rate nobody charges; it has to surface as unclassified for a human.
        Assert.Null(VatAccounting.BandFor(UkBands, 2500));
        Assert.Null(VatAccounting.BandFor(UkBands, 1000));
        Assert.Null(VatAccounting.BandFor(Array.Empty<VatBand>(), 2000));
    }

    [Fact]
    public void Zero_rated_and_exempt_are_distinct_classes_at_the_same_rate()
    {
        // Both charge the customer nothing; only one permits input-tax recovery. A model that
        // stores just "0%" can never produce a partial-exemption figure.
        var zero = new VatBand("zero", "Zero rated", VatClass.Zero, 0, DateTime.UnixEpoch);
        var exempt = new VatBand("exempt", "Exempt", VatClass.Exempt, 0, DateTime.UnixEpoch);

        Assert.Equal(zero.RateBp, exempt.RateBp);            // indistinguishable by rate…
        Assert.NotEqual(zero.Class, exempt.Class);           // …but not by class
        Assert.Equal(0, VatAccounting.OutputTaxOn(50_000, zero.RateBp));
        Assert.Equal(0, VatAccounting.OutputTaxOn(50_000, exempt.RateBp));
    }

    [Fact]
    public void A_return_line_reports_the_due_figure_the_charged_figure_and_the_gap()
    {
        // The gap is never hidden: small means penny rounding, large means a pricing fault.
        var line = new VatReturnLine("standard", "20%", VatClass.Standard, 2000,
            GrossPence: 15_379_518, OutputTaxPence: VatAccounting.OutputTaxOn(15_379_518, 2000),
            ChargedPence: 2_562_175);

        Assert.Equal(2_563_253, line.OutputTaxPence);        // the legal figure
        Assert.Equal(1_078, line.RoundingDifferencePence);   // £10.78 the tills under-charged
        Assert.Equal(line.GrossPence - line.OutputTaxPence, line.NetPence);
    }
}
