using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// ⚠⚠ MATT, 2026-08-13: *"You cannot have a discount greater than the basket."* Binding default 22.
///
/// These pin the gate that stops a basket becoming un-completable. Without it, £5 off plus £5 off an
/// £8 basket threw inside `CheckoutCommit.ApplyAlterations` and the operator was told *"Nothing has
/// been taken — try again"* — advice that could never work, with no hint that a discount was why.
/// </summary>
public class BasketDiscountTests
{
    // ── headroom ──

    [Fact]
    public void Headroom_is_the_basket_less_what_is_already_off()
    {
        Assert.Equal(800, BasketDiscounts.HeadroomPence(800, 0));
        Assert.Equal(300, BasketDiscounts.HeadroomPence(800, 500));
        Assert.Equal(0, BasketDiscounts.HeadroomPence(800, 800));
    }

    /// <summary>⚠ Never negative. An over-discounted basket has NO room, not negative room —
    /// a negative headroom compared against a request would silently allow anything.</summary>
    [Fact]
    public void Headroom_never_goes_negative()
    {
        Assert.Equal(0, BasketDiscounts.HeadroomPence(800, 1200));
        Assert.Equal(0, BasketDiscounts.HeadroomPence(0, 0));
        Assert.Equal(0, BasketDiscounts.HeadroomPence(-500, 0));
    }

    // ── the decision ──

    [Fact]
    public void A_discount_within_the_basket_is_allowed()
    {
        var d = BasketDiscounts.Authorise(500, 800, 0);
        Assert.True(d.IsAllowed);
        Assert.Equal(800, d.HeadroomPence);
        Assert.Equal(0, d.AlreadyPence);
    }

    /// <summary>⚠ THE BOUNDARY IS INCLUSIVE. "Everything free" is a real thing a manager does; only
    /// MORE than everything is refused. An off-by-one here would block a legitimate 100% staff
    /// discount, which is a refusal nobody would understand at a counter.</summary>
    [Fact]
    public void A_discount_equal_to_the_whole_basket_is_allowed()
    {
        Assert.True(BasketDiscounts.Authorise(800, 800, 0).IsAllowed);
    }

    [Fact]
    public void A_penny_over_the_basket_is_refused()
    {
        var d = BasketDiscounts.Authorise(801, 800, 0);
        Assert.False(d.IsAllowed);
        Assert.Equal(DiscountVerdict.ExceedsBasket, d.Verdict);
        Assert.Equal(800, d.HeadroomPence);
    }

    /// <summary>THE CASE THAT MADE SALES UN-COMPLETABLE: £5 off, then £5 more off an £8 basket. The
    /// second is judged against what is LEFT (£3), not against the original £8 — judging it any
    /// other way passes this gate and throws at the commit one.</summary>
    [Fact]
    public void A_second_discount_is_judged_against_what_is_LEFT()
    {
        var d = BasketDiscounts.Authorise(500, 800, 500);

        Assert.False(d.IsAllowed);
        Assert.Equal(DiscountVerdict.ExceedsBasket, d.Verdict);
        Assert.Equal(300, d.HeadroomPence);   // what the operator may still take off
        Assert.Equal(500, d.AlreadyPence);    // ...and why it is less than the basket total
    }

    /// <summary>⚠ `AlreadyPence` exists so the caller can explain a headroom SMALLER than the basket.
    /// Told only "the most you can take off is £3.00" on an £8 basket, an operator reasonably decides
    /// the till is wrong.</summary>
    [Fact]
    public void The_decision_carries_what_is_already_off_so_the_message_can_explain_itself()
    {
        Assert.Equal(500, BasketDiscounts.Authorise(100, 800, 500).AlreadyPence);
    }

    [Fact]
    public void A_fully_discounted_basket_has_no_room_left()
    {
        var d = BasketDiscounts.Authorise(1, 800, 800);
        Assert.False(d.IsAllowed);
        Assert.Equal(DiscountVerdict.ExceedsBasket, d.Verdict);
        Assert.Equal(0, d.HeadroomPence);
    }

    /// <summary>⚠ Its own verdict, not a zero headroom. A refund-only basket needs to be told that a
    /// return cannot be discounted — "£0.00 is the most you can take off" is true and explains
    /// nothing. `VatLineMath.ForLine` drops a discount on a return by design, so money apportioned
    /// onto one vanishes and the sale stops reconciling.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-2000)]   // a refund-only basket
    public void A_basket_with_no_sale_lines_is_its_own_refusal(long saleLinesGross)
    {
        var d = BasketDiscounts.Authorise(500, saleLinesGross, 0);
        Assert.Equal(DiscountVerdict.NothingToDiscount, d.Verdict);
        Assert.Equal(0, d.HeadroomPence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_discount_of_nothing_or_less_is_refused(long requested)
    {
        var d = BasketDiscounts.Authorise(requested, 800, 0);
        Assert.Equal(DiscountVerdict.NotAnAmount, d.Verdict);
        // ⚠ Still reports the headroom: the refusal is about the REQUEST, and the operator is about
        // to type another number.
        Assert.Equal(800, d.HeadroomPence);
    }

    /// <summary>⚠ The shared rule and the commit-time guard must agree on the boundary, or a basket
    /// passes one and throws at the other. This asserts the pair directly: anything this rule allows
    /// is something `DiscountApportionment.Across` will accept.</summary>
    [Theory]
    [InlineData(800, 0, 800)]     // the whole basket
    [InlineData(800, 500, 300)]   // exactly the remainder
    [InlineData(1999, 0, 1999)]
    public void Whatever_this_rule_allows_the_apportioner_accepts(
        long gross, long already, long requested)
    {
        Assert.True(BasketDiscounts.Authorise(requested, gross, already).IsAllowed);

        // The commit path spreads it over the lines' REMAINING gross — the same figure.
        var remaining = gross - already;
        var shares = DiscountApportionment.Across(requested, new[] { remaining });
        Assert.Equal(requested, shares[0]);
    }
}
