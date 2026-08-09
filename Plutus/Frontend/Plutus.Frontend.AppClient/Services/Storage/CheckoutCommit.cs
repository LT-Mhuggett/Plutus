using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Models;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>What a checkout commit did, in terms the till can act on.</summary>
    /// <param name="Committed">⚠ False means NOTHING was written and the basket must NOT be
    /// cleared. A checkout that clears the screen after a failed save loses the sale AND the
    /// evidence, and the customer is standing there.</param>
    public sealed record CommitOutcome(bool Committed, Guid SaleId, long DeviceSeq, string Message);

    /// <summary>
    /// Turns the till's basket into a platform sale and commits it (cutover step 11).
    ///
    /// ⚠ THIS IS THE MOMENT THE MAUI TILL BECOMES A PLUTUS TILL. Until now checkout wrote a legacy
    /// EF object graph and called `db.Save()`: the sale existed only on that machine, never reached
    /// `/api/v1/sales`, and appeared in no report, no VAT return and no other till's history.
    ///
    /// ⚠ ONE ROW, ONE TRANSACTION. `TillStore.CommitSaleAsync` writes the sale and its outbox entry
    /// as the SAME row, so they cannot disagree — and it allocates `DeviceSeq` inside that
    /// transaction, so two checkouts racing cannot reuse a sequence the server would dedupe away.
    ///
    /// ⚠ IN A SERVICE, NOT THE VIEWMODEL, so it is testable without a device. `TillViewModel` has
    /// no test coverage at all; this does.
    /// </summary>
    public static class CheckoutCommit
    {
        /// <summary>
        /// Build the payload from a basket. Pure — no I/O — so the arithmetic can be tested.
        ///
        /// ⚠ DECIMAL → PENCE AT THIS BOUNDARY IS LOSSLESS, and only because of step 10: the prices
        /// in the basket ORIGINATED as pence from `EffectivePricePairAsync` and were divided by 100
        /// purely to satisfy the legacy model. Multiplying back recovers exactly what was resolved.
        /// It would NOT be lossless for a price a human typed as a decimal — which is why
        /// `Money.FromDecimal` rounds away from zero rather than banker's, and why the legacy
        /// basket is being retired rather than kept.
        /// </summary>
        public static IReadOnlyList<BasketLine> LinesFrom(IEnumerable<IBasketRecord> basket)
        {
            var lines = new List<BasketLine>();

            foreach (var record in basket ?? Enumerable.Empty<IBasketRecord>())
            {
                // Notes and alterations are not sale lines: an alteration's money is folded into
                // the line it discounts, and a note carries no value at all.
                if (record is not BasketItem item) continue;

                var isReturn = record is BasketReturnItem;

                lines.Add(new BasketLine(
                    // Left empty on purpose — SaleAssembler DERIVES it from businessId + IdOne and
                    // would refuse a mismatched one. One place knows that rule.
                    ItemId: Guid.Empty,
                    IdOne: item.Item?.Id ?? string.Empty,
                    Name: item.Item?.Name ?? string.Empty,
                    UnitIncPence: Pence.FromDecimal(item.Price),
                    UnitExPence: Pence.FromDecimal(item.PriceExTax),
                    Quantity: item.Quantity,
                    // ⚠ Discounts are not carried on the line in the legacy basket — they are
                    // separate BasketAlteration records whose money is already reflected in the
                    // adjusted Price above. Passing a DiscountPence here as well would take it off
                    // twice. Real per-line discounts arrive with the basket reshape.
                    DiscountPence: 0,
                    VatBandKey: null,
                    OverriddenFromPence: null,
                    IsReturn: isReturn,
                    OriginSaleId: isReturn ? OriginOf(record) : null));
            }

            return lines;
        }

        private static Guid? OriginOf(IBasketRecord record) =>
            record is BasketReturnItem r && Guid.TryParse(r.ReturnSaleId, out var id) ? id : null;

        /// <summary>
        /// Assemble and commit. ⚠ Returns an outcome instead of throwing: the caller is a checkout
        /// with a customer waiting, and it needs to decide whether to clear the basket.
        /// </summary>
        public static async Task<CommitOutcome> CommitAsync(
            IEnumerable<IBasketRecord> basket,
            IReadOnlyList<IngestTender> tenders,
            Guid? operatorUserId,
            CancellationToken ct = default)
        {
            try
            {
                var lines = LinesFrom(basket);
                if (lines.Count == 0)
                    return new CommitOutcome(false, Guid.Empty, 0, "There is nothing to sell in this basket.");

                var credentials = await Connectivity.SecureDeviceCredentialStore.LoadAsync().ConfigureAwait(false);
                if (credentials?.DeviceId is not Guid deviceId)
                    return new CommitOutcome(false, Guid.Empty, 0,
                        "This till isn't connected to Plutus, so the sale can't be recorded. Enrol it first.");

                // ⚠ The LEGACY business id — item ids derive from it, and the wrong one silently
                // attaches every line to a different item than the web till would.
                if (await TillPlacement.BusinessIdAsync(ct).ConfigureAwait(false) is not Guid businessId)
                    return new CommitOutcome(false, Guid.Empty, 0,
                        "This till doesn't know which business it belongs to yet. Check the connection on the Plutus tab, then try again.");

                var saleId = Uuid7.New();

                // ⚠ The BUSINESS DAY is the till's WALL CLOCK date, not UTC — a sale at 00:30 local
                // belongs to the day the shop calls it, and every X/Z and VAT period is grouped by
                // that. `till-design` C2 records this as deliberate.
                var businessDay = DateOnly.FromDateTime(DateTime.Now);

                var request = SaleAssembler.Assemble(
                    saleId, deviceId, deviceSeq: 0, businessId, lines, tenders,
                    businessDay, DateTime.UtcNow, operatorUserId);

                // ⚠ DeviceSeq is allocated INSIDE the store's transaction and written into the
                // payload there — the 0 above is a placeholder, never what gets sent.
                var committed = await TillStoreAccess.UseAsync(
                    s => s.CommitSaleAsync(request, ct), ct).ConfigureAwait(false);

                return new CommitOutcome(true, committed.SaleId, committed.DeviceSeq, "Sale recorded.");
            }
            catch (Exception ex)
            {
                // ⚠ The basket survives. A checkout that clears the screen after a failed commit
                // loses the sale and the evidence at the same time.
                Analytics.CrashLog.Write("CheckoutCommit.CommitAsync", ex);
                return new CommitOutcome(false, Guid.Empty, 0,
                    "The sale couldn't be recorded on this till. Nothing has been taken — try again, and see the Plutus tab's log if it keeps failing.");
            }
        }

        /// <summary>
        /// The tenders for this sale, from the legacy payment rows.
        ///
        /// ⚠ Mapped through `SharedKernel.Tenders.FromMethodName`, which is the SAME rule the web
        /// till uses — including checking "gift" BEFORE "credit", because a gift card that lands in
        /// the store-credit bucket reconciles against the wrong liability.
        /// </summary>
        public static IReadOnlyList<IngestTender> TendersFrom(
            IEnumerable<(string? MethodName, decimal Amount, decimal Change)> payments)
        {
            var tenders = (payments ?? Enumerable.Empty<(string?, decimal, decimal)>())
                .Select(p => new IngestTender
                {
                    TenderType = Tenders.FromMethodName(p.MethodName),
                    AmountPence = Pence.FromDecimal(p.Amount),
                    ChangePence = Pence.FromDecimal(p.Change),
                })
                .ToList();

            // ⚠ A sale must have at least one tender or the assembler refuses it. A basket that
            // reached checkout with no payment row is a bug upstream, and defaulting to "cash" here
            // would record money as taken that nobody counted.
            return tenders;
        }
    }
}
