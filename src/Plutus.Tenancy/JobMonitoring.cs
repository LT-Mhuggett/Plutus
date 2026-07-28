#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>
    /// WP13.3 cadence evaluation (a RetentionSweeper pass). For every (JobName, TenantId) that has
    /// ever run, take its latest run and: if it FAILED, or it's been silent longer than the job's
    /// registered max-silence window, raise a keyed alert (once — the alerter upserts); otherwise
    /// clear it. So killing one tenant's Woo poll raises exactly one "job-stall:woo-poll:<tenant>"
    /// alert within a sweep, and it clears when the poll resumes. Jobs with no cadence entry are
    /// ignored.
    /// </summary>
    public static class JobMonitor
    {
        public static async Task EvaluateAsync(MySqlDbContext db, IOperatorAlerter alerter, DateTime nowUtc, CancellationToken ct = default)
        {
            // JobRuns is a global table (no tenant filter). Retention keeps this bounded.
            var runs = await db.JobRuns.AsNoTracking().ToListAsync(ct);
            foreach (var g in runs.GroupBy(r => (r.JobName, r.TenantId)))
            {
                if (!JobCadence.MaxSilence.TryGetValue(g.Key.JobName, out var window)) continue;

                var latest = g.OrderByDescending(r => r.StartedAtUtc).First();
                var key = JobCadence.AlertKey(latest.JobName, latest.TenantId);
                var reference = latest.FinishedAtUtc ?? latest.StartedAtUtc;

                if (latest.Status == JobStatus.Failed)
                    await alerter.RaiseAsync(key, latest.JobName, latest.TenantId, "failed",
                        $"Last run of '{latest.JobName}' failed: {latest.Detail}", ct);
                else if (nowUtc - reference > window)
                    await alerter.RaiseAsync(key, latest.JobName, latest.TenantId, "silent",
                        $"'{latest.JobName}' has been silent since {reference:u} (max {window}).", ct);
                else
                    await alerter.ClearAsync(key, ct);
            }
        }
    }

    /// <summary>
    /// WP17.1 connector-health evaluation (a RetentionSweeper pass, sibling of JobMonitor). For each
    /// (Connector, TenantId) in ConnectorRuns: raise a keyed alert if the connector has been silent
    /// longer than its registered window OR its error streak has crossed the threshold (naming the
    /// connector + tenant); clear it when it recovers. Generalises the woo-poll heartbeat to every
    /// connector for free.
    /// </summary>
    public static class ConnectorMonitor
    {
        public static async Task EvaluateAsync(MySqlDbContext db, IOperatorAlerter alerter, DateTime nowUtc, CancellationToken ct = default)
        {
            var runs = await db.ConnectorRuns.AsNoTracking().ToListAsync(ct);
            foreach (var r in runs)
            {
                var silenceKey = ConnectorRegistry.SilentAlertKey(r.Connector, r.TenantId);
                var window = ConnectorRegistry.SilenceWindow(r.Connector);
                var last = r.LastActivityAtUtc;
                if (last == null || nowUtc - last.Value > window)
                    await alerter.RaiseAsync(silenceKey, r.Connector, r.TenantId, "connector-silent",
                        $"Connector '{r.Connector}' silent for tenant {r.TenantId} since {(last?.ToString("u") ?? "never")} (max {window}).", ct);
                else
                    await alerter.ClearAsync(silenceKey, ct);

                var errorKey = ConnectorRegistry.ErrorAlertKey(r.Connector, r.TenantId);
                if (r.ErrorStreak >= ConnectorRegistry.ErrorStreakAlertThreshold)
                    await alerter.RaiseAsync(errorKey, r.Connector, r.TenantId, "connector-error",
                        $"Connector '{r.Connector}' has {r.ErrorStreak} consecutive failures for tenant {r.TenantId}: {r.LastError}", ct);
                else
                    await alerter.ClearAsync(errorKey, ct);
            }
        }
    }

    /// <summary>WP13.3 retention: drop JobRuns older than the window, but always KEEP the latest
    /// run per (JobName, TenantId) so cadence evaluation and the dashboard never lose a job's last
    /// state, however long ago it ran.</summary>
    public static class JobRunsRetention
    {
        public const int RetentionDays = 35;

        public static async Task<int> PurgeAsync(MySqlDbContext db, DateTime cutoffUtc, CancellationToken ct = default)
        {
            var all = await db.JobRuns.ToListAsync(ct);
            var keep = new HashSet<Guid>(all
                .GroupBy(r => (r.JobName, r.TenantId))
                .Select(grp => grp.OrderByDescending(r => r.StartedAtUtc).First().Id));

            var stale = all.Where(r => r.StartedAtUtc < cutoffUtc && !keep.Contains(r.Id)).ToList();
            if (stale.Count == 0) return 0;
            db.CurrentUser = "jobruns-retention";
            db.JobRuns.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
            return stale.Count;
        }
    }
}
