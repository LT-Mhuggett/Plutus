using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// ⚠⚠ FINDING Y, 2026-08-13, and it was real money. Matt, hand-testing the till: *"I do not believe
/// either till is taking into account the split payment return? … when I try to return an item that was
/// split, it wants to put the full amount to that card."*
///
/// He was right at every layer. MAUI restricted the SET of tenders a refund could go back to (finding G)
/// and capped none of the amounts; the web till restricted nothing at all; and `RefundRules.Authorise`
/// capped only the sale TOTAL. So a £2.00 cash + £2.40 card sale could be refunded £4.40 to the card:
/// the card credited £2.40 more than it ever took, the £2 left in the drawer, the till balancing, and
/// no report anywhere disagreeing. Reverse the signs and it is a way to walk cash out of a shop.
///
/// These tests are the rule that stops it. ⚠ Every one is about MONEY, so per the execution protocol
/// they are mutation-checked: breaking the rule must fail a NAMED test, not merely "a test".
/// </summary>
public class RefundTenderSplitTests
{
    private static IEnumerable<KeyValuePair<byte, long>> Tendered(params (byte Type, long Pence)[] rows) =>
        rows.Select(r => new KeyValuePair<byte, long>(r.Type, r.Pence));

    /// <summary>£2.00 cash + £2.40 card — Matt's basket.</summary>
    private static IReadOnlyList<TenderCapacity> SplitSale() =>
        RefundRules.RefundCapacities(Tendered((Tenders.Cash, 200), (Tenders.Card, 240)));

    // ── What each tender may give back ───────────────────────────────────────

    [Fact]
    public void Each_tender_can_give_back_what_it_took()
    {
        var capacities = SplitSale();

        Assert.Equal(2, capacities.Count);
        Assert.Equal(200, capacities.Single(c => c.TenderType == Tenders.Cash).RemainingPence);
        Assert.Equal(240, capacities.Single(c => c.TenderType == Tenders.Card).RemainingPence);
    }

    /// <summary>
    /// ⚠ SUMMED, NOT LAST-WINS. One sale can pay twice with the same method — two cash tenders, or a
    /// card taken in two goes — and treating the second as a replacement would understate what that
    /// tender took and refuse a legitimate refund.
    /// </summary>
    [Fact]
    public void Two_tenders_of_the_same_method_add_up()
    {
        var capacities = RefundRules.RefundCapacities(
            Tendered((Tenders.Cash, 200), (Tenders.Cash, 150)));

        Assert.Single(capacities);
        Assert.Equal(350, capacities[0].RemainingPence);
    }

    /// <summary>⚠ The wire carries refunds as NEGATIVE amounts. Magnitudes must compare regardless.</summary>
    [Fact]
    public void Signed_wire_amounts_are_read_as_magnitudes()
    {
        var capacities = RefundRules.RefundCapacities(
            Tendered((Tenders.Card, -240)),
            Tendered((Tenders.Card, -100)));

        Assert.Equal(240, capacities[0].TookPence);
        Assert.Equal(140, capacities[0].RemainingPence);
    }

    [Fact]
    public void What_has_already_gone_back_reduces_the_remainder()
    {
        var capacities = RefundRules.RefundCapacities(
            Tendered((Tenders.Cash, 200), (Tenders.Card, 240)),
            Tendered((Tenders.Card, 240)));

        Assert.Equal(200, capacities.Single(c => c.TenderType == Tenders.Cash).RemainingPence);
        Assert.Equal(0, capacities.Single(c => c.TenderType == Tenders.Card).RemainingPence);
    }

    /// <summary>
    /// ⚠ Corrupt data reads as "nothing left", never as a negative some caller subtracts into a payout —
    /// the same clamp the sale-level remainder uses.
    /// </summary>
    [Fact]
    public void More_refunded_than_was_taken_clamps_at_zero()
    {
        var capacities = RefundRules.RefundCapacities(
            Tendered((Tenders.Card, 240)), Tendered((Tenders.Card, 900)));

        Assert.Equal(0, capacities[0].RemainingPence);
    }

