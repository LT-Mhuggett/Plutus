using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CustomViews;
using Mopups.Services;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Views.CustomViews;

namespace Plutus.Frontend.AppClient.Helpers.CustomViews
{
    /// <summary>
    /// Show the one-screen checkout and hand back what was tendered — §5c item 2.
    ///
    /// ⚠ ONE PUSH, ONE AWAIT, ONE POP **IN A `finally`** — the rule `InputAlertHelper.ShowAsync`'s
    /// header records the hard way. An un-popped popup is a 40%-black sheet over a till nobody can
    /// dismiss, mid-shift, with a full basket.
    ///
    /// ⚠⚠ **THE PAYMENTS ARE BUILT HERE, FROM `TenderSettlement`, AND NOWHERE ELSE.** The screen
    /// collects text; this turns it into money exactly once, using the same functions the web till
    /// uses. In particular the CHANGE per method comes from `Assess`, which apportions it so
    /// Σchange == the overpay to the penny — the v1 pipeline rejects a sale where net tender ≠ gross,
    /// and it says nothing useful when it does.
    ///
    /// ⚠⚠ **BACKED OUT MEANS `null`, AND THE CALLER MUST HANDLE IT** (dialog contract D4 rule 4). Not
    /// an empty list — an empty list is indistinguishable from "tendered nothing", and a caller that
    /// treated the two alike would commit a sale with no payments on it.
    /// </summary>
    public static class CheckoutHelper
    {
        /// <summary>What came back off the checkout screen.</summary>
        public sealed record Result(
            /// <summary>⚠ NULL when the operator backed out. Never an empty list — see the class remarks.</summary>
            IReadOnlyList<TenderPayment> Payments,
            long ChangePence,
            /// <summary>They pressed "Pay with a gift card" — the caller scans, then reopens this.</summary>
            bool WantsGiftCard)
        {
            public static readonly Result Abandoned = new(null, 0, false);

            public static readonly Result GiftCard = new(null, 0, true);

            public bool IsAbandoned => Payments is null && !WantsGiftCard;
        }

        /// <param name="body">The screen, already built AND already wired by the caller.
        /// ⚠ It arrives prebuilt because the caller subscribes to `CardTenderedChanged` before it is
        /// shown — the card fee is a BASKET line, and only the caller may touch the basket.</param>
        public static async Task<Result> ShowAsync(CheckoutAlert body)
        {
            var totalPence = body.TotalPence;
            var rows = body.Rows;
            var outcome = Result.Abandoned;

            // ⚠ `interuptable: true` — tapping away cancels, like the web till's overlay click. Nothing
            // here has been committed, so there is no half-finished state to protect: the money moves
            // only after Complete, in the caller.
            var popUp = new AlertDialogBase<bool>(body, true);

            void Finish() => popUp.PageClosedTaskCompletionSource.TrySetResult(true);

            // ⚠ EVERY EXIT COMPLETES THE SAME TASK, so there is exactly one way out and no path that
            // leaves the sheet up. ✕, Cancel and tapping away all land on `Abandoned`.
            body.CloseRequested += (_, _) => { outcome = Result.Abandoned; Finish(); };

            body.GiftCardRequested += (_, _) => { outcome = Result.GiftCard; Finish(); };

            body.CompleteRequested += (_, _) =>
            {
                // ⚠ RE-ASSESSED HERE, not trusted from the screen's own enabled state. The button is
                // a hint; this is the decision. A stale `IsEnabled` — an event that arrived out of
                // order, a keystroke mid-tap — must not be able to complete a sale that does not
                // balance, because the next thing that happens is a drawer opening.
                var settled = Settle(totalPence, body.Amounts, rows);
                if (settled is null) return;      // ⚠ Does not balance → the tap does nothing.

                outcome = settled;
                Finish();
            };

            await Services.UIHandeling.Modal.ShowAsync(async () =>
            {
                try
                {
                    await MopupService.Instance.PushAsync(popUp);
                    return await popUp.PageClosedTask;
                }
                finally
                {
                    try
                    {
                        await MopupService.Instance.PopAsync();
                    }
                    catch (Exception ex)
                    {
                        // ⚠ Swallowed only here: a failing pop must not replace the real exception.
                        Debug.WriteLine(ex);
                    }
                }
            });

            return outcome;
        }

        /// <summary>
        /// Turn the typed amounts into payments, or null when the screen does not balance.
        ///
        /// ⚠ THE ONLY PLACE THE SCREEN BECOMES MONEY. Everything it does comes out of
        /// `TenderSettlement`, so the MAUI till and the web till cannot disagree about a basket.
        ///
        /// ⚠ A REFUND IS NEGATIVE ON THE WIRE, because the ingest invariant is
        /// `Σ tender − Σ change == GrossPence` and a refund's gross is negative. Sending positives on
        /// a refund quarantines the sale, and `OutboxPusher` treats a 202 as terminal — the refund
        /// would never retry and never arrive.
        /// </summary>
        internal static Result Settle(
            long totalPence,
            IReadOnlyDictionary<int, string> amounts,
            IReadOnlyList<CheckoutAlert.Row> rows)
        {
            var parsed = TenderSettlement.ParseAmounts(amounts);
            var methods = rows.Select(r => new TenderMethodRef(r.PayId, r.IsChangeable)).ToList();
            var settlement = TenderSettlement.Assess(totalPence, parsed, methods);

            if (TenderSettlement.Refusal(settlement, parsed, p => p.ToString()) != null) return null;

            var refunding = settlement.Refunding;

            var payments = parsed.PerMethod
                // ⚠ Ordered by pay-method id so the payment list — and therefore the receipt — reads
                // the same on two tills settling the same basket.
                .OrderBy(kv => kv.Key)
                .Select(kv => new TenderPayment(
                    rows.First(r => r.PayId == kv.Key).Name,
                    refunding ? -kv.Value : kv.Value,
                    settlement.ChangeByPayId.TryGetValue(kv.Key, out var change) ? change : 0))
                .ToList();

            // ⚠ A sale with no payments cannot be right, and a zero-total basket should not have got
            // this far — refuse rather than commit an empty tender list.
            if (payments.Count == 0) return null;

            return new Result(payments, settlement.Overpay, false);
        }
    }
}
