using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The loop says WHY it refused, so the screen can tell the operator.
///
/// ⚠ Matt, 2026-08-11, from the shop floor: over-paying on a card said *"Something went wrong"* and
/// under-paying in cash said the same. Neither is a fault — both are ordinary operator actions the
/// loop correctly refuses — but it refused in SILENCE and simply re-asked. In front of a customer
/// that reads as the till ignoring what was typed.
///
/// ⚠ The loop knew exactly which case had occurred. It just had no way to say so.
/// </summary>
public class TenderLoopRefusalTests
{
    private static Func<long, long, Task<TenderChoice>> Method(string name, bool givesChange) =>
        (_, _) => Task.FromResult(new TenderChoice(name, givesChange));

    /// <summary>Answers the amount prompt with each value in turn.</summary>
    private static Func<long, Task<TenderAmount>> Amounts(params long[] pence)
    {
        var i = 0;
        return _ => Task.FromResult(i < pence.Length ? TenderAmount.Of(pence[i++]) : TenderAmount.Abandoned);
    }

    [Fact]
    public async Task Over_paying_on_a_card_reports_OverpaidWithoutChange()
    {
        // ⚠ THE ONE MATT HIT. £20 offered against a £3.30 card sale — a card cannot hand back the
        // difference, so the loop refuses and asks again.
        var reasons = new List<TenderRefusal>();

        await TenderLoop.RunAsync(330, Method("Card", givesChange: false), Amounts(2000, 330),
            onRefused: (r, _) => { reasons.Add(r); return Task.CompletedTask; });

        Assert.Equal(new[] { TenderRefusal.OverpaidWithoutChange }, reasons);
    }

    [Fact]
    public async Task Over_paying_in_CASH_is_not_refused_at_all()
    {
        // ⚠ The mirror case, and it must stay silent — cash gives change, so £20 for £3.30 is a
        // perfectly ordinary sale. A message here would be noise on the commonest path in the shop.
        var reasons = new List<TenderRefusal>();

        var outcome = await TenderLoop.RunAsync(330, Method("Cash", givesChange: true), Amounts(2000),
            onRefused: (r, _) => { reasons.Add(r); return Task.CompletedTask; });

        Assert.Empty(reasons);
        Assert.Equal(1670, outcome.ChangePence);
    }

    [Fact]
    public async Task Entering_nothing_reports_Zero()
    {
        var reasons = new List<TenderRefusal>();

        await TenderLoop.RunAsync(500, Method("Cash", givesChange: true), Amounts(0, 500),
            onRefused: (r, _) => { reasons.Add(r); return Task.CompletedTask; });

        Assert.Equal(new[] { TenderRefusal.Zero }, reasons);
    }

    [Fact]
    public async Task Money_going_the_WRONG_WAY_reports_WrongDirection()
    {
        // ⚠ Paying money OUT to settle a sale. On a refund both numbers are negative, which is why
        // this is a sign comparison rather than `paid > total`.
        var reasons = new List<TenderRefusal>();

        await TenderLoop.RunAsync(500, Method("Cash", givesChange: true), Amounts(-500, 500),
            onRefused: (r, _) => { reasons.Add(r); return Task.CompletedTask; });

        Assert.Equal(new[] { TenderRefusal.WrongDirection }, reasons);
    }

    [Fact]
    public async Task UNDER_paying_is_never_a_refusal_it_is_a_SPLIT_PAYMENT()
    {
        // ⚠⚠ THE ONE THAT ANSWERS "THERE DOES NEED TO BE THE OPTION OF SPLIT PAYMENTS". There
        // already is: an amount smaller than the balance is ACCEPTED, and the loop asks again for
        // what is left. £4 then £6 settles a £10 basket as two tenders.
        //
        // So the capability was never missing — nothing on the screen said the second prompt was
        // the rest of the same sale, and a refusal message would have made it look like an error.
        var reasons = new List<TenderRefusal>();

        var outcome = await TenderLoop.RunAsync(1000, Method("Cash", givesChange: true), Amounts(400, 600),
            onRefused: (r, _) => { reasons.Add(r); return Task.CompletedTask; });

        Assert.Empty(reasons);
        Assert.False(outcome.Abandoned);
        Assert.Equal(2, outcome.Payments.Count);
        Assert.Equal(400, outcome.Payments[0].AmountPence);
        Assert.Equal(600, outcome.Payments[1].AmountPence);
    }

    [Fact]
    public async Task The_outstanding_balance_travels_with_the_reason()
    {
        // The screen quotes it back — "£3.30 is still to pay" — so it has to be the balance at the
        // moment of refusal, not the basket total.
        long? reported = null;

        await TenderLoop.RunAsync(1000, Method("Card", givesChange: false), Amounts(400, 5000, 600),
            onRefused: (_, outstanding) => { reported ??= outstanding; return Task.CompletedTask; });

        // £4 was taken first, so the refusal that follows is against the remaining £6.
        Assert.Equal(600, reported);
    }

    [Fact]
    public async Task A_loop_with_NO_callback_still_works()
    {
        // ⚠ The parameter is optional so existing callers keep compiling. A null callback must not
        // change behaviour — least of all on the refusal path.
        var outcome = await TenderLoop.RunAsync(330, Method("Card", givesChange: false), Amounts(2000, 330));

        Assert.False(outcome.Abandoned);
        Assert.Equal(330, outcome.Payments[0].AmountPence);
    }
}
