using System;

namespace Plutus.Contracts.Client;

// ─────────────────────────────────────────────────────────────────────────────
// WP1: enrolment, device tokens and the till-facing read contracts. Mirrors
// src/Plutus.Tenancy/{EnrolmentService.cs, Controllers/TillsController.cs, Controllers/TokensController.cs}
// and Controllers/StoresController.cs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>POST /api/v1/tills/enrol — AllowAnonymous, rate-limited.</summary>
public sealed record EnrolRequest(string EnrolmentCode);

/// <summary>410 Gone if the code was reused, expired or is unknown. ⚠ ClientSecret goes to
/// platform secure storage, NEVER the local database file.</summary>
public sealed record EnrolResult(Guid DeviceId, string ClientSecret, Guid TillId, Guid TenantId);

/// <summary>POST /api/v1/tokens/device — 401 if the secret is wrong or the device is revoked.</summary>
public sealed record DeviceTokenRequest(Guid DeviceId, string ClientSecret);

public sealed record DeviceTokenResult(string AccessToken, int ExpiresInSeconds);

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
    VatRatePointDto[] Rates);

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
