using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Connectivity;
using Plutus.Frontend.AppClient.Services.Storage;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Sales
{
    /// <summary>
    /// What the till found out about a sale someone wants to return against.
    /// </summary>
    /// <param name="UnitIncPence">⚠ WHAT THE CUSTOMER ACTUALLY PAID for this item on that sale —
    /// not today's price. A price that has moved since would otherwise refund the wrong amount in
    /// whichever direction the shop happens to lose.</param>
    public sealed record ReturnResolution(
        RefundDecision Decision,
        long UnitIncPence,
        long UnitExPence,
        Guid OriginSaleId)
    {
        public bool IsAllowed => Decision.IsAllowed;
    }

    /// <summary>
    /// Looks a sale up and decides whether it can be refunded (cutover step 16).
    ///
    /// ⚠ IT REPLACES A BLOCK THAT COULD NOT WORK AND CRASHED WHEN IT TRIED. The legacy code queried
    /// the local legacy `Trans` table — empty on a portal-provisioned till, and permanently so,
    /// because sales are committed to the new store — then called `trans.First()`, which throws
    /// `InvalidOperationException` from an `async void` with no catch the moment the item is not on
    /// that sale. Its "already refunded" test counted QUANTITIES on this machine only.
    ///
    /// ⚠ THE SERVER IS PREFERRED WHENEVER IT ANSWERS, and this is the whole point: goods bought on
    /// till B and returned at till A are ordinary retail, and only the platform knows what has
    /// already been given back somewhere else. A local record is used only INSIDE the rolling
    /// window, where this till can still see its own history; outside it the answer is "reconnect",
    /// never a guess.
    ///
    /// ⚠ THE CAP IS PER SALE, NOT PER LINE — say so rather than imply precision it does not have.
    /// `LocalRefunds` records money per ORIGINAL SALE, so this enforces Matt's rule in aggregate
    /// (binding default 12: never more than was paid) but cannot by itself stop the same LINE being
    /// refunded twice within a larger sale total. Per-line accounting is the server's, in step 17.
    /// </summary>
    public static class ReturnLookup
    {
        /// <summary>
        /// Resolve a sale and decide the refund.
        /// </summary>
        /// <param name="saleIdText">As typed or scanned. The receipt barcode is the platform sale
        /// id without dashes, so both forms are accepted.</param>
        /// <param name="itemIdOne">The barcode of the item coming back.</param>
        /// <param name="quantity">How many are coming back.</param>
        public static async Task<ReturnResolution> ResolveAsync(
            string saleIdText, string itemIdOne, int quantity, CancellationToken ct = default)
        {
            if (!TryParseSaleId(saleIdText, out var saleId))
                return NotFound("That doesn't look like a sale number from a Plutus receipt.");

            // ── the server first: only it knows what other tills have refunded ──
            var fromServer = await TryServerAsync(saleId, ct).ConfigureAwait(false);
            if (fromServer is not null)
                return Decide(fromServer.Value.Source, fromServer.Value.Sale, itemIdOne, quantity,
                    fromServer.Value.AlreadyRefundedPence, saleId);

            // ── then this till's own record, and only inside the window ──
            var local = await TryLocalAsync(saleId, ct).ConfigureAwait(false);
            if (local is null)
                return NotFound(
                    "That sale isn't on this till, and Plutus can't be reached to look it up. "
                    + "Reconnect and try again.");

            return Decide(local.Value.Source, local.Value.Sale, itemIdOne, quantity,
                local.Value.AlreadyRefundedPence, saleId);
        }

        /// <summary>
        /// ⚠ Accepts BOTH forms. The receipt prints the id with no dashes (`"N"`), because that is
        /// what fits a barcode; an operator reading it off a portal screen types the dashed form.
        /// Refusing one of them means a legitimate receipt that cannot be refunded.
        /// </summary>
        internal static bool TryParseSaleId(string text, out Guid saleId)
        {
            saleId = Guid.Empty;
            return !string.IsNullOrWhiteSpace(text) && Guid.TryParse(text.Trim(), out saleId) && saleId != Guid.Empty;
        }

        private static ReturnResolution NotFound(string reason) =>
            new(new RefundDecision(RefundVerdict.UnknownSale, 0, false, reason), 0, 0, Guid.Empty);

        /// <summary>The sale as the platform holds it, plus everything already refunded against it.</summary>
        private static async Task<(SaleRecordSource Source, SaleLines Sale, long AlreadyRefundedPence)?>
            TryServerAsync(Guid saleId, CancellationToken ct)
        {
            try
            {
                // ⚠ THE OPERATOR'S TOKEN, NOT THE TILL'S — and this is why cross-till refunds have
                // never once worked. `GET /api/v1/sales/{saleId}` is gated
                // `perm:portal.financials.view,pos.reports.view,pos.refund`; `perm:*` policies
                // resolve from RBAC by the token's **userId**, and a DEVICE token has no userId. So
                // this call answered 403 every single time, the `catch` below swallowed it exactly
                // as designed, and the till fell back to its own record — reporting "we have no
                // record of that sale" for goods bought at another branch, on a platform holding
                // the sale all along. Nothing logged, because nothing was wrong.
                //
                // ⚠ Null when nobody is signed in, which is CORRECT rather than a degradation: the
                // platform's answer to "may this person see this sale" is a permission held by a
                // person, and there is no person. The local record still answers for this till's own
                // sales, which is the common case.
                var api = await PlutusApi.GetOperatorAsync(ct).ConfigureAwait(false);
                if (api is null) return null;

                var dto = await api.GetSaleAsync(saleId, ct).ConfigureAwait(false);
                if (dto is null) return null;   // offline, or the platform genuinely has no such sale

                var lines = dto.Lines.Select(l => new SaleLineFacts(
                    l.ItemIdOne ?? string.Empty, l.Qty, l.UnitPricePence, l.UnitExPence, l.LineGrossPence)).ToList();

                // ⚠ THE SERVER'S FIGURE PLUS WHAT THIS TILL IS STILL HOLDING, and the sum is what
                // stops a double refund inside the drain window. The platform only counts refunds it
                // has RECEIVED; a refund sits in the outbox for up to a minute. On 2026-08-10 the
                // same £13.99 went out twice inside 97 milliseconds — the server answered "nothing
                // refunded" both times, entirely correctly, while this till's own record knew.
                //
                // ⚠ ADDED, not max()'d. They do not overlap: the server counts DELIVERED refunds
                // (from any till), this counts UNDELIVERED ones (from this till), so a delivered
                // refund is in exactly one of them. Taking the larger would miss the case where
                // another till has refunded and this one also has something queued.
                var undelivered = await TillStoreAccess.TryUseAsync(
                    s => s.UndeliveredRefundedPenceAsync(saleId, ct), ct).ConfigureAwait(false);

                return (SaleRecordSource.Server, new SaleLines(dto.GrossPence, lines),
                    dto.AlreadyRefundedPence + undelivered);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("ReturnLookup.TryServerAsync", ex);
                return null;   // fall through to the local record; never a payout on an exception
            }
        }

        private static async Task<(SaleRecordSource Source, SaleLines Sale, long AlreadyRefundedPence)?>
            TryLocalAsync(Guid saleId, CancellationToken ct)
        {
            try
            {
                var payload = await TillStoreAccess.UseAsync(s => s.FindLocalSaleAsync(saleId, ct), ct).ConfigureAwait(false);
                if (payload is null) return null;

                var source = RefundRules.ClassifyLocal(payload.OccurredAtUtc, DateTime.UtcNow);

                var already = await TillStoreAccess.UseAsync(
                    s => s.AlreadyRefundedPenceAsync(saleId, ct), ct).ConfigureAwait(false);

                var lines = payload.Lines.Select(l =>
                {
                    var meta = LineMeta.FromJson(l.DiscountsJson);
                    return new SaleLineFacts(
                        meta?.ItemIdOne ?? string.Empty, l.Qty, l.UnitPricePence,
                        meta?.ExUnitPence ?? l.UnitPricePence, l.LineGrossPence);
                }).ToList();

                return (source, new SaleLines(payload.GrossPence, lines), already);
            }
            catch (Exception ex)
            {
                Analytics.CrashLog.Write("ReturnLookup.TryLocalAsync", ex);
                return null;
            }
        }

        /// <summary>
        /// Find the item on the sale and put the numbers through the shared rule.
        /// </summary>
        internal static ReturnResolution Decide(
            SaleRecordSource source, SaleLines sale, string itemIdOne, int quantity,
            long alreadyRefundedPence, Guid saleId)
        {
            // ⚠ THE CRASH THIS REPLACES. The legacy code did `trans.First()` on the matching rows,
            // which throws when the item is not on that sale — from an `async void` with no catch,
            // so scanning the wrong receipt closed the app. It is an ordinary mistake at a counter
            // and deserves a sentence, not a crash.
            var line = sale.Lines.FirstOrDefault(l =>
                !string.IsNullOrEmpty(l.ItemIdOne) &&
                string.Equals(l.ItemIdOne, itemIdOne, StringComparison.OrdinalIgnoreCase));

            if (line is null)
                return new ReturnResolution(
                    new RefundDecision(RefundVerdict.UnknownSale, 0, false,
                        "That item isn't on this sale. Check the receipt is the right one."),
                    0, 0, saleId);

            // ⚠ Positive magnitudes into the shared rule: a sale's own lines are positive, but a
            // sale that was ITSELF a refund has negative ones, and a negative "original" would make
            // every remainder nonsense.
            var requested = Math.Abs(line.UnitIncPence) * Math.Max(1, quantity);

            var decision = RefundRules.Authorise(
                source, Math.Abs(sale.GrossPence), alreadyRefundedPence, requested);

            return new ReturnResolution(decision, Math.Abs(line.UnitIncPence), Math.Abs(line.UnitExPence), saleId);
        }

        /// <summary>The facts a refund decision needs about one line, from either record.</summary>
        internal sealed record SaleLineFacts(
            string ItemIdOne, int Qty, long UnitIncPence, long UnitExPence, long LineGrossPence);

        /// <summary>A sale reduced to what a refund decision needs, from either record.</summary>
        internal sealed record SaleLines(long GrossPence, System.Collections.Generic.IReadOnlyList<SaleLineFacts> Lines);
    }
}
