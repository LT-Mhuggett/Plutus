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
    /// <param name="Permission">⚠ WHICH permission was refused, and <paramref name="AmountPence"/>
    /// for how much. The caller MUST escalate with these rather than with the permission it happened
    /// to be thinking about: <see cref="TillGate.CheckCheckout"/> refuses on
    /// <see cref="PermissionCatalogue.PosSell"/> first, so a caller that hardcodes "refund" asks a
    /// supervisor to authorise a £0 refund and then lets an ordinary sale through on the strength of
    /// it — nobody having been asked whether this operator may sell at all.</param>
    public sealed record GateDecision(
        bool Allowed, bool NeedsOverride, string Message, string Permission = null, long? AmountPence = null)
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
        /// May this operator do ANY ONE of these, for <paramref name="amountPence"/>?
        ///
        /// ⚠ IT EXISTS BECAUSE THE TILL'S GATE MUST MIRROR THE SERVER'S, and on 2026-08-11 it did
        /// not. `POST /api/v1/stock/movements` is gated
        /// `perm:portal.stock.adjust,pos.stock.adjust` — **either** grants it — but the screen in
        /// front of it asked `Check(…, PosStockAdjust)` alone. An **Owner**, who holds the portal
        /// code, was therefore refused BY THE TILL for an action the platform would have accepted.
        ///
        /// ⚠ That failure mode is the nasty one: the refusal is polite, correct-looking, and
        /// mentions a permission the operator does in fact have an equivalent of. Nobody debugs a
        /// message that reads like a deliberate policy.
        ///
        /// ⚠ THE REFUSAL NAMES THE FIRST PERMISSION, which is why the till-side code goes first in
        /// every call: "you need pos.stock.adjust" is actionable; "you need portal.stock.adjust" on
        /// a till screen sends somebody to the wrong place.
        /// </summary>
        public static GateDecision CheckAny(
            SignedInOperator? operatorSignedIn, long? amountPence, params string[] permissions)
        {
            if (permissions is null || permissions.Length == 0)
                throw new ArgumentException("At least one permission is required.", nameof(permissions));

            foreach (var permission in permissions)
            {
                var decision = Check(operatorSignedIn, permission, amountPence);
                if (decision.Allowed) return decision;
            }

            // ⚠ The FIRST one's refusal — see the header. A null operator produces the "nobody is
            // signed in" wording either way, which is the right answer regardless of which code
            // was being asked about.
            return Check(operatorSignedIn, permissions[0], amountPence);
        }

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
                    "Nobody is signed in on this till, so this can't be authorised. Sign in and try again.",
                    permission, amountPence);

            if (operatorSignedIn.Can(permission, amountPence))
                return GateDecision.Ok;

            // ⚠ "Not at all" and "not for this much" lead somewhere different: one needs a
            // different person, the other needs a more senior one. Saying which saves an argument
            // at the counter.
            //
            // ⚠ `hasItAtAll` requires an amount. Reaching here with a null amount means
            // `Can(permission, null)` ALREADY returned false above — the operator provably lacks
            // the permission outright — so testing it again would say "can't authorise this amount"
            // about an action that has no amount. Every price-override refusal used to read that way.
            var hasItAtAll = amountPence is not null && operatorSignedIn.Can(permission, null);

            return new GateDecision(false, NeedsOverride: true, hasItAtAll
                ? $"{operatorSignedIn.DisplayName} can't authorise this amount. A supervisor can."
                : $"{operatorSignedIn.DisplayName} doesn't have permission for this. A supervisor can authorise it.",
                permission, amountPence);
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
        /// <summary>
        /// Has this operator anything to do in the portal?
        ///
        /// ⚠⚠ C2 TWIN of `hasPortalAccess` in the web till's `sibling.ts`:
        /// `scopes.some(s => s.startsWith("portal.") || s === "platform-admin")`. **Two languages, one
        /// rule** — if the definition of "may use the portal" changes, it changes here too, or the
        /// two tills disagree about who is shown the door.
        ///
        /// ⚠ UI ONLY, and it must stay that way. The portal gates every one of its own endpoints on
        /// the same permissions regardless, so hiding this button is a courtesy — not showing a
        /// cashier a door they will be turned away from. It is not security and must never be relied
        /// on as such.
        /// </summary>
        public static bool MayOpenPortal(SignedInOperator? operatorSignedIn) =>
            operatorSignedIn?.Grants?.Any(g =>
                g.Code is not null &&
                (g.Code.StartsWith("portal.", StringComparison.OrdinalIgnoreCase)
                 || g.Code.Equals("platform-admin", StringComparison.OrdinalIgnoreCase))) == true;

        public static long RefundAmountPence(IEnumerable<IBasketRecord> basket)
        {
            var returns = CheckoutCommit.LinesFrom(basket).Where(l => l.IsReturn).ToList();
            if (returns.Count == 0) return 0;

            // Total() signs returns negative; a ceiling is compared against a magnitude.
            return Math.Abs(SaleAssembler.Total(returns).GrossPence);
        }

        /// <summary>Does this basket contain anything being returned?</summary>
        public static bool HasReturns(IEnumerable<IBasketRecord> basket) =>
            basket?.Any(r => r.IsReturn) == true;

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
