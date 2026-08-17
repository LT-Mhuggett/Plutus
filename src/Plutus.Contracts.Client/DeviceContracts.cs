using System;

namespace Plutus.Contracts.Client;

// ─────────────────────────────────────────────────────────────────────────────
// WP1: enrolment, device tokens and the till-facing read contracts. Mirrors
// src/Plutus.Tenancy/{EnrolmentService.cs, Controllers/TillsController.cs, Controllers/TokensController.cs}
// and Controllers/StoresController.cs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// GET /api/v1/ping — AllowAnonymous, touches no database.
///
/// ⚠ This answers "can I reach a Plutus backend", NOT "does the platform still accept this till".
/// The two questions have different answers and different people to call, and collapsing them is
/// how an operator ends up rebooting a router because their device credential was revoked. The
/// second question is <see cref="DeviceTokenRequest"/>.
///
/// <c>UtcNow</c> is the server's clock. Device tokens are HMAC-signed with an expiry and VAT bands
/// are effective-dated, so a till whose own clock has drifted produces sales the server judges
/// against a different instant — worth warning about before it quarantines a day's takings.
/// </summary>
public sealed record PingResult(bool Ok, DateTime UtcNow, string? ApiVersion);

/// <summary>POST /api/v1/tills/enrol — AllowAnonymous, rate-limited.</summary>
public sealed record EnrolRequest(string EnrolmentCode);

/// <summary>410 Gone if the code was reused, expired or is unknown. ⚠ ClientSecret goes to
/// platform secure storage, NEVER the local database file.</summary>
public sealed record EnrolResult(Guid DeviceId, string ClientSecret, Guid TillId, Guid TenantId);

/// <summary>POST /api/v1/tokens/device — 401 if the secret is wrong or the device is revoked.</summary>
public sealed record DeviceTokenRequest(Guid DeviceId, string ClientSecret);

public sealed record DeviceTokenResult(string AccessToken, int ExpiresInSeconds);

/// <summary>
/// POST /api/v1/tills/agent-status (FE3.0) — what this till found when it polled its local hardware
/// agent.
///
/// ⚠ A NULL <c>AgentVersion</c> means "I looked and there is no agent on this PC", which the portal
/// shows. It is a reading, not a missing field, so it is sent rather than skipped.
///
/// ⚠ EVERYTHING BUT THE DEVICE ID IS NULLABLE ON PURPOSE — the server's own record says why: this
/// project has NRT on, and `[ApiController]` turns a null in a non-nullable property into an
/// automatic 400 before the action ever runs.
/// </summary>
public sealed record AgentStatusRequest(
    Guid DeviceId, string? AgentVersion, string? PrinterName, bool? PrinterOnline);

/// <summary>
/// POST /api/v1/tills/unenrol-request — this till asks to be taken off the estate.
///
/// ⚠ A DEVICE MAY ONLY UN-ENROL ITSELF; the server refuses any other <c>DeviceId</c> with a 403.
/// Without that, one enrolled till could start the removal of every other till in the estate, and
/// removals are approved from a portal queue where a flood of plausible requests gets waved through.
///
/// ⚠ It does NOT remove anything. The device becomes <c>PendingRemoval</c> and **keeps trading**
/// until somebody approves it in the portal.
/// </summary>
public sealed record UnenrolRequest(Guid DeviceId);

/// <summary>
/// GET /api/v1/tills/devices/{deviceId}/status — <c>"Active"</c> | <c>"PendingRemoval"</c> |
/// <c>"Revoked"</c>. Gated <c>sales.ingest</c>, one indexed lookup.
///
/// ⚠ <c>PendingRemoval</c> KEEPS TRADING by design — a manager has asked for the till back but
/// nobody has approved it, and stopping a shop's till on a request would make un-enrolment a denial
/// of service. Only <c>Revoked</c> stops.
///
/// ⚠ This is the ONLY revocation signal that reaches a till. Device tokens are HMAC bearer tokens
/// with no server-side denylist, so revoking a device does not invalidate its outstanding token —
/// the till keeps working for up to its 12h TTL unless it polls this.
/// </summary>
/// <param name="TillId">Which till this device is. ⚠ Nullable only because an older backend does
/// not send it — a till must treat null as "ask again later", never as "not enrolled".</param>
public sealed record DeviceStatusResult(string Status, Guid? TillId = null)
{
    public bool IsRevoked => string.Equals(Status, "Revoked", StringComparison.OrdinalIgnoreCase);
    public bool IsPendingRemoval => string.Equals(Status, "PendingRemoval", StringComparison.OrdinalIgnoreCase);
}

