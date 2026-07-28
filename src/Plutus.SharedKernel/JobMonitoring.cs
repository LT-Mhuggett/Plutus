using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.SharedKernel;

/// <summary>
/// WP13.3 heartbeat seam: wrap a background job so its start / finish / outcome lands in JobRuns.
/// Callers depend only on this; the implementation writes the rows. Recording never changes the
/// job's result — a heartbeat failure is swallowed so monitoring can't break the work it watches.
/// </summary>
public interface IJobHeartbeat
{
    Task TrackAsync(string jobName, Guid? tenantId, Func<CancellationToken, Task> work, CancellationToken ct = default);
}

/// <summary>
/// WP13.3 alerting seam. Default implementation logs at Error and upserts an OperatorAlerts row
/// keyed by <paramref name="alertKey"/> — so a persistently-broken job updates one row (bumping a
/// counter + LastSeen) rather than spamming new alerts. Email/webhook adapters come later.
/// </summary>
public interface IOperatorAlerter
{
    Task RaiseAsync(string alertKey, string jobName, Guid? tenantId, string kind, string message, CancellationToken ct = default);
    Task ClearAsync(string alertKey, CancellationToken ct = default);
}

/// <summary>
/// Code-defined expected-cadence registry (like the permission catalogue): the maximum time a job
/// may be silent before it's treated as stalled. Per-tenant jobs (e.g. "woo-poll") share one entry
/// keyed by job name; the monitor evaluates each (jobName, tenantId) pair seen in JobRuns against it.
/// </summary>
public static class JobCadence
{
    public static readonly IReadOnlyDictionary<string, TimeSpan> MaxSilence =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            ["retention-sweeper"] = TimeSpan.FromHours(2),
            ["usage-sweep"] = TimeSpan.FromHours(2),
            ["commercial-sweep"] = TimeSpan.FromHours(26), // WP16.1/16.2 daily-ish churn + renewal pass

            ["woo-poll"] = TimeSpan.FromMinutes(30),
            ["request-stats-flush"] = TimeSpan.FromMinutes(5),
            ["backup"] = TimeSpan.FromHours(26),   // nightly + slack
        };

    /// <summary>The stable alert key for a (job, tenant) pair — "platform" stands in for null.</summary>
    public static string AlertKey(string jobName, Guid? tenantId) =>
        $"job-stall:{jobName}:{(tenantId.HasValue ? tenantId.Value.ToString() : "platform")}";
}
