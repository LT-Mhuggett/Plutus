using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.Client.Core;

/// <summary>What the operator picked when asked how they are paying.</summary>
/// <param name="MethodName">The tender's name, or <see langword="null"/> if they backed out.</param>
/// <param name="GivesChange">Whether this method can hand money back — cash can, card cannot.</param>
/// <param name="SurchargePence">A fee this METHOD adds to the sale (a card surcharge). ⚠ The loop
/// applies it AT MOST ONCE however many times the method is chosen — see
/// <see cref="TenderLoop.RunAsync"/>.</param>
/// <param name="CapPence">
/// ⚠⚠ THE MOST THIS METHOD MAY TAKE, or 0 for no limit — finding Y, 2026-08-13.
///
/// On a REFUND this is what the method actually took on the original sale, less anything already
/// given back to it (<see cref="Plutus.SharedKernel.RefundRules.RefundCapacities"/>). Matt found the
/// hole by hand: *"when I try to return an item that was split, it wants to put the full amount to
/// that card."* £2.00 cash + £2.40 card refunded £4.40 to the card credits the card £2.40 it never
/// took and leaves the £2.00 in the drawer.
///
/// ⚠ ENFORCED BY THE LOOP, not merely used to pre-fill a box. A caller that pre-fills sensibly and
/// then accepts whatever comes back has no rule in it — the operator can always type over a default,
/// and on a touch till they routinely do.
///
/// ⚠ Zero means UNCAPPED, which is right for an ordinary sale: nothing limits how much cash a
/// customer may hand over. It does NOT mean "this method may take nothing" — a spent tender is
/// removed from the offered set instead, so the operator is never shown a method that cannot be used.
/// </param>
public sealed record TenderChoice(
    string? MethodName, bool GivesChange, long SurchargePence = 0, long CapPence = 0)
{
    /// <summary>The operator backed out of choosing a method.</summary>
    public static readonly TenderChoice Abandoned = new(null, false);

    public bool IsAbandoned => MethodName is null;
}

/// <summary>What the operator entered when asked how much.</summary>
public sealed record TenderAmount(long Pence, bool IsAbandoned)
{
    /// <summary>They backed out of the amount prompt. ⚠ NOT the same as entering nothing.</summary>
    public static readonly TenderAmount Abandoned = new(0, true);

    public static TenderAmount Of(long pence) => new(pence, false);
}

/// <summary>One tender actually taken.</summary>
public sealed record TenderPayment(string MethodName, long AmountPence, long ChangePence);

/// <summary>
/// Why the loop would not take an amount, so the SCREEN can say so.
///
/// ⚠ IT EXISTS BECAUSE THE LOOP REFUSED IN SILENCE. Matt, 2026-08-11: over-paying on a card said
/// *"Something went wrong"* and under-paying in cash said the same. Both are ordinary operator
/// actions with obvious explanations — the loop knew exactly which one had happened and simply
/// re-asked, so the only thing in front of the customer was a prompt that had apparently ignored
/// them.
///
/// ⚠ The WORDING stays in the UI layer. This is the reason; the sentence is the screen's.
/// </summary>
public enum TenderRefusal
{
    None = 0,

    /// <summary>Zero settles nothing, so accepting it is an infinite loop with a friendly face.</summary>
    Zero = 1,

    /// <summary>Money going the wrong way — paying out to settle a sale, or in to settle a refund.</summary>
    WrongDirection = 2,

    /// <summary>⚠ More than the balance on a method that cannot hand the difference back. A card
    /// cannot give change; the operator who typed £20 for a £3 sale meant to type £3.</summary>
    OverpaidWithoutChange = 3,

    /// <summary>
    /// ⚠⚠ More than this METHOD took on the sale being refunded (finding Y). Distinct from
    /// <see cref="OverpaidWithoutChange"/>, which is about the sale's balance: this one is about where
    /// the money is allowed to go. Refunding £4.40 to a card that took £2.40 of a split payment is
    /// within the sale's balance and still wrong.
    /// </summary>
    OverTenderCapacity = 4,
}

/// <summary>
/// How the tendering ended.
///
/// ⚠ <see cref="Abandoned"/> means NOTHING WAS TAKEN and the caller must leave the basket alone.
/// It is not a partial success: the payments list is empty.
/// </summary>
public sealed record TenderOutcome(
    bool Abandoned,
    IReadOnlyList<TenderPayment> Payments,
    long ChangePence,
    long SurchargePence,
    long SettledTotalPence)
{
    public static TenderOutcome GaveUp(long surchargePence = 0) =>
        new(true, Array.Empty<TenderPayment>(), 0, surchargePence, 0);
}

