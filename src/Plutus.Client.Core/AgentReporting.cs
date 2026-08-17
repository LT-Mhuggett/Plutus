using System;

namespace Plutus.Client.Core;

/// <summary>
/// What a till last found when it asked its local hardware agent (FE3.0).
///
/// ⚠ A NULL <see cref="AgentVersion"/> IS A REAL ANSWER, not a missing one — it means the till looked
/// and there is no agent on this PC, which is a fleet fact the portal's Locations page shows. It must
/// be reported, not skipped.
/// </summary>
public sealed record AgentSnapshot(string? AgentVersion, string? PrinterName, bool? PrinterOnline)
{
    /// <summary>Nothing found — the till asked and no agent answered.</summary>
    public static AgentSnapshot None { get; } = new(null, null, null);
}

/// <summary>
/// When a till should tell the platform about its hardware agent.
///
/// ⚠⚠ THIS IS A C2 TWIN. The web till has held the same rule since FE3.0 in
/// `hardware.ts reportAgentStatus` — send when the snapshot CHANGED, or when the last confirmation
/// has gone stale. MAUI needed the identical rule, and the choice was to write it here once rather
/// than let a second copy appear in the till. See till-design.md C2.
///
/// ⚠ WHY NOT JUST SEND EVERY TICK. MAUI's cadence is **60 seconds**; the web till reports every five
/// minutes at most. Agent telemetry changes when somebody plugs a printer in — perhaps twice a year —
/// so beating on the endpoint every minute would be 300× the useful traffic, from every till in
/// every shop, to overwrite a row with what it already said. ⚠ It is also explicitly *telemetry, not
/// audit*: the server overwrites in place and writes no `AuditLogs` row, precisely so a poller cannot
/// grow an audit table.
///
/// ⚠ BUT SILENCE IS NOT FREE EITHER, which is why staleness forces a send: the portal shows
/// `agentReportedAtUtc`, and a till that only ever reported once looks indistinguishable from a till
/// that has been switched off since.
/// </summary>
public static class AgentReporting
{
    /// <summary>
    /// Re-confirm an unchanged snapshot this often. ⚠ 6 hours, the web till's `REPORT_EVERY_MS`
    /// exactly — "even unchanged, re-confirm a few times a day".
    /// </summary>
    public static readonly TimeSpan Reconfirm = TimeSpan.FromHours(6);

    /// <summary>
    /// Should this snapshot be sent?
    /// </summary>
    /// <param name="current">What the till just found. ⚠ Never null — "no agent" is
    /// <see cref="AgentSnapshot.None"/>, not a null snapshot.</param>
    /// <param name="lastSent">The last snapshot the platform ACCEPTED, or null if none ever was.
    /// ⚠ Only ever set from a successful POST — recording it on a failed one would suppress the
    /// retry and lose the reading for six hours.</param>
    /// <param name="lastSentAtUtc">When that happened, or null.</param>
    public static bool ShouldSend(
        AgentSnapshot current, AgentSnapshot? lastSent, DateTime? lastSentAtUtc, DateTime nowUtc)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));

        // ⚠ Nothing has ever been accepted — send, whatever it says. This is the case that puts a
        // newly enrolled till on the portal's fleet list at all.
        if (lastSent is null || lastSentAtUtc is not DateTime sentAt) return true;

        // ⚠ Record equality, so a printer going offline or an agent upgrade is noticed. This is the
        // reason `AgentSnapshot` is a record and not a class.
        if (current != lastSent) return true;

        // ⚠ `>=`, and computed forwards from the send. A till whose clock jumps BACKWARDS would
        // otherwise wait for the clock rather than the interval — and clock skew on shop PCs is
        // common enough that the platform ships a drift check for it.
        return nowUtc - sentAt >= Reconfirm || nowUtc < sentAt;
    }
}
