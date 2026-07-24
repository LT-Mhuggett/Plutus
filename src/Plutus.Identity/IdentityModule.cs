using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.SharedKernel;

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
            // WP3.1 RBAC: effective-permission resolution + the dynamic "perm:*" policy
            // provider. Auth-scheme agnostic — registered under B2C and the test scheme alike.
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "RBAC requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new EffectivePermissionsService(ctx);
            });
            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

            // TEST-ENV ONLY: flag-gated. With DISABLE_AUTH_DEV_ONLY set, B2C validation is
            // replaced by the scope-aware PlutusTokenAuthHandler and real scope-based policies
            // (T1.2). Off by default so production B2C behaviour is unchanged.
            if (configuration.GetValue<bool>("DISABLE_AUTH_DEV_ONLY"))
            {
                System.Console.WriteLine("!!! DISABLE_AUTH_DEV_ONLY active — B2C replaced by PlutusToken bearer + scope policies. TEST USE ONLY. !!!");

                services.AddAuthentication(o =>
                {
                    // Make the test scheme the default so [Authorize] evaluates against it
                    // (overrides the JwtBearer default set by Microsoft.Identity.Web).
                    o.DefaultAuthenticateScheme = PlutusTokenAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = PlutusTokenAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, PlutusTokenAuthHandler>(PlutusTokenAuthHandler.SchemeName, null);

                services.AddAuthorizationBuilder()
                    .AddPolicy(PlutusPolicies.PlatformAdmin, p => p.RequireAuthenticatedUser().RequireClaim("scope", PlutusPolicies.PlatformAdmin))
                    .AddPolicy(PlutusPolicies.PortalTillsEnrol, p => p.RequireAuthenticatedUser().RequireClaim("scope", PlutusPolicies.PortalTillsEnrol))
                    .AddPolicy(PlutusPolicies.Device, p => p.RequireAuthenticatedUser().RequireClaim("scope", PlutusPolicies.Device))
                    .AddPolicy(PlutusPolicies.SalesIngest, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                        ctx.User.HasClaim("scope", PlutusPolicies.Device) || ctx.User.HasClaim("scope", PlutusPolicies.PosSell)));
            }
            return services;
        }
    }
}
