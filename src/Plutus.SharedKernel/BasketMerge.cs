namespace Plutus.SharedKernel
{
    /// <summary>
    /// When does a newly-added unit join an existing basket line, and when does it start a new one?
    ///
    /// ⚠⚠ **THIS IS A MONEY RULE, AND THE TWO TILLS DISAGREED ABOUT IT UNTIL 2026-08-18.**
    ///
    /// The web till has always refused to merge into a line whose price was changed by hand
    /// (`basket.ts`: `find(l => l.item.idOne === … && !l.adjusted && !l.discount && !l.isReturn)`).
    /// MAUI had **two** merge paths with **two different rules**: its search path compared the price
    /// pair and so behaved correctly by arithmetic accident, but its *selected-line fast path* checked
    /// only the item id and that the line was not a return.
    ///
    /// **So on MAUI: ring a £5 item, adjust it to 50p, leave the line selected, scan the same item
    /// again — and the second unit joined the adjusted line at 50p.** The shop sold the second one for
    /// a tenth of its price, silently, with the receipt as the only evidence. On the web till the same
    /// sequence produces a second line at £5.
    ///
    /// ⚠ THE FAULT WAS TWO PATHS, NOT A MISSING CHECK. Both call sites now ask this one question, so
    /// there is no "fast path" left to be a different rule.
    ///
    /// ⚠ C1/C2: this is the .NET half. Its TypeScript twin is the `find` predicate in
    /// `Plutus.Frontend.WebApp/src/till/basket.ts`, and what pins them together is the shared vector
    /// table — `BasketMergeTests` here and `basketMerge.test.ts` there run the SAME cases. Two tills
    /// that disagree about when a line merges disagree about what the customer is charged.
    /// </summary>
    public static class BasketMerge
    {
        /// <summary>
        /// May the unit being added join this line?
        /// </summary>
        /// <param name="sameItem">Is it the same catalogue item? ⚠ Compared by the caller, because the
        /// two tills identify items differently (`Item.Id` vs `item.idOne`) and neither identity
        /// belongs in a shared rule.</param>
        /// <param name="lineIsReturn">⚠ A return line NEVER takes a sale unit. Goods going back and
        /// goods going out are opposite directions of money on one row.</param>
        /// <param name="lineAdjusted">⚠⚠ THE ONE THIS FIXES. A hand-typed price is a decision about
        /// the unit in front of the operator, not a new price for the item — so the next unit starts
        /// its own line at the catalogue price and the operator adjusts it too if they meant to.</param>
        /// <param name="lineHasDiscount">⚠ Same argument as an adjusted line: a discount was granted
        /// against what was in the basket at the time. ⚠ MAUI carries discounts as their own basket
        /// records rather than as a field on the line, so it passes `false` and says why at the call
        /// site — this parameter exists so the rule is complete and so the web till can pass its own
        /// per-line flag.</param>
        /// <param name="lineIncPence">The line's current inc-VAT unit price.</param>
        /// <param name="lineExPence">The line's current ex-VAT unit price.</param>
        /// <param name="catalogueIncPence">What the catalogue says this item costs, inc VAT.</param>
        /// <param name="catalogueExPence">And ex VAT. ⚠ BOTH halves are compared: a line agreeing on
        /// the inc price while disagreeing on the ex price has a different VAT position, and merging
        /// them would put two VAT answers on one row.</param>
        public static bool CanMerge(
            bool sameItem,
            bool lineIsReturn,
            bool lineAdjusted,
            bool lineHasDiscount,
            long lineIncPence,
            long lineExPence,
            long catalogueIncPence,
            long catalogueExPence)
        {
            if (!sameItem) return false;
            if (lineIsReturn) return false;

            // ⚠ Checked BEFORE the price comparison and not instead of it. Adjusting a price to
            // exactly the catalogue figure would otherwise pass the arithmetic test and re-merge,
            // which is the accident the old search path was relying on.
            if (lineAdjusted) return false;
            if (lineHasDiscount) return false;

            return lineIncPence == catalogueIncPence && lineExPence == catalogueExPence;
        }
    }
}
