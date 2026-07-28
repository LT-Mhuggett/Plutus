#nullable disable

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Plutus.Infrastructure.Monitoring
{
    /// <summary>
    /// WP14.1: logs every request made by an impersonation token (actor operator + target user +
    /// method + path) so the full support session is reconstructable from the logs. Cheap — one
    /// claim check per request, a log line only when impersonating.
    /// </summary>
    public sealed class ImpersonationAuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ImpersonationAuditMiddleware> _logger;

        public ImpersonationAuditMiddleware(RequestDelegate next, ILogger<ImpersonationAuditMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.User?.HasClaim("impersonating", "true") == true)
                _logger.LogWarning(
                    "IMPERSONATED request: actor={Actor} target={Target} {Method} {Path}",
                    context.User.FindFirst("actor")?.Value,
                    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    context.Request.Method, context.Request.Path);

            await _next(context);
        }
    }

    public static class ImpersonationAuditRegistration
    {
        public static IApplicationBuilder UsePlutusImpersonationAudit(this IApplicationBuilder app) =>
            app.UseMiddleware<ImpersonationAuditMiddleware>();
    }
}
