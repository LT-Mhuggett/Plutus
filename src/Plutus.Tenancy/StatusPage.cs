using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;

namespace Plutus.Tenancy
{
    public sealed class StatusPageOptions
    {
        /// <summary>Where to atomically write status.json. Unset ⇒ the writer is inert (dev/test,
        /// and any host without a status vhost). Set on the server to the status vhost's docroot.</summary>
        public string OutputPath { get; set; }
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>Last-hour aggregate 5xx rate above this (percent) marks the box "degraded".</summary>
        public double DegradedErrorRatePct { get; set; } = 5.0;
    }

    /// <summary>
    /// WP15.2 status-page feeder: every interval it writes a small status.json snapshot (overall
    /// state + last-hour aggregate error rate + any open Incident announcements) to a file served
    /// by a separate static vhost. Because the file is served by the vhost — NOT the app — the
    /// status page stays reachable when the backend is down; the static page flags the snapshot as
    /// stale once generatedAtUtc falls more than two minutes behind, which is exactly what a dead
    /// backend produces. Inert unless STATUS_JSON_PATH is set (so dev/test never touch the disk).
    /// Uses a fresh unscoped (Guid.Empty) context so the read spans every tenant.
    /// </summary>
    public sealed class StatusPageWriter : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly StatusPageOptions _opts;
        private readonly ILogger<StatusPageWriter> _logger;

        public StatusPageWriter(IServiceScopeFactory scopes, StatusPageOptions opts, ILogger<StatusPageWriter> logger)
        {
            _scopes = scopes;
            _opts = opts;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_opts.OutputPath)) return; // inert without a target
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await WriteOnceAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _logger.LogError(ex, "StatusPageWriter tick failed; retrying next interval."); }

                try { await Task.Delay(_opts.Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task WriteOnceAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var opts = scope.ServiceProvider.GetService<DbContextOptions<MySqlDbContext>>();
            if (opts == null) return; // SQLite dev host — nothing to serve
            using var db = new MySqlDbContext(opts, new FixedTenantContext(Guid.Empty));

            var now = DateTime.UtcNow;
            var since = now.AddHours(-1);
            var stats = await db.TenantRequestStats.AsNoTracking()
                .Where(x => x.MinuteUtc >= since)
                .Select(x => new { x.Count, x.Err5xx }).ToListAsync(ct);
            long count = stats.Sum(x => x.Count), err5xx = stats.Sum(x => x.Err5xx);
            var errorRatePct = count == 0 ? 0.0 : Math.Round(err5xx * 100.0 / count, 2);

            var openIncidents = await db.PlatformAnnouncements.AsNoTracking()
                .Where(a => a.Severity == AnnouncementSeverity.Incident && a.StartsAtUtc <= now && a.EndsAtUtc >= now)
                .OrderBy(a => a.StartsAtUtc)
                .Select(a => new { a.Title, since = a.StartsAtUtc })
                .ToListAsync(ct);

            var status = openIncidents.Count > 0 ? "incident"
                : errorRatePct > _opts.DegradedErrorRatePct ? "degraded"
                : "operational";

            var payload = new
            {
                service = "plutus",
                generatedAtUtc = now,
                status,
                errorRatePct,
                requestsLastHour = count,
                openIncidents,
            };

            // Atomic replace: write a temp file then move over the target so a reader never sees a
            // half-written file.
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _opts.OutputPath + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _opts.OutputPath, overwrite: true);
        }
    }
}
