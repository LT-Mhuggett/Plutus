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
        /// ⚠⚠ NOTHING CONVERTS HERE ANY MORE (step 11b, 2026-08-16). This used to read
        /// `Pence.FromDecimal(item.Price)` under a comment arguing the conversion was lossless
        /// "because the prices ORIGINATED as pence and were divided by 100 to satisfy the legacy
        /// model" — true, but an argument that had to be re-made every time somebody touched the
        /// path, and one that would have stopped being true the day a price was typed by a human.
        ///
        /// The basket now holds integer pence as its source of truth and `Price` is a pounds-shaped
        /// VIEW for the bindings. There is no conversion left to be right or wrong about.
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

                var isReturn = record.IsReturn;

                sources.Add(item);
                lines.Add(new BasketLine(
                    // Left empty on purpose — SaleAssembler DERIVES it from businessId + IdOne and
                    // would refuse a mismatched one. One place knows that rule.
                    ItemId: Guid.Empty,
                    IdOne: item.Item?.Id ?? string.Empty,
                    Name: item.Item?.Name ?? string.Empty,
                    // ⚠ STRAIGHT FROM THE BASKET'S PENCE (step 11b). This used to be
                    // `Pence.FromDecimal(item.Price)`, lossless only because the prices had
                    // ORIGINATED as pence and been divided by 100 — an argument that had to be
                    // re-made every time somebody touched this path. There is nothing to argue
                    // about now, because nothing converts.
                    UnitIncPence: item.PricePence,
                    UnitExPence: item.PriceExTaxPence,
                    Quantity: item.Quantity,
                    // Filled in below from the basket's alterations.
                    DiscountPence: 0,
                    VatBandKey: null,
                    OverriddenFromPence: null,
                    IsReturn: isReturn,
                    OriginSaleId: isReturn ? OriginOf(record) : null,
                    // ⚠ Multi-barcode: the code actually scanned, when it was not the item's own.
                    // `SaleAssembler` omits it when it matches `IdOne`, so an ordinary line's
                    // metadata is unchanged.
                    ScannedBarcode: item.ScannedBarcode));
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
                var discountPence = Math.Abs(alteration.PricePence) * Math.Max(1, alteration.Quantity);
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

                        // ⚠ Binding default 22(c). The attribution follows the MONEY, share by
                        // share, using the SAME `shares[t]` the line's own DiscountPence just took —
                        // never a second apportionment. A basket-wide "£5 off, damaged box" lands on
                        // three lines as three authorities of 167p/167p/166p, and they sum back to
                        // the £5 an auditor is asking about.
                        //
                        // ⚠ APPENDED, never replaced. A line can carry a member's tier discount AND
                        // a manual one; overwriting here would leave one of them attributed and the
                        // other anonymous, and `DiscountPence` — being their sum — could not say
                        // which was which.
                        DiscountAuthorities = Appended(
                            lines[targets[t]].DiscountAuthorities,
                            AuthorityOf(alteration, shares[t])),
                    };
            }
        }

        /// <summary>
        /// This alteration's audit record for ONE line, carrying that line's share of the money.
        ///
        /// ⚠ RETURNS NULL WHEN THE ALTERATION HAS NO REASON, and that is not the same as refusing.
        /// The refusal happens where the discount is APPLIED (`TillViewModel`), while the operator
        /// can still act on it. By the time a basket reaches checkout the money is already on it —
        /// dropping the sale here would lose a paid-for basket over a missing string. What this does
        /// instead is send NOTHING rather than an authority with a blank reason, so an empty record
        /// never masquerades as a complete one.
        ///
        /// ⚠ It is reachable: a basket parked before 2026-08-14 and recalled afterwards has
        /// alterations with no reason on them, and that is exactly the case where inventing one
        /// would be worst.
        /// </summary>
        private static DiscountAuthority? AuthorityOf(BasketAlteration alteration, long sharePence)
        {
            var reason = DiscountAudit.NormaliseReason(alteration.DiscountReason);
            if (reason is null) return null;

            return new DiscountAuthority(
                reason,
                sharePence,
                alteration.RequestedByUserId,
                alteration.AuthorisedByUserId,
                alteration.AuthorisedByName);
        }

        /// <summary>Add one authority to a line's list, leaving what is already there alone.</summary>
        private static IReadOnlyList<DiscountAuthority> Appended(
            IReadOnlyList<DiscountAuthority> existing, DiscountAuthority? addition)
        {
            if (addition is not DiscountAuthority a) return existing;

            var list = existing is null ? new List<DiscountAuthority>() : new List<DiscountAuthority>(existing);
            list.Add(a);
            return list;
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

        /// <summary>
        /// Why goods are coming back, for the sale's note.
        ///
        /// ⚠ THE OPERATOR TYPES THIS AND IT WAS BEING THROWN AWAY. `ExecuteReturn` demands a reason
        /// before it will proceed, stores it on the `BasketReturnItem` — and nothing ever sent it.
        /// The platform recorded refunds with no explanation at all, which is the one field anybody
        /// asks about later: `SaleAdjustment.Reason` is the audit answer to "why did this money go
        /// back out of the drawer".
        ///
        /// Distinct reasons are joined; identical ones (the usual case — one reason, several lines)
        /// collapse to a single sentence rather than repeating.
        /// </summary>
        internal static string ReturnReasonOf(IEnumerable<IBasketRecord> basket)
        {
            var reasons = (basket ?? Enumerable.Empty<IBasketRecord>())
                .OfType<BasketItem>()
                .Where(r => r.IsReturn)
                .Select(r => r.Reason)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return reasons.Count == 0 ? null : string.Join("; ", reasons);
        }

        /// <summary>
        /// The basket line that SELLS a gift card, priced by the tenant's voucher treatment (WP13).
        ///
        /// ⚠⚠ THE PRICE PAIR IS THE VAT DECLARATION. Under `Multi` ex == inc, so
        /// `VatLineMath.ForLine` derives 0bp and the sale of the card declares no VAT — it is stored
        /// value, not a supply, and the VAT falls due when the card is SPENT. Under `Single` the VAT
        /// is inside the face value. `GiftCardVat` owns that decision; nothing here invents it.
        ///
        /// ⚠ A REAL LINE AGAINST THE PROVISIONED `GIFT-CARD` ROW, exactly like the card surcharge —
        /// a money-carrying `BasketNote` is refused by the commit's reconciliation guard, and the
        /// server's stock projection needs an `itemIdOne` it recognises.
        ///
        /// ⚠ QUANTITY IS ALWAYS 1. Two cards are two lines with two codes; one line of quantity two
        /// would activate one code twice, which the server refuses on the second attempt — after the
        /// customer has been charged for both.
        /// </summary>
        /// <param name="standardRateBp">The PUBLISHED standard rate, for the `Single` treatment.</param>
        public static BasketItem GiftCardItem(
            string code, long loadedPence, VoucherTreatment treatment, int standardRateBp)
        {
            var (inc, ex) = GiftCardVat.PairFor(loadedPence, treatment, standardRateBp);

            // Pence ÷ 100 into the legacy decimal model is lossless; LinesFrom multiplies back.
            return new BasketItem(new Database.Models.ItemModel
            {
                Id = GiftCards.ItemIdOne,
                Name = "Gift card",
                Price = inc / 100m,
                ExPrice = ex / 100m,
                Vat = new Database.Models.TaxModel { Name = "" },
            }, quantity: 1)
            {
                GiftCardCode = code,
            };
        }

        /// <summary>
        /// The gift cards this basket is SELLING — code and the amount to load, in basket order.
        ///
        /// ⚠ Each must be activated on the server BEFORE the sale is recorded, and a failure must
        /// abort: a card that cannot be loaded (one already active, say) has to stop the sale before
        /// the customer is charged for it.
        /// </summary>
        internal static IReadOnlyList<(string Code, long AmountPence)> GiftCardsSoldIn(
            IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .OfType<BasketItem>()
                .Where(i => !string.IsNullOrWhiteSpace(i.GiftCardCode))
                .Select(i => (i.GiftCardCode, i.PricePence * Math.Max(1, i.Quantity)))
                .ToList();

        /// <summary>
        /// The notes that go on the receipt, in basket order — an operator's note, and the label of
        /// every discount applied.
        ///
        /// ⚠ EXTRACTED FROM `ExecuteCheckoutTransaction` (step 11b, 2026-08-14), where it sat inside
        /// a ~200-line `async void` and could not be exercised without a UI host. That method is the
        /// only cluster of money-adjacent logic in this app with no coverage at all, and every
        /// checkout defect so far has been found by hand.
        ///
        /// ⚠ `BasketAlteration` DERIVES FROM `BasketNote`, so one type test covers both. The original
        /// asked `bR is BasketNote || bR is BasketAlteration`, which reads as though alterations were
        /// a separate case and would send a maintainer looking for a difference that does not exist.
        ///
        /// ⚠ A basket ITEM is not a note and must never appear here — `OfType` is doing real work,
        /// not tidying: a receipt listing every line twice is a receipt nobody trusts.
        ///
        /// ⚠ Blank notes are dropped. An empty line on a printed receipt looks like a printer fault.
        /// </summary>
        internal static IReadOnlyList<string> ReceiptNotesFrom(IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .OfType<BasketNote>()
                .Select(n => n.Note?.Note)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();

        /// <summary>Is the surcharge already in this basket? Applied ONCE per sale — a split
        /// payment across two cards must not charge the flat fee twice.</summary>
        public static bool HasSurcharge(IEnumerable<IBasketRecord> basket) =>
            basket?.Any(r => r is BasketItem b && b.Item?.Id == CardSurchargeVat.ItemIdOne) == true;

        /// <summary>
        /// What the basket is worth — every record, returns negated, quantity applied.
        ///
        /// ⚠ Converted per record, never as `Pence.FromDecimal(Σ prices)`. Summing decimals first
        /// and rounding once gives a different answer from rounding each line, and this figure has
        /// to match one that was built line by line.
        ///
        /// ⚠⚠ **THE ONLY PLACE THIS SUM LIVES, SINCE 2026-08-21 (step 11b).** Its own header used to
        /// read *"by the SAME sum the till's own `sale.Total` uses"* — describing, rather than being,
        /// the one derivation. `TillViewModel` had **four** copies of it in `decimal` pounds
        /// (`SaleIncTax`, `SaleExTax`, and `sale.Total`/`TotalExTax` at two sites), and one of them
        /// finished `Pence.FromDecimal(sale.Total)` — **precisely the round-the-sum mistake the
        /// paragraph above forbids**, feeding the number the checkout screen tenders against while
        /// this method guarded the commit. They agree today only because `Price` is an exact
        /// projection of `PricePence`; nothing was holding that, and the reconciliation guard's
        /// refusal message would have been the operator's only clue.
        /// </summary>
        public static long BasketMoneyPence(IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .Sum(r => r.PricePence * r.Quantity * (r.IsReturn ? -1L : 1L));

        /// <summary>
        /// The same sum, ex-VAT — what the till shows beside the gross.
        ///
        /// ⚠ NOT `BasketMoneyPence` minus a VAT calculation. Every record carries its own
        /// ex-VAT figure, and re-deriving VAT here would be a fourth opinion about the split
        /// (rule 3: gross − ex, never rate arithmetic — `till-design.md` C1).
        /// </summary>
        public static long BasketMoneyExPence(IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .Sum(r => r.PriceExTaxPence * r.Quantity * (r.IsReturn ? -1L : 1L));

        /// <summary>
        /// Does this basket only send goods BACK?
        ///
        /// ⚠⚠ IT DECIDES REAL MONEY BEHAVIOUR, and it had no test until 2026-08-21. A refund-only
        /// basket may be tendered **only to the methods the original sale used** (Matt, 2026-08-11:
        /// *"if it was a card payment, needs to go back to card"* — refunding a card sale in cash is
        /// the oldest till fraud there is), it takes the finding-Y per-tender refund caps, and it
        /// attracts **no card surcharge**. Getting it wrong in either direction is a money fault:
        /// false → a cash payout on a card sale; true → a customer refused the tender they want.
        ///
        /// ⚠ A SALE LINE, NOT A RECORD. `BasketNote`, `BasketAlteration` and the card-surcharge line
        /// are not goods, so a basket holding one return and a discount is still refund-only.
        ///
        /// ⚠⚠ AN EMPTY BASKET ANSWERS **TRUE**, and that is the behaviour as it shipped — this method
        /// was lifted out of `ExecuteCheckoutTransaction` verbatim on 2026-08-21 and the predicate is
        /// unchanged, deliberately. Tightening it to *"has at least one return"* while extracting it
        /// would have been a silent money-path change smuggled in behind a refactor, and the empty
        /// case is unreachable anyway: an empty basket offers the origin-tender list, tenders
        /// nothing, and `SaleAssembler` refuses a sale with no tender. **Recorded rather than
        /// tidied** — if it should read false, that is a decision with its own test, not a
        /// side effect of moving code. Pinned by `An_empty_basket_answers_true_because_that_is_what_shipped`.
        /// </summary>
        public static bool IsRefundOnly(IEnumerable<IBasketRecord> basket) =>
            !(basket ?? Enumerable.Empty<IBasketRecord>())
                .Any(r => r is BasketItem && !r.IsReturn);

        private static Guid? OriginOf(IBasketRecord record) =>
            record is BasketItem r && r.IsReturn && Guid.TryParse(r.ReturnSaleId, out var id) ? id : null;

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
                // ⚠ THE SHARED RULE. The drawer reconciles against these takings, so a cash event
                // and a sale must never disagree about what "today" is — see SharedKernel.BusinessDay.
                var businessDay = SharedKernel.BusinessDay.Today();

                var request = SaleAssembler.Assemble(
                    saleId, deviceId, deviceSeq: 0, businessId, lines, tenders,
                    businessDay, DateTime.UtcNow, operatorUserId, note: ReturnReasonOf(basket));

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

                // ⚠⚠ THE DAY MUST STILL BE OPEN. Matt, 2026-08-11: *"I was able to make a sale with
                // the till closed… I was also able to refund it."*
                //
                // He was right, and the consequence is worse than it looked: the sale WAS recorded.
                // The day-closed gate existed only on the CASH-EVENT path — on the till and on the
                // server — and nothing on the sales path asked at all. So a sale rung after a Z-read
                // committed locally, drained normally, and the SERVER ACCEPTED IT as 201 Recorded
                // against a day whose takings had already been counted and banked.
                //
                // ⚠ That is the real damage. A lost sale is one problem; a sale added to a day that
                // has already been reconciled means the Z-read, the banking and the platform's
                // figures for that day disagree for ever, and nothing anywhere flags it. The
                // variance surfaces weeks later as an unexplained discrepancy.
                //
                // ⚠ REFUSED AT THE COMMIT, not at the button — this is the one place every sale and
                // every refund passes through, it already owns the "refuse rather than record
                // something wrong" decision above, and it leaves the basket intact by contract.
                // Gating the screen instead would leave the other callers open.
                //
                // ⚠ The SERVER needs its own half of this rule and does not have it yet — recorded
                // in `Build/archive/handrun-2026-08-11.md`. Until then this is a single gate, which is why
                // it is at the last possible moment rather than the first.
                // ⚠ `BusinessDay.Wire` — the SAME string the cash events are stored under. Comparing
                // a differently-formatted date would find no Z read and let the sale through, which
                // is the quietest possible way for this guard to do nothing at all.
                if (await TillStoreAccess.UseAsync(
                        s => s.IsDayClosedAsync(SharedKernel.BusinessDay.Wire(businessDay), ct), ct)
                    .ConfigureAwait(false))
                {
                    return new CommitOutcome(false, Guid.Empty, 0,
                        $"{businessDay:d MMMM} has been closed with a Z read, so nothing more can be rung " +
                        "up against it. Nothing has been taken — the basket is still here. If the " +
                        "shop is still trading, the day was closed too early: open a new float.");
                }

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
