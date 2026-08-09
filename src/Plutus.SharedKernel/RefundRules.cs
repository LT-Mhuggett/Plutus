using System;

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
}
