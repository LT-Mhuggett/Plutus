using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Models;
using Plutus.Frontend.AppClient.Services.Storage;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Security
{
    /// <summary>Whether an action may proceed, and what to tell the person if not.</summary>
    /// <param name="NeedsOverride">Refused, but a supervisor could authorise it. ⚠ False for
    /// "nobody is signed in" — that is not something another person's password fixes.</param>
    public sealed record GateDecision(bool Allowed, bool NeedsOverride, string Message)
    {
        public static readonly GateDecision Ok = new(true, false, "");
    }

    /// <summary>
    /// The till's permission gate (cutover step 12).
    ///
    /// ⚠ IT REPLACES A MODEL THAT COULD NOT WORK. The legacy gate looked up string action names
    /// ("Till", "Refund20", "Refund100") in a local `AuthActions` table via
    /// `Authorisation.IsAuthorised`, which a portal-provisioned till does not have — and its
    /// supervisor-override helper, `RequestAuthorisedUserInput`, **never assigns the id it
    /// returns**, so entering correct credentials re-prompts for ever and only Cancel escapes.
    /// Worse, the caller then re-tested the ORIGINAL operator's permissions and ignored the
    /// authoriser entirely. Supervisor override on this till has never once succeeded.
    ///
    /// ⚠ A NULL OPERATOR BLOCKS. `AppViewModel.SignedInOperator` is null after a legacy local
    /// login, and "we don't know who this is" must never resolve to "let them". A till that falls
    /// open when it cannot identify the operator has no ceilings at all — the failure is silent and
    /// the evidence is a refund nobody can attribute.
    /// </summary>
    public static class TillGate
    {
        /// <summary>
        /// May this operator do <paramref name="permission"/>, for <paramref name="amountPence"/>?
        ///
        /// ⚠ Delegates to <see cref="SignedInOperator.Can"/> — the shared rule, which applies the
        /// grant's ceiling, its time window against the TILL's clock, and the offline staleness
        /// tier. Nothing is re-implemented here; this only decides what to do with the answer.
        /// </summary>
        public static GateDecision Check(SignedInOperator? operatorSignedIn, string permission, long? amountPence = null)
        {
            if (operatorSignedIn is null)
                return new GateDecision(false, NeedsOverride: false,
                    "Nobody is signed in on this till, so this can't be authorised. Sign in and try again.");

            if (operatorSignedIn.Can(permission, amountPence))
                return GateDecision.Ok;

            // ⚠ "Not at all" and "not for this much" lead somewhere different: one needs a
            // different person, the other needs a more senior one. Saying which saves an argument
            // at the counter.
            var atAll = amountPence is null || operatorSignedIn.Can(permission, null);

            return new GateDecision(false, NeedsOverride: true, atAll
                ? $"{operatorSignedIn.DisplayName} can't authorise this amount. A supervisor can."
                : $"{operatorSignedIn.DisplayName} doesn't have permission for this. A supervisor can authorise it.");
        }

        /// <summary>
        /// What this basket would actually refund, in pence, as a POSITIVE number.
        ///
        /// ⚠ THIS FIXES A REAL HOLE. The legacy check summed each return line's UNIT price and
        /// ignored quantity — `Sum(bRI => bRI.Price)` — so five £30 returns on one line tested as
        /// £30 and sailed through a £100 ceiling that should have stopped them at £150. The ceiling
        /// was not being enforced against the money actually leaving the drawer.
        ///
        /// ⚠ Computed with `SaleAssembler.Total`, the SAME arithmetic that builds the payload, so
        /// the amount authorised is exactly the amount refunded — including quantity, discounts and
        /// price overrides. A separately-written total here would be a second opinion about money.
        /// </summary>
        public static long RefundAmountPence(IEnumerable<IBasketRecord> basket)
        {
            var returns = CheckoutCommit.LinesFrom(basket).Where(l => l.IsReturn).ToList();
            if (returns.Count == 0) return 0;

            // Total() signs returns negative; a ceiling is compared against a magnitude.
            return Math.Abs(SaleAssembler.Total(returns).GrossPence);
        }

        /// <summary>Does this basket contain anything being returned?</summary>
        public static bool HasReturns(IEnumerable<IBasketRecord> basket) =>
            basket?.Any(r => r is BasketReturnItem) == true;

        /// <summary>
        /// The gate for completing a sale: always <see cref="PermissionCatalogue.PosSell"/>, plus
        /// <see cref="PermissionCatalogue.PosRefund"/> against the refunded amount when the basket
        /// contains returns.
        ///
        /// ⚠ The refund ceiling is checked against the WHOLE refunded amount, not per line. Five
        /// £30 lines is a £150 refund however it was rung up, and splitting it across lines must
        /// not be a way under a ceiling.
        /// </summary>
        public static GateDecision CheckCheckout(SignedInOperator? operatorSignedIn, IEnumerable<IBasketRecord> basket)
        {
            var sell = Check(operatorSignedIn, PermissionCatalogue.PosSell);
            if (!sell.Allowed) return sell;

            if (!HasReturns(basket)) return GateDecision.Ok;

            return Check(operatorSignedIn, PermissionCatalogue.PosRefund, RefundAmountPence(basket));
        }
    }
}