/// <summary>
/// Taking payment for a basket: choose a method, enter an amount, repeat until it is settled.
///
/// ⚠ WHY THIS IS ITS OWN CLASS, AND WHY IT LIVES HERE. It was ~90 lines in the middle of
/// `TillViewModel.ExecuteCheckoutTransaction`, a ~200-line `async void` that also assembles the
/// sale, adds the surcharge line, commits and prints. **Nothing in that method could be exercised
/// without a running UI host**, so all three tendering defects found on 2026-08-10 shipped and were
/// found by hand:
///
///   • the amount prompt had no Cancel button, no background dismiss and swallowed Escape, so the
///     operator could not leave it at all;
///   • cancelling FELL THROUGH — the payment was appended with £0 and the loop re-opened the same
///     inescapable prompt for ever;
///   • entering `0` did the same thing, because `paid += 0` never moves `paid` toward the total.
///
/// Every one is a loop that cannot terminate, and every one is trivial to write a test for once the
/// loop is separated from the dialogs. That is the whole point of this file.
///
/// ⚠ IT KNOWS NOTHING ABOUT UI. The two callbacks are how it asks; whether they are MAUI dialogs, a
/// web till's modal or a test's scripted answers is not its business. That is what makes it the
/// single home for the tendering RULE across every .NET till — till-design C1.
///
/// ⚠ MONEY IS <see langword="long"/> PENCE, and refunds are NEGATIVE totals throughout. The rules
/// below are written in terms of what is still OUTSTANDING, so a refund is the same algorithm with
/// the sign flipped rather than a second code path — which is what the original had, and why
/// over-refunding slipped past its `paid > total` guard.
/// </summary>
public static class TenderLoop
{
    /// <summary>
    /// ⚠ A STUCK-UI BACKSTOP, not a business rule. If this many tenders in a row are refused, the
    /// prompts are not reaching a human — an operator who cannot pay presses Cancel long before
    /// twenty tries. Without it a caller whose amount prompt returns instantly (a mis-wired dialog,
    /// exactly what happened on 2026-08-10) spins this loop for ever with no way out.
    /// </summary>
    public const int MaxConsecutiveRefusals = 20;

