using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Plutus.Identity
{
    /// <summary>
    /// Composition entry point for the Identity module (T0.2 `AddPlutusModule` pattern).
    /// The host calls this once; module internals are not referenced directly.
    /// </summary>
    public static class IdentityModule
    {
        public static IServiceCollection AddPlutusIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            // TEST-ENV ONLY: flag-gated TestTokenAuth replaces B2C validation. With
            // TEST_TOKEN_SECRET set this REQUIRES a bearer token issued by the login
            // endpoint; off by default so production B2C behaviour is unchanged.
            if (configuration.GetValue<bool>("DISABLE_AUTH_DEV_ONLY"))
            {
                System.Console.WriteLine("!!! DISABLE_AUTH_DEV_ONLY active — B2C replaced by TestTokenAuth bearer tokens. TEST USE ONLY. !!!");
                services.AddSingleton<IPolicyEvaluator, DevAuthBypassEvaluator>();
            }
            return services;
        }
    }
}
