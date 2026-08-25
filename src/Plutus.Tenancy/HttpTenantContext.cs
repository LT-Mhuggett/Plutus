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
                // ⚠⚠ `tid` WINS OVER PLATFORM-ADMIN, AND THE ORDER OF THESE TWO LINES IS THE FIX.
                // Matt, 2026-08-25: *"I logged into the 'New store' and I could see Kapow data, e.g.
                // number of tills."* He had impersonated the new tenant, and this returned
                // `Guid.Empty` anyway because the platform-admin check came first — which the query
                // filter reads as "show every tenant". So impersonation, whose entire purpose is to
                // NARROW an operator to one shop, was removing scoping altogether.
                //
                // ⚠⚠ AND THE BOUNDARY MIDDLEWARE CANNOT CATCH IT, BY CONSTRUCTION.
                // `OperatorBoundaryMiddleware` 403s a platform-admin from tenant data only while
                // they carry NO `tid` and are NOT impersonating. Impersonating stamps a `tid`, so
                // the middleware deliberately steps aside — it is explicitly relying on this
                // property to do the scoping from that point on. Two controls, each assuming the
                // other holds the line.
                //
                // ⚠ A PLAIN platform-admin (no `tid`) is still unscoped, and must be: provisioning
                // has to write across tenants and the operator console lists all of them.
                var tid = User?.FindFirst("tid")?.Value;
                if (Guid.TryParse(tid, out var id)) return id;
                if (IsPlatformAdmin) return Guid.Empty; // unscoped: query filter shows all tenants
                return WellKnownTenants.Kapow;
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
