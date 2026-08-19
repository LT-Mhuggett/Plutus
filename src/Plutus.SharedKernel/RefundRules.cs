using System;
using System.Collections.Generic;
using System.Linq;

namespace Plutus.SharedKernel;

/// <summary>Where the till got the original sale it is refunding against.</summary>
public enum SaleRecordSource
{
    /// <summary>Not found anywhere.</summary>
    NotFound = 0,

    /// <summary>Fetched from <c>GET /api/v1/sales/{saleId}</c>. ⚠ AUTHORITATIVE — it is the only
    /// record that knows about refunds taken on OTHER tills.</summary>
    Server = 1,

    /// <summary>Found in this till's own rolling window of pushed sales, with no server answer.
    /// The offline fallback.</summary>
    LocalInWindow = 2,

    /// <summary>Known to be older than the rolling window, or held only as an archived/pruned
    /// stub. ⚠ NOT a usable basis for refunding money.</summary>
    LocalOutsideWindow = 3,
}

/// <summary>What the till may do about a requested refund.</summary>
public enum RefundVerdict
{
    /// <summary>Refund <see cref="RefundDecision.AllowedPence"/>.</summary>
    Allowed = 0,

    /// <summary>The sale exists but has nothing left to give back.</summary>
    NothingLeft = 1,

    /// <summary>⚠ REFUSE, and say why. The till cannot establish what is still refundable, so
    /// paying out would be guessing with the shop's money.</summary>
    NeedsConnection = 2,

    /// <summary>The platform has no such sale. ⚠ Distinct from <see cref="NeedsConnection"/>: this
    /// one is answered and negative, and telling an operator to "check the connection" when the
    /// receipt is simply not ours sends them to reboot a router for no reason.</summary>
    UnknownSale = 3,
}

/// <summary>
/// A refund decision.
///
/// ⚠ <see cref="Reason"/> carries NO money in it, deliberately. <c>Money.cs</c> states that
/// formatting is a client concern, and a shared rule that baked "£" into a sentence would be
/// wrong the first time a tenant trades in another currency — and unlocalisable for the MAUI till,
/// which runs its strings through <c>I18N_L10N</c>. The amounts are fields; the caller composes
/// the sentence.
/// </summary>
/// <param name="WasCapped">The request was larger than the remainder and has been reduced to it.
/// ⚠ The caller MUST surface this — silently handing over less than was asked for is how a
/// disputed refund becomes an argument at the counter.</param>
/// <param name="RemainingPence">What was refundable before this refund.</param>
/// <param name="AlreadyRefundedPence">What had already been given back.</param>
public sealed record RefundDecision(
    RefundVerdict Verdict,
    long AllowedPence,
    bool WasCapped,
    string Reason,
    long RemainingPence = 0,
    long AlreadyRefundedPence = 0)
{
    public bool IsAllowed => Verdict == RefundVerdict.Allowed;
}

/// <summary>
/// How much of one tender is still refundable on a sale.
///
/// ⚠ EVERYTHING HERE IS A MAGNITUDE, never a signed wire amount. A refund travels as a NEGATIVE
/// gross with negative tender amounts; the rules below are written in positives so that "took" and
/// "gave back" compare directly. Callers pass <c>Math.Abs</c>. Mixing the two conventions is how the
/// original over-refund guard failed: <c>paid &gt; total</c> cannot express a limit when both numbers
/// are negative.
/// </summary>
/// <param name="TenderType">The <see cref="Tenders"/> byte — cash, card, credit, gift card…</param>
/// <param name="TookPence">What this tender took on the original sale, positive.</param>
/// <param name="RefundedPence">What has already gone back to this tender, positive.</param>
public readonly record struct TenderCapacity(byte TenderType, long TookPence, long RefundedPence)
{
    /// <summary>What may still go back to this tender. Clamped at zero, like the sale-level figure.</summary>
    public long RemainingPence => RefundRules.RemainingPence(TookPence, RefundedPence);
}

/// <summary>
/// Whether a proposed per-tender refund split may proceed.
/// </summary>
/// <param name="OffendingTenderType">The tender that broke the rule; meaningless when allowed.</param>
/// <param name="RequestedPence">What was asked of that tender.</param>
/// <param name="AllowedPence">What that tender could actually give back.</param>
public sealed record TenderSplitDecision(
    bool IsAllowed,
    string Reason,
    byte OffendingTenderType = 0,
    long RequestedPence = 0,
    long AllowedPence = 0);

