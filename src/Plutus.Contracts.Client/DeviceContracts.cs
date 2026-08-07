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
