using System;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP-TICKETS — is a ticket waiting on the client to READ it?
///
/// ⚠⚠ MATT, 2026-08-21: *"When I reply to a live ticket, how is the user informed?"* They were not.
/// This is the rule behind the badge that now says so, and it is shared: the server counts with it
/// (`SupportController`, `HeartbeatController`), the portal and both tills render from it.
///
/// ⚠ THE CASES BELOW ARE THE WHOLE ARGUMENT for not badging on STATUS. `WaitingOnClient` is set by
/// an operator reply and is ALSO what a ticket sits in while the client thinks about it — so status
/// alone lights up for ever on a thread somebody has already read and decided to leave.
/// </summary>
public class SupportRulesTests
{
    private static readonly DateTime Reply = new(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);

    private const byte Open = 0;
    private const byte WaitingOnClient = 1;
    private const byte Closed = 2;

    /// <summary>⚠⚠ THE CASE THE FEATURE EXISTS FOR: support answered, nobody has looked.</summary>
    [Fact]
    public void An_operator_reply_nobody_has_opened_is_unread()
    {
        Assert.True(SupportRules.IsUnreadByClient(WaitingOnClient, true, Reply, null));
    }

    [Fact]
    public void Reading_after_the_reply_clears_it()
    {
        Assert.False(SupportRules.IsUnreadByClient(WaitingOnClient, true, Reply, Reply.AddSeconds(1)));
    }

    /// <summary>⚠ A read from BEFORE the reply does not count — that is the second reply on a thread
    /// somebody opened yesterday, and it is exactly as unread as the first.</summary>
    [Fact]
    public void A_read_older_than_the_reply_leaves_it_unread()
    {
        Assert.True(SupportRules.IsUnreadByClient(WaitingOnClient, true, Reply, Reply.AddMinutes(-5)));
    }

    /// <summary>
    /// ⚠⚠ A READ IN THE SAME INSTANT COUNTS AS READ. A thread opened as the reply lands stamps the
    /// same tick, and treating that as unread would make the badge survive the very act of clearing
    /// it — a notification the operator cannot dismiss.
    /// </summary>
    [Fact]
    public void A_read_in_the_same_instant_counts_as_read()
    {
        Assert.False(SupportRules.IsUnreadByClient(WaitingOnClient, true, Reply, Reply));
    }

    /// <summary>⚠ THE CLIENT SPOKE LAST — there is nothing to read, whatever the status says.</summary>
    [Fact]
    public void A_thread_the_client_spoke_on_last_is_not_unread()
    {
        Assert.False(SupportRules.IsUnreadByClient(Open, false, Reply, null));
    }

    /// <summary>⚠⚠ A CLOSED TICKET IS NEVER UNREAD. Closing lands last as an operator action, and a
    /// badge nagging a shop to read a finished conversation is one they learn to ignore — and then
    /// they ignore the one that matters.</summary>
    [Fact]
    public void A_closed_ticket_is_never_unread()
    {
        Assert.False(SupportRules.IsUnreadByClient(Closed, true, Reply, null));
    }

    /// <summary>⚠ A ticket with no messages at all cannot be unread — a raise with no body is not a
    /// reply waiting to be seen.</summary>
    [Fact]
    public void A_ticket_with_no_messages_is_not_unread()
    {
        Assert.False(SupportRules.IsUnreadByClient(Open, true, null, null));
    }

    /// <summary>
    /// ⚠ STATUS ALONE IS NOT THE SIGNAL, stated as a test: the same `WaitingOnClient` ticket is
    /// unread or not depending only on who spoke last and whether anybody looked.
    /// </summary>
    [Fact]
    public void The_same_status_can_be_read_or_unread()
    {
        Assert.True(SupportRules.IsUnreadByClient(WaitingOnClient, true, Reply, null));
        Assert.False(SupportRules.IsUnreadByClient(WaitingOnClient, true, Reply, Reply.AddHours(1)));
    }
}