/// <summary>
/// WP11 — whether a refund may proceed, and for how much.
///
/// ⚠ THIS IS A MONEY PATH, and its whole purpose is that a till never pays out on a guess. The
/// failure this prevents is specific and expensive: a customer returns an item on till A that was
/// bought on till B, till A has no local record, and it either refuses a legitimate refund or —
/// far worse — accepts one that has already been refunded somewhere else. Only the SERVER record
/// knows about refunds taken on other tills.
///
/// ⚠ NEVER A SILENT ACCEPTANCE. Every path that cannot establish the remainder returns a refusal
/// carrying a reason a cashier can act on. "Needs connection" and "no such sale" are deliberately
/// different verdicts — sending someone to reboot a router because the receipt was from another
/// shop wastes their time and the customer's.
///
/// ⚠ In SharedKernel, not in one till, because the arithmetic is the same everywhere and the
/// server will want to enforce the identical remainder when it validates the posted refund. Two
/// implementations disagreeing about "how much is left" is a shop that can be refunded twice.
/// </summary>
public static class RefundRules
{
    /// <summary>
    /// How long a pushed sale stays locally refundable without the server. Matches the retention
    /// window the outbox prunes on (retrofit plan: *"window of recent sales (default 14 days) for
    /// reprint and X/Z"*), because a till must not claim to refund from a record it has deleted.
    /// </summary>
    public static readonly TimeSpan DefaultRollingWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// What is still refundable on a sale.
    ///
    /// ⚠ Clamped at zero rather than allowed to go negative. A sale showing more refunded than was
    /// ever paid is corrupt data, and the safe reading of corrupt data on a money path is "nothing
    /// left", never a negative that some caller subtracts into a payout.
    /// </summary>
    public static long RemainingPence(long originalPence, long alreadyRefundedPence)
    {
        if (originalPence <= 0) return 0;
        var already = Math.Max(0, alreadyRefundedPence);
        var remaining = originalPence - already;
        return remaining > 0 ? remaining : 0;
    }

    /// <summary>
    /// Classify a locally-held sale against the rolling window.
    ///
    /// ⚠ A sale dated in the FUTURE counts as in-window. Till clocks drift and get set wrong, and
    /// treating a slightly-future timestamp as "outside the window" would refuse refunds on sales
    /// rung up minutes earlier on the same machine.
    /// </summary>
    public static SaleRecordSource ClassifyLocal(
        DateTime saleOccurredUtc, DateTime nowUtc, TimeSpan? window = null)
    {
        var w = window ?? DefaultRollingWindow;
        var age = nowUtc - saleOccurredUtc;
        return age <= w ? SaleRecordSource.LocalInWindow : SaleRecordSource.LocalOutsideWindow;
    }

    /// <summary>
    /// Decide a refund.
    /// </summary>
    /// <param name="source">Where the original sale came from. ⚠ The single most important input:
    /// a local record cannot see another till's refunds.</param>
    /// <param name="originalPence">What the customer paid, positive.</param>
    /// <param name="alreadyRefundedPence">Already given back against this sale, positive.</param>
    /// <param name="requestedPence">What is being asked for now, positive.</param>
    public static RefundDecision Authorise(
        SaleRecordSource source, long originalPence, long alreadyRefundedPence, long requestedPence)
    {
        // ⚠ Checked BEFORE anything about the sale, because a zero or negative request is a bug in
        // the caller and must not be dressed up as a money decision.
        if (requestedPence <= 0)
            return new RefundDecision(RefundVerdict.NothingLeft, 0, false,
                "Nothing was asked for — enter the amount to refund.");

        switch (source)
        {
            case SaleRecordSource.NotFound:
                // ⚠ Answered and negative. Not a connection problem.
                return new RefundDecision(RefundVerdict.UnknownSale, 0, false,
                    "That sale isn't on this account. Check the receipt is ours, and that the number is right.");

            case SaleRecordSource.LocalOutsideWindow:
                return new RefundDecision(RefundVerdict.NeedsConnection, 0, false,
                    "This sale is older than this till can verify on its own. Reconnect to refund it — "
                    + "the amount already refunded elsewhere can't be checked offline.");

            case SaleRecordSource.Server:
            case SaleRecordSource.LocalInWindow:
                break;

            default:
                // A source this build does not recognise must not become a payout.
                return new RefundDecision(RefundVerdict.NeedsConnection, 0, false,
                    "This till can't confirm what's refundable on that sale. Reconnect and try again.");
        }

        var already = Math.Max(0, alreadyRefundedPence);
        var remaining = RemainingPence(originalPence, already);
        if (remaining == 0)
            return new RefundDecision(RefundVerdict.NothingLeft, 0, false,
                "That sale has already been refunded in full.", 0, already);

        // The DoD is "caps at the refundable remainder" — cap and SAY SO, rather than refusing.
        // The customer gets what is actually owed, and the operator is told why it differs.
        var capped = requestedPence > remaining;
        var allowed = capped ? remaining : requestedPence;

        return new RefundDecision(RefundVerdict.Allowed, allowed, capped,
            capped
                ? "Part of this sale has already been refunded, so only the remainder can be given back."
                : "Refund approved.",
            remaining, already);
    }

