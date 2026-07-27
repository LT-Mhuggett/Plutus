namespace Plutus.Identity;

/// <summary>
/// Phase 9: decides which authentication scheme a bearer token belongs to when a real IdP
/// (entra/keycloak) is active. A compact HMAC device/enrolment token has TWO dot-segments
/// (body.sig) and must go to <see cref="PlutusTokenAuthHandler"/>; a real OIDC JWT has THREE
/// (header.payload.sig) and goes to the IdP's JwtBearer. This is the single rule that keeps
/// till client-credentials working under every provider (the phase DoD), so it is extracted
/// here to be unit-tested independently of the DI wiring that consumes it.
/// </summary>
public static class AuthSchemeRouter
{
    /// <summary>JwtBearerDefaults.AuthenticationScheme, kept as a literal to avoid a package ref.</summary>
    public const string JwtBearerScheme = "Bearer";

    /// <summary>The scheme to forward to, given the raw Authorization header value. Non-bearer or
    /// JWT-shaped tokens fall through to the IdP (which then 401-challenges an absent/garbage token).</summary>
    public static string Select(string? authorizationHeader)
    {
        if (!string.IsNullOrEmpty(authorizationHeader) &&
            authorizationHeader.StartsWith("Bearer ", System.StringComparison.Ordinal))
        {
            var token = authorizationHeader["Bearer ".Length..];
            if (token.Split('.').Length == 2) return PlutusTokenAuthHandler.SchemeName;
        }
        return JwtBearerScheme;
    }
}
