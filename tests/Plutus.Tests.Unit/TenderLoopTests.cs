using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Taking payment is the last thing that happens before money changes hands, and until 2026-08-10
/// none of it was reachable by a test — it lived inside a ~200-line `async void` MAUI command. Three
/// separate ways of trapping the operator in a loop that could not terminate shipped as a result,
/// and were found by a person using the till rather than by anything here.
///
/// These tests are the reason `TenderLoop` was extracted. Every one of them describes a way the
/// original could hang, take the wrong money, or lose a sale.
/// </summary>
public class TenderLoopTests
{
    /// <summary>Scripted answers, so a test can play an operator without a UI.</summary>
    private sealed class Operator
    {
        private readonly Queue<TenderChoice> _choices = new();
        private readonly Queue<TenderAmount> _amounts = new();

        public int ChoicesAsked { get; private set; }
        public int AmountsAsked { get; private set; }
        public readonly List<long> OutstandingWhenAsked = new();

        /// <summary>What the tender PICKER was told each time it was raised: (outstanding, paid).</summary>
        public readonly List<(long Outstanding, long Paid)> PickerWasTold = new();

        public Operator Picks(string method, bool givesChange, long surchargePence = 0)
        {
            _choices.Enqueue(new TenderChoice(method, givesChange, surchargePence));
            return this;
        }

        public Operator PicksCash(int times = 1)
        {
            for (var i = 0; i < times; i++) Picks("Cash", givesChange: true);
            return this;
        }

        public Operator PicksCard(long surchargePence = 0)
        {
            Picks("Card", givesChange: false, surchargePence);
            return this;
        }

        public Operator AbandonsMethod() { _choices.Enqueue(TenderChoice.Abandoned); return this; }

        public Operator Pays(params long[] pence)
        {
            foreach (var p in pence) _amounts.Enqueue(TenderAmount.Of(p));
            return this;
        }

        public Operator AbandonsAmount() { _amounts.Enqueue(TenderAmount.Abandoned); return this; }

        public Task<TenderChoice> ChooseAsync(long outstanding, long paidSoFar)
        {
            ChoicesAsked++;
            PickerWasTold.Add((outstanding, paidSoFar));
            // ⚠ Falls back to Abandoned rather than throwing on an empty queue: a test whose loop
            // runs longer than expected should FAIL ON ITS ASSERTION, not on a queue exception that
            // hides which rule broke.
            return Task.FromResult(_choices.Count > 0 ? _choices.Dequeue() : TenderChoice.Abandoned);
        }

        public Task<TenderAmount> AskAsync(long outstanding)
        {
            AmountsAsked++;
            OutstandingWhenAsked.Add(outstanding);
            return Task.FromResult(_amounts.Count > 0 ? _amounts.Dequeue() : TenderAmount.Abandoned);
        }
    }

    private static Task<TenderOutcome> Run(long totalPence, Operator op) =>
        TenderLoop.RunAsync(totalPence, op.ChooseAsync, op.AskAsync);

    // ── The ordinary paths ───────────────────────────────────────────────────

    [Fact]
    public async Task Exact_cash_settles_the_sale_with_no_change()
    {
        var op = new Operator().PicksCash().Pays(330);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(330, outcome.SettledTotalPence);
        Assert.Equal(0, outcome.ChangePence);
        var payment = Assert.Single(outcome.Payments);
        Assert.Equal(330, payment.AmountPence);
    }

    [Fact]
    public async Task Overpaying_in_cash_gives_change()
    {
        var op = new Operator().PicksCash().Pays(500);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(170, outcome.ChangePence);
        Assert.Equal(170, Assert.Single(outcome.Payments).ChangePence);
    }

    [Fact]
    public async Task A_split_payment_accumulates_to_exactly_the_total()
    {
        var op = new Operator().PicksCash(3).Pays(100, 100, 130);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(3, outcome.Payments.Count);
        Assert.Equal(330, outcome.Payments.Sum(p => p.AmountPence));
        Assert.Equal(0, outcome.ChangePence);
    }

    /// <summary>
    /// ⚠⚠ THE TENDER PICKER IS TOLD WHAT HAS BEEN TAKEN, and this is the test for the fault that made
    /// a working split payment look broken. Matt, 2026-08-13, £4.40 basket, £2 cash: *"it takes me back
    /// to the 'Card or cash' screen but doesn't tell me anything has been paid or there is X to pay. I
    /// assume its not actually working."*
    ///
    /// The money was never lost — the test above has always proved that. What the screen could not do
    /// was SAY so, because the loop handed it the outstanding balance and nothing else.
    /// </summary>
    [Fact]
    public async Task The_picker_is_told_what_has_been_paid_so_far()
    {
        var op = new Operator().PicksCash(3).Pays(200, 140, 100);

        await Run(440, op);

        // First ask: nothing taken yet. Then £2.00, then £3.40 — the operator's own running total.
        Assert.Equal(new[] { (440L, 0L), (240L, 200L), (100L, 340L) }, op.PickerWasTold);
    }

