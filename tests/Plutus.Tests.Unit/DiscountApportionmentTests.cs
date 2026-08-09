using System;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// ⚠ EVERY TEST HERE IS ABOUT THE PARTS SUMMING TO THE WHOLE. A penny lost in apportionment is not
/// a rounding nicety — the server rejects the entire sale as `202 Quarantined`, which the outbox
/// pusher treats as terminal, so the sale is destroyed after the customer has paid and left.
/// </summary>
public class DiscountApportionmentTests
{
    [Fact]
    public void The_parts_sum_to_the_whole_even_when_it_does_not_divide()
    {
        // £10 across three equal lines is 333.33p each — the classic missing penny.
        var shares = DiscountApportionment.Across(1000, new long[] { 1000, 1000, 1000 });

        Assert.Equal(1000, shares.Sum());
        // The odd penny goes somewhere, exactly once.
        Assert.Equal(new long[] { 334, 333, 333 }, shares);
    }

    [Fact]
    public void A_discount_is_split_in_proportion_to_what_each_line_is_worth()
    {
        var shares = DiscountApportionment.Across(300, new long[] { 1000, 2000 });

        Assert.Equal(new long[] { 100, 200 }, shares);
        Assert.Equal(300, shares.Sum());
    }

    /// <summary>⚠ The whole point of largest-remainder. Brute-forced across many awkward splits,
    /// because "it summed correctly for my example" is how the missing penny survives review.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(1000)]
    [InlineData(4999)]
    public void The_parts_always_sum_to_the_whole_across_awkward_baskets(long discount)
    {
        var lines = new long[] { 1999, 550, 4999, 1, 12345, 700 };

        foreach (var take in Enumerable.Range(1, lines.Length))
        {
            var subset = lines.Take(take).ToArray();
            if (discount > subset.Sum()) continue;

            var shares = DiscountApportionment.Across(discount, subset);

            Assert.Equal(discount, shares.Sum());
            // ⚠ And no line is taken below zero — a negative line gross fails the server's own
            // per-line invariant, so an over-large share would trade one rejection for another.
            Assert.All(shares.Select((s, i) => (Share: s, Gross: subset[i])),
                x => Assert.InRange(x.Share, 0, x.Gross));
        }
    }

    /// <summary>⚠ Determinism. Two tills apportioning the same basket must put the odd penny on the
    /// SAME line, or one basket produces two different receipts.</summary>
    [Fact]
    public void Ties_break_on_the_earlier_line_so_two_tills_agree()
    {
        var first = DiscountApportionment.Across(100, new long[] { 300, 300, 300 });
        var again = DiscountApportionment.Across(100, new long[] { 300, 300, 300 });

        Assert.Equal(first, again);
        Assert.Equal(new long[] { 34, 33, 33 }, first);
    }

    /// <summary>⚠ Refused, not clamped. Silently capping would send a total nobody at the counter
    /// agreed to — and Matt's rule for refunds is the same shape: you cannot give back more than
    /// was paid.</summary>
    [Fact]
    public void A_discount_larger_than_the_goods_is_refused()
    {
        var tooBig = Assert.Throws<ArgumentOutOfRangeException>(
            () => DiscountApportionment.Across(5000, new long[] { 1000, 1000 }));

        Assert.Contains("cannot exceed", tooBig.Message);
    }

    [Fact]
    public void A_discount_of_exactly_the_whole_basket_is_allowed()
    {
        var shares = DiscountApportionment.Across(2000, new long[] { 1000, 1000 });
        Assert.Equal(new long[] { 1000, 1000 }, shares);
    }

    /// <summary>⚠ A negative discount would ADD money to the line, and the sale would total more
    /// than the customer was charged.</summary>
    [Fact]
    public void A_negative_discount_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DiscountApportionment.Across(-100, new long[] { 1000 }));
    }

    [Fact]
    public void Nothing_off_takes_nothing_off()
    {
        Assert.Equal(new long[] { 0, 0 }, DiscountApportionment.Across(0, new long[] { 1000, 500 }));
    }

    /// <summary>A free line attracts none of the discount — there is nothing to take off it.</summary>
    [Fact]
    public void A_zero_value_line_takes_no_share()
    {
        var shares = DiscountApportionment.Across(100, new long[] { 0, 1000 });
        Assert.Equal(new long[] { 0, 100 }, shares);
    }
}
