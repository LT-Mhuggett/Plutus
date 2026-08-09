using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP11's hard gate — whether a refund may proceed, and for how much.
///
/// ⚠ The failure being designed out: a customer returns an item on till A that was bought on till
/// B. Till A has no local record, so it either refuses a legitimate refund or — far worse —
/// accepts one that has already been refunded elsewhere. **Only the server record knows about
/// refunds taken on other tills**, which is why the source of the record is the first input.
/// </summary>
public class RefundRulesTests
{
    // ── the remainder ──

    [Theory]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 400, 600)]
    [InlineData(1000, 1000, 0)]
    public void Remaining_is_what_was_paid_less_what_has_gone_back(
        long original, long already, long expected) =>
        Assert.Equal(expected, RefundRules.RemainingPence(original, already));

    /// <summary>⚠ Corrupt data reads as "nothing left", never as a negative some caller subtracts
    /// into a payout. More refunded than was ever paid is a bug somewhere; it must not become money
    /// leaving the till.</summary>
    [Theory]
    [InlineData(1000, 1500)]   // over-refunded
    [InlineData(0, 0)]         // a zero-value sale
    [InlineData(-500, 0)]      // a negative "sale"
    [InlineData(1000, -100)]   // a negative refund history
    public void Remaining_never_goes_negative(long original, long already) =>
        Assert.True(RefundRules.RemainingPence(original, already) >= 0);

    // ── the rolling window ──

    [Fact]
    public void A_recent_local_sale_is_refundable_offline()
    {
        var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SaleRecordSource.LocalInWindow,
            RefundRules.ClassifyLocal(now.AddDays(-13), now));
    }

    [Fact]
    public void A_local_sale_past_the_window_is_not()
    {
        var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SaleRecordSource.LocalOutsideWindow,
            RefundRules.ClassifyLocal(now.AddDays(-15), now));
    }

    /// <summary>⚠ A future-dated sale counts as in-window. Till clocks drift and get set wrong, and
    /// treating a slightly-future timestamp as "too old" would refuse refunds on sales rung up
    /// minutes earlier on the same machine.</summary>
    [Fact]
    public void A_clock_skewed_future_sale_is_still_in_window()
    {
        var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SaleRecordSource.LocalInWindow,
            RefundRules.ClassifyLocal(now.AddHours(2), now));
    }

    [Fact]
    public void The_window_default_matches_the_outbox_retention_it_relies_on() =>
        // A till must not claim to refund from a record the outbox has already pruned.
        Assert.Equal(TimeSpan.FromDays(14), RefundRules.DefaultRollingWindow);

    // ── the decision ──

    [Theory]
    [InlineData(SaleRecordSource.Server)]
    [InlineData(SaleRecordSource.LocalInWindow)]
    public void A_known_sale_refunds_up_to_its_remainder(SaleRecordSource source)
    {
        var d = RefundRules.Authorise(source, originalPence: 1000, alreadyRefundedPence: 0, requestedPence: 1000);

        Assert.True(d.IsAllowed);
        Assert.Equal(1000, d.AllowedPence);
        Assert.False(d.WasCapped);
    }

    /// <summary>The DoD says "caps at the refundable remainder" — so cap, and flag it. Handing over
    /// less than was asked for without saying so is how a refund becomes an argument.</summary>
    [Fact]
    public void Asking_for_more_than_is_left_caps_and_says_so()
    {
        var d = RefundRules.Authorise(SaleRecordSource.Server, 1000, alreadyRefundedPence: 600, requestedPence: 1000);

        Assert.True(d.IsAllowed);
        Assert.Equal(400, d.AllowedPence);
        Assert.True(d.WasCapped);
        Assert.Equal(400, d.RemainingPence);
        Assert.Equal(600, d.AlreadyRefundedPence);
    }

    [Fact]
    public void A_fully_refunded_sale_gives_nothing_back()
    {
        var d = RefundRules.Authorise(SaleRecordSource.Server, 1000, 1000, 500);

        Assert.Equal(RefundVerdict.NothingLeft, d.Verdict);
        Assert.Equal(0, d.AllowedPence);
    }

    /// <summary>⚠ THE ONE THAT MATTERS. Offline, past the window, the till cannot know what has
    /// already been refunded on another till — so it refuses and says why. Never a silent
    /// acceptance, and never a payout on a guess.</summary>
    [Fact]
    public void An_old_sale_this_till_cannot_verify_is_REFUSED_not_guessed()
    {
        var d = RefundRules.Authorise(SaleRecordSource.LocalOutsideWindow, 1000, 0, 1000);

        Assert.Equal(RefundVerdict.NeedsConnection, d.Verdict);
        Assert.Equal(0, d.AllowedPence);
        Assert.False(d.IsAllowed);
        Assert.NotEmpty(d.Reason);
    }

    /// <summary>⚠ "No such sale" is NOT "needs connection". Telling an operator to check the network
    /// when the receipt simply isn't ours sends them to reboot a router for no reason, with a
    /// customer waiting.</summary>
    [Fact]
    public void An_unknown_sale_is_answered_negatively_rather_than_blamed_on_the_network()
    {
        var d = RefundRules.Authorise(SaleRecordSource.NotFound, 1000, 0, 1000);

        Assert.Equal(RefundVerdict.UnknownSale, d.Verdict);
        Assert.NotEqual(RefundVerdict.NeedsConnection, d.Verdict);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_nonsense_request_is_never_dressed_up_as_a_money_decision(long requested)
    {
        var d = RefundRules.Authorise(SaleRecordSource.Server, 1000, 0, requested);

        Assert.False(d.IsAllowed);
        Assert.Equal(0, d.AllowedPence);
    }

    /// <summary>⚠ No refusal may be silent — every non-allowed verdict has to carry something a
    /// cashier can act on, because the operator is standing in front of the customer.</summary>
    [Theory]
    [InlineData(SaleRecordSource.NotFound)]
    [InlineData(SaleRecordSource.LocalOutsideWindow)]
    public void Every_refusal_explains_itself(SaleRecordSource source)
    {
        var d = RefundRules.Authorise(source, 1000, 0, 1000);

        Assert.False(d.IsAllowed);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    /// <summary>⚠ A source added by a later build must fail CLOSED. A new enum member arriving at
    /// an old till has to refuse, not fall through into a payout.</summary>
    [Fact]
    public void An_unrecognised_record_source_refuses_rather_than_paying_out()
    {
        var d = RefundRules.Authorise((SaleRecordSource)99, 1000, 0, 1000);

        Assert.False(d.IsAllowed);
        Assert.Equal(0, d.AllowedPence);
    }

    /// <summary>⚠ The money-losing case, stated as its own test: a local record CANNOT see a refund
    /// taken on another till. Both sources cap at the remainder they were given — so the caller's
    /// duty is to prefer the SERVER record whenever it has one, and that is what makes the offline
    /// path a fallback rather than an equal.</summary>
    [Fact]
    public void A_local_record_is_only_ever_as_good_as_what_it_knows()
    {
        // Same sale, same request. The server knows £6 has gone back; the offline till doesn't.
        var server = RefundRules.Authorise(SaleRecordSource.Server, 1000, 600, 1000);
        var local = RefundRules.Authorise(SaleRecordSource.LocalInWindow, 1000, 0, 1000);

        Assert.Equal(400, server.AllowedPence);
        Assert.Equal(1000, local.AllowedPence);
    }
}