/// <summary>GET /api/v1/tills/{id}/name — how a till learns which STORE it belongs to (the key
/// that makes receipts, store info and themes per-store).</summary>
public sealed record TillNameResult(Guid Id, string? Name, int? StoreId);

/// <summary>GET /api/v1/stores/{id}/info — read-only; the portal is the source of truth (WP6.1).
/// ⚠ BusinessId is the LEGACY Business id, not the tenant id — it seeds
/// <c>DeterministicGuid.ForItem</c>, and using the tenant id instead corrupts item ids silently.
/// It is an additive field: null against a server that predates WP1.</summary>
public sealed record StoreInfoResult(
    int StoreId,
    string? Name,
    string? BusinessName,
    Guid? BusinessId,
    string? VatNumber,
    string? AdLine1,
    string? AdLine2,
    string? City,
    string? PostCode,
    string? Country,
    string? ContactNumber,
    string? OpeningHoursJson);

/// <summary>GET /api/v1/themes/effective — FE10. baseMode is "system" | "light" | "dark";
/// colorsJson is an opaque slot blob owned by the frontends.</summary>
public sealed record EffectiveThemeResult(
    string Source, string? ThemeKey, string? Name, string BaseMode, string? ColorsJson);

/// <summary>GET /api/v1/stores/{id}/receipt-template — the per-store receipt layout the till
/// prints with, so a receipt is controlled from the portal rather than hardcoded per client.</summary>
public sealed record ReceiptTemplateResult(int StoreId, string? ReceiptTemplateJson);

// ─────────────────────────────────────────────────────────────────────────────
// WP2c — the published VAT band contract. THE PORTAL IS THE SOURCE OF VAT TRUTH:
// a till receives bands, applies them, and reports what it charged. No till may
// hold a hard-coded VAT rate.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One dated rate on a band. The band is the identity; this is one value it held.</summary>
public sealed record VatRatePointDto(int RateBp, DateTime EffectiveFromUtc);

/// <summary>
/// A VAT band as the portal publishes it.
///
/// ⚠ <see cref="VatClass"/> is NOT derivable from <see cref="RateBp"/>. "Zero" and "Exempt" are
/// both 0% to the customer and different in law — zero-rated is a taxable supply with input-tax
/// recovery, exempt is not taxable and blocks it. Never infer one from the rate.
///
/// ⚠ <see cref="Rates"/> carries FUTURE points as well as past ones. That is what lets a till that
/// goes offline today start charging a rate change that lands next week — see
/// <see cref="VatBandsResult"/>.
/// </summary>
public sealed record VatBandDto(
    string Key,
    string DisplayName,
    string VatClass,
    /// <summary>The rate in force at the moment the server answered. A till that has been offline
    /// since must re-derive from <see cref="Rates"/> rather than trusting this.</summary>
    int RateBp,
    DateTime EffectiveFromUtc,
    VatRatePointDto[] Rates,
    /// <summary>
    /// WHICH legacy tax rows mean this band. An item carries a <c>TaxId</c>, so this is how a till
    /// knows an item is EXEMPT rather than merely 0% — a distinction the RATE can never carry,
    /// because zero-rated and exempt are both 0% to the customer and different in law.
    ///
    /// ⚠ Empty is normal and is NOT an error: the mapping is dormant until a tenant actually has
    /// two bands at one rate, so a shop like Kapow is never asked to fill it in.
    /// </summary>
    int[]? LegacyTaxIds = null);

/// <summary>
/// GET /api/v1/vat/bands — sales.ingest, so a device OR an operator token can read it. Cached on
/// the catalogue-sync cadence.
///
/// ⚠ THE WHOLE TIMELINE IS SHIPPED ON PURPOSE. Caching only "the rate right now" would put a till
/// back in the position WP2b exists to catch: offline across a rate change, still charging the old
/// rate, its backlog quarantined on reconnect. With the timeline it moves itself on the day.
/// </summary>
public sealed record VatBandsResult(DateTime AsOfUtc, string Basis, VatBandDto[] Bands)
{
    /// <summary>The rate in force for one band at <paramref name="atUtc"/> — the most recent point
    /// at or before that instant. Null if the band is unknown or had not started yet.</summary>
    public int? RateBpAt(string key, DateTime atUtc)
    {
        foreach (var b in Bands)
        {
            if (!string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            int? found = null;
            DateTime best = DateTime.MinValue;
            foreach (var p in b.Rates)
                if (p.EffectiveFromUtc <= atUtc && p.EffectiveFromUtc >= best)
                {
                    best = p.EffectiveFromUtc;
                    found = p.RateBp;
                }
            return found;
        }
        return null;
    }
}