    /// <summary>
    /// ⚠ AND IT INCLUDES THE SURCHARGE, which is exactly why this figure comes from the loop rather
    /// than from the caller's basket total. A caller deriving `paid = myTotal - outstanding` is right
    /// until a tenant switches a card fee on, and then wrong by the fee — on a screen telling an
    /// operator how much money they are holding.
    /// </summary>
    [Fact]
    public async Task What_has_been_paid_accounts_for_a_surcharge_the_loop_added()
    {
        // £4.40 basket, 50p card fee applied on the first pick, then £2.00 and the £2.90 balance.
        var op = new Operator().PicksCard(surchargePence: 50).PicksCash().Pays(200, 290);

        var outcome = await Run(440, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(490, outcome.Payments.Sum(p => p.AmountPence));

        // ⚠ The SECOND ask must report £2.00 taken against £2.90 left — not £2.40, which is what a
        // basket-derived figure would have said.
        Assert.Equal((290L, 200L), op.PickerWasTold[1]);
    }

    [Fact]
    public async Task Each_prompt_asks_for_what_is_STILL_outstanding()
    {
        var op = new Operator().PicksCash(2).Pays(100, 230);

        await Run(330, op);

        Assert.Equal(new long[] { 330, 230 }, op.OutstandingWhenAsked);
    }

    [Fact]
    public async Task A_zero_total_basket_asks_for_nothing()
    {
        var op = new Operator();

        var outcome = await Run(0, op);

        Assert.False(outcome.Abandoned);
        Assert.Empty(outcome.Payments);
        Assert.Equal(0, op.ChoicesAsked);
    }

    // ── Backing out. THE defect: cancelling used to fall through ─────────────

    /// <summary>
    /// ⚠ THE BUG THAT TRAPPED THE OPERATOR. Cancelling the amount prompt used to append a £0
    /// payment, leave the running total untouched, and return to a loop conditioned on
    /// "paid != total" — which reopened the same prompt for ever. Nothing may be taken, and the
    /// caller must be told to leave the basket alone.
    /// </summary>
    [Fact]
    public async Task Backing_out_of_the_AMOUNT_takes_nothing_and_abandons()
    {
        var op = new Operator().PicksCash().AbandonsAmount();

        var outcome = await Run(330, op);

        Assert.True(outcome.Abandoned);
        Assert.Empty(outcome.Payments);
        Assert.Equal(0, outcome.ChangePence);
    }

    [Fact]
    public async Task Backing_out_of_the_METHOD_takes_nothing_and_abandons()
    {
        var op = new Operator().AbandonsMethod();

        var outcome = await Run(330, op);

        Assert.True(outcome.Abandoned);
        Assert.Empty(outcome.Payments);
        Assert.Equal(0, op.AmountsAsked);
    }

    /// <summary>
    /// ⚠ Abandoning PART WAY through a split payment must still take nothing. A partial outcome
    /// would leave the operator holding cash the sale has no record of.
    /// </summary>
    [Fact]
    public async Task Abandoning_midway_through_a_split_payment_takes_nothing()
    {
        var op = new Operator().PicksCash(2).Pays(100).AbandonsAmount();

        var outcome = await Run(330, op);

        Assert.True(outcome.Abandoned);
        Assert.Empty(outcome.Payments);
    }

    // ── Tenders that can never settle anything ───────────────────────────────

    /// <summary>
    /// ⚠ ZERO IS AN INFINITE LOOP WITH A FRIENDLY FACE. The original did `paid += amount`
    /// unconditionally, so entering 0 left the total unmoved and reopened the prompt — for ever.
    /// It must be refused and re-asked, and a later real amount must still settle the sale.
    /// </summary>
    [Fact]
    public async Task A_zero_tender_is_refused_and_the_operator_is_asked_again()
    {
        var op = new Operator().PicksCash(2).Pays(0, 330);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(330, Assert.Single(outcome.Payments).AmountPence);
        Assert.Equal(2, op.AmountsAsked);
    }

    /// <summary>
    /// ⚠ A tender must move the balance TOWARD settlement. Handing money out to settle a sale (or
    /// taking it in to settle a refund) walks the total away from zero, and the original's
    /// `paid > total` test could not express this for refunds because both numbers are negative.
    /// </summary>
    [Fact]
    public async Task A_tender_pointing_the_wrong_way_is_refused()
    {
        var op = new Operator().PicksCash(2).Pays(-100, 330);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(330, Assert.Single(outcome.Payments).AmountPence);
    }

    /// <summary>⚠ A stuck prompt must not spin for ever — see `MaxConsecutiveRefusals`.</summary>
    [Fact]
    public async Task An_endlessly_refused_tender_gives_up_rather_than_spinning()
    {
        var op = new Operator();
        for (var i = 0; i < 200; i++) { op.PicksCash(); op.Pays(0); }

        var outcome = await Run(330, op);

        Assert.True(outcome.Abandoned);
        Assert.True(op.AmountsAsked <= TenderLoop.MaxConsecutiveRefusals,
            $"asked {op.AmountsAsked} times; the backstop is {TenderLoop.MaxConsecutiveRefusals}");
    }

    [Fact]
    public async Task A_refusal_followed_by_a_good_tender_resets_the_backstop()
    {
        var op = new Operator();
        for (var i = 0; i < 15; i++) { op.PicksCash(); op.Pays(0); }
        op.PicksCash().Pays(100);
        for (var i = 0; i < 15; i++) { op.PicksCash(); op.Pays(0); }
        op.PicksCash().Pays(230);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(330, outcome.Payments.Sum(p => p.AmountPence));
    }

    // ── Change, and the methods that cannot give it ──────────────────────────

    /// <summary>
    /// ⚠ A card cannot hand money back. Over-tendering on one must be refused and re-asked, never
    /// silently accepted — the difference would be change the drawer never gave.
    /// </summary>
    [Fact]
    public async Task Overpaying_on_a_method_that_gives_no_change_is_refused()
    {
        var op = new Operator().PicksCard().PicksCard().Pays(500, 330);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(0, outcome.ChangePence);
        Assert.Equal(330, Assert.Single(outcome.Payments).AmountPence);
    }

    // ── The card surcharge ───────────────────────────────────────────────────

    [Fact]
    public async Task Choosing_a_surcharged_method_raises_the_total()
    {
        var op = new Operator().PicksCard(surchargePence: 7).Pays(337);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(7, outcome.SurchargePence);
        Assert.Equal(337, outcome.SettledTotalPence);
        Assert.Equal(337, op.OutstandingWhenAsked[0]);
    }

    /// <summary>
    /// ⚠ ONCE PER SALE, NOT ONCE PER TENDER. A basket split across two card payments must not be
    /// charged the flat fee twice. The original left this to the caller re-checking the basket on
    /// every pass; here it is a rule of the loop, so a caller cannot get it wrong.
    /// </summary>
    [Fact]
    public async Task The_surcharge_is_applied_ONCE_across_a_split_card_payment()
    {
        var op = new Operator()
            .PicksCard(surchargePence: 7)
            .PicksCard(surchargePence: 7)
            .Pays(100, 237);

        var outcome = await Run(330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(7, outcome.SurchargePence);
        Assert.Equal(337, outcome.SettledTotalPence);
        Assert.Equal(337, outcome.Payments.Sum(p => p.AmountPence));
    }

    /// <summary>⚠ Abandoning after a surcharge was added still takes NOTHING.</summary>
    [Fact]
    public async Task Abandoning_after_a_surcharge_was_added_still_takes_nothing()
    {
        var op = new Operator().PicksCard(surchargePence: 7).AbandonsAmount();

        var outcome = await Run(330, op);

        Assert.True(outcome.Abandoned);
        Assert.Empty(outcome.Payments);
    }

    // ── Refunds: the same algorithm with the sign flipped ────────────────────

    [Fact]
    public async Task A_refund_settles_on_a_negative_total()
    {
        var op = new Operator().PicksCash().Pays(-330);

        var outcome = await Run(-330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(-330, outcome.SettledTotalPence);
        Assert.Equal(-330, Assert.Single(outcome.Payments).AmountPence);
        Assert.Equal(0, outcome.ChangePence);
    }

    /// <summary>
    /// ⚠ THE HOLE THE ORIGINAL HAD. Its guard was `paid > sale.Total`, which for a refund compares
    /// two negatives and never fires — so handing back MORE than the basket owed walked straight
    /// through. Here it is the same magnitude comparison as a sale.
    /// </summary>
    [Fact]
    public async Task Handing_back_MORE_than_the_refund_owes_is_not_silently_accepted()
    {
        var op = new Operator().PicksCash(2).Pays(-400, -330);

        var outcome = await Run(-330, op);

        Assert.False(outcome.Abandoned);
        // -400 overshoots -330; cash gives change, so it settles with 70p back IN.
        Assert.Equal(-330, outcome.SettledTotalPence);
        Assert.Equal(-70, outcome.Payments[0].ChangePence);
    }

    [Fact]
    public async Task A_split_refund_accumulates_to_exactly_the_negative_total()
    {
        var op = new Operator().PicksCash(2).Pays(-100, -230);

        var outcome = await Run(-330, op);

        Assert.False(outcome.Abandoned);
        Assert.Equal(-330, outcome.Payments.Sum(p => p.AmountPence));
    }
}
