#nullable disable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.RateLimiting
{
    public sealed class TenantRateLimitOptions
    {
        /// <summary>Requests/second allowed per tenant when no ratelimit.rps entitlement is set.</summary>
        public int DefaultRps { get; set; } = 50;
        /// <summary>How long a tenant's resolved rps is cached before the entitlement is re-read
        /// (this is the max lag for a WP14.2 override to take effect — no restart needed).</summary>
        public int CacheTtlSeconds { get; set; } = 30;
    }

    /// <summary>
    /// WP13.5 per-tenant rate limiter over the framework's partitioned limiter (no new package).
    /// The partition key is (tenantId, rps) so a tenant floods only its own bucket (noisy-neighbour
    /// isolation) AND a changed rps (entitlement/override) spins a fresh bucket within the cache TTL
    /// — no restart. The rps is resolved from the ratelimit.rps entitlement, cached; the first touch
    /// uses the default while the async refresh runs.
    /// </summary>
    public sealed class TenantRateLimiter : IDisposable
    {
        private readonly PartitionedRateLimiter<Guid> _rl;
        private readonly ConcurrentDictionary<Guid, (long Rps, long ExpTicks)> _cache = new();
        private readonly IServiceScopeFactory _scopes;
        private readonly TenantRateLimitOptions _opts;

        public TenantRateLimiter(IServiceScopeFactory scopes, TenantRateLimitOptions opts)
        {
            _scopes = scopes;
            _opts = opts;
            _rl = PartitionedRateLimiter.Create<Guid, (Guid, long)>(tid =>
            {
                var rps = RpsFor(tid);
                return RateLimitPartition.GetFixedWindowLimiter((tid, rps), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = (int)Math.Max(1, rps),
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                });
            });
        }

        public ValueTask<RateLimitLease> AcquireAsync(Guid tenantId) => _rl.AcquireAsync(tenantId, 1);

        private long RpsFor(Guid tid)
        {
            var now = DateTime.UtcNow.Ticks;
            if (_cache.TryGetValue(tid, out var c) && c.ExpTicks > now) return c.Rps;
            _ = RefreshAsync(tid);                         // fire-and-forget; warms/refreshes the cache
            return c.Rps > 0 ? c.Rps : _opts.DefaultRps;   // best-known, else the plan default
        }

        private async Task RefreshAsync(Guid tid)
        {
            long rps = _opts.DefaultRps;
            try
            {
                using var scope = _scopes.CreateScope();
                var ent = scope.ServiceProvider.GetService<IEntitlementService>();
                if (ent != null && await ent.GetLimitAsync(tid, Entitlements.RateLimitRps) is long v && v > 0) rps = v;
            }
            catch { /* dev host / lookup failure → default */ }
            _cache[tid] = (rps, DateTime.UtcNow.AddSeconds(_opts.CacheTtlSeconds).Ticks);
        }

        public void Dispose() => _rl.Dispose();
    }

    /// <summary>WP13.5 middleware: throttle per tenant AFTER auth (tenant resolved) and BEFORE the
    /// request-health middleware so rejected requests don't skew stats. Platform-admin and
    /// device/till tokens are exempt — a busy till must never be throttled (D16).</summary>
    public sealed class TenantRateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TenantRateLimiter _limiter;

        public TenantRateLimitMiddleware(RequestDelegate next, TenantRateLimiter limiter)
        {
            _next = next;
            _limiter = limiter;
        }

        public async Task Invoke(HttpContext context)
        {
            var tenant = context.RequestServices.GetService<ITenantContext>();
            var tid = tenant?.TenantId ?? Guid.Empty;
            // Exempt platform-admin (Guid.Empty) and any device/till token (has a DeviceId).
            if (tid == Guid.Empty || tenant?.DeviceId != null) { await _next(context); return; }

            using var lease = await _limiter.AcquireAsync(tid);
            if (lease.IsAcquired) { await _next(context); return; }

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsync("Rate limit exceeded for this tenant.");
        }
    }

    public static class TenantRateLimitRegistration
    {
        public static IServiceCollection AddPlutusTenantRateLimiting(this IServiceCollection services, IConfiguration config = null)
        {
            var opts = new TenantRateLimitOptions();
            if (int.TryParse(config?["RATE_LIMIT_DEFAULT_RPS"], out var rps) && rps > 0) opts.DefaultRps = rps;
            services.AddSingleton(opts);
            services.AddSingleton<TenantRateLimiter>();
            return services;
        }

        public static IApplicationBuilder UsePlutusTenantRateLimiting(this IApplicationBuilder app) =>
            app.UseMiddleware<TenantRateLimitMiddleware>();
    }
}
