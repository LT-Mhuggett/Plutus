using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Models;

namespace Plutus.Tenancy
{
    public sealed class RetentionOptions
    {
        public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
        /// <summary>Used/expired enrolment codes older than this are purged.</summary>
        public int EnrolmentCodeRetentionDays { get; set; } = 30;
    }

    /// <summary>
    /// WP10.4 retention + offboarding sweeper (same shape as the outbox dispatcher): every interval
    /// it executes due tenant deletions (Pending schedules past their grace window) and purges
    /// ephemeral data (expired enrolment codes). Sales are never purged (6-year legal retention).
    /// Inert on the SQLite dev host. Destructive deletes are guarded in TenantLifecycleService
    /// (never the founding tenant) and audited.
    /// </summary>
    public sealed class RetentionSweeper : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly RetentionOptions _opts;
        private readonly ILogger<RetentionSweeper> _logger;

        public RetentionSweeper(IServiceScopeFactory scopes, RetentionOptions opts, ILogger<RetentionSweeper> logger)
        {
            _scopes = scopes;
            _opts = opts;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                    if (db != null)
                    {
                        var lifecycle = new TenantLifecycleService(db);
                        await ExecuteDueDeletionsAsync(db, lifecycle, stoppingToken);
                        var purged = await lifecycle.PurgeExpiredEnrolmentCodesAsync(_opts.EnrolmentCodeRetentionDays, stoppingToken);
                        if (purged > 0) _logger.LogInformation("Retention: purged {Count} expired enrolment codes.", purged);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RetentionSweeper iteration failed; retrying next tick.");
                }

                try { await Task.Delay(_opts.Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task ExecuteDueDeletionsAsync(MySqlDbContext db, TenantLifecycleService lifecycle, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var due = await db.DeletionSchedules
                .Where(d => d.Status == TenantLifecycleService.DeletionPending && d.ExecuteAfterUtc <= now)
                .ToListAsync(ct);

            foreach (var schedule in due)
            {
                try
                {
                    var tables = await lifecycle.HardDeleteTenantAsync(schedule.TenantId, ct);
                    // The schedule row survives the tenant wipe (global platform record) and IS the
                    // audit trail — RequestedBy + ExecutedAtUtc. (No tenant-owned AuditLogs row: the
                    // tenant's audit table was just deleted, and the sweeper has no tenant context.)
                    db.CurrentUser = "retention-sweeper";
                    schedule.Status = TenantLifecycleService.DeletionExecuted;
                    schedule.ExecutedAtUtc = now;
                    await db.SaveChangesAsync(ct);
                    _logger.LogWarning("Retention: hard-deleted tenant {TenantId} ({Tables} tables).", schedule.TenantId, tables);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retention: deletion of tenant {TenantId} failed; will retry.", schedule.TenantId);
                }
            }
        }
    }
}
