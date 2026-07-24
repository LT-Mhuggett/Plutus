#nullable disable

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plutus.SharedKernel;

namespace Plutus.Identity
{
    /// <summary>
    /// WP3.1 enforcement plumbing: `[Authorize(Policy = "perm:portal.users.manage")]` gates an
    /// endpoint on an RBAC permission. The requirement passes when the caller holds the
    /// permission ANYWHERE in the tenant (union semantics; the coarse gate for list/manage
    /// surfaces) — endpoints that operate on a specific node re-check at that node via
    /// <see cref="EffectivePermissionsService.ResolveAsync"/>. `platform-admin` scope always
    /// passes. WP3.2's admin controllers are the consumers.
    /// </summary>
    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public const string PolicyPrefix = "perm:";
        public PermissionRequirement(string code) => Code = code;
        public string Code { get; }
    }

    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IServiceProvider _services;
        public PermissionAuthorizationHandler(IServiceProvider services) => _services = services;

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User?.HasClaim("scope", "platform-admin") == true)
            {
                context.Succeed(requirement);
                return;
            }

            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out var id)) return;

            // Scoped resolution: the handler is a singleton, the DbContext is per-request.
            using var scope = _services.CreateScope();
            var permissions = scope.ServiceProvider.GetRequiredService<EffectivePermissionsService>();
            if (await permissions.HasAnywhereAsync(id, requirement.Code, DateTime.Now))
                context.Succeed(requirement);
        }
    }

    /// <summary>Builds "perm:*" policies on demand; every other policy name falls through to
    /// the default provider (where the T1.2 scope policies live).</summary>
    public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
            => _fallback = new DefaultAuthorizationPolicyProvider(options);

        public Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
        {
            if (policyName != null && policyName.StartsWith(PermissionRequirement.PolicyPrefix, StringComparison.Ordinal))
            {
                var code = policyName.Substring(PermissionRequirement.PolicyPrefix.Length);
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(code))
                    .Build();
                return Task.FromResult(policy);
            }
            return _fallback.GetPolicyAsync(policyName);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
    }
}