    // ── THE FAULT ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE BUG, in one assertion. £4.40 on the card against a sale where the card took £2.40.
    /// </summary>
    [Fact]
    public void The_whole_refund_cannot_go_on_the_card_when_the_card_only_took_part_of_it()
    {
        var decision = RefundRules.AuthoriseSplit(SplitSale(), Tendered((Tenders.Card, 440)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(Tenders.Card, decision.OffendingTenderType);
        Assert.Equal(440, decision.RequestedPence);
        Assert.Equal(240, decision.AllowedPence);   // what it may actually give back
    }

    [Fact]
    public void The_split_the_customer_actually_paid_is_allowed()
    {
        var decision = RefundRules.AuthoriseSplit(
            SplitSale(), Tendered((Tenders.Cash, 200), (Tenders.Card, 240)));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void A_partial_refund_within_one_tender_is_allowed()
    {
        var decision = RefundRules.AuthoriseSplit(SplitSale(), Tendered((Tenders.Card, 100)));

        Assert.True(decision.IsAllowed);
    }

    /// <summary>
    /// ⚠ THE SALE-LEVEL CAP FALLS OUT FOR FREE: if every tender is within what it took, the sum is
    /// within what the sale took. This pins that the total cannot be exceeded by spreading it around.
    /// </summary>
    [Fact]
    public void The_total_cannot_be_exceeded_by_spreading_it_across_both_tenders()
    {
        var decision = RefundRules.AuthoriseSplit(
            SplitSale(), Tendered((Tenders.Cash, 200), (Tenders.Card, 241)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(Tenders.Card, decision.OffendingTenderType);
    }

    /// <summary>
    /// ⚠ Two requests against ONE tender are summed before the comparison — otherwise a screen could
    /// slip past the cap by listing the same method twice.
    /// </summary>
    [Fact]
    public void Two_requests_against_the_same_tender_are_summed_before_the_cap_is_applied()
    {
        var decision = RefundRules.AuthoriseSplit(
            SplitSale(), Tendered((Tenders.Card, 200), (Tenders.Card, 100)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(Tenders.Card, decision.OffendingTenderType);
        Assert.Equal(300, decision.RequestedPence);
        Assert.Equal(240, decision.AllowedPence);
    }

    // ── ⚠⚠ The three-way answer for ONE tender. C2 twin: `capacityFor` in tendering.ts ──

    /// <summary>
    /// ⚠⚠ NULL IS NOT ZERO, and the MAUI till could not tell them apart until 2026-08-19 — which is how
    /// a card an earlier refund had already used up came to accept the money a second time.
    ///
    /// ⚠ An empty list is IGNORANCE: not a refund at all, or an origin sale this till could not read.
    /// Nothing may be enforced from it, and the server gate is what stands behind that.
    /// </summary>
    [Fact]
    public void An_empty_capacity_list_caps_nothing_because_it_knows_nothing()
    {
        Assert.Null(RefundRules.CapacityFor(Array.Empty<TenderCapacity>(), Tenders.Card));
        Assert.Null(RefundRules.CapacityFor(null, Tenders.Card));
    }

    [Fact]
    public void A_tender_in_the_list_is_capped_at_what_it_has_left()
    {
        Assert.Equal(200, RefundRules.CapacityFor(SplitSale(), Tenders.Cash));
        Assert.Equal(240, RefundRules.CapacityFor(SplitSale(), Tenders.Card));
    }

    /// <summary>
    /// ⚠⚠ FINDING G, SECOND LOCK. A tender the sale never used may take nothing back — enforced by the
    /// rule and not only by which buttons the picker offers, because relying on the picker alone is
    /// precisely how this went missing.
    /// </summary>
    [Fact]
    public void A_tender_absent_from_a_populated_list_may_take_nothing()
    {
        var cardOnly = RefundRules.RefundCapacities(Tendered((Tenders.Card, 440)));

        Assert.Equal(0, RefundRules.CapacityFor(cardOnly, Tenders.Cash));
    }

    /// <summary>
    /// ⚠⚠ THE DEFECT OF 2026-08-19 AT ITS SOURCE. A spent card is STILL offered by the picker
    /// (`OriginTenderTypesAsync` returns every type the origin used, spent or not), so the loop asks
    /// about it — and the answer must be 0 meaning "nothing", never null meaning "help yourself".
    ///
    /// ⚠ The web till reaches the same 0 by a different route: its `refundCapacities` DROPS a spent
    /// tender from the list entirely, so `capacityFor` falls through to 0. Same answer, and worth knowing
    /// they differ in the middle — anything that ITERATES capacities sees a spent tender here and not
    /// there. That is a display difference, not a money one.
    /// </summary>
    [Fact]
    public void A_spent_tender_answers_zero_rather_than_null()
    {
        var spent = RefundRules.RefundCapacities(
            Tendered((Tenders.Card, 240)), Tendered((Tenders.Card, 240)));

        Assert.Equal(0, RefundRules.CapacityFor(spent, Tenders.Card));
    }

    // ── Fail closed ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE FRAUD FINDING G EXISTS TO STOP, now enforced by the RULE rather than by one till's
    /// action sheet: a card sale refunded from the cash drawer. A day of card sales refunded in cash
    /// empties the drawer and leaves the card takings untouched.
    /// </summary>
    [Fact]
    public void Cash_cannot_refund_a_card_only_sale()
    {
        var cardOnly = RefundRules.RefundCapacities(Tendered((Tenders.Card, 440)));

        var decision = RefundRules.AuthoriseSplit(cardOnly, Tendered((Tenders.Cash, 440)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(Tenders.Cash, decision.OffendingTenderType);
    }

    /// <summary>
    /// ⚠ A part-cash-part-card sale cannot be refunded ENTIRELY in cash either — even though cash was
    /// one of the methods used. This is the operational limit worth knowing about: a shop whose card
    /// terminal is down cannot hand the whole lot over in notes.
    /// </summary>
    [Fact]
    public void A_split_sale_cannot_be_refunded_entirely_in_cash()
    {
        var decision = RefundRules.AuthoriseSplit(SplitSale(), Tendered((Tenders.Cash, 440)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(Tenders.Cash, decision.OffendingTenderType);
        Assert.Equal(200, decision.AllowedPence);
    }

    /// <summary>⚠ A tender byte this build does not know must not become a payout.</summary>
    [Fact]
    public void An_unrecognised_tender_is_refused()
    {
        var decision = RefundRules.AuthoriseSplit(SplitSale(), Tendered((200, 100)));

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void A_tender_with_nothing_left_is_refused_rather_than_capped_at_zero()
    {
        var spent = RefundRules.RefundCapacities(
            Tendered((Tenders.Card, 240)), Tendered((Tenders.Card, 240)));

        var decision = RefundRules.AuthoriseSplit(spent, Tendered((Tenders.Card, 100)));

        Assert.False(decision.IsAllowed);
        Assert.Equal(0, decision.AllowedPence);
    }

    [Fact]
    public void An_empty_request_is_refused()
    {
        Assert.False(RefundRules.AuthoriseSplit(SplitSale(), Tendered()).IsAllowed);
    }

    /// <summary>⚠ A zero row means a screen built a payment line it never filled in. Refusing is how
    /// that gets noticed rather than posting a £0 tender nothing reconciles against.</summary>
    [Fact]
    public void A_zero_amount_is_refused()
    {
        var decision = RefundRules.AuthoriseSplit(SplitSale(), Tendered((Tenders.Cash, 0)));

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void No_capacities_at_all_refuses_everything()
    {
        var decision = RefundRules.AuthoriseSplit(
            Array.Empty<TenderCapacity>(), Tendered((Tenders.Cash, 100)));

        Assert.False(decision.IsAllowed);
    }

    /// <summary>
    /// ⚠ The reason never carries a formatted amount — `Money.cs` makes formatting a client concern,
    /// and a shared rule that baked "£" into a sentence would be wrong in the first other currency and
    /// unlocalisable for the MAUI till. The figures are fields; the caller composes the sentence.
    /// </summary>
    [Fact]
    public void Reasons_carry_no_currency_symbols()
    {
        var refused = RefundRules.AuthoriseSplit(SplitSale(), Tendered((Tenders.Card, 440)));

        Assert.DoesNotContain("£", refused.Reason);
        Assert.False(string.IsNullOrWhiteSpace(refused.Reason));
    }
}

/// <summary>
/// ⚠ Finding Y piece 4b: reading how a sale the PLATFORM holds was paid. The server has always sent
/// `tenders` on `GET /api/v1/sales/{saleId}`; the contract had no property for them, so a till
/// refunding another till's sale could not cap anything. **The data was there and nobody asked.**
/// </summary>
public class SaleDtoTenderPairsTests
{
    [Fact]
    public void The_enum_names_the_server_sends_map_to_wire_bytes()
    {
        var sale = new Plutus.Contracts.Client.SaleDto
        {
            Tenders = new()
            {
                new() { TenderType = "Cash", AmountPence = 200 },
                new() { TenderType = "Card", AmountPence = 240 },
            },
        };

        var capacities = RefundRules.RefundCapacities(Plutus.Client.Core.SaleDtoTenders.TenderPairs(sale));

        Assert.Equal(200, capacities.Single(c => c.TenderType == Tenders.Cash).TookPence);
        Assert.Equal(240, capacities.Single(c => c.TenderType == Tenders.Card).TookPence);
    }

    [Fact]
    public void Every_tender_name_the_platform_can_serialise_is_understood()
    {
        foreach (var (name, expected) in new (string, byte)[]
                 {
                     ("Cash", Tenders.Cash), ("Card", Tenders.Card), ("Online", Tenders.Online),
                     ("Credit", Tenders.Credit), ("GiftCard", Tenders.GiftCard),
                 })
        {
            Assert.True(Tenders.TryFromWireName(name, out var actual), name);
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// ⚠⚠ AN UNKNOWN NAME IS DROPPED, NOT CALLED A CARD. `Tenders.FromMethodName` falls back to Card
    /// on purpose — a cashier typing an odd method name should still be able to sell. On THIS path the
    /// same leniency would hand the card a capacity it never earned, out of somebody else's money.
    /// </summary>
    [Fact]
    public void An_unknown_tender_name_is_dropped_rather_than_treated_as_a_card()
    {
        Assert.False(Tenders.TryFromWireName("Bitcoin", out _));

        var sale = new Plutus.Contracts.Client.SaleDto
        {
            Tenders = new() { new() { TenderType = "Bitcoin", AmountPence = 440 } },
        };

        Assert.Empty(Plutus.Client.Core.SaleDtoTenders.TenderPairs(sale));

        // ⚠ And the lenient mapper WOULD have said Card — the difference this test exists for.
        Assert.Equal(Tenders.Card, Tenders.FromMethodName("Bitcoin"));
    }

    [Fact]
    public void A_sale_with_no_tenders_yields_nothing_rather_than_throwing()
    {
        Assert.Empty(Plutus.Client.Core.SaleDtoTenders.TenderPairs(new Plutus.Contracts.Client.SaleDto()));
        Assert.Empty(Plutus.Client.Core.SaleDtoTenders.TenderPairs(null));
    }

    /// <summary>⚠ A refund's tenders are negative on the wire; the rules compare magnitudes.</summary>
    [Fact]
    public void Negative_wire_amounts_become_magnitudes()
    {
        var sale = new Plutus.Contracts.Client.SaleDto
        {
            Tenders = new() { new() { TenderType = "Card", AmountPence = -240 } },
        };

        Assert.Equal(240, Plutus.Client.Core.SaleDtoTenders.TenderPairs(sale).Single().Value);
    }
}
