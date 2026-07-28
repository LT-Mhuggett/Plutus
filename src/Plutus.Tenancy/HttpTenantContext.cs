using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// Request-scoped <see cref="ITenantContext"/> resolved from the authenticated principal's
    /// claims — <c>tid</c> (tenant), <c>did</c> (device), <c>scope</c> — never from the request
    /// body or URL (D2). Falls back to the Kapow tenant when no <c>tid</c> claim is present, so
    /// the platform behaves as single-tenant until real tenants are provisioned (T1.2).
    /// Platform-admin principals run unscoped (<see cref="Guid.Empty"/>) so provisioning can
    /// see across tenants.
    /// </summary>
    public sealed class HttpTenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

        private ClaimsPrincipal? User => _accessor.HttpContext?.User;

        public bool IsPlatformAdmin
        {
            get
            {
                // HasClaim (not FindFirst) — a multi-scope token has several "scope" claims and
                // FindFirst would only inspect the first, missing platform-admin behind another.
                return (User?.HasClaim("scope", "platform-admin") ?? false)
                    || (User?.IsInRole("platform-admin") ?? false);
            }
        }

        public Guid TenantId
        {
            get
            {
                if (IsPlatformAdmin) return Guid.Empty; // unscoped: query filter shows all tenants
                var tid = User?.FindFirst("tid")?.Value;
                return Guid.TryParse(tid, out var id) ? id : WellKnownTenants.Kapow;
            }
        }

        public Guid? DeviceId
        {
            get
            {
                var did = User?.FindFirst("did")?.Value;
                return Guid.TryParse(did, out var id) ? id : (Guid?)null;
            }
        }
    }
}
