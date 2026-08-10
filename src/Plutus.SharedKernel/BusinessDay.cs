using System;

namespace Plutus.SharedKernel;

/// <summary>
/// Which trading day a till is on.
///
/// ⚠ WHY THIS IS A RULE AND NOT AN EXPRESSION. The drawer reconciles against the day's TAKINGS:
/// expected cash = opening float + cash tenders for this till and business day + paid-ins −
/// paid-outs. If a cash event and a sale disagree by one day about what "today" is, the Z-read
/// balances against the wrong takings and the variance is pure fiction — with no clue in the
/// numbers that the dates were the problem. It has to be computed identically by both, so it is
/// computed in one place.
///
/// ⚠ LOCAL WALL-CLOCK, NOT UTC, and that is deliberate. A till in London trading until 01:00 on a
/// summer evening is still on the previous day's takings; UTC would file that hour under tomorrow
/// and split one shift's banking across two days. The web till says the same thing in one line —
/// `pipeline.ts businessDay()` returns the local Y-M-D — and this is its .NET twin (till-design C2).
///
/// ⚠ NO ROLLOVER HOUR YET. A shop trading past midnight will eventually want "the day starts at
/// 04:00", and that belongs HERE when it comes rather than in each caller. Today both tills agree
/// on midnight, which is the property that matters.
/// </summary>
public static class BusinessDay
{
    /// <summary>The trading day for a moment in LOCAL time.</summary>
    public static DateOnly For(DateTime localNow) => DateOnly.FromDateTime(localNow);

    /// <summary>The trading day right now.</summary>
    public static DateOnly Today() => For(DateTime.Now);

    /// <summary>
    /// The wire form: ISO `yyyy-MM-dd`.
    ///
    /// ⚠ INVARIANT CULTURE, always. A locale-shaped date is parsed differently at the other end —
    /// `10/08/2026` is two different days depending on who reads it — and the failure lands on a
    /// banking report, months later, as a day that will not reconcile.
    /// </summary>
    public static string Wire(DateOnly day) =>
        day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
