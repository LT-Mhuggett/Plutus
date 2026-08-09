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
    /// <param name="Request">⚠ The payload that was ACTUALLY committed, so the receipt prints from
    /// it rather than re-summing the basket. Two independent totals for one sale means the paper in
    /// the customer's hand and the platform's record can differ by a penny, with no way to tell
    /// which they were charged. Null when nothing was committed.</param>
    public sealed record CommitOutcome(
        bool Committed, Guid SaleId, long DeviceSeq, string Message, IngestSaleRequest Request = null);

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
            var records = (basket ?? Enumerable.Empty<IBasketRecord>()).ToList();
            var lines = new List<BasketLine>();

            // Kept alongside the lines so an alteration can be matched back to the items it was
            // applied to — `BasketAlteration.ItemsAssocitated` holds the BasketItem instances.
            var sources = new List<BasketItem>();

            foreach (var record in records)
            {
                if (record is not BasketItem item) continue;

                var isReturn = record is BasketReturnItem;

                sources.Add(item);
                lines.Add(new BasketLine(
                    // Left empty on purpose — SaleAssembler DERIVES it from businessId + IdOne and
                    // would refuse a mismatched one. One place knows that rule.
                    ItemId: Guid.Empty,
                    IdOne: item.Item?.Id ?? string.Empty,
                    Name: item.Item?.Name ?? string.Empty,
                    UnitIncPence: Pence.FromDecimal(item.Price),
                    UnitExPence: Pence.FromDecimal(item.PriceExTax),
                    Quantity: item.Quantity,
                    // Filled in below from the basket's alterations.
                    DiscountPence: 0,
                    VatBandKey: null,
                    OverriddenFromPence: null,
                    IsReturn: isReturn,
                    OriginSaleId: isReturn ? OriginOf(record) : null));
            }

            ApplyAlterations(records, sources, lines);

            return lines;
        }

        /// <summary>
        /// Fold each <see cref="BasketAlteration"/>'s money into the lines it was applied to.
        ///
        /// ⚠ THIS IS THE STEP 11 DEFECT. The original code skipped alterations with a comment
        /// claiming "an alteration's money is already reflected in the adjusted Price above". That
        /// was FALSE: `ExecuteAlterTransaction` appends a separate `BasketAlteration` record — a
        /// `BasketNote` carrying a NEGATIVE price — and never touches `BasketItem.Price`. So the
        /// discount was dropped from the payload while the till's own `sale.Total`, which sums
        /// EVERY basket record, still included it. The tenders settled against the discounted
        /// total, `GrossPence` was assembled from the undiscounted lines, and the server's
        /// `Σ tender − Σ change == GrossPence` invariant rejected the sale as `202 Quarantined` —
        /// which `OutboxPusher` treats as terminal and never retries. Every discounted sale would
        /// have been lost, silently, the moment the outbox started draining.
        ///
        /// ⚠ The platform model has nowhere else to put it: no basket-level discount field, and
        /// `GrossPence` must equal Σ line gross. A basket-level "£5 off" therefore has to be
        /// apportioned across the lines before it can be sent at all.
        /// </summary>
        private static void ApplyAlterations(
            IReadOnlyList<IBasketRecord> records, IReadOnlyList<BasketItem> sources, List<BasketLine> lines)
        {
            foreach (var alteration in records.OfType<BasketAlteration>())
            {
                // The alteration's price is negative — it is money coming off. Apportionment works
                // in magnitudes.
                var discountPence = Pence.FromDecimal(Math.Abs(alteration.Price)) * Math.Max(1, alteration.Quantity);
                if (discountPence == 0) continue;

                var targets = TargetsOf(alteration, sources, lines);

                // ⚠ Left alone rather than spread somewhere plausible. A discount with no line to
                // land on — every associated item removed from the basket, or applied only to
                // returns, which `VatLineMath.ForLine` drops by design — is money the payload
                // cannot carry. `Reconciles` below refuses the sale rather than sending a total
                // that disagrees with what the customer was charged.
                if (targets.Count == 0) continue;

                var grosses = targets
                    .Select(i => lines[i].UnitIncPence * lines[i].Quantity - lines[i].DiscountPence)
                    .ToList();

                var shares = DiscountApportionment.Across(discountPence, grosses);

                for (var t = 0; t < targets.Count; t++)
                    lines[targets[t]] = lines[targets[t]] with
                    {
                        DiscountPence = lines[targets[t]].DiscountPence + shares[t],
                    };
            }
        }

        /// <summary>
        /// Which line indices an alteration applies to. Sale lines only — a discount apportioned
        /// onto a return would vanish inside <c>VatLineMath.ForLine</c>, which drops the discount on
        /// a return by design, and the sale would stop reconciling.
        /// </summary>
        private static List<int> TargetsOf(
            BasketAlteration alteration, IReadOnlyList<BasketItem> sources, IReadOnlyList<BasketLine> lines)
        {
            var targets = new List<int>();
            var associated = alteration.ItemsAssocitated?.ToList();

            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].IsReturn) continue;

                // No association at all is a whole-basket discount.
                if (associated is null || associated.Count == 0)
                {
                    targets.Add(i);
                    continue;
                }

                // ⚠ Reference first, IdOne second. The instances match while the basket is live;
                // after a stored transaction is recalled they are fresh objects deserialised from
                // JSON, so identity is gone and the barcode is all that is left. Two lines of the
                // same item then both attract a share — the TOTAL stays exact, which is what the
                // reconcile invariant tests, and the split between two identical items is not a
                // difference anybody can observe on a receipt.
                if (associated.Any(a => ReferenceEquals(a, sources[i])) ||
                    associated.Any(a => !string.IsNullOrEmpty(a?.Item?.Id) && a.Item.Id == sources[i].Item?.Id))
                    targets.Add(i);
            }

            return targets;
        }

        /// <summary>
        /// The card-surcharge line for this basket, priced by the SHARED rules — or null when no
        /// fee applies (no surcharge configured, nothing being sold, or a refund-only basket).
        ///
        /// ⚠ A REAL BasketItem AGAINST THE PROVISIONED `CARD-SURCHARGE` ROW, replacing the legacy
        /// `BasketNote` the reconciliation guard refuses. Its VAT FOLLOWS THE BASKET — the fee is
        /// further consideration for the main supply (Bookit C-607/14 / NEC C-130/15), so a fee on
        /// zero-rated goods carries no VAT, on standard-rated goods 20%, and on a mixed basket the
        /// blend. `CardSurchargeVat` owns both halves; nothing here invents arithmetic.
        ///
        /// ⚠ Computed on the sale lines AFTER discounts, EXCLUDING returns — the goods actually
        /// being paid for. A refund attracts no fee.
        /// </summary>
        public static BasketItem SurchargeItem(
            IEnumerable<IBasketRecord> basket, int surchargeBp, long surchargeFlatPence)
        {
            var saleLines = LinesFrom(basket).Where(l => !l.IsReturn).ToList();
            if (saleLines.Count == 0) return null;

            var totals = SaleAssembler.Total(saleLines);
            var fee = CardSurchargeVat.FeePence(surchargeBp, surchargeFlatPence, totals.GrossPence);
            if (fee == 0) return null;

            var (inc, ex) = CardSurchargeVat.PairFor(fee, totals.GrossPence, totals.ExPence);

            // Pence ÷ 100 into the legacy decimal model is lossless; LinesFrom multiplies back.
            return new BasketItem(new Database.Models.ItemModel
            {
                Id = CardSurchargeVat.ItemIdOne,
                Name = "Card surcharge",
                Price = inc / 100m,
                ExPrice = ex / 100m,
                Vat = new Database.Models.TaxModel { Name = "" },
            }, quantity: 1);
        }

        /// <summary>Is the surcharge already in this basket? Applied ONCE per sale — a split
        /// payment across two cards must not charge the flat fee twice.</summary>
        public static bool HasSurcharge(IEnumerable<IBasketRecord> basket) =>
            basket?.Any(r => r is BasketItem b && b.Item?.Id == CardSurchargeVat.ItemIdOne) == true;

        /// <summary>
        /// What the basket is worth, by the SAME sum the till's own `sale.Total` uses — every
        /// record, returns negated, quantity applied.
        ///
        /// ⚠ Converted per record, never as `Pence.FromDecimal(Σ prices)`. Summing decimals first
        /// and rounding once gives a different answer from rounding each line, and this figure has
        /// to match one that was built line by line.
        /// </summary>
        public static long BasketMoneyPence(IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .Sum(r => Pence.FromDecimal(r.Price) * r.Quantity * (r is BasketReturnItem ? -1L : 1L));

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

                // ⚠ THE LAST POINT AT WHICH A MIS-TOTALLED SALE IS STILL VISIBLE. The server
                // enforces `Σ tender − Σ change == GrossPence` and answers `202 Quarantined` when
                // it fails — and `OutboxPusher` treats 202 as TERMINAL and never retries. So a
                // basket carrying money the lines cannot represent would queue here, look
                // successful to the operator, and be destroyed hours later with the customer long
                // gone. Refusing now costs one sale; not refusing loses it after it was paid for.
                var basketMoney = BasketMoneyPence(basket);
                if (request.GrossPence != basketMoney)
                    return new CommitOutcome(false, Guid.Empty, 0,
                        $"This basket totals {basketMoney / 100m:C2} but its lines add up to " +
                        $"{request.GrossPence / 100m:C2}, so it can't be recorded correctly. " +
                        "This is usually a card surcharge or a discount that isn't attached to any " +
                        "item — remove it and ring the sale again. Nothing has been taken.");

                // ⚠ DeviceSeq is allocated INSIDE the store's transaction and written into the
                // payload there — the 0 above is a placeholder, never what gets sent.
                var committed = await TillStoreAccess.UseAsync(
                    s => s.CommitSaleAsync(request, ct), ct).ConfigureAwait(false);

                return new CommitOutcome(true, committed.SaleId, committed.DeviceSeq, "Sale recorded.", request);
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
