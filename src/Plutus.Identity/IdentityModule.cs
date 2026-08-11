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
            // ⚠ ROLE RECONCILE ON BOOT (2026-08-11). Adding a permission is two things: the
            // CATALOGUE entry compiles into the binary, but the GRANT — which role holds it — is a
            // ROW in each tenant's database, and a compiler cannot write rows. Until now that row
            // arrived only when somebody remembered to run `Plutus.SeedMigrator rbac`, and a
            // forgotten step whose failure is a polite refusal is a step that gets forgotten.
            //
            // ⚠ Additive, idempotent, and never fatal — see `RolePermissionReconciler`. The tool
            // still exists and is still the way to run it against a database out of band.
            services.AddHostedService<RolePermissionReconciler>();

            // WP3.1 RBAC: effective-permission resolution + the dynamic "perm:*" policy
            // provider. Auth-scheme agnostic — registered under every provider.
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "RBAC requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new EffectivePermissionsService(ctx);
            });
            // FE9.1 password set / reset / invite tokens.
            services.AddScoped(sp =>
            {
                var ctx = sp.GetRequiredService<RepositoryContext>() as MySqlDbContext
                    ?? throw new InvalidOperationException(
                        "Password reset requires the MySqlDbContext (server build), not the SQLite dev context.");
                return new PasswordResetService(ctx);
            });
            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

            // T1.2 scope-based policies. These read the `scope` claim and are IDENTICAL across
            // providers — under `test` the HMAC handler emits the claims, under `entra`/`keycloak`
            // the RbacClaimsTransformation does. Registered unconditionally so the scheme can be
            // swapped (Phase 9) without touching authorization. The scheme itself is selected by
            // the host's ConfigurePlutusAuthentication (it owns the B2C/JwtBearer packages).
            services.AddAuthorizationBuilder()
                .AddPolicy(PlutusPolicies.PlatformAdmin, p => p.RequireAuthenticatedUser().RequireClaim("scope", PlutusPolicies.PlatformAdmin))
                .AddPolicy(PlutusPolicies.PortalTillsEnrol, p => p.RequireAuthenticatedUser().RequireClaim("scope", PlutusPolicies.PortalTillsEnrol))
                .AddPolicy(PlutusPolicies.Device, p => p.RequireAuthenticatedUser().RequireClaim("scope", PlutusPolicies.Device))
                .AddPolicy(PlutusPolicies.SalesIngest, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                    ctx.User.HasClaim("scope", PlutusPolicies.Device) || ctx.User.HasClaim("scope", PlutusPolicies.PosSell)))
                .AddPolicy(PlutusPolicies.TillsName, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                    ctx.User.HasClaim("scope", PlutusPolicies.PortalTillsEnrol) || ctx.User.HasClaim("scope", PlutusPolicies.Device)))
                .AddPolicy(PlutusPolicies.TillsUnenrolRequest, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                    ctx.User.HasClaim("scope", PlutusPolicies.PortalTillsEnrol) || ctx.User.HasClaim("scope", PlutusPolicies.Device)));

            return services;
        }
    }
}
