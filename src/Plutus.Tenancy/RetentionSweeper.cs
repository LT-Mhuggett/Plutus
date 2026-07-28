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
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;

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

                        // WP13.2 request-health retention (35 days). Deletes bypass the tenant
                        // guard, so the scoped context purges all tenants via IgnoreQueryFilters.
                        var statsPurged = await RequestStatsRetention.PurgeAsync(
                            db, DateTime.UtcNow.AddDays(-RequestStatsRetention.RetentionDays), stoppingToken);
                        if (statsPurged > 0) _logger.LogInformation("Retention: purged {Count} request-stat rows.", statsPurged);

                        // WP13.3 job-cadence evaluation + JobRuns retention (JobRuns/OperatorAlerts
                        // are global tables — no tenant guard). The alerter is registered by the host.
                        var alerter = scope.ServiceProvider.GetService<IOperatorAlerter>();
                        if (alerter != null)
                        {
                            await JobMonitor.EvaluateAsync(db, alerter, DateTime.UtcNow, stoppingToken);
                            await ConnectorMonitor.EvaluateAsync(db, alerter, DateTime.UtcNow, stoppingToken); // WP17.1
                        }
                        await JobRunsRetention.PurgeAsync(
                            db, DateTime.UtcNow.AddDays(-JobRunsRetention.RetentionDays), stoppingToken);

                        // WP13.1 counted-metrics sweep. Needs a fresh UNSCOPED context (the scoped
                        // one resolves to a single tenant) so it can write every tenant's counts.
                        // MySQL-only: like the rest of the sweeper it's inert on the SQLite dev
                        // host, and a second context on that host's shared in-memory connection
                        // would contend with the request pipeline.
                        var opts = scope.ServiceProvider.GetService<DbContextOptions<MySqlDbContext>>();
                        if (opts != null && db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            var heartbeat = scope.ServiceProvider.GetService<IJobHeartbeat>();
                            var day = DateOnly.FromDateTime(DateTime.UtcNow);
                            async Task Sweep(System.Threading.CancellationToken c)
                            {
                                using var meterDb = new MySqlDbContext(opts, new FixedTenantContext(Guid.Empty));
                                await UsageSweep.RunAsync(meterDb, day, c);
                            }
                            if (heartbeat != null) await heartbeat.TrackAsync("usage-sweep", null, Sweep, stoppingToken);
                            else await Sweep(stoppingToken);

                            // WP16.1/16.2 commercial-ops sweeps — cross-tenant, unscoped, alerter-backed
                            // (raise once / clear on recovery). Wrapped in a heartbeat so their own
                            // silence is monitored. Inert without an alerter.
                            if (alerter != null)
                            {
                                async Task CommercialSweep(System.Threading.CancellationToken c)
                                {
                                    using var opsDb = new MySqlDbContext(opts, new FixedTenantContext(Guid.Empty));
                                    await ChurnSweep.EvaluateAsync(opsDb, alerter, DateTime.UtcNow, c);
                                    await RenewalSweep.EvaluateAsync(opsDb, alerter, DateTime.UtcNow, c);
                                    await ComplianceSweep.EvaluateAsync(opsDb, alerter, DateTime.UtcNow, c); // WP18.2 dpa-missing
                                }
                                if (heartbeat != null) await heartbeat.TrackAsync("commercial-sweep", null, CommercialSweep, stoppingToken);
                                else await CommercialSweep(stoppingToken);
                            }
                        }
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
