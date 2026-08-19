using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The .NET half of the ONE-SCREEN tendering twin — §5c item 2, 2026-08-19.
///
/// ⚠⚠ **THESE ARE THE SAME VECTORS AS `till/tendering.test.ts`, ON PURPOSE.** That file has carried
/// the web till's one-screen arithmetic since 2026-08-17; this side had only `TenderLoop`, which
/// answers a different question (may this ONE tender be taken) and cannot say whether a whole screen
/// balances. Building MAUI's checkout to look like the web till's means running the web till's
/// arithmetic, and a twin nobody executes on both sides is how two tills come to disagree by a penny
/// on every VAT return afterwards.
///
/// **Add a case here, add it there.** A twin pinned on one side only is not pinned.
///
/// ⚠ CASH is changeable, CARD is not. That pairing is easy to invert, so it is pinned on both sides.
/// </summary>
public class TenderSettlementTests
{
    private static readonly TenderMethodRef Cash = new(1, IsChangeable: true);
    private static readonly TenderMethodRef Card = new(2, IsChangeable: false);
    private static readonly List<TenderMethodRef> Methods = new() { Cash, Card };

    /// <summary>⚠ The same formatting the till shows, so the sentences are compared as an operator reads them.</summary>
    private static string Gbp(long pence) =>
        (pence / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-GB"));

    private static ParsedAmounts Parse(params (int Id, string Text)[] rows) =>
        TenderSettlement.ParseAmounts(rows.ToDictionary(r => r.Id, r => r.Text));

    // ── parseAmounts ──────────────────────────────────────────────────────────

    [Fact]
    public void An_untouched_row_is_skipped_rather_than_read_as_zero()
    {
        var p = Parse((1, ""), (2, "   "));

        Assert.True(p.Valid);
        Assert.Equal(0, p.Paid);
        Assert.Empty(p.PerMethod);
    }

    /// <summary>
    /// ⚠ A basket that is 90% parseable is not 90% payable. Completing on a partial read would take a
    /// different sum from the one on screen.
    /// </summary>
    [Fact]
    public void One_bad_box_invalidates_the_whole_set()
    {
        Assert.False(Parse((1, "4.00"), (2, "abc")).Valid);
    }

    [Fact]
    public void Two_methods_read_as_two_tenders()
    {
        var p = Parse((1, "4.00"), (2, "6.00"));

        Assert.True(p.Valid);
        Assert.Equal(1000, p.Paid);
        Assert.Equal(400, p.PerMethod[1]);
        Assert.Equal(600, p.PerMethod[2]);
    }

    /// <summary>
    /// ⚠⚠ THE PARSER IS THE FIRST PLACE A PENNY CAN GO WRONG, and it is a C2 twin of `money.ts
    /// parsePence`. It takes an optional `£` and at most two decimals — a negative is a typo, not a
    /// refund, and `1.` is not an amount.
    /// </summary>
    [Theory]
    [InlineData("4.00", 400L)]
    [InlineData("£4.00", 400L)]
    [InlineData("  4.5  ", 450L)]
    [InlineData("0", 0L)]
    [InlineData("8.15", 815L)]      // ⚠ the classic double-rounding case: 8.15 × 100 is 814.999… in binary
    [InlineData("0.01", 1L)]
    [InlineData("19.99", 1999L)]
    public void Money_text_reads_as_pence(string text, long expected) =>
        Assert.Equal(expected, TenderSettlement.ParsePence(text));

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("££")]
    [InlineData("-4.00")]           // ⚠ a negative tender is a typo, never a refund
    [InlineData("4.")]
    [InlineData("4.000")]           // ⚠ three decimals is not money
    [InlineData("1,000")]           // ⚠ separators are refused rather than guessed at
    [InlineData("4 00")]
    [InlineData(null)]
    public void Anything_that_is_not_money_is_refused(string text) =>
        Assert.Null(TenderSettlement.ParsePence(text));

    // ── the split-payment case ────────────────────────────────────────────────

    [Fact]
    public void A_ten_pound_basket_settled_four_then_six()
    {
        var first = TenderSettlement.Assess(1000, Parse((1, "4.00")), Methods);
        Assert.Equal(600, first.Remaining);
        Assert.Equal(0, first.Overpay);

        var parsed = Parse((1, "4.00"), (2, "6.00"));
        var both = TenderSettlement.Assess(1000, parsed, Methods);
        Assert.Equal(0, both.Remaining);
        Assert.Equal(0, both.Overpay);
        Assert.Null(TenderSettlement.Refusal(both, parsed, Gbp));
    }

