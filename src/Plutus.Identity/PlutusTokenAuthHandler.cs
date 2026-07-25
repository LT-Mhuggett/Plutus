using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// TEST-ENVIRONMENT authentication (paired with the scope-based policies below): validates a
    /// Bearer token — either an operator token (TestTokenAuth) or a device token (T1.2) — against
    /// the shared HMAC secret, then emits the claims real authorization runs against: NameId /
    /// objectidentifier for operators, plus `scope` (one claim per space-delimited scope), `tid`
    /// and `did`. Unlike the old all-or-nothing DevAuthBypassEvaluator this is a real
    /// authentication handler, so [Authorize(Policy=...)] evaluates scopes normally. Replaced by
    /// B2C/OIDC at the production swap (this stays gated behind DISABLE_AUTH_DEV_ONLY).
    /// </summary>
    public sealed class PlutusTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "PlutusToken";
        private readonly string _secret;
        private readonly string _apiScopes;

        public PlutusTokenAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration)
            : base(options, logger, encoder)
        {
            _secret = configuration["TEST_TOKEN_SECRET"] ?? string.Empty;
            // The legacy CRUD controllers still carry Microsoft.Identity.Web [RequiredScope]
            // filters that demand the B2C API.Read / API.Write scopes (claim "scp"). In test
            // mode this handler REPLACES B2C, so it must also stand in for those scopes —
            // otherwise every generic /api/{Entity}/Index & write 403s. Values from config so
            // they track the B2C app registration; sensible fallback if unset.
            var read = configuration["OpenAPI:Scopes:APIRead:Name"];
            var write = configuration["OpenAPI:Scopes:APIWrite:Name"];
            var scopes = new[] { read, write }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            _apiScopes = scopes.Length > 0 ? string.Join(' ', scopes) : "API.Read API.Write";
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var token = header["Bearer ".Length..];
            var json = CompactToken.Validate(token, _secret);
            if (json == null) return Task.FromResult(AuthenticateResult.Fail("Invalid token signature."));

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Device tokens use "exp"; operator tokens (TestTokenAuth/Newtonsoft) use "Exp".
                if ((root.TryGetProperty("exp", out var expEl) || root.TryGetProperty("Exp", out expEl)) &&
                    expEl.TryGetInt64(out var exp) &&
                    exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    return Task.FromResult(AuthenticateResult.Fail("Token expired."));

                var identity = new ClaimsIdentity(SchemeName);

                if (root.TryGetProperty("did", out var didEl) && didEl.GetString() is { Length: > 0 } did)
                {
                    // Device token: tid + did + scope:"device".
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, did));
                    identity.AddClaim(new Claim("did", did));
                    if (root.TryGetProperty("tid", out var tidEl) && tidEl.GetString() is { Length: > 0 } tid)
                        identity.AddClaim(new Claim("tid", tid));
                    AddScopes(identity, root.TryGetProperty("scope", out var sc) ? sc.GetString() : "device");
                }
                else
                {
                    // Operator token (TestTokenAuth shape).
                    var empId = root.TryGetProperty("EmployeeId", out var e) ? e.GetString() : null;
                    if (!string.IsNullOrEmpty(empId))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, empId));
                        // Controllers read this claim in their base ctor (SetCurrentUser).
                        identity.AddClaim(new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", empId));
                    }
                    if (root.TryGetProperty("Name", out var n) && n.GetString() is { } name)
                        identity.AddClaim(new Claim(ClaimTypes.Name, name));
                    if (root.TryGetProperty("Tid", out var t) && t.GetString() is { Length: > 0 } tid)
                        identity.AddClaim(new Claim("tid", tid));
                    AddScopes(identity, root.TryGetProperty("Scope", out var scp) ? scp.GetString() : null);
                }

                // Stand in for the B2C API scopes the legacy [RequiredScope] filters demand
                // (claim type "scp", space-delimited). Independent of the "scope" claims above
                // that drive the T1.2 scope + WP3.1 perm policies.
                identity.AddClaim(new Claim("scp", _apiScopes));

                var principal = new ClaimsPrincipal(identity);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
            }
            catch (JsonException)
            {
                return Task.FromResult(AuthenticateResult.Fail("Malformed token payload."));
            }
        }

        private static void AddScopes(ClaimsIdentity identity, string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope)) return;
            foreach (var s in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                identity.AddClaim(new Claim("scope", s));
        }
    }
}
