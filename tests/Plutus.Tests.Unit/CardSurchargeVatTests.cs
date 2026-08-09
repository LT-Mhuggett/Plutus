using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The card-surcharge VAT rule: the fee is further consideration for the main supply
/// (<i>Bookit</i> C-607/14 / <i>NEC</i> C-130/15), so it takes the basket's own VAT mix.
///
/// ⚠ THE FAILure MODE IS A PLAUSIBLE-LOOKING CONSTANT. Hardcoding 20% on the fee is what most
/// systems do, it reconciles perfectly — every invariant passes — and it is wrong on every basket
/// that is not purely standard-rated. Only these tests stand between that shortcut and a VAT
/// return.
/// </summary>
public class CardSurchargeVatTests
{
    /// <summary>A standard-rated basket: the fee carries the same 20%.</summary>
    [Fact]
    public void On_a_standard_rated_basket_the_fee_is_standard_rated()
    {
        // £120 gross / £100 ex — pure 20%. A 50p fee → ex 42p (41.67 rounded), VAT 8p.
        var (inc, ex) = CardSurchargeVat.PairFor(50, 12000, 10000);

        Assert.Equal(50, inc);
        Assert.Equal(42, ex);
        // ⚠ 1905bp, not 2000: the pence rounding wobbles the derived rate, and the wobble is
        // price-dependent (till-design C2). The MONEY is exact; the declared rate is descriptive.
        Assert.Equal(1905, VatLineMath.RateBpFromPair(inc, ex));
    }

    /// <summary>
    /// ⚠ THE ONE A HARDCODED RATE GETS WRONG. A surcharge on zero-rated goods (children's books,
    /// most food) is itself zero-rated — the fee follows the goods, and charging VAT on it would
    /// put output tax on a VAT return that HMRC says is not due.
    /// </summary>
    [Fact]
    public void On_a_zero_rated_basket_the_fee_carries_no_VAT()
    {
        var (inc, ex) = CardSurchargeVat.PairFor(50, 10000, 10000);

        Assert.Equal(50, inc);
        Assert.Equal(50, ex);   // ex == inc → VAT 0, rate 0
    }

    /// <summary>A mixed basket: the fee is apportioned by value — half standard, half zero here,
    /// so the fee's effective rate sits in between.</summary>
    [Fact]
    public void On_a_mixed_basket_the_fee_is_apportioned_by_value()
    {
        // £120 standard (ex £100) + £120 zero-rated (ex £120) = gross 240, ex 220.
        var (inc, ex) = CardSurchargeVat.PairFor(120, 24000, 22000);

        Assert.Equal(120, inc);
        Assert.Equal(110, ex);  // 120 × 220/240 — VAT 10p, not the 20p a hardcoded rate would take
    }

    /// <summary>
    /// ⚠ ONE rounding, away from zero — and MULTIPLY BEFORE DIVIDING. 3 × 10000 ÷ 12000 is exactly
    /// 2.5; computed as 3 × (10000 ÷ 12000) it arrives as 2.4999… and rounds DOWN. This test
    /// failed against the ratio-first implementation and is what forced products-first.
    /// </summary>
    [Fact]
    public void The_ex_figure_rounds_away_from_zero_once()
    {
        var (_, ex) = CardSurchargeVat.PairFor(50, 12000, 10000);
        Assert.Equal(42, ex);   // 41.67 — truncation would say 41

        var (_, exHalf) = CardSurchargeVat.PairFor(3, 12000, 10000);
        Assert.Equal(3, exHalf); // exactly 2.5 — rounds AWAY to 3; banker's or ratio-first say 2
    }

    [Fact]
    public void A_zero_fee_is_a_zero_pair()
    {
        Assert.Equal((0L, 0L), CardSurchargeVat.PairFor(0, 12000, 10000));
    }

    /// <summary>⚠ A negative fee is a discount wearing the wrong hat, and the two must not blur —
    /// a return drops a discount and keeps a fee, so mixing them corrupts refunds.</summary>
    [Fact]
    public void A_negative_fee_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CardSurchargeVat.PairFor(-50, 12000, 10000));
    }

    /// <summary>⚠ No positive basket, nothing to follow — a surcharge on a refund-only basket has
    /// no supply to take its treatment from, and the caller is doing something illegal anyway.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void A_basket_with_no_positive_sale_value_is_refused(long gross)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CardSurchargeVat.PairFor(50, gross, 0));
    }

    /// <summary>An ex total outside [0, gross] means the caller summed the wrong lines.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(12001)]
    public void An_impossible_ex_total_is_refused(long ex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CardSurchargeVat.PairFor(50, 12000, ex));
    }

    // ── the fee itself ──

    /// <summary>Percent + flat, the shape of every acquirer's own pricing: 1.69% + 20p on £10 is
    /// 17p + 20p = 37p.</summary>
    [Fact]
    public void The_fee_is_percent_plus_flat()
    {
        Assert.Equal(37, CardSurchargeVat.FeePence(surchargeBp: 169, flatPence: 20, basketGrossPence: 1000));
    }

    [Fact]
    public void A_zero_setting_charges_nothing()
    {
        Assert.Equal(0, CardSurchargeVat.FeePence(0, 0, 10000));
    }

    /// <summary>⚠ Multiply before dividing, round once away from zero — 25bp of £1.00 is exactly
    /// 2.5p and must round to 3, the same convention as every shared money rule.</summary>
    [Fact]
    public void The_percent_half_rounds_away_from_zero()
    {
        Assert.Equal(3, CardSurchargeVat.FeePence(25, 0, 1000));
    }

    /// <summary>A refund-only or empty basket attracts NO fee — and this path returns zero rather
    /// than throwing, because "don't surcharge this" is an answer, not an error.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void No_positive_sale_value_means_no_fee(long gross)
    {
        Assert.Equal(0, CardSurchargeVat.FeePence(169, 20, gross));
    }

    [Fact]
    public void A_negative_setting_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CardSurchargeVat.FeePence(-1, 0, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardSurchargeVat.FeePence(0, -1, 1000));
    }

    /// <summary>
    /// The pair feeds the SAME line arithmetic as every other line — rate derived from the pair,
    /// VAT = gross − ex. The fee line is ordinary, which is the whole point: nothing downstream
    /// needs to know it is a fee.
    /// </summary>
    [Fact]
    public void The_pair_makes_an_ordinary_line()
    {
        var (inc, ex) = CardSurchargeVat.PairFor(120, 24000, 22000);
        var line = VatLineMath.ForLine(inc, ex, quantity: 1, discountPence: 0, isReturn: false);

        Assert.Equal(120, line.LineGrossPence);
        Assert.Equal(10, line.VatAmountPence);
    }
}
