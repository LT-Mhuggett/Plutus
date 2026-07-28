using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.SharedKernel;

namespace Plutus.Entities.Models
{
    public enum JobStatus : byte { Running = 0, Succeeded = 1, Failed = 2 }

    /// <summary>
    /// WP13.3 background-job run record. GLOBAL (not tenant-owned) — TenantId is plain data
    /// (null = platform-wide) so a heartbeat can write from any context without the tenant guard.
    /// One row per tracked invocation; retention keeps the latest per (JobName, TenantId).
    /// </summary>
    public class JobRun
    {
        public Guid Id { get; set; }
        public string JobName { get; set; }
        public Guid? TenantId { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public JobStatus Status { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// WP13.3 operator alert (the dashboard's feed). GLOBAL. Keyed by <see cref="AlertKey"/> so a
    /// recurring problem updates one row (LastSeen + Occurrences) instead of spamming — cleared
    /// when the job recovers. Kind is "silent" | "failed".
    /// </summary>
    public class OperatorAlert
    {
        public Guid Id { get; set; }
        public string AlertKey { get; set; }
        public string JobName { get; set; }
        public Guid? TenantId { get; set; }
        public string Kind { get; set; }
        public string Message { get; set; }
        public DateTime RaisedAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
        public DateTime? ClearedAtUtc { get; set; }
        public int Occurrences { get; set; }
    }

    /// <summary>DB ops for JobRuns (global table) — static so both the heartbeat impl and tests use
    /// one path. Caller saves.</summary>
    public static class JobRunStore
    {
        public static JobRun Begin(MySqlDbContext db, Guid id, string jobName, Guid? tenantId, DateTime startedUtc)
        {
            var run = new JobRun { Id = id, JobName = jobName, TenantId = tenantId, StartedAtUtc = startedUtc, Status = JobStatus.Running };
            db.JobRuns.Add(run);
            return run;
        }

        public static async Task FinishAsync(MySqlDbContext db, Guid id, JobStatus status, string detail, CancellationToken ct = default)
        {
            var run = await db.JobRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run == null) return;
            run.Status = status;
            run.FinishedAtUtc = DateTime.UtcNow;
            run.Detail = detail;
        }
    }

    /// <summary>Keyed upsert for OperatorAlerts (global table): raising the same key updates one
    /// row (LastSeen + Occurrences) rather than spamming; clearing stamps ClearedAtUtc. Caller
    /// saves. This is the "alerts once, no repeat spam" guarantee.</summary>
    public static class OperatorAlertStore
    {
        public static async Task RaiseAsync(
            MySqlDbContext db, string alertKey, string jobName, Guid? tenantId, string kind, string message, DateTime nowUtc, CancellationToken ct = default)
        {
            var row = await db.OperatorAlerts.FirstOrDefaultAsync(a => a.AlertKey == alertKey, ct);
            if (row == null)
            {
                db.OperatorAlerts.Add(new OperatorAlert
                {
                    Id = Uuid7.New(), AlertKey = alertKey, JobName = jobName, TenantId = tenantId,
                    Kind = kind, Message = message, RaisedAtUtc = nowUtc, LastSeenAtUtc = nowUtc, Occurrences = 1,
                });
                return;
            }
            row.LastSeenAtUtc = nowUtc;
            row.Kind = kind;
            row.Message = message;
            if (row.ClearedAtUtc != null) { row.ClearedAtUtc = null; row.RaisedAtUtc = nowUtc; row.Occurrences = 1; } // re-raise after recovery
            else row.Occurrences++;
        }

        public static async Task ClearAsync(MySqlDbContext db, string alertKey, DateTime nowUtc, CancellationToken ct = default)
        {
            var row = await db.OperatorAlerts.FirstOrDefaultAsync(a => a.AlertKey == alertKey && a.ClearedAtUtc == null, ct);
            if (row == null) return;
            row.ClearedAtUtc = nowUtc;
        }
    }
}
