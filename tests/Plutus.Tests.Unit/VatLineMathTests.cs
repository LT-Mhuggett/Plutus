using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The shared till VAT arithmetic, pinned to the numbers the WEB TILL produces
/// (`Plutus.Frontend.WebApp/src/api.ts`, the historic reference implementation).
///
/// WHY THESE TESTS MATTER MORE THAN THEY LOOK: the point of `VatLineMath` is that a till on Windows,
/// macOS or Linux gets the same figures without re-deriving them. That only holds if this code
/// reproduces the web till exactly — so every case below is a number the web till would ship for the
/// same basket line. If one of these ever has to change, the web till changes in the same commit or
/// the two tills disagree penny-for-penny on every VAT return, silently and forever.
/// </summary>
public class VatLineMathTests
{
    [Fact]
    public void A_real_Kapow_line_declares_2002bp_not_2000()
    {
        // £14.99 inc / £12.49 ex is 20% priced to the penny, and the pair yields 2001.6 → 2002.
        // ⚠ This wobble is CORRECT. An earlier ingest check compared declared rates against clean
        // band values and would have quarantined ordinary trade.
        var line = VatLineMath.ForLine(1499, 1249, quantity: 1, discountPence: 0, isReturn: false);

        Assert.Equal(2002, line.VatRateBp);
        Assert.Equal(1499, line.LineGrossPence);
        Assert.Equal(1249, line.LineExPence);
        Assert.Equal(250, line.VatAmountPence);      // gross − ex, not rate arithmetic
    }

    [Fact]
    public void VAT_is_gross_minus_ex_which_DIFFERS_from_rate_arithmetic()
    {
        // The divergence is real and one-directional, and the receipt is what the customer holds —
        // so gross−ex wins. Rate arithmetic on this line would give round(1499 × 2002/12002) = 250…
        // pick a line where they visibly differ: £1.00 inc / £0.83 ex.
        var line = VatLineMath.ForLine(100, 83, 1, 0, false);
        Assert.Equal(17, line.VatAmountPence);                       // 100 − 83

        var viaRate = (long)Math.Round(100 * line.VatRateBp / (10000m + line.VatRateBp),
            MidpointRounding.AwayFromZero);
        Assert.Equal(17, viaRate);   // agrees here…
        // …but the SUM over ten such lines is 170, while the fraction on the £10.00 total is 167 —
        // which is exactly why the VAT return uses the fraction on takings, not the sum of lines.
        Assert.Equal(167, VatAccounting.OutputTaxOn(1000, 2000));
    }

    [Fact]
    public void A_zero_rated_line_declares_no_rate_and_no_VAT()
    {
        var line = VatLineMath.ForLine(800, 800, 1, 0, false);
        Assert.Equal(0, line.VatRateBp);
        Assert.Equal(0, line.VatAmountPence);
        Assert.Equal(800, line.LineGrossPence);
    }

    [Fact]
    public void A_free_line_has_no_price_pair_to_reason_about_and_declares_nothing()
    {
        // Guessing a rate for a £0 line is worse than declaring none.
        Assert.Equal(0, VatLineMath.RateBpFromPair(0, 0));
        Assert.Equal(0, VatLineMath.RateBpFromPair(500, 0));
        Assert.Equal(0, VatLineMath.RateBpFromPair(0, 500));
    }

    [Fact]
    public void Quantity_multiplies_the_pair_and_the_rate_is_unchanged()
    {
        var line = VatLineMath.ForLine(1499, 1249, quantity: 3, discountPence: 0, isReturn: false);
        Assert.Equal(3, line.Qty);
        Assert.Equal(4497, line.LineGrossPence);
        Assert.Equal(3747, line.LineExPence);
        Assert.Equal(750, line.VatAmountPence);
        Assert.Equal(2002, line.VatRateBp);      // a property of the pair, not the quantity
    }

    [Fact]
    public void A_discount_is_scaled_into_the_ex_VAT_figure_by_the_ex_over_inc_ratio()
    {
        // £14.99/£12.49 with £1.00 off: gross 1399. The discount's ex-VAT share is
        // round(100 × 1249/1499) = round(83.32) = 83, so ex = 1249 − 83 = 1166 and VAT = 233.
        var line = VatLineMath.ForLine(1499, 1249, quantity: 1, discountPence: 100, isReturn: false);

        Assert.Equal(1399, line.LineGrossPence);
        Assert.Equal(1166, line.LineExPence);
        Assert.Equal(233, line.VatAmountPence);
        Assert.Equal(100, line.DiscountPence);
        // The invariant that matters: gross − ex == VAT, always, however the discount rounded.
        Assert.Equal(line.LineGrossPence - line.LineExPence, line.VatAmountPence);
    }

    [Fact]
    public void A_RETURN_negates_every_money_figure_and_DROPS_the_discount()
    {
        // ⚠ Refunding a discounted sale returns what the customer actually PAID. Re-applying the
        // discount to the refund would give them back less than they handed over.
        var line = VatLineMath.ForLine(1499, 1249, quantity: 1, discountPence: 100, isReturn: true);

        Assert.Equal(-1, line.Qty);
        Assert.Equal(-1499, line.LineGrossPence);
        Assert.Equal(-1249, line.LineExPence);
        Assert.Equal(-250, line.VatAmountPence);
        Assert.Equal(0, line.DiscountPence);      // dropped, not negated
        Assert.Equal(2002, line.VatRateBp);       // still a property of the pair
    }

    [Fact]
    public void A_negative_quantity_is_REFUSED_rather_than_silently_double_negated()
    {
        // Passing qty −1 with isReturn:true would negate twice and produce a POSITIVE refund — money
        // moving the wrong way, with nothing to catch it. Make the misuse impossible to express.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VatLineMath.ForLine(1499, 1249, quantity: -1, discountPence: 0, isReturn: true));
    }

    [Fact]
    public void Rounding_follows_the_web_till_not_dotnet_bankers_rounding()
    {
        // ⚠ JS Math.round(x.5) goes away from zero; .NET's default is banker's rounding (to even).
        // A discount whose ex-VAT share lands exactly on a half is where the two would part company.
        // £2.00 inc / £1.00 ex (100%), £1.00 discount → ex share = round(100 × 0.5) = 50 exactly;
        // pick a genuine half: £3.00 inc / £2.00 ex, discount 3 → 3 × 2/3 = 2.0 (no half).
        // The reliable half: inc 200, ex 100, discount 1 → 1 × 0.5 = 0.5 → 1 (away from zero),
        // whereas banker's rounding would give 0.
        var line = VatLineMath.ForLine(200, 100, 1, discountPence: 1, isReturn: false);
        Assert.Equal(199, line.LineGrossPence);
        Assert.Equal(99, line.LineExPence);          // 100 − 1, NOT 100 − 0
        Assert.Equal(100, line.VatAmountPence);
    }

    [Fact]
    public void The_rate_is_derived_the_same_way_the_ingest_guard_expects()
    {
        // The ingest check judges the price PAIR, and this is the pair it will see. Agreement here
        // is what stops the compliance guard quarantining a till's ordinary trade.
        foreach (var (inc, ex, expected) in new[]
        {
            (1499L, 1249L, 2002), (1200L, 1000L, 2000), (1175L, 1000L, 1750),
            (525L, 500L, 500), (800L, 800L, 0), (1100L, 1000L, 1000),
        })
        {
            Assert.Equal(expected, VatLineMath.RateBpFromPair(inc, ex));
            Assert.True(VatRateHistory.Explains(inc, ex, expected),
                $"{inc}/{ex} should be explained by its own derived rate {expected}bp");
        }
    }
}
