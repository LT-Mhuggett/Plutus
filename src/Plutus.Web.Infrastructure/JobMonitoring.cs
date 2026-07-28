#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.Monitoring
{
    /// <summary>
    /// WP13.3 IJobHeartbeat default impl. Writes a Running JobRun (its own scope, committed so it's
    /// visible mid-run), runs the work, then updates the row to Succeeded/Failed. Recording is
    /// best-effort — a heartbeat write failure is logged and swallowed so it can't break the job —
    /// but a Failed job's exception is re-thrown after being recorded. Inert on the SQLite dev host
    /// (RepositoryContext there is not a MySqlDbContext), where it simply runs the work.
    /// </summary>
    public sealed class JobHeartbeat : IJobHeartbeat
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<JobHeartbeat> _logger;

        public JobHeartbeat(IServiceScopeFactory scopes, ILogger<JobHeartbeat> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task TrackAsync(string jobName, Guid? tenantId, Func<CancellationToken, Task> work, CancellationToken ct = default)
        {
            var id = Uuid7.New();
            await WriteAsync(db => { JobRunStore.Begin(db, id, jobName, tenantId, DateTime.UtcNow); return Task.CompletedTask; });
            try
            {
                await work(ct);
                await WriteAsync(db => JobRunStore.FinishAsync(db, id, JobStatus.Succeeded, null, ct));
            }
            catch (Exception ex)
            {
                await WriteAsync(db => JobRunStore.FinishAsync(db, id, JobStatus.Failed, Trunc(ex.Message, 500), ct));
                throw; // the job's failure must propagate — we only recorded it
            }
        }

        private async Task WriteAsync(Func<MySqlDbContext, Task> op)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                if (db == null) return; // dev/SQLite host — no JobRuns to write
                db.CurrentUser = "job-heartbeat";
                await op(db);
                await db.SaveChangesAsync();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "JobHeartbeat write failed (non-fatal)."); }
        }

        private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }

    /// <summary>WP13.3 IOperatorAlerter default impl: log at Error + keyed upsert into OperatorAlerts
    /// (via OperatorAlertStore). Inert on the SQLite dev host.</summary>
    public sealed class OperatorAlerter : IOperatorAlerter
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<OperatorAlerter> _logger;

        public OperatorAlerter(IServiceScopeFactory scopes, ILogger<OperatorAlerter> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task RaiseAsync(string alertKey, string jobName, Guid? tenantId, string kind, string message, CancellationToken ct = default)
        {
            _logger.LogError("Operator alert [{Kind}] {Job} tenant={Tenant}: {Message}", kind, jobName, tenantId, message);
            await WriteAsync(db => OperatorAlertStore.RaiseAsync(db, alertKey, jobName, tenantId, kind, message, DateTime.UtcNow, ct));
        }

        public async Task ClearAsync(string alertKey, CancellationToken ct = default) =>
            await WriteAsync(db => OperatorAlertStore.ClearAsync(db, alertKey, DateTime.UtcNow, ct));

        private async Task WriteAsync(Func<MySqlDbContext, Task> op)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                if (db == null) return;
                db.CurrentUser = "operator-alerter";
                await op(db);
                await db.SaveChangesAsync();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "OperatorAlerter write failed (non-fatal)."); }
        }
    }

    /// <summary>WP17.1 IConnectorHealth default impl: keyed upsert into ConnectorRuns (via
    /// ConnectorRunStore). Best-effort — a write failure is logged and swallowed so it can never
    /// break the connector it monitors. Inert on the SQLite dev host.</summary>
    public sealed class ConnectorHealth : IConnectorHealth
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ConnectorHealth> _logger;

        public ConnectorHealth(IServiceScopeFactory scopes, ILogger<ConnectorHealth> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public async Task RecordAsync(string connector, Guid tenantId, ConnectorActivity activity, bool ok, string error = null, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
                if (db == null) return; // dev/SQLite host — no ConnectorRuns to write
                db.CurrentUser = "connector-health";
                await ConnectorRunStore.RecordAsync(db, connector, tenantId, activity, ok, error, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "ConnectorHealth write failed (non-fatal)."); }
        }
    }

    public static class JobMonitoringRegistration
    {
        public static IServiceCollection AddPlutusJobMonitoring(this IServiceCollection services)
        {
            services.AddSingleton<IJobHeartbeat, JobHeartbeat>();
            services.AddSingleton<IOperatorAlerter, OperatorAlerter>();
            services.AddSingleton<IConnectorHealth, ConnectorHealth>();
            return services;
        }
    }
}
