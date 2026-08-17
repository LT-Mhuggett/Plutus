using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// What an operator typed into the **Percent** box (Matt's ruling, 2026-08-17: *"make it %"*).
///
/// ⚠⚠ THE BUG THESE EXIST FOR. The MAUI till did <c>item.Price * decimal.Parse(typed)</c> straight
/// off the box, so typing <c>10</c> for 10% multiplied the price **by ten** — a £20 item took £200
/// off. It was contained only because `DiscountDecision` refuses a discount larger than the basket,
/// so nobody was overcharged; what actually happened was that **no percentage discount above 100%
/// could ever be applied**, and the operator was told the basket was too small rather than that they
/// had typed the wrong thing.
/// </summary>
public class PercentDiscountInputTests
{
    // ── the ruling ────────────────────────────────────────────────────────────────────────────

    /// <summary>⚠⚠ 10 MEANS 10%. This is the ruling, and the one assertion the old code failed.</summary>
    [Theory]
    [InlineData("10", 0.10)]
    [InlineData("5", 0.05)]
    [InlineData("100", 1.00)]
    [InlineData("0", 0.00)]
    [InlineData("12.5", 0.125)]
    [InlineData("33.33", 0.3333)]
    public void A_typed_percent_becomes_its_fraction(string typed, double expected)
    {
        Assert.Equal((decimal)expected, PercentDiscountInput.FractionFromTyped(typed));
    }

    /// <summary>
    /// ⚠⚠ AND THE FRACTION IS SAFE TO HAND STRAIGHT TO THE MONEY RULE. `LineDiscounts.Percentage`
    /// throws above 1, so this is the contract between the two: anything this returns, that accepts.
    /// A £20 item at 10% must be £2 off — the old maths made it £200.
    /// </summary>
    [Fact]
    public void Ten_percent_off_a_twenty_pound_item_is_two_pounds()
    {
        var fraction = PercentDiscountInput.FractionFromTyped("10")!.Value;

        var off = LineDiscounts.Percentage(unitIncPence: 2000, quantity: 1, fraction, isReturn: false);

        Assert.Equal(200, off);          // £2.00
        Assert.NotEqual(20000, off);     // £200.00 — what `Price * 10` produced
    }

    [Fact]
    public void One_hundred_percent_is_the_whole_line_and_no_more()
    {
        var fraction = PercentDiscountInput.FractionFromTyped("100")!.Value;

        Assert.Equal(2000, LineDiscounts.Percentage(2000, 1, fraction, isReturn: false));
    }

    // ── what is refused ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ OVER 100 IS REFUSED HERE, POLITELY. It must not reach `LineDiscounts.Percentage`, which
    /// throws — an operator mistyping into a till is not a programming error and must get a sentence
    /// rather than a crash on the selling screen.
    /// </summary>
    [Theory]
    [InlineData("101")]
    [InlineData("1000")]
    [InlineData("100.01")]
    public void More_than_one_hundred_percent_is_refused(string typed) =>
        Assert.Null(PercentDiscountInput.FractionFromTyped(typed));

    /// <summary>
    /// ⚠ NEGATIVE IS NOT A DISCOUNT. The old code applied `Math.Abs`, which turned "-10" into a 10%
    /// discount — quietly doing something the operator never asked for.
    /// </summary>
    [Theory]
    [InlineData("-10")]
    [InlineData("-0.5")]
    public void A_negative_percent_is_refused_not_flipped(string typed) =>
        Assert.Null(PercentDiscountInput.FractionFromTyped(typed));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("ten")]
    [InlineData("1o")]
    public void Something_that_is_not_a_number_is_refused(string typed) =>
        Assert.Null(PercentDiscountInput.FractionFromTyped(typed));

    /// <summary>⚠ A trailing % is ACCEPTED — operators type the character they just read off the
    /// label, and refusing it is the pedantry that gets a till called broken.</summary>
    [Theory]
    [InlineData("10%")]
    [InlineData("10 %")]
    [InlineData(" 10% ")]
    public void A_typed_percent_sign_is_accepted(string typed) =>
        Assert.Equal(0.10m, PercentDiscountInput.FractionFromTyped(typed));

    // ── the legacy round trip ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ LEGACY ROWS HOLD A FRACTION. `DiscountModel.Amount` from the old NatApp table is 0.1 for
    /// 10% — which is exactly why the original multiply looked plausible. A box that now means
    /// "percent" must PRE-FILL as 10, or every migrated discount reads as 0.1% and applies a
    /// hundredth of itself.
    ///
    /// ⚠ Matt, 2026-08-17: *"Do not drop anything… retain all legacy sales"* — legacy discount rows
    /// have to keep working through this change, which is what this pins.
    /// </summary>
    [Theory]
    [InlineData(0.10, 10)]
    [InlineData(0.05, 5)]
    [InlineData(1.00, 100)]
    [InlineData(0.125, 12.5)]
    [InlineData(0.00, 0)]
    public void A_legacy_fraction_pre_fills_as_a_percent(double stored, double shown) =>
        Assert.Equal((decimal)shown, PercentDiscountInput.PercentFromFraction((decimal)stored));

    /// <summary>⚠ And the round trip closes: what is shown, typed back, is what was stored. Otherwise
    /// re-saving a migrated discount would silently change it.</summary>
    [Theory]
    [InlineData(0.10)]
    [InlineData(0.05)]
    [InlineData(0.125)]
    [InlineData(1.00)]
    public void Showing_then_re_typing_a_legacy_fraction_is_lossless(double stored)
    {
        var shown = PercentDiscountInput.PercentFromFraction((decimal)stored);

        Assert.Equal((decimal)stored, PercentDiscountInput.FractionFromTyped(shown.ToString()));
    }

    // ── what the operator is told ─────────────────────────────────────────────────────────────

    /// <summary>⚠ The refusal names the bound AND gives an example — "invalid" leaves somebody
    /// guessing whether the problem is the number, the format, or the till.</summary>
    [Fact]
    public void The_refusal_shows_what_was_typed_and_what_was_wanted()
    {
        var message = PercentDiscountInput.RefusalMessage("1000");

        Assert.Contains("1000", message);
        Assert.Contains("100", message);
        Assert.Contains("10 for 10%", message);
    }
}
