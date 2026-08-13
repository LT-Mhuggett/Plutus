using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The members' auto-discount eligibility rule (C1/C2). ⚠ These tests are the pin against the web
/// till's `basket.ts` case `applyMemberDiscount` and `TillPage.tsx:122–123` — the .NET half of a
/// twin whose other half is TypeScript. They exist because MAUI has to grow this rule to close a
/// LIVE money difference, and a second implementation that merely looked right would charge members
/// differently on two counters for the same basket.
/// </summary>
public class MemberDiscountTests
{
    // ── does the membership grant anything at all ──

    [Theory]
    [InlineData(true, false, 0.10, true)]    // live Gold member
    [InlineData(true, true, 0.10, false)]    // ⚠ EXPIRED grants nothing, rate ignored not trusted
    [InlineData(true, false, 0.00, false)]   // a tier with no discount
    [InlineData(false, false, 0.10, false)]  // no customer attached
    public void Applies_only_for_a_live_membership_with_a_rate(
        bool hasMembership, bool expired, decimal rate, bool expected)
    {
        Assert.Equal(expected, MemberDiscount.Applies(hasMembership, expired, rate));
    }

    // ── which lines it touches ──

    [Fact]
    public void An_ordinary_line_is_eligible()
    {
        Assert.True(MemberDiscount.LineIsEligible(isReturn: false, hasDiscount: false, isGiftCard: false));
    }

    /// <summary>A refund gives back what the customer actually paid — discounting it would refund
    /// them LESS than they handed over. Same rule as <c>LineDiscounts</c>.</summary>
    [Fact]
    public void A_return_line_is_never_eligible()
    {
        Assert.False(MemberDiscount.LineIsEligible(isReturn: true, hasDiscount: false, isGiftCard: false));
    }

    /// <summary>⚠ NO STACKING. A manual or catalogue discount already on the line WINS. Stacking a
    /// members' rate on a staff discount is how a basket leaves for less than cost with nobody
    /// having chosen that.</summary>
    [Fact]
    public void A_line_that_already_has_a_discount_is_not_eligible()
    {
        Assert.False(MemberDiscount.LineIsEligible(isReturn: false, hasDiscount: true, isGiftCard: false));
    }

    /// <summary>⚠ A gift-card line is stored value — a LIABILITY, not a supply. Discounting it hands
    /// over £50 of spendable money for £45, and that £45 then buys discounted goods too.</summary>
    [Fact]
    public void A_gift_card_line_is_not_eligible()
    {
        Assert.False(MemberDiscount.LineIsEligible(isReturn: false, hasDiscount: false, isGiftCard: true));
    }

    // ── the composed call a till actually makes ──

    [Fact]
    public void ForLine_discounts_an_eligible_line_at_the_tier_rate()
    {
        // £4.40 × 1 at 10% = 44p
        Assert.Equal(44, MemberDiscount.ForLine(440, 1, 0.10m, true, false, false, false, false));
        // ⚠ Rounded on the WHOLE line, not per unit and summed: 3 × £1.99 = £5.97, 10% = 59.7p → 60p.
        // Per-unit would give 3 × 20p = 60p here but drifts on other quantities — the shared
        // LineDiscounts.Percentage owns that, and this asserts ForLine really delegates to it.
        Assert.Equal(60, MemberDiscount.ForLine(199, 3, 0.10m, true, false, false, false, false));
    }

    [Theory]
    [InlineData(true, false, false)]   // return
    [InlineData(false, true, false)]   // already discounted
    [InlineData(false, false, true)]   // gift card
    public void ForLine_gives_nothing_for_an_ineligible_line(bool isReturn, bool hasDiscount, bool isGiftCard)
    {
        Assert.Equal(0, MemberDiscount.ForLine(440, 1, 0.10m, true, false, isReturn, hasDiscount, isGiftCard));
    }

    [Fact]
    public void ForLine_gives_nothing_when_the_membership_does_not_apply()
    {
        Assert.Equal(0, MemberDiscount.ForLine(440, 1, 0.10m, hasMembership: true, expired: true,
            isReturn: false, hasDiscount: false, isGiftCard: false));
        Assert.Equal(0, MemberDiscount.ForLine(440, 1, 0.10m, hasMembership: false, expired: false,
            isReturn: false, hasDiscount: false, isGiftCard: false));
    }

    /// <summary>⚠ The guard inherited from <c>LineDiscounts.Percentage</c> must survive the
    /// composition: a percent NUMBER (10) where a fraction (0.10) belongs is how the legacy till
    /// multiplied a price BY TEN and charged it.</summary>
    [Fact]
    public void A_percent_number_instead_of_a_fraction_still_throws_through_ForLine()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            MemberDiscount.ForLine(440, 1, 10m, true, false, false, false, false));
    }

    // ── the label the customer reads ──

    [Theory]
    [InlineData("Gold", 0.10, "Gold 10%")]
    [InlineData("Club", 0.05, "Club 5%")]
    [InlineData("Platinum", 0.125, "Platinum 13%")]   // display rounds; the MATHS does not
    public void Label_matches_the_web_tills_wording(string tier, decimal rate, string expected)
    {
        Assert.Equal(expected, MemberDiscount.Label(tier, rate));
    }

    /// <summary>The rounded LABEL must never be mistaken for the rate used in the arithmetic —
    /// 12.5% displays as 13% but must discount by 12.5%.</summary>
    [Fact]
    public void A_rounded_label_does_not_change_the_money()
    {
        Assert.Equal("Platinum 13%", MemberDiscount.Label("Platinum", 0.125m));
        Assert.Equal(50, MemberDiscount.ForLine(400, 1, 0.125m, true, false, false, false, false)); // 12.5% of £4 = 50p
    }
}