    /// <summary>
    /// What may go back to each tender the original sale used.
    ///
    /// ⚠⚠ WHY THIS EXISTS — finding Y, 2026-08-13. Matt: *"when I try to return an item that was
    /// split, it wants to put the full amount to that card. It needs to be aware of how the payments
    /// were split for it to work."* Until this, the tills knew only the SET of tenders a sale used
    /// (finding G) and capped none of the amounts, and <see cref="Authorise"/> capped only the sale
    /// TOTAL — so £2.00 cash + £2.40 card could be refunded £4.40 to the card. The card ends up
    /// credited £2.40 more than it ever took, the £2 stays in the drawer, the till balances, and no
    /// report anywhere disagrees. Reverse the signs and it is a way to walk cash out of a shop.
    ///
    /// ⚠ Tenders the sale did not use are simply absent from the result, which is the machine-readable
    /// form of "the money goes back the way it came".
    /// </summary>
    /// <param name="originTenders">What each tender took on the original sale, positive.</param>
    /// <param name="alreadyRefundedByTender">What has already gone back per tender, positive. ⚠ From
    /// the SERVER where possible: only it knows about refunds taken on other tills.</param>
    public static IReadOnlyList<TenderCapacity> RefundCapacities(
        IEnumerable<KeyValuePair<byte, long>> originTenders,
        IEnumerable<KeyValuePair<byte, long>>? alreadyRefundedByTender = null)
    {
        if (originTenders is null) return Array.Empty<TenderCapacity>();

        // ⚠ SUMMED, not last-wins. One sale can pay twice with the same method — two cash tenders, or
        // a card taken in two goes — and treating the second as a replacement would understate what
        // that tender took and refuse a legitimate refund.
        var took = new Dictionary<byte, long>();
        foreach (var t in originTenders)
        {
            var magnitude = Math.Abs(t.Value);
            took[t.Key] = took.TryGetValue(t.Key, out var running) ? running + magnitude : magnitude;
        }

        var back = new Dictionary<byte, long>();
        foreach (var r in alreadyRefundedByTender ?? Enumerable.Empty<KeyValuePair<byte, long>>())
        {
            var magnitude = Math.Abs(r.Value);
            back[r.Key] = back.TryGetValue(r.Key, out var running) ? running + magnitude : magnitude;
        }

        return took
            .Where(t => t.Value > 0)
            .Select(t => new TenderCapacity(t.Key, t.Value, back.TryGetValue(t.Key, out var b) ? b : 0))
            .OrderBy(c => c.TenderType)
            .ToList();
    }

    /// <summary>
    /// What ONE tender may take, in the three-way shape a tender loop needs: <see langword="null"/> for
    /// "nothing caps this", <c>0</c> for "this tender may take nothing", or the remaining capacity.
    ///
    /// ⚠⚠ THE THREE-WAY ANSWER IS THE WHOLE POINT, and the reason this is a rule rather than a line of
    /// LINQ at a call site. Null and 0 were the same value in the MAUI till until 2026-08-19: a card an
    /// earlier refund had already used up reports 0 remaining, is still OFFERED by the picker, and
    /// "0 means uncapped" then let the next refund put the money back onto it all over again.
    ///
    /// ⚠ AN EMPTY LIST IS THE UNKNOWN CASE — not a refund at all, or an origin sale this till could not
    /// read — and nothing may be enforced from ignorance. A zero INSIDE a populated list is a real limit
    /// of nothing.
    ///
    /// ⚠ A TENDER ABSENT FROM A POPULATED LIST took nothing on the origin sale, so it may take nothing
    /// back. That is finding G — a card sale must not be refunded out of the cash drawer — enforced here
    /// as well as at the picker, because one lock on that door was how it came to be missing.
    ///
    /// ⚠⚠ C2 TWIN of <c>capacityFor</c> in <c>till/tendering.ts</c>, decision for decision: the same
    /// three cases with the same meanings. Two tills that disagree here disagree about how much money is
    /// allowed to leave — see till-design C2.
    /// </summary>
    /// <param name="capacities">From <see cref="RefundCapacities"/>, or empty when unknown.</param>
    /// <param name="tenderType">The <see cref="Tenders"/> byte being asked about.</param>
    public static long? CapacityFor(IReadOnlyList<TenderCapacity>? capacities, byte tenderType)
    {
        if (capacities is null || capacities.Count == 0) return null;

        // ⚠ NOT `FirstOrDefault` — the struct trap documented in `AuthoriseSplit` below. No match hands
        // back `default`, whose `TenderType` is 0, which is `Tenders.Cash`.
        foreach (var c in capacities)
            if (c.TenderType == tenderType) return c.RemainingPence;

        return 0;
    }

