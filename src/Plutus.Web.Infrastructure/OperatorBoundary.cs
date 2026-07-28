#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.Security
{
    /// <summary>
    /// OP1: a platform operator must never see CLIENT data. A principal whose only authority is
    /// <c>platform-admin</c> (no tenant identity, not impersonating) is 403'd from every
    /// tenant-scoped API — the operator reaches client data only through the audited impersonation
    /// flow (which stamps a <c>tid</c>), never directly. Without this, plain <c>[Authorize]</c>
    /// tenant reads leak cross-tenant because the ambient tenant context of an operator resolves to
    /// <see cref="Guid.Empty"/> (the query filter's cross-tenant bypass).
    ///
    /// Allow-list = the operator's own surfaces + auth + harmless metadata; everything else under
    /// <c>/api/</c> is client data and blocked. Escape hatch <c>OPERATOR_BOUNDARY_DISABLED=true</c>
    /// bypasses it without a redeploy (security control on the live login path).
    /// </summary>
    public sealed class OperatorBoundaryMiddleware
    {
        // Prefixes an operator MAY hit (case-insensitive). Everything else under /api/ is tenant data.
        private static readonly string[] Allowed =
        {
            "/api/v1/platform",       // the operator surfaces
            "/api/v1/tenants",        // tenant lifecycle (platform-admin gated)
            "/api/v1/announcements",  // any-auth; operator sees global announcements
            "/api/auth",              // login / token
            "/api/v1/payments/gateway/catalogue", // harmless provider metadata
        };

        private readonly RequestDelegate _next;
        private readonly bool _disabled;

        public OperatorBoundaryMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<OperatorBoundaryMiddleware> logger)
        {
            _next = next;
            _disabled = configuration.GetValue<bool>("OPERATOR_BOUNDARY_DISABLED");
            if (_disabled) logger.LogWarning("OPERATOR_BOUNDARY_DISABLED=true — operators are NOT blocked from tenant data.");
        }

        public async Task Invoke(HttpContext context)
        {
            var user = context.User;
            if (!_disabled
                && user?.Identity?.IsAuthenticated == true
                && user.HasClaim("scope", PlutusPolicies.PlatformAdmin)  // HasClaim, not FindFirst — multi-scope tokens
                && user.FindFirst("tid") == null                          // no tenant identity…
                && user.FindFirst("impersonating") == null                // …and not impersonating
                && IsTenantDataPath(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"detail\":\"Operators access client data via impersonation only.\"}");
                return;
            }

            await _next(context);
        }

        private static bool IsTenantDataPath(PathString path)
        {
            var p = path.Value ?? string.Empty;
            if (!p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false; // non-API (SPA, swagger) passes
            foreach (var a in Allowed)
                if (p.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return false;     // operator-legitimate
            return true;                                                                   // everything else = client data
        }
    }

    public static class OperatorBoundaryRegistration
    {
        public static IApplicationBuilder UsePlutusOperatorBoundary(this IApplicationBuilder app) =>
            app.UseMiddleware<OperatorBoundaryMiddleware>();
    }
}
