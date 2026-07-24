using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Plutus.Identity
{
    /// <summary>
    /// TEST-ENVIRONMENT ONLY: replaces B2C JWT validation on [Authorize] endpoints with
    /// the TestTokenAuth scheme while the B2C tenant is unavailable (architecture §11).
    ///
    /// Behaviour when active (DISABLE_AUTH_DEV_ONLY=true AND TEST_TOKEN_SECRET set):
    /// requests must carry "Authorization: Bearer &lt;token&gt;" issued by the login endpoint;
    /// anything else gets 401. A real authentication gate, not a blanket bypass — but the
    /// tokens are only as strong as the test secret. Removed at the OIDC swap.
    /// </summary>
    public class DevAuthBypassEvaluator : IPolicyEvaluator
    {
        private readonly string _secret;

        public DevAuthBypassEvaluator(IConfiguration configuration)
        {
            _secret = configuration["TEST_TOKEN_SECRET"];
        }

        public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
        {
            var header = context.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ") ? header["Bearer ".Length..] : null;
            var payload = TestTokenAuth.Validate(token, _secret ?? string.Empty);

            if (payload == null)
                return Task.FromResult(AuthenticateResult.Fail("Missing or invalid test token."));

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, payload.EmployeeId.ToString()),
                new Claim(ClaimTypes.Name, payload.Name ?? "unknown"),
                // Controllers read this claim in their constructors (SetCurrentUser).
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", payload.EmployeeId.ToString()),
            }, "TestToken");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "TestToken")));
        }

        public Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object resource)
            => Task.FromResult(authenticationResult.Succeeded
                ? PolicyAuthorizationResult.Success()
                : PolicyAuthorizationResult.Challenge());
    }
}
