using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Identity;

/// <summary>
/// Phase 9 (IdP swap): the provider-agnostic bridge that lets a real IdP (Entra External ID or
/// Keycloak) authenticate a user while authorization stays exactly as it was. A real IdP only
/// proves identity — its JWT carries a verified email but none of Plutus's RBAC scopes. This
/// transformation runs AFTER JWT validation, matches the token's email to a Plutus user
/// (<see cref="WebCredential"/> — the login identity), and injects the SAME claim set the
/// HMAC login path emits: <c>NameIdentifier</c> + <c>objectidentifier</c> (the Plutus
/// EmployeeId every controller reads), the RBAC-derived <c>scope</c> claims, and the legacy
/// B2C <c>scp</c> API scopes. So swapping IdP changes the token source, never the API contract.
///
/// It deliberately does NOTHING for device/enrolment tokens (validated by
/// <see cref="PlutusTokenAuthHandler"/>, a different scheme) and for the test HMAC scheme, whose
/// tokens already carry their scopes — that keeps till client-credentials unaffected by the swap.
/// </summary>
public sealed class RbacClaimsTransformation : IClaimsTransformation
{
    /// <summary>Idempotency marker: <see cref="IClaimsTransformation"/> may run more than once
    /// per request, so a second pass must be a no-op.</summary>
    private const string MarkerClaim = "plutus:rbac-mapped";

    /// <summary>The JwtBearer scheme name (JwtBearerDefaults.AuthenticationScheme). Kept as a
    /// literal so this module needs no JwtBearer package reference — only IdP JWT principals,
    /// authenticated under this scheme, are mapped; device/test HMAC principals are skipped.</summary>
    private const string JwtBearerScheme = "Bearer";

    private static readonly string[] EmailClaimTypes =
    {
        "email", ClaimTypes.Email, "preferred_username", "upn", "unique_name",
    };

    private readonly MySqlDbContext _db;
    private readonly EffectivePermissionsService _permissions;
    private readonly IConfiguration _config;
    private readonly ILogger<RbacClaimsTransformation> _logger;

    public RbacClaimsTransformation(
        MySqlDbContext db,
        EffectivePermissionsService permissions,
        IConfiguration config,
        ILogger<RbacClaimsTransformation> logger)
    {
        _db = db;
        _permissions = permissions;
        _config = config;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity id || !id.IsAuthenticated)
            return principal;

        // Only IdP (JWT) principals need mapping. Device tokens (scheme "PlutusToken") and the
        // test HMAC scheme already carry NameIdentifier + scope claims — leave them untouched.
        if (id.AuthenticationType != JwtBearerScheme)
            return principal;

        if (id.HasClaim(c => c.Type == MarkerClaim))
            return principal;
        id.AddClaim(new Claim(MarkerClaim, "1"));

        var email = EmailClaimTypes
            .Select(t => principal.FindFirst(t)?.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("IdP token carried no email claim; principal left unauthorized.");
            return principal;
        }

        var normalized = email.Trim().ToLowerInvariant();
        var cred = await _db.WebCredentials.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Email.ToLower() == normalized);
        if (cred == null)
        {
            // Authenticated by the IdP, but not a known Plutus user → no scopes (403 on any
            // gated endpoint). Logged so an unmapped hire is diagnosable.
            _logger.LogWarning("IdP-authenticated email {Email} has no Plutus user; no scopes granted.", normalized);
            return principal;
        }

        var employeeId = cred.EmployeeId;

        // The IdP's subject must NOT masquerade as the Plutus EmployeeId. JwtBearer is configured
        // with MapInboundClaims=false so `sub` is not auto-mapped to NameIdentifier, but strip any
        // stray NameIdentifier defensively before stamping the real one.
        foreach (var stray in id.FindAll(ClaimTypes.NameIdentifier).ToList())
            id.RemoveClaim(stray);

        id.AddClaim(new Claim(ClaimTypes.NameIdentifier, employeeId.ToString()));
        // Legacy controllers read the objectidentifier claim in their base ctor (SetCurrentUser).
        id.AddClaim(new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", employeeId.ToString()));

        foreach (var scope in await _permissions.ResolveLoginScopesAsync(employeeId, DateTime.Now))
            id.AddClaim(new Claim("scope", scope));

        // Stand in for the B2C API scopes the legacy [RequiredScope] filters demand (claim "scp").
        var read = _config["OpenAPI:Scopes:APIRead:Name"];
        var write = _config["OpenAPI:Scopes:APIWrite:Name"];
        var apiScopes = new[] { read, write }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        id.AddClaim(new Claim("scp", apiScopes.Length > 0 ? string.Join(' ', apiScopes) : "API.Read API.Write"));

        return principal;
    }
}