    /// <summary>⚠ The words the operator is owed — the same sentence the web till shows.</summary>
    [Fact]
    public void It_says_how_much_is_left_rather_than_only_refusing()
    {
        var p = Parse((1, "4.00"));

        Assert.Equal("£6.00 still to pay.",
            TenderSettlement.Refusal(TenderSettlement.Assess(1000, p, Methods), p, Gbp));
    }

    // ── change ────────────────────────────────────────────────────────────────

    [Fact]
    public void Change_is_due_on_a_cash_overpay_and_can_be_given()
    {
        var s = TenderSettlement.Assess(330, Parse((1, "20.00")), Methods);

        Assert.Equal(1670, s.Overpay);
        Assert.True(s.ChangeOk);
        Assert.Equal(1670, s.ChangeByPayId[1]);
    }

    /// <summary>
    /// ⚠ A CARD CANNOT GIVE CHANGE. Overpaying £20 on a £3.30 basket by card is not a sale with
    /// change, it is a refusal — the same rule `TenderLoop` mutation-checks from the other direction.
    /// </summary>
    [Fact]
    public void Change_cannot_come_from_a_card_so_the_sale_is_refused_with_a_reason()
    {
        var p = Parse((2, "20.00"));
        var s = TenderSettlement.Assess(330, p, Methods);

        Assert.Equal(1670, s.Overpay);
        Assert.False(s.ChangeOk);
        Assert.Equal("£16.70 over, and only cash can give change back.",
            TenderSettlement.Refusal(s, p, Gbp));
    }

    [Fact]
    public void Change_comes_only_out_of_the_cash_half_of_a_mixed_overpay()
    {
        // £5 basket, £2 card + £5 cash = £7 paid, £2 over — all of it from the cash.
        var s = TenderSettlement.Assess(500, Parse((1, "5.00"), (2, "2.00")), Methods);

        Assert.Equal(200, s.Overpay);
        Assert.Equal(500, s.ChangeablePaid);
        Assert.True(s.ChangeOk);
        Assert.Equal(200, s.ChangeByPayId[1]);
        Assert.False(s.ChangeByPayId.ContainsKey(2));
    }

