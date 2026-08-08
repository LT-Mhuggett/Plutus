using System;

namespace Plutus.Contracts.Client;

/// <summary>
/// GET /api/v1/notifications?unackedOnly=true — a "pick from floor" note.
///
/// A web order sold something that is also sitting on the shop floor, so somebody has to physically
/// pull it off the shelf before a walk-in customer buys the same copy. ⚠ That makes these notes
/// **perishable and operational**, not informational: one nobody sees becomes an oversell, and an
/// oversell on a web order is a refund and an apology.
/// </summary>
/// <param name="StoreId">Which store must pull the stock. ⚠ NULL means every store's tills — a
/// note the fulfilment store has not been decided for, which everyone should see rather than
/// nobody.</param>
public sealed record PickNoteDto(
    Guid Id,
    string Message,
    long WooOrderId,
    int? StoreId,
    DateTime CreatedAtUtc,
    DateTime? AckedAtUtc);

/// <summary>
/// GET /api/v1/announcements/active — a platform message (maintenance window, incident).
///
/// ⚠ Read-only and NOT acknowledgeable, unlike a pick note. Nothing on the till may hide one: the
/// server decides when it stops being active by its own start/end window, and a till that let an
/// operator dismiss "we are down for maintenance at 6pm" would be a till whose staff are the last
/// to know.
/// </summary>
/// <param name="Severity">Server-side enum name — <c>Info</c> · <c>Maintenance</c> ·
/// <c>Incident</c> (<c>AnnouncementSeverity</c>). ⚠ **`Info` is PORTAL-ONLY and must not reach a
/// till**: the till banner is for things that change what an operator should do in the next hour,
/// and filling it with notices trains people to ignore it. See
/// <c>NoticesClient.ShowsOnATill</c>, which is the one place that rule lives.
/// ⚠ A STRING rather than an enum on purpose — a till that meets a severity a later backend added
/// must still be able to show the message rather than fail to deserialise the whole batch.</param>
public sealed record AnnouncementDto(
    Guid Id,
    string Severity,
    string Title,
    string Body,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);
