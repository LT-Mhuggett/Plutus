#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.Health
{
    /// <summary>Fixed latency histogram bounds in ms (no dependency). N bounds → N+1 buckets, the
    /// last being the overflow (> last bound).</summary>
    internal static class LatencyBuckets
    {
        public static readonly int[] Bounds = { 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000 };
    }

    /// <summary>Immutable snapshot of one (tenant, route-group) accumulator at flush time.</summary>
    public readonly record struct RequestStatSnapshot(
        Guid TenantId, string RouteGroup, long Count, long Err4xx, long Err5xx, int P50Ms, int P95Ms, int MaxMs);

    /// <summary>Per (tenant, route-group) running tally with a fixed-bucket latency histogram.
    /// A tiny lock keeps Record cheap (a few field writes) — well under the 1 ms overhead budget.</summary>
    internal sealed class RouteGroupTally
    {
        private readonly object _lock = new();
        private long _count, _e4, _e5;
        private int _max;
        private readonly long[] _hist = new long[LatencyBuckets.Bounds.Length + 1];

        public void Record(int elapsedMs, int status)
        {
            lock (_lock)
            {
                _count++;
                if (status >= 500) _e5++;
                else if (status >= 400) _e4++;
                if (elapsedMs > _max) _max = elapsedMs;
                int i = 0;
                while (i < LatencyBuckets.Bounds.Length && elapsedMs > LatencyBuckets.Bounds[i]) i++;
                _hist[i]++;
            }
        }

        public (long count, long e4, long e5, int p50, int p95, int max) Snapshot()
        {
            lock (_lock) { return (_count, _e4, _e5, Pct(0.50), Pct(0.95), _max); }
        }

        private int Pct(double p)
        {
            long total = 0; foreach (var h in _hist) total += h;
            if (total == 0) return 0;
            long threshold = (long)Math.Ceiling(p * total);
            long cum = 0;
            for (int i = 0; i < _hist.Length; i++)
            {
                cum += _hist[i];
                if (cum >= threshold)
                    return i < LatencyBuckets.Bounds.Length ? LatencyBuckets.Bounds[i] : _max; // overflow → observed max
            }
            return _max;
        }
    }

    /// <summary>
    /// WP13.2 in-memory request-health store (singleton). The middleware Records into it on every
    /// request; the flusher SnapshotAndResets it once a minute. A minute's worth of counts lives
    /// only here until flushed, so a restart loses at most the current partial minute — by design.
    /// </summary>
    public sealed class RequestStatsAccumulator
    {
        private ConcurrentDictionary<(Guid, string), RouteGroupTally> _map = new();

        public void Record(Guid tenantId, string routeGroup, int elapsedMs, int status) =>
            _map.GetOrAdd((tenantId, routeGroup), _ => new RouteGroupTally()).Record(elapsedMs, status);

        /// <summary>Atomically swap in a fresh map and aggregate the drained one.</summary>
        public IReadOnlyList<RequestStatSnapshot> SnapshotAndReset()
        {
            var old = Interlocked.Exchange(ref _map, new ConcurrentDictionary<(Guid, string), RouteGroupTally>());
            var result = new List<RequestStatSnapshot>(old.Count);
            foreach (var kv in old)
            {
                var (count, e4, e5, p50, p95, max) = kv.Value.Snapshot();
                if (count == 0) continue;
                result.Add(new RequestStatSnapshot(kv.Key.Item1, kv.Key.Item2, count, e4, e5, p50, p95, max));
            }
            return result;
        }
    }

    /// <summary>Maps a request path to a coarse, BOUNDED route group (≤2 segments, stops at the
    /// first id-like segment) so cardinality can't blow up from path parameters.</summary>
    public static class RouteGroups
    {
        public static string Of(string path)
        {
            if (string.IsNullOrEmpty(path)) return "root";
            var parts = new List<string>();
            foreach (var s in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (s.Equals("api", StringComparison.OrdinalIgnoreCase)) continue;
                if (s.Length >= 2 && (s[0] is 'v' or 'V') && char.IsDigit(s[1])) continue; // v1, v2…
                if (IsIdLike(s)) break;
                parts.Add(s.ToLowerInvariant());
                if (parts.Count == 2) break;
            }
            return parts.Count == 0 ? "root" : string.Join("/", parts);
        }

        private static bool IsIdLike(string s) =>
            s.Length > 24 || Guid.TryParse(s, out _) || long.TryParse(s, out _);
    }

    /// <summary>WP13.2 middleware: times every request and records elapsed + status against the
    /// caller's tenant. Placed AFTER auth so the tenant is resolved. The measurement is a
    /// stopwatch delta plus one dictionary Record — sub-millisecond.</summary>
    public sealed class RequestHealthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RequestStatsAccumulator _acc;

        public RequestHealthMiddleware(RequestDelegate next, RequestStatsAccumulator acc)
        {
            _next = next;
            _acc = acc;
        }

        public async Task Invoke(HttpContext context)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                await _next(context);
            }
            finally
            {
                var elapsedMs = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var tid = context.RequestServices.GetService<ITenantContext>()?.TenantId ?? Guid.Empty;
                _acc.Record(tid, RouteGroups.Of(context.Request.Path.Value), elapsedMs, context.Response.StatusCode);
            }
        }
    }

    /// <summary>Writes one minute's snapshots to TenantRequestStats and folds the request counts
    /// into the api.requests usage metric. Idempotent per minute (additive upsert) so a re-flush
    /// of the same minute never double-counts on a straight re-run. Given an UNSCOPED context.</summary>
    public static class RequestStatsFlush
    {
        public static async Task WriteAsync(
            MySqlDbContext db, IReadOnlyList<RequestStatSnapshot> snaps, DateTime minuteUtc, CancellationToken ct = default)
        {
            db.CurrentUser = "request-health-flush";
            foreach (var s in snaps)
            {
                var row = await db.TenantRequestStats.IgnoreQueryFilters().FirstOrDefaultAsync(
                    x => x.TenantId == s.TenantId && x.MinuteUtc == minuteUtc && x.RouteGroup == s.RouteGroup, ct);
                if (row == null)
                    db.TenantRequestStats.Add(new TenantRequestStats
                    {
                        TenantId = s.TenantId, MinuteUtc = minuteUtc, RouteGroup = s.RouteGroup,
                        Count = s.Count, Err4xx = s.Err4xx, Err5xx = s.Err5xx, P50Ms = s.P50Ms, P95Ms = s.P95Ms, MaxMs = s.MaxMs,
                    });
                else
                {
                    row.Count += s.Count; row.Err4xx += s.Err4xx; row.Err5xx += s.Err5xx;
                    row.P50Ms = s.P50Ms; row.P95Ms = s.P95Ms;
                    if (s.MaxMs > row.MaxMs) row.MaxMs = s.MaxMs;
                }
                await UsageMeter.AddAsync(db, s.TenantId, DateOnly.FromDateTime(minuteUtc), UsageMetrics.ApiRequests, s.Count, ct);
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Per-minute flush hosted service. Drains the accumulator every minute (bounding its
    /// memory) and, on the MySQL server build only, writes the snapshots via an UNSCOPED context so
    /// it can persist every tenant's rows. Inert (drains-and-drops) on the SQLite dev host.</summary>
    public sealed class RequestStatsFlusher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly RequestStatsAccumulator _acc;
        private readonly ILogger<RequestStatsFlusher> _logger;

        public RequestStatsFlusher(IServiceScopeFactory scopes, RequestStatsAccumulator acc, ILogger<RequestStatsFlusher> logger)
        {
            _scopes = scopes;
            _acc = acc;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (OperationCanceledException) { break; }

                try
                {
                    var minute = TruncateToMinute(DateTime.UtcNow);
                    var snaps = _acc.SnapshotAndReset(); // always drain to bound memory
                    if (snaps.Count == 0) continue;

                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                    var opts = scope.ServiceProvider.GetService<DbContextOptions<MySqlDbContext>>();
                    if (db == null || opts == null ||
                        db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) != true)
                        continue; // dev/SQLite host — drained above, nothing persisted

                    using var wdb = new MySqlDbContext(opts, new FixedTenantContext(Guid.Empty));
                    await RequestStatsFlush.WriteAsync(wdb, snaps, minute, stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogError(ex, "RequestStatsFlusher flush failed; continuing."); }
            }
        }

        private static DateTime TruncateToMinute(DateTime t) =>
            new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
    }

    public static class RequestHealthRegistration
    {
        public static IServiceCollection AddPlutusRequestHealth(this IServiceCollection services)
        {
            services.AddSingleton<RequestStatsAccumulator>();
            services.AddHostedService<RequestStatsFlusher>();
            return services;
        }

        public static IApplicationBuilder UsePlutusRequestHealth(this IApplicationBuilder app) =>
            app.UseMiddleware<RequestHealthMiddleware>();
    }
}
