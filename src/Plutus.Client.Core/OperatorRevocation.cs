using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

/// <summary>Why a signed-in operator must be put out of the till.</summary>
public enum RevocationReason
{
    /// <summary>They are still on the roster — carry on.</summary>
    None = 0,

    /// <summary>
    /// They are gone from a roster the server DID answer with. Deactivated in the portal, or their
    /// last `pos.*` permission removed — the roster endpoint filters on `e.Active` and on holding
    /// at least one till permission, so both land here and both mean the same thing at a counter.
    /// </summary>
    NoLongerPermitted = 1,
}

/// <summary>
/// Has the signed-in operator been revoked since they signed in?
///
/// ⚠ WHY THIS IS A SHARED, TESTED RULE AND NOT AN `if` IN THE CADENCE. Matt, 2026-08-11: *"If a
/// user is disabled, the user needs immediately logging out."* The check that decides that is one
/// line long and has exactly one way to be catastrophically wrong — and the wrong version looks
/// identical to the right one at a glance.
///
/// ⚠⚠ **A ROSTER THAT COULD NOT BE FETCHED IS NOT AN EMPTY ROSTER.** `OperatorSync.RefreshAsync`
/// returns null when the server could not be asked, and deliberately leaves the cached roster
/// alone — because replacing a good roster with nothing when the wifi drops would lock a shop out
/// of its own till. If this check treated "no answer" as "not on the list", then **every dropped
/// connection would sign the whole shop out**, mid-sale, with a message accusing the operator of
/// being disabled. That is a worse outage than the one it protects against, and it would happen on
/// the flakiest sites first.
///
/// So revocation is only ever concluded from a roster the server actually sent.
///
/// ⚠ A roster that comes back EMPTY is still an answer, and a real one — it means nobody is
/// assigned to this till any more. That signs the operator out, correctly.
/// </summary>
public static class OperatorRevocation
{
    /// <summary>
    /// ⚠ The wording is Matt's, verbatim (2026-08-11). It is deliberately not "you have been
    /// logged out" — the operator needs to know it is their ACCOUNT, not the till, so they go to
    /// the right person instead of rebooting the machine.
    /// </summary>
    public const string DisabledMessage =
        "Your account has been disabled, please speak to your manager";

    /// <summary>
    /// Decide whether the signed-in operator may stay.
    /// </summary>
    /// <param name="signedInUserId">Null when nobody is signed in — nothing to revoke.</param>
    /// <param name="roster">⚠ NULL when the server could not be asked. Never treated as empty.</param>
    public static RevocationReason Check(Guid? signedInUserId, TillOperatorsResult? roster)
    {
        // Nobody signed in — the login screen already re-reads the roster on its way in.
        if (signedInUserId is not Guid userId) return RevocationReason.None;

        // ⚠⚠ THE LOAD-BEARING LINE. No answer is not an empty answer — see the header. A till that
        // signed its shop out every time the broadband hiccuped would be turned off within a week,
        // and then nothing would be enforced at all.
        if (roster is null) return RevocationReason.None;

        var operators = roster.Operators ?? Array.Empty<TillOperatorDto>();

        return operators.Any(o => o.UserId == userId)
            ? RevocationReason.None
            : RevocationReason.NoLongerPermitted;
    }
}