    /// <summary>
    /// Run the tendering.
    /// </summary>
    /// <param name="totalPence">What the basket comes to. NEGATIVE for a refund.</param>
    /// <param name="chooseMethod">
    /// Ask which tender. Receives **what is still outstanding, and what has been taken so far**.
    ///
    /// ⚠ THE SECOND ARGUMENT EXISTS BECAUSE A SPLIT PAYMENT LOOKED BROKEN. Matt, 2026-08-13, on a
    /// £4.40 basket: *"I press cash, put in £2, it takes me back to the 'Card or cash' screen but
    /// doesn't tell me anything has been paid or there is X to pay. I assume its not actually
    /// working."* It was working — this loop had the £2 and the £2.40 — but the screen it handed back
    /// to knew neither, so the till looked like it had swallowed the money.
    ///
    /// ⚠ It comes from HERE rather than being derived by the caller on purpose. The caller's running
    /// total is the basket, and the loop adds the card surcharge to what is owed — so a caller
    /// computing `paid = myTotal - outstanding` would be right until a tenant switched a card fee on,
    /// and then quietly wrong by the fee, on a screen showing an operator how much money they hold.
    /// </param>
    /// <param name="askAmount">
    /// Ask how much. Receives **what is still outstanding, and the most this tender may take** — the
    /// second being the smaller of the balance and the chosen method's <see cref="TenderChoice.CapPence"/>.
    /// ⚠ Pre-fill the box with the SECOND number: a default the loop will refuse teaches an operator to
    /// type over it.
    /// </param>
    /// <param name="onRefused">
    /// ⚠ TELL THE OPERATOR WHY, before asking again. Optional only so existing callers and tests
    /// keep compiling — a real screen must supply it, because a prompt that re-appears without
    /// explanation reads as the till ignoring what was typed. It is awaited, so the message is
    /// gone before the next dialog opens.
    /// </param>
    public static async Task<TenderOutcome> RunAsync(
        long totalPence,
        Func<long, long, Task<TenderChoice>> chooseMethod,
        Func<long, long, Task<TenderAmount>> askAmount,
        CancellationToken ct = default,
        Func<TenderRefusal, long, Task>? onRefused = null)
    {
        if (chooseMethod is null) throw new ArgumentNullException(nameof(chooseMethod));
        if (askAmount is null) throw new ArgumentNullException(nameof(askAmount));

        var payments = new List<TenderPayment>();
        var outstanding = totalPence;
        var total = totalPence;
        long surcharge = 0;
        long change = 0;
        var refusals = 0;

        // ⚠ A ZERO-TOTAL BASKET IS ALREADY SETTLED. Asking for a tender would be a prompt no answer
        // can satisfy: any amount overshoots, and zero is refused.
        while (outstanding != 0)
        {
            ct.ThrowIfCancellationRequested();

            if (refusals >= MaxConsecutiveRefusals) return TenderOutcome.GaveUp(surcharge);

            // ⚠ `total` already carries any surcharge this loop applied, so this is what the operator
            // is actually holding — not what the basket came to.
            var choice = await chooseMethod(outstanding, total - outstanding).ConfigureAwait(false);

            // ⚠ A BLANK NAME IS ABANDONMENT, not a nameless tender. The name is what
            // `Tenders.FromMethodName` turns into the wire byte every payment-split report groups
            // by, so a payment recorded without one is money that reconciles against nothing.
            var method = choice?.MethodName;
            if (choice is null || choice.IsAbandoned || string.IsNullOrWhiteSpace(method))
                return TenderOutcome.GaveUp(surcharge);

            // ⚠ AT MOST ONCE, and this is a rule of the LOOP rather than a courtesy of the caller.
            // A split payment across two card tenders must not charge the flat fee twice, and the
            // original relied on the caller re-checking the basket each pass to prevent it.
            if (choice.SurchargePence != 0 && surcharge == 0)
            {
                surcharge = choice.SurchargePence;
                total += surcharge;
                outstanding += surcharge;
            }

            // ⚠ THE MOST THIS PROMPT MAY LEGITIMATELY BE ANSWERED WITH. Normally the whole balance; on
            // a refund, no more than the chosen method took on the original sale (finding Y). Passed to
            // the caller so the box can be pre-filled with a number that will be ACCEPTED — a prompt
            // whose default is refused is a prompt that teaches the operator to ignore it.
            var mostAllowed = choice.CapPence > 0 && choice.CapPence < Math.Abs(outstanding)
                ? choice.CapPence * Math.Sign(outstanding)
                : outstanding;

            var answer = await askAmount(outstanding, mostAllowed).ConfigureAwait(false);
            if (answer is null || answer.IsAbandoned) return TenderOutcome.GaveUp(surcharge);

            var amount = answer.Pence;

            // ⚠⚠ THE CAP IS THE LOOP'S RULE, NOT THE SCREEN'S (finding Y). Checked BEFORE the
            // change/overpay rules below, because "that money cannot go back this way" is a different
            // and stricter statement than "this method cannot give change" — and reporting the wrong one
            // sends the operator looking for a different card rather than splitting the refund.
            if (choice.CapPence > 0 && Math.Abs(amount) > choice.CapPence)
            {
                refusals++;
                if (onRefused is not null)
                    await onRefused(TenderRefusal.OverTenderCapacity, outstanding).ConfigureAwait(false);
                continue;
            }

            // ⚠ ZERO NEVER SETTLES ANYTHING, so accepting it is an infinite loop with a friendly
            // face. The original did `paid += amount` unconditionally and re-prompted for ever.
            // ⚠ Nor may a tender run AGAINST what is outstanding — handing money out to settle a
            // sale, or taking it in to settle a refund. `paid > total` could not express this for
            // refunds, where both numbers are negative.
            if (amount == 0 || Math.Sign(amount) != Math.Sign(outstanding))
            {
                refusals++;
                if (onRefused is not null)
                    await onRefused(amount == 0 ? TenderRefusal.Zero : TenderRefusal.WrongDirection,
                                    outstanding).ConfigureAwait(false);
                continue;
            }

            if (Math.Abs(amount) > Math.Abs(outstanding))
            {
                // ⚠ Overpaying is only allowed when the method can hand the difference back.
                // Refusing re-asks rather than abandoning: an operator who typed £20 for a £3 sale
                // on a card meant to type £3.
                if (!choice.GivesChange)
                {
                    refusals++;
                    if (onRefused is not null)
                        await onRefused(TenderRefusal.OverpaidWithoutChange, outstanding)
                            .ConfigureAwait(false);
                    continue;
                }

                change = amount - outstanding;
                payments.Add(new TenderPayment(method, amount, change));
                outstanding = 0;
            }
            else
            {
                payments.Add(new TenderPayment(method, amount, 0));
                outstanding -= amount;
            }

            refusals = 0;
        }

        return new TenderOutcome(false, payments, change, surcharge, total);
    }
}
