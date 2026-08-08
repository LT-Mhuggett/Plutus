using System;

namespace Plutus.Contracts.Client;

// ─────────────────────────────────────────────────────────────────────────────
// WP8 — who may sign in at this till, and what they may do, cached so it works with the network
// off. This is the contract that turns an enrolled till from "connected" into "usable".
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One grant on the wire: a permission, its ceiling, and the window it lives in.
///
/// ⚠ THE WINDOW IS SHIPPED RAW, NOT PRE-EVALUATED, and that is the whole design. If the server
/// answered "is this operator allowed right now" at sync time, a Saturday-only supervisor synced on
/// a Wednesday would arrive with NO permissions and keep them until the next sync — silently. The
/// till evaluates against its own clock at the moment of the action, using
/// <c>Plutus.SharedKernel.PermissionGrant.IsActiveAt</c>, the same code the server uses.
/// </summary>
public sealed record OperatorGrantDto(
    string Code,
    long? MaxPence,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    byte? DaysOfWeekMask,
    TimeOnly? WindowStartLocal,
    TimeOnly? WindowEndLocal);

/// <summary>
/// One person who may sign in at this till.
///
/// ⚠ <see cref="CredentialHashBase64"/> is a PBKDF2 hash, never a password — but it is still the
/// operator's *platform* credential, so this payload is the reason a till's local storage matters.
/// See <c>SharedKernel.OfflineCredentials</c> for how long it may be trusted, and till-design §9.8
/// for why that is tiered rather than a single number.
///
/// Null hash = a staff member with no web login yet. They are listed (so the till can show who
/// exists) and simply cannot sign in.
/// </summary>
public sealed record TillOperatorDto(
    Guid UserId,
    string DisplayName,
    string? Email,
    string? CredentialHashBase64,
    string? CredentialSaltBase64,
    OperatorGrantDto[] Grants);

/// <summary>
/// GET /api/v1/tills/{tillId}/operators — the till's roster.
///
/// ⚠ <see cref="AsOfUtc"/> is what the staleness horizons are measured from. It is the SERVER's
/// clock on purpose: a till with a wrong clock would otherwise decide its own credentials were
/// fresh for ever, or expire them the moment they arrived.
/// </summary>
public sealed record TillOperatorsResult(Guid TillId, DateTime AsOfUtc, TillOperatorDto[] Operators);
