using System;
using System.Text.Json.Serialization;

namespace Plutus.Contracts.Client;

/// <summary>
/// The five things that can happen to a till's cash drawer, as the wire spells them.
///
/// ⚠ STRINGS, AND THAT IS DELIBERATE. The server parses this with
/// <c>Enum.TryParse&lt;CashEventType&gt;(req.Type, true, …)</c>, and its enum lives in
/// <c>Plutus.Entities</c> — a BACKEND project a till must never reference. So the vocabulary exists
/// twice by construction, exactly like <c>TenderType</c> (till-design C2). The difference from
/// tenders is that these travel as NAMES rather than as bytes, so a rename breaks loudly at the
/// 400 rather than silently re-labelling history. Pinned by <c>CashEventTypeParityTests</c>.
///
/// ⚠ Do not add a sixth without adding it server-side FIRST. An unknown type is a 400, so a till
/// that invents one cannot bank money — it just fails, on the busiest day of the year.
/// </summary>
public static class CashEventTypes
{
    /// <summary>Money put IN at the start of the day, before trading.</summary>
    public const string OpenFloat = "OpenFloat";

    /// <summary>Money added mid-session for a reason that is not a sale.</summary>
    public const string PaidIn = "PaidIn";

    /// <summary>Money taken out mid-session — a supplier paid in cash, a till lift to the safe.</summary>
    public const string PaidOut = "PaidOut";

    /// <summary>A mid-session count. ⚠ Reads the drawer, never closes the day.</summary>
    public const string XSnapshot = "XSnapshot";

    /// <summary>The end-of-day count. ⚠ ONE PER BUSINESS DAY — the server 409s every event that
    /// follows one, not merely a second Z.</summary>
    public const string ZClose = "ZClose";

    /// <summary>
    /// Reverse a Z close so the day can trade again — supervisor and above.
    ///
    /// ⚠ Matt, 2026-08-11: *"I need to be able to override a Z-closed till. A supervisor or above
    /// needs to be able to reverse the close."* A day closed early — or closed by accident, or
    /// closed on a test till — otherwise stranded that till until midnight.
    ///
    /// ⚠⚠ A COMPENSATING EVENT, NOT A DELETION, and this is the whole design. Deleting the ZClose
    /// would erase the fact that somebody counted and banked the drawer, along with the variance the
    /// platform calculated against it. **Both events stay**: the day was closed at 17:32 and
    /// reopened at 17:41, by a named person, for a stated reason. That is what a ledger is for, and
    /// it is the only version of this feature that can survive being asked about in three months.
    ///
    /// ⚠ SO "IS THE DAY CLOSED?" BECOMES "WHICH CAME LAST?" — every gate that used to look for the
    /// existence of a ZClose must now compare the newest ZClose against the newest ZReopen. A gate
    /// left on `Any(ZClose)` would refuse a reopened day for ever, and a gate that forgot the rule
    /// entirely would let a genuinely closed day keep trading.
    ///
    /// ⚠ IT CARRIES NO MONEY. Reopening does not move a penny — the float, the takings and the
    /// counted figure are all still what they were. It only makes the day writable again, which is
    /// why it needs no counted amount and must never be mistaken for a second float.
    /// </summary>
    public const string ZReopen = "ZReopen";

    public static readonly string[] All = { OpenFloat, PaidIn, PaidOut, XSnapshot, ZClose, ZReopen };

    /// <summary>Does this type need a counted figure? X and Z are counts; the others are movements.</summary>
    public static bool NeedsCount(string type) =>
        type == XSnapshot || type == ZClose;

    /// <summary>Does this type need a reason? ⚠ Money leaving or entering a drawer outside a sale
    /// is the one movement with no other record of WHY, so the server refuses it without one.</summary>
    public static bool NeedsReason(string type) =>
        type == PaidIn || type == PaidOut;
}

/// <summary>
/// A cash movement or count, as the till reports it.
///
/// ⚠ TWIN of <c>Plutus.Cash.CashEventRequest</c>. The contracts project has no references and no
/// packages **because it ships onto tills**, so the server declares its own copy rather than being
/// referenced by it — the same convention as EnrolResult, HeartbeatRequest and the sale contracts.
/// Change one shape, change both. Recorded in till-design C2.
/// </summary>
public sealed class CashEventRequest
{
    /// <summary>⚠ MINTED BY THE TILL, and it is what makes this safe to retry. The server replays a
    /// known eventId back as 200 with the stored outcome, so a float posted twice because the line
    /// dropped mid-request is recorded once. A server-minted id could not do that.</summary>
    [JsonPropertyName("eventId")] public Guid EventId { get; set; }

    /// <summary>⚠ Optional, and checked against the token when both are present. The till id is
    /// derived SERVER-side from the device; a body that disagrees is a 403 rather than a
    /// reattribution.</summary>
    [JsonPropertyName("deviceId")] public Guid DeviceId { get; set; }

    /// <summary>One of <see cref="CashEventTypes"/>.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>⚠ The TRADING day, not the calendar day. A till open past midnight is still on
    /// yesterday's business day, and banking that splits at midnight reconciles against nothing.</summary>
    [JsonPropertyName("businessDay")] public DateOnly BusinessDay { get; set; }

    [JsonPropertyName("occurredAtUtc")] public DateTime OccurredAtUtc { get; set; }

    /// <summary>⚠ NON-NEGATIVE for float, paid-in and paid-out — the TYPE carries the direction,
    /// not the sign. A negative paid-out is a paid-in nobody meant, and the server 400s it.</summary>
    [JsonPropertyName("amountPence")] public long AmountPence { get; set; }

    /// <summary>What was actually in the drawer. ⚠ Required for X and Z, meaningless otherwise —
    /// it is the whole point of a read, and the server refuses one without it.</summary>
    [JsonPropertyName("countedPence")] public long? CountedPence { get; set; }

    [JsonPropertyName("reason")] public string? Reason { get; set; }

    [JsonPropertyName("operatorUserId")] public Guid? OperatorUserId { get; set; }
}

/// <summary>
/// What the platform recorded, including what it EXPECTED to be there.
///
/// ⚠ THE VARIANCE IS THE SERVER'S TO COMPUTE, never the till's. Expected = opening float + cash
/// takings (tenders minus change, for that till and business day) + paid-ins − paid-outs, and only
/// the platform can see the sales half of that — including sales another device on the same till
/// posted. A till that computed its own expected figure would disagree with the banking report and
/// the shop would have no way to tell which was right.
/// </summary>
public sealed class CashEventResult
{
    [JsonPropertyName("eventId")] public Guid EventId { get; set; }
    [JsonPropertyName("tillId")] public Guid TillId { get; set; }
    [JsonPropertyName("businessDay")] public string? BusinessDay { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("amountPence")] public long AmountPence { get; set; }
    [JsonPropertyName("countedPence")] public long? CountedPence { get; set; }

    /// <summary>What the platform believes should be in the drawer. Null until an X or Z is taken.</summary>
    [JsonPropertyName("expectedPence")] public long? ExpectedPence { get; set; }

    /// <summary>⚠ Counted minus expected. NEGATIVE means the drawer is DOWN — money the shop
    /// expected and does not have — which is the direction anyone reading this actually cares about.</summary>
    [JsonPropertyName("variancePence")] public long? VariancePence { get; set; }
}
