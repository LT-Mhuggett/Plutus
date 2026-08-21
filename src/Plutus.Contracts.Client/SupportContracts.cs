using System;

namespace Plutus.Contracts.Client;

/// <summary>
/// OP4 / WP6.3 — a support ticket as a till sees it. `GET /api/v1/support/tickets`.
///
/// ⚠ <see cref="Status"/> and <see cref="Severity"/> are the RAW BYTES of the server's
/// `SupportStatus` / `SupportSeverity` enums, deliberately. A client that mapped them to its own
/// strings on the wire would have to be updated in lockstep with the server to stay readable; the
/// numbers are stable, and turning them into words is a client display rule with one home —
/// <c>Plutus.Client.Core.SupportLabels</c>.
/// </summary>
public sealed record SupportTicketDto(
    Guid Id,
    string Subject,
    byte Status,
    byte Severity,
    string RaisedByName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,

    // ── WP-TICKETS, 2026-08-21 ──────────────────────────────────────────────────────────────────
    //
    // ⚠ OPTIONAL WITH DEFAULTS, so an older backend still deserialises into this record rather than
    // throwing on a till somebody has not updated yet.

    /// <summary>When it closed. ⚠ Matt: *"the button 'Close' … there is nothing visual within the
    /// ticket itself?"* This is what the thread's "closed on …" line is drawn from.</summary>
    DateTime? ClosedAtUtc = null,

    /// <summary>Who asked to close — true the operator, false the client, null nobody.</summary>
    bool? ClosureRequestedByOperator = null,

    DateTime? ClosureRequestedAtUtc = null,

    /// <summary>When this shop last opened the thread. ⚠ With the last message's author it decides
    /// the unread badge — see `SharedKernel.SupportRules.IsUnreadByClient`.</summary>
    DateTime? ClientLastReadAtUtc = null);

/// <summary>
/// One message in a ticket's thread. `GET /api/v1/support/tickets/{id}/messages`.
///
/// ⚠ <see cref="FromOperator"/> is the whole point of the record: it is what separates "the shop
/// said this" from "Plutus said this", and a thread that renders both the same is unreadable.
/// </summary>
public sealed record SupportMessageDto(
    bool FromOperator,
    string AuthorName,
    string Body,
    DateTime AtUtc);

/// <summary>Raise a ticket. `POST /api/v1/support/tickets`.</summary>
public sealed record RaiseTicketRequest(string Subject, string Body, byte Severity);

/// <summary>Reply on an existing thread. `POST /api/v1/support/tickets/{id}/messages`.</summary>
public sealed record TicketReplyRequest(string Body);

/// <summary>
/// WP-TICKETS — how many replies this shop has not read, and the newest few subjects.
///
/// ⚠ A COUNT PLUS A HANDFUL OF SUBJECTS, not the tickets. A badge saying "3" wants a tooltip naming
/// the most recent, and the caller should not have to fetch the whole list to build one.
/// </summary>
public sealed record UnreadSupportDto(int Unread, string[] Subjects);
