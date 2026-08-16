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
    DateTime UpdatedAtUtc);

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
