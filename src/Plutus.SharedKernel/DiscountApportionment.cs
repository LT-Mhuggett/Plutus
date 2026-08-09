using System;
using System.Collections.Generic;

namespace Plutus.SharedKernel;

/// <summary>
/// Splitting one discount across the several lines it applies to.
///
/// ⚠ THIS EXISTS BECAUSE A BASKET-LEVEL DISCOUNT HAS NOWHERE ELSE TO GO. The platform sale model
/// has no basket-level discount field and no fee field: <c>GrossPence</c> must equal the sum of the
/// line grosses, and a line's gross is <c>UnitPrice × Qty − Discount</c>. So "£5 off these three
/// items" has to become three per-line discounts before it can be sent at all.
///
/// ⚠ THE PARTS MUST SUM TO THE WHOLE, EXACTLY. Three lines sharing £10 by proportion give
/// £3.33 + £3.33 + £3.33 = £9.99, and the missing penny fails the server's reconcile invariant —
/// <c>Σ tender − Σ change == GrossPence</c> — which rejects the whole sale as
/// <c>202 Quarantined</c>. Largest-remainder hands the odd pennies out rather than dropping them.
///
/// ⚠ IT IS DETERMINISTIC. Ties break on the earlier line, so the same basket apportioned on two
/// tills produces the same pennies on the same lines. A tie broken by dictionary order or by
/// whichever line was enumerated first would put the penny on a different line on each till, and
/// the two receipts for one basket would differ.
/// </summary>
public static class DiscountApportionment
{
    /// <summary>
    /// Split <paramref name="discountPence"/> across lines in proportion to
    /// <paramref name="lineGrossPence"/>, so the parts sum to exactly the whole.
    /// </summary>
    /// <param name="discountPence">The total to hand out, as a POSITIVE number of pence.</param>
    /// <param name="lineGrossPence">Each line's gross, inc-VAT, before this discount.</param>
    /// <returns>One discount per line, in the order given. Sums to <paramref name="discountPence"/>.</returns>
    public static long[] Across(long discountPence, IReadOnlyList<long> lineGrossPence)
    {
        if (lineGrossPence is null) throw new ArgumentNullException(nameof(lineGrossPence));

        if (discountPence < 0)
            throw new ArgumentOutOfRangeException(nameof(discountPence),
                "Pass the discount as a positive magnitude. A negative one would ADD money to the "
                + "line and the sale would total more than the customer was charged.");

        var shares = new long[lineGrossPence.Count];
        if (discountPence == 0) return shares;

        long total = 0;
        for (var i = 0; i < lineGrossPence.Count; i++)
        {
            if (lineGrossPence[i] < 0)
                throw new ArgumentOutOfRangeException(nameof(lineGrossPence),
                    "A line's gross cannot be negative here. Returns are apportioned separately — "
                    + "VatLineMath.ForLine drops the discount on a return, so a share landing on one "
                    + "would silently vanish from the sale and the totals would stop reconciling.");
            total += lineGrossPence[i];
        }

        // ⚠ REFUSED, not clamped. A discount bigger than the goods it applies to means the basket
        // and the discount disagree about what is being sold, and silently capping it would send a
        // sale whose total nobody at the counter agreed to.
        if (discountPence > total)
            throw new ArgumentOutOfRangeException(nameof(discountPence),
                $"A discount of {discountPence}p was applied to lines worth {total}p. A discount "
                + "cannot exceed the value of the lines it applies to.");

        // Floor share by proportion, then hand the remaining pennies to the largest fractional
        // parts. Done in integers throughout: `discount * gross` can be large, so this is the one
        // place the arithmetic widens rather than going through decimal and rounding twice.
        var remainders = new (int Index, long Numerator)[lineGrossPence.Count];
        long handedOut = 0;

        for (var i = 0; i < lineGrossPence.Count; i++)
        {
            var numerator = discountPence * lineGrossPence[i];
            shares[i] = numerator / total;
            handedOut += shares[i];
            remainders[i] = (i, numerator % total);
        }

        // ⚠ Ties break on the EARLIER line — `Array.Sort` is unstable, so the index is part of the
        // comparison rather than left to it. Without this the odd penny lands wherever the sort
        // happened to put equal keys, which is not guaranteed to be the same on another machine.
        Array.Sort(remainders, (a, b) =>
            a.Numerator != b.Numerator ? b.Numerator.CompareTo(a.Numerator) : a.Index.CompareTo(b.Index));

        for (var r = 0; handedOut < discountPence; r++)
        {
            var i = remainders[r % remainders.Length].Index;

            // Never take a line below zero — the pennies go to a line that still has room.
            if (shares[i] >= lineGrossPence[i]) continue;

            shares[i]++;
            handedOut++;
        }

        return shares;
    }
}
