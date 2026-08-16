using System.Net;
using Plutus.Contracts.Client;

namespace Plutus.Client.Core;

/// <summary>What a till must do about its own standing with the platform.</summary>
public enum DeviceStanding
{
    /// <summary>Carry on trading. ⚠ Also the answer when the platform could not be asked.</summary>
    Trading = 0,

    /// <summary>
    /// A manager has asked for this till back and nobody has approved it yet.
    /// ⚠ STILL TRADES — see <see cref="DeviceRevocation"/>.
    /// </summary>
    RemovalRequested = 1,

    /// <summary>The platform has revoked this device. Stop.</summary>
    Revoked = 2,
}

/// <summary>
/// Is this till still allowed to be a till? (WP4, step 21.)
///
/// ⚠⚠ WHY IT HAS TO BE POLLED AT ALL. Device tokens are HMAC bearer tokens with **no server-side
/// denylist**, so revoking a device in the portal does not invalidate the token it is already
/// holding: a lost or stolen till keeps selling for up to its **12h TTL** unless it asks. This
/// endpoint is the only revocation signal that reaches a till.
///
/// ⚠ ASKED VIA DEVICE STATUS, NEVER BY MINTING A TOKEN. The token endpoint is rate-limited to
/// 5/min per IP, so polling that would make a healthy till report itself revoked (429) — and in a
/// shop where several tills share one public IP, they would do it to each other.
///
/// ⚠⚠ **PendingRemoval KEEPS TRADING, DELIBERATELY.** Somebody has requested the till back and
/// nobody has approved it. Stopping a shop's till on an unapproved *request* would turn un-enrolment
/// into a way to take a competitor's — or an angry ex-employee's — shop down. Only an approved
/// revocation stops anything.
///
/// ⚠⚠ **AND A STATUS WE COULD NOT GET IS "CARRY ON", NEVER "STOP".** Same load-bearing rule as
/// <see cref="OperatorRevocation"/>: if a failed poll stopped the till, every dropped connection
/// would close a shop mid-sale, and it would happen on the flakiest sites first. That is a worse
/// outage than the one this prevents. Revocation is only ever concluded from an answer the platform
/// actually gave.
/// </summary>
public static class DeviceRevocation
{
    /// <summary>
    /// ⚠ It says the TILL, not the operator — otherwise somebody tries another login, then another,
    /// and concludes the staff accounts are broken. And it names the portal, because that is where
    /// the person who can undo it is.
    /// </summary>
    public const string RevokedMessage =
        "This till has been removed in Plutus and can no longer be used. "
        + "Speak to your manager — it can be re-enrolled from the portal.";

    /// <summary>
    /// Decide from one device-status poll.
    /// </summary>
    /// <param name="code">The HTTP status. ⚠ See below — most codes mean "ask again later".</param>
    /// <param name="body">⚠ NULL when the platform did not answer usefully. Never a revocation.</param>
    public static DeviceStanding Check(HttpStatusCode code, DeviceStatusResult? body)
    {
        // ⚠⚠ ONLY AN EXPLICIT "Revoked" STOPS A TILL. Nothing else here does, and the omissions are
        // deliberate:
        //
        //   • **401/403** are also what a clock-skewed till, a mis-issued token or a misconfigured
        //     gateway produces. Stopping a shop on an auth blip is the outage this class exists to
        //     avoid.
        //   • **404** looks terminal and is not reliably so — it is equally "wrong device id" or a
        //     tenant filter that did not match, and a routing mistake would close every till at
        //     once. ⚠ Approving a removal sets `Status = Revoked`; it does NOT delete the row
        //     (`TillsController.DecideRemoval`), so a genuinely un-enrolled till DOES get the
        //     explicit answer and is caught by it.
        //
        // Both still reach a human: `ConnectivityProbe` reports them on the Plutus tab as a refused
        // credential, which is where somebody is reading rather than serving a customer.
        if (code != HttpStatusCode.OK || body is null) return DeviceStanding.Trading;

        if (body.IsRevoked) return DeviceStanding.Revoked;

        return body.IsPendingRemoval ? DeviceStanding.RemovalRequested : DeviceStanding.Trading;
    }

    /// <summary>⚠ The only standing that stops a till. Named so no caller writes the comparison
    /// itself and quietly includes <see cref="DeviceStanding.RemovalRequested"/>.</summary>
    public static bool MustStop(DeviceStanding standing) => standing == DeviceStanding.Revoked;
}