    /// <summary>
    /// May this refund be split across these tenders, in these amounts?
    ///
    /// ⚠ THE SALE-LEVEL CAP FALLS OUT OF THIS FOR FREE: if every tender is within what it took, the
    /// sum is within what the sale took. <see cref="Authorise"/> still runs — it owns the source
    /// question ("can this till even see the remainder?"), which this cannot answer.
    ///
    /// ⚠ NO OVERRIDE, SUPERVISOR INCLUDED, exactly as binding default 12 rules for the total. A
    /// ceiling authorises up to what is owed, never beyond it, and "beyond it" here means paying a
    /// card back money it never took.
    ///
    /// ⚠ THE OPERATIONAL CONSEQUENCE, stated rather than discovered at a counter: a customer who paid
    /// part-cash-part-card CANNOT be refunded entirely in cash, even if the card terminal is down. That
    /// is deliberate — it is the same rule that stops a card sale being refunded from the drawer — but
    /// it is a real shop-floor limit and it belongs in front of an owner, not buried here.
    /// </summary>
    /// <param name="capacities">From <see cref="RefundCapacities"/>.</param>
    /// <param name="requestedByTender">What is being asked of each tender now, positive.</param>
    public static TenderSplitDecision AuthoriseSplit(
        IReadOnlyList<TenderCapacity> capacities,
        IEnumerable<KeyValuePair<byte, long>> requestedByTender)
    {
        var asked = new Dictionary<byte, long>();
        foreach (var r in requestedByTender ?? Enumerable.Empty<KeyValuePair<byte, long>>())
        {
            var magnitude = Math.Abs(r.Value);

            // ⚠ A zero line is a caller bug, not a refund of nothing: it means a screen built a
            // payment row it never filled in. Refusing is how that gets noticed.
            if (magnitude == 0)
                return new TenderSplitDecision(false,
                    "One of the refund amounts is empty. Enter how much goes back to each method.",
                    r.Key);

            asked[r.Key] = asked.TryGetValue(r.Key, out var running) ? running + magnitude : magnitude;
        }

        if (asked.Count == 0)
            return new TenderSplitDecision(false,
                "Nothing was asked for — enter the amount to refund.");

        // ⚠⚠ A DICTIONARY, NOT `FirstOrDefault`, AND THE REASON IS A TRAP WORTH KNOWING.
        // `TenderCapacity` is a STRUCT, so `FirstOrDefault` on no match hands back `default` —
        // `TenderType = 0`, which is `Tenders.Cash`. A "did we find it?" test written against that
        // value therefore reads as "yes, cash" for a sale that never took a penny in cash. The first
        // draft of this method did exactly that and its tests still passed, because a second check
        // (`TookPence <= 0`) happened to catch the case; the mutation check found it by refusing to
        // fail. A guard that only works because another guard is behind it is not a guard.
        var remainingByTender = new Dictionary<byte, long>();
        foreach (var c in capacities ?? Array.Empty<TenderCapacity>())
            if (c.TookPence > 0) remainingByTender[c.TenderType] = c.RemainingPence;

        foreach (var (tenderType, amount) in asked.OrderBy(a => a.Key))
        {
            // ⚠ FAILS CLOSED on a tender the sale never used — including one this build does not
            // recognise. A refund destination that cannot be traced to the original payment is exactly
            // the case that must never become a payout.
            if (!remainingByTender.TryGetValue(tenderType, out var remaining))
                return new TenderSplitDecision(false,
                    "That sale wasn't paid with this method, so the money can't go back that way.",
                    tenderType, amount);

            if (remaining == 0)
                return new TenderSplitDecision(false,
                    "This method has already had everything it took given back.",
                    tenderType, amount);

            if (amount > remaining)
                return new TenderSplitDecision(false,
                    "That is more than this method took on the original sale.",
                    tenderType, amount, remaining);
        }

        return new TenderSplitDecision(true, "Refund split approved.");
    }
}