    /// <summary>
    /// ⚠⚠ THE PENNY. Σchange must equal the overpay EXACTLY or the v1 pipeline rejects the sale for
    /// breaking `net tender == gross`, and the operator is told nothing useful.
    ///
    /// ⚠⚠ THESE NUMBERS ARE CHOSEN, NOT ARBITRARY — the web till's suite records why, and the same
    /// trap applies here for a different reason. £9.99 owed, £5.00 + £5.00 cash → **1p over across two
    /// EQUAL tenders**, so each proportional share is exactly 0.5p. Round the shares independently and
    /// you get 1p + 1p = **2p of change for a 1p overpay**. Unequal tenders cannot catch this bug
    /// (their shares round the same way with or without the remainder rule); equal ones always can.
    ///
    /// ⚠ In .NET it catches a SECOND bug the TypeScript cannot have: `Math.Round(0.5)` here is
    /// **banker's rounding** by default and would give 0p + 0p = 0p. The `MidpointRounding.AwayFromZero`
    /// in `ApportionChange` is what this asserts.
    /// </summary>
    [Fact]
    public void Change_sums_exactly_to_the_overpay_across_two_cash_methods()
    {
        var cash2 = new TenderMethodRef(3, IsChangeable: true);
        var methods = new List<TenderMethodRef> { Cash, Card, cash2 };

        var s = TenderSettlement.Assess(999, Parse((1, "5.00"), (3, "5.00")), methods);

        Assert.Equal(1, s.Overpay);
        Assert.Equal(s.Overpay, s.ChangeByPayId.Values.Sum());

        // ⚠ And ONE of them absorbs it, not both.
        Assert.Equal(new long[] { 0, 1 }, s.ChangeByPayId.Values.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void There_is_no_change_map_when_nothing_is_over()
    {
        var s = TenderSettlement.Assess(1000, Parse((1, "10.00")), Methods);

        Assert.Equal(0, s.Overpay);
        Assert.Empty(s.ChangeByPayId);
    }

    [Fact]
    public void Nothing_is_apportioned_when_the_overpay_is_on_an_unchangeable_method()
    {
        Assert.Empty(TenderSettlement.ApportionChange(Parse((2, "20.00")), Methods, 1670, 0));
    }

    // ── refunds ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ A refund gives money BACK, so nothing here gives change on top of it — both would hand over
    /// the same money twice. Identical rule to `TillTenders.Offered(refundOnly: true)`.
    /// </summary>
    [Fact]
    public void A_refund_never_produces_change_however_much_is_handed_back()
    {
        var s = TenderSettlement.Assess(-1000, Parse((1, "10.00")), Methods);

        Assert.True(s.Refunding);
        Assert.Equal(1000, s.Owed);
        Assert.Equal(0, s.Overpay);
        Assert.Empty(s.ChangeByPayId);
    }

    [Fact]
    public void A_refund_refuses_handing_back_more_than_is_owed()
    {
        var p = Parse((1, "12.00"));
        var s = TenderSettlement.Assess(-1000, p, Methods);

        Assert.True(s.OverRefund);
        Assert.Equal("That's more than the refund owes — hand back exactly £10.00.",
            TenderSettlement.Refusal(s, p, Gbp));
    }

    [Fact]
    public void A_refund_settles_when_the_amount_matches_exactly()
    {
        var p = Parse((1, "10.00"));

        Assert.Null(TenderSettlement.Refusal(TenderSettlement.Assess(-1000, p, Methods), p, Gbp));
    }

    // ── the rest button ───────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ Reported 2026-08-07 on the web till: using the bare remainder made "rest" toggle 0.00 ↔ full
    /// on an already-filled row, and left an overpaid row untouched.
    /// </summary>
    [Fact]
    public void Rest_recomputes_a_filled_row_from_the_others_rather_than_toggling_it()
    {
        // £10 owed, this row holds £4, another holds £2 → paid £6, own £4 → rest is £8.
        Assert.Equal(800, TenderSettlement.RestFor(1000, 600, 400));
    }

    [Fact]
    public void Rest_covers_the_whole_basket_when_nothing_else_is_entered() =>
        Assert.Equal(1000, TenderSettlement.RestFor(1000, 0, 0));

    [Fact]
    public void Rest_never_goes_negative_when_the_others_already_overpay() =>
        Assert.Equal(0, TenderSettlement.RestFor(1000, 1200, 0));

    /// <summary>
    /// ⚠ Matt, 2026-08-19: *"the 'Rest' button needs to only ever put the max credit they have at the
    /// time in. There is No point putting the full number in."*
    /// </summary>
    [Fact]
    public void Rest_is_capped_at_what_the_method_actually_holds() =>
        Assert.Equal(250, TenderSettlement.RestFor(1000, 0, 0, 250));

    // ── an unparseable set ────────────────────────────────────────────────────

    [Fact]
    public void An_unparseable_set_refuses_with_the_reason_and_reports_nothing_paid()
    {
        var p = Parse((1, "££"));
        var s = TenderSettlement.Assess(1000, p, Methods);

        Assert.Equal(0, s.Paid);
        Assert.Equal(1000, s.Remaining);
        Assert.Equal("One of those amounts isn't a number.", TenderSettlement.Refusal(s, p, Gbp));
    }

    /// <summary>
    /// ⚠⚠ THE ODD PENNY MUST LAND ON THE SAME ROW EVERY TIME, AND ON THE SAME ROW AS THE WEB TILL'S.
    /// A `Dictionary`'s enumeration order is not a promise, so `ApportionChange` orders by pay-method
    /// id — without that, "the last changeable method" could be a different row from one run to the
    /// next, and two tills splitting the same overpay would put the penny in different places on the
    /// receipt.
    ///
    /// ⚠ On these numbers it lands on the LOWER id, and that is worth stating because it reads
    /// backwards: each non-last share is £5/£10 of 1p = **exactly 0.5p, which rounds UP to 1p**, and
    /// the last row then absorbs the remainder of **0**. Same in JavaScript, where `Math.round(0.5)` is
    /// also 1. What matters is that both tills agree, not which row wins.
    ///
    /// ⚠ Entered in the OPPOSITE order to the ids, so a run that happened to agree by luck of insertion
    /// order would fail here.
    /// </summary>
    [Fact]
    public void The_odd_penny_lands_on_the_same_row_every_time()
    {
        var cash2 = new TenderMethodRef(3, IsChangeable: true);
        var methods = new List<TenderMethodRef> { Cash, cash2 };

        for (var run = 0; run < 5; run++)
        {
            var s = TenderSettlement.Assess(999, Parse((3, "5.00"), (1, "5.00")), methods);

            Assert.Equal(1, s.ChangeByPayId[1]);
            Assert.Equal(0, s.ChangeByPayId[3]);
            Assert.Equal(s.Overpay, s.ChangeByPayId.Values.Sum());
        }
    }
}
