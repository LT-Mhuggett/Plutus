using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Tenancy;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP13.3 job monitoring: the cadence monitor raises exactly one keyed alert for a silent or
/// failed job (and clears healthy ones), the alert store never spams (keyed upsert), and JobRuns
/// retention always keeps the latest run per job.
/// </summary>
public class JobMonitoringTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc);

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
               new FixedTenantContext(Guid.Empty)) { CurrentUser = "jobmon-test" };

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        return conn;
    }

    private static JobRun Run(string job, Guid? tenant, DateTime finished, JobStatus status = JobStatus.Succeeded) => new()
    {
        Id = Uuid7.New(), JobName = job, TenantId = tenant,
        StartedAtUtc = finished.AddSeconds(-1), FinishedAtUtc = finished, Status = status,
    };

    private sealed class FakeAlerter : IOperatorAlerter
    {
        public readonly List<(string Key, string Kind)> Raised = new();
        public readonly List<string> Cleared = new();
        public Task RaiseAsync(string alertKey, string jobName, Guid? tenantId, string kind, string message, CancellationToken ct = default)
        { Raised.Add((alertKey, kind)); return Task.CompletedTask; }
        public Task ClearAsync(string alertKey, CancellationToken ct = default)
        { Cleared.Add(alertKey); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Evaluate_raises_once_for_silent_or_failed_and_clears_healthy()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            db.JobRuns.Add(Run("woo-poll", A, Now.AddHours(-1)));          // silent (>30m)
            db.JobRuns.Add(Run("woo-poll", B, Now.AddMinutes(-5)));        // healthy
            db.JobRuns.Add(Run("usage-sweep", null, Now.AddMinutes(-1), JobStatus.Failed)); // failed
            db.SaveChanges();
        }

        var alerter = new FakeAlerter();
        using (var db = Ctx(conn))
            await JobMonitor.EvaluateAsync(db, alerter, Now);

        Assert.Equal(2, alerter.Raised.Count); // exactly one per silent + failed job
        Assert.Contains((JobCadence.AlertKey("woo-poll", A), "silent"), alerter.Raised);
        Assert.Contains((JobCadence.AlertKey("usage-sweep", null), "failed"), alerter.Raised);
        Assert.Contains(JobCadence.AlertKey("woo-poll", B), alerter.Cleared); // healthy → cleared
    }

    [Fact]
    public async Task Alert_store_is_keyed_and_does_not_spam()
    {
        using var conn = Open();
        var key = JobCadence.AlertKey("woo-poll", A);

        for (int i = 0; i < 3; i++)
            using (var db = Ctx(conn))
            {
                await OperatorAlertStore.RaiseAsync(db, key, "woo-poll", A, "silent", "still down", Now.AddMinutes(i));
                await db.SaveChangesAsync();
            }

        using (var check = Ctx(conn))
        {
            var row = check.OperatorAlerts.Single(a => a.AlertKey == key);
            Assert.Equal(3, row.Occurrences);       // one row, bumped — not three rows
            Assert.Null(row.ClearedAtUtc);
        }

        // clear, then a fresh raise re-opens it (Occurrences reset)
        using (var db = Ctx(conn)) { await OperatorAlertStore.ClearAsync(db, key, Now.AddMinutes(5)); await db.SaveChangesAsync(); }
        using (var db = Ctx(conn)) { await OperatorAlertStore.RaiseAsync(db, key, "woo-poll", A, "silent", "down again", Now.AddMinutes(6)); await db.SaveChangesAsync(); }
        using (var check = Ctx(conn))
        {
            var row = check.OperatorAlerts.Single(a => a.AlertKey == key);
            Assert.Null(row.ClearedAtUtc);
            Assert.Equal(1, row.Occurrences);       // re-raised fresh
        }
    }

    [Fact]
    public async Task Retention_keeps_latest_run_per_job_even_if_old()
    {
        using var conn = Open();
        using (var db = Ctx(conn))
        {
            db.JobRuns.Add(Run("woo-poll", A, Now.AddDays(-50))); // ancient, non-latest → purge
            db.JobRuns.Add(Run("woo-poll", A, Now.AddDays(-40))); // latest (still old) → KEEP
            db.SaveChanges();
        }

        int purged;
        using (var db = Ctx(conn))
            purged = await JobRunsRetention.PurgeAsync(db, Now.AddDays(-JobRunsRetention.RetentionDays));

        Assert.Equal(1, purged);
        using var check = Ctx(conn);
        var remaining = check.JobRuns.Single();
        Assert.Equal(Now.AddDays(-40), remaining.FinishedAtUtc); // the latest survived
    }
}
