using System;
using System.Collections.Generic;
using System.Linq;
using Plutus.Frontend.AppClient.Models;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Services.Storage
{
    /// <summary>
    /// Every AUTOMATIC discount, in the shape a MAUI basket can carry it — the member's tier and any
    /// live scheduled rule ("Wednesday Warhammer"), decided by the one shared resolver.
    ///
    /// ⚠⚠ THIS IS <see cref="MemberDiscountBasket"/> GENERALISED, NOT A SECOND ENGINE BESIDE IT. That
    /// class solved the hard half — a MAUI basket holds a discount as a `BasketAlteration` that names
    /// its eligible lines explicitly, because `CheckoutCommit.TargetsOf` excludes returns but NOT
    /// already-discounted or gift-card lines. Everything here reuses that shape; what is new is that
    /// there can now be MORE THAN ONE automatic discount on a basket at once, because two rules can
    /// target different categories and a member's tier may out-bid one of them and not the other.
    ///
    /// ⚠ SO IT BUILDS ONE ALTERATION PER WINNING DISCOUNT, grouping the lines that share it. One
    /// alteration per LINE would work arithmetically and would fill the operator's screen with a row
    /// per item; one alteration for everything cannot express "10% off these three, 5% off that one".
    ///
    /// ⚠⚠ THE RESOLVER IS <see cref="AutoDiscounts.ForLine"/> AND NOTHING HERE DECIDES ANYTHING. Not
    /// which discount wins (decision D2 — the larger, never both), not eligibility, not the
    /// arithmetic. This class turns the shared verdict into basket records, exactly as
    /// `MemberDiscountBasket` did for one source.
    /// </summary>
    public static class AutoDiscountBasket
    {
        /// <summary>
        /// The automatic alterations currently on this basket.
        ///
        /// ⚠ BY THE `Automatic` FLAG, not by discount id. The members' discount has a sentinel id and
        /// could be found that way; a scheduled rule's id is a REAL catalogue id, indistinguishable
        /// from the same discount applied by hand off the Alterations list. Matching on id would make
        /// the resolver overwrite an operator's own choice.
        /// </summary>
        public static IReadOnlyList<BasketAlteration> ExistingOn(IEnumerable<IBasketRecord> basket) =>
            (basket ?? Enumerable.Empty<IBasketRecord>())
                .OfType<BasketAlteration>()
                .Where(a => a.Automatic)
                .ToList();

        /// <summary>
        /// Which lines an automatic discount may touch.
        ///
        /// ⚠ FOUR EXCLUSIONS, and the reasoning is <see cref="MemberDiscountBasket.EligibleLines"/>'s
        /// verbatim — a return, an already-discounted line (NO STACKING), a gift card, and the card
        /// surcharge. The shared <see cref="MemberDiscount.LineIsEligible"/> owns the first three; the
        /// fourth is MAUI's alone because only this till puts the fee in the basket as a real line.
        /// </summary>
        /// <param name="excluding">The automatic alterations being REPLACED. ⚠ Without this every
        /// line looks "already discounted" on the second pass and the discount silently collapses to
        /// nothing — the re-entrancy trap, and the reason `MemberDiscountBasket` grew the same
        /// parameter.</param>
        public static IReadOnlyList<BasketItem> EligibleLines(
            IEnumerable<IBasketRecord> basket,
            IReadOnlyCollection<BasketAlteration> excluding = null,
            IReadOnlyCollection<BasketItem> waived = null)
        {
            var records = (basket ?? Enumerable.Empty<IBasketRecord>()).ToList();
            var ignore = excluding ?? Array.Empty<BasketAlteration>();

            bool IsIgnored(BasketAlteration a) => ignore.Any(x => ReferenceEquals(x, a));

            var discounted = records
                .OfType<BasketAlteration>()
                .Where(a => !IsIgnored(a))
                .SelectMany(a => a.ItemsAssocitated ?? Enumerable.Empty<BasketItem>())
                .ToList();

            // ⚠ A whole-basket alteration (no association) discounts EVERYTHING, so nothing is
            // eligible. Answering "all of them" here would stack an automatic rate on top of an
            // operator's "£5 off the basket".
            var everythingIsDiscounted = records
                .OfType<BasketAlteration>()
                .Any(a => !IsIgnored(a) && (a.ItemsAssocitated is null || !a.ItemsAssocitated.Any()));

            if (everythingIsDiscounted) return Array.Empty<BasketItem>();

            return records
                .OfType<BasketItem>()
                .Where(i => !i.IsReturn)
                .Where(i => !GiftCards.IsActivation(i.Item?.Id))
                .Where(i => !string.Equals(i.Item?.Id, CardSurchargeVat.ItemIdOne, StringComparison.OrdinalIgnoreCase))
                // ⚠ Reference identity, matching `CheckoutCommit.TargetsOf`'s first test. Two lines of
                // the same item are distinct basket lines and only the discounted one is out.
                .Where(i => !discounted.Any(d => ReferenceEquals(d, i)))
                // ⚠ A line whose automatic discount the operator TOOK OFF stays off. Without this the
                // promise "you can always charge full price" lasts until the next scan, because the
                // rebuild would put it straight back.
                .Where(i => waived is null || !waived.Any(w => ReferenceEquals(w, i)))
                .ToList();
        }

        /// <summary>
        /// Build every automatic alteration this basket should carry — an empty list when none.
        ///
        /// ⚠ THE ARITHMETIC IS THE SHARED RULE'S, per line, then summed per group — never a rate
        /// applied to a basket total. `LineDiscounts` rounds on the whole LINE, so one multiplication
        /// over a group would disagree with the web till by a penny on mixed baskets, which is exactly
        /// the drift C2 exists to stop.
        /// </summary>
        /// <param name="nowLocal">⚠ THE TILL'S OWN CLOCK, at the moment of the sale. This is what makes
        /// a Wednesday rule work on a till that has been offline since Monday.</param>
        public static IReadOnlyList<BasketAlteration> Build(
            IEnumerable<IBasketRecord> basket,
            MemberStanding member,
            IReadOnlyList<ScheduledDiscount> rules,
            DateTime nowLocal,
            Guid operatorUserId,
            IReadOnlyCollection<BasketAlteration> replacing = null,
            IReadOnlyCollection<BasketItem> waived = null)
        {
            var eligible = EligibleLines(basket, replacing, waived);
            if (eligible.Count == 0) return Array.Empty<BasketAlteration>();

            // What each eligible line should take, and the group it belongs to.
            var byDiscount = new Dictionary<(int Id, int Type, decimal Fraction, long Pence, string Name), List<BasketItem>>();

            foreach (var line in eligible)
            {
                var got = AutoDiscounts.ForLine(
                    new AutoDiscountLine(
                        UnitIncPence: line.PricePence,
                        Quantity: line.Quantity,
                        CategoryId: CategoryOf(line),
                        ItemIdOne: line.Item?.Id,
                        IsReturn: false,
                        // ⚠ FALSE, and that is not a shortcut: `EligibleLines` has ALREADY removed
                        // every line an operator discounted. Passing "does this line have any
                        // discount" would include the resolver's own previous answer and collapse the
                        // discount to nothing on the second pass.
                        HasManualDiscount: false,
                        IsGiftCard: false,
                        IsCardSurcharge: false),
                    member,
                    rules,
                    nowLocal);

                if (got is null) continue;

                var key = (got.DiscountId, got.Type, got.PercentFraction, got.FixedAmountPence, got.Name);
                if (!byDiscount.TryGetValue(key, out var group))
                    byDiscount[key] = group = new List<BasketItem>();
                group.Add(line);
            }

            var built = new List<BasketAlteration>();

            // ⚠ Ordered so two tills — and two runs on one till — produce the same records in the same
            // order. The basket is a visible list and a set that reshuffles itself between scans looks
            // broken even when every figure is right.
            foreach (var entry in byDiscount.OrderBy(e => e.Key.Id).ThenBy(e => e.Key.Name, StringComparer.Ordinal))
            {
                var alteration = BuildOne(entry.Key.Id, entry.Key.Type, entry.Key.Fraction, entry.Key.Pence,
                    entry.Key.Name, entry.Value, member, rules, nowLocal, operatorUserId);

                if (alteration != null) built.Add(alteration);
            }

            return built;
        }

        /// <summary>One alteration for one discount and the lines that take it.</summary>
        private static BasketAlteration BuildOne(
            int discountId, int type, decimal percentFraction, long fixedAmountPence, string name,
            List<BasketItem> lines,
            MemberStanding member,
            IReadOnlyList<ScheduledDiscount> rules,
            DateTime nowLocal,
            Guid operatorUserId)
        {
            long incPence = 0, exPence = 0;

            foreach (var line in lines)
            {
                // ⚠ RE-ASKED THROUGH THE RESOLVER, not taken from the grouping key, even though the
                // answer is already known. It re-applies every gate, so a mistake in the grouping above
                // cannot charge the wrong money — defence in depth on a money rule, the same shape
                // `MemberDiscountBasket` uses and the same reason `LineDiscounts` re-guards returns.
                var got = AutoDiscounts.ForLine(
                    new AutoDiscountLine(
                        line.PricePence, line.Quantity, CategoryOf(line), line.Item?.Id,
                        false, false, false, false),
                    member, rules, nowLocal);

                if (got is null || got.DiscountId != discountId) continue;

                incPence += got.Pence;

                // ⚠ The ex half comes from the SAME figures applied to the ex price, mirroring how the
                // manual percentage path builds its pair. It is a DISPLAY figure —
                // `VatLineMath.ForLine` re-splits the real money per line at commit — but a pair that
                // disagrees with itself would show an operator a VAT total the receipt contradicts.
                exPence += type == DiscountKinds.FixedAmount
                    ? LineDiscounts.FixedPerUnit(fixedAmountPence, line.Quantity, isReturn: false)
                    : LineDiscounts.Percentage(line.PriceExTaxPence, line.Quantity, percentFraction, isReturn: false);
            }

            if (incPence <= 0) return null;

            // ⚠ NEGATIVE, like every alteration — it is money coming off. `CheckoutCommit` works in
            // magnitudes and `BasketMoneyPence` sums signed prices, so the sign is load-bearing.
            var alteration = new BasketAlteration(
                name,
                new Models.TillDiscount { Id = discountId },
                lines,
                -(incPence / 100m),
                -(exPence / 100m))
            {
                // ⚠ What tells the next rebuild that this is OURS to replace, and not an operator's to
                // leave alone. See the property's own note.
                Automatic = true,
            };

            // ⚠ Binding default 22(c) — "all discounts need to be tracked". An automatic discount still
            // gets a record: it is the one most likely to be queried later ("why is this basket 10%
            // under?"). ⚠ No authoriser, because nobody authorised it — the TIER or the RULE did, and
            // both are portal-configured. Naming the operator would manufacture a self-approval, which
            // `DiscountAudit` refuses outright.
            var authority = DiscountAudit.Automatic(name, incPence, operatorUserId);
            alteration.DiscountReason = authority.Reason;
            alteration.RequestedByUserId = authority.RequestedByUserId;
            alteration.AuthorisedByUserId = null;
            alteration.AuthorisedByName = null;

            return alteration;
        }

        /// <summary>
        /// The line's V2 CATALOGUE category, for rule targeting.
        ///
        /// ⚠⚠ FROM THE BASKET LINE, NOT FROM `Item.Cat`. The legacy `ItemModel` has an int `CatId`
        /// pointing at the NatApp `Categories` table, which is empty on a portal till — reading it
        /// would answer "no category" for every item in the shop and a category rule would silently
        /// never fire. See <see cref="BasketItem.CategoryId"/>.
        ///
        /// ⚠ Answered OFFLINE, at the scanner, with no round trip — the whole reason the category id
        /// is carried on the line at all. ⚠ Null is safe: a rule targeting a category does not match,
        /// so the line is charged the shelf price rather than guessed at.
        /// </summary>
        private static Guid? CategoryOf(BasketItem line) =>
            line?.CategoryId is Guid c && c != Guid.Empty ? c : null;
    }
}
