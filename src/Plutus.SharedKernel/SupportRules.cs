using System;

namespace Plutus.SharedKernel;

/// <summary>
/// **Support-desk rules that both the server and every till must agree on.**
///
/// ⚠ In `SharedKernel` rather than in `Client.Core` because the SERVER needs them: `SupportController`
/// answers the unread count the heartbeat carries, and `Plutus.Tenancy` must not reference a client
/// library. `Client.Core.SupportLabels` delegates here so MAUI reads naturally.
/// </summary>
public static class SupportRules
{
    /// <summary>
    /// **Is this ticket waiting on the CLIENT to read it?**
    ///
    /// ⚠⚠ WP-TICKETS, 2026-08-21. Matt: *"When I reply to a live ticket, how is the user informed?"*
    /// They were not — the operator answered, the ticket flipped to `WaitingOnClient`, and nothing
    /// anywhere told the shop. This is the rule behind the badge that now says so.
    ///
    /// ⚠⚠ **THE LAST MESSAGE'S AUTHOR IS THE SIGNAL, NOT THE STATUS.** `WaitingOnClient` is set by
    /// an operator reply and is also what a ticket sits in while the client thinks about it — so
    /// badging on status alone would light up for ever on a thread somebody has already read and
    /// decided to leave. Unread means: the last word is theirs, and nobody here has opened it since.
    ///
    /// ⚠ A CLOSED TICKET IS NEVER UNREAD. Closing is itself an operator action that lands last; a
    /// badge nagging a shop to read a conversation that has finished is a badge they learn to
    /// ignore, and then they ignore the one that matters.
    /// </summary>
    /// <param name="status">`SupportStatus` — Closed tickets never count.</param>
    /// <param name="lastMessageFromOperator">Whether the newest message is the operator's.</param>
    /// <param name="lastMessageAtUtc">When that message landed.</param>
    /// <param name="clientLastReadAtUtc">When the client side last opened the thread; null = never.</param>
    public static bool IsUnreadByClient(
        byte status,
        bool lastMessageFromOperator,
        DateTime? lastMessageAtUtc,
        DateTime? clientLastReadAtUtc)
    {
        if (status == 2) return false;                  // Closed
        if (!lastMessageFromOperator) return false;     // the client spoke last — nothing to read
        if (lastMessageAtUtc is null) return false;     // no messages at all

        // ⚠ `>=`, NOT `>`. A read stamped in the same tick as the message it read — which happens
        // when a thread is opened the instant a reply lands — must count as read, or the badge
        // survives the very act of clearing it.
        return clientLastReadAtUtc is null || clientLastReadAtUtc < lastMessageAtUtc;
    }
}
