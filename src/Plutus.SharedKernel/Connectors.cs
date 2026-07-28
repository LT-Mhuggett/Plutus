using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.SharedKernel;

/// <summary>The kinds of connector activity whose recency we track (WP17.1).</summary>
public enum ConnectorActivity { Poll, Webhook, Outbound }

/// <summary>
/// WP17.1 connector-health seam — the generalisation of the Woo-specific heartbeats. Any connector
/// (webstore, and future ones) records its poll / webhook / outbound activity here; the default
/// impl upserts a ConnectorRun row. Recording is best-effort and never throws into the caller's
/// path (a health-write failure must not break the connector). Inert on the SQLite dev host.
/// </summary>
public interface IConnectorHealth
{
    Task RecordAsync(string connector, Guid tenantId, ConnectorActivity activity, bool ok, string? error = null, CancellationToken ct = default);
}

/// <summary>
/// Code-defined connector registry (like <see cref="JobCadence"/>): the maximum time a connector
/// may be silent (no activity of any kind) before it's treated as stalled, and the error-streak at
/// which it's treated as failing. The monitor pass reads this to raise/clear WP13.3 alerts.
/// </summary>
public static class ConnectorRegistry
{
    public const string Woo = "woo";

    public static readonly IReadOnlyDictionary<string, TimeSpan> MaxSilence =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            [Woo] = TimeSpan.FromMinutes(45),   // polls every ~20m (WP6) — 45m of silence is a stall
            ["dummy"] = TimeSpan.FromMinutes(5), // sample/test connector
        };

    /// <summary>Consecutive failures at or above this raise an error alert regardless of silence.</summary>
    public const int ErrorStreakAlertThreshold = 5;

    public static TimeSpan SilenceWindow(string connector) =>
        MaxSilence.TryGetValue(connector, out var w) ? w : TimeSpan.FromHours(1);

    public static string SilentAlertKey(string connector, Guid tenantId) => $"connector-silent:{connector}:{tenantId}";
    public static string ErrorAlertKey(string connector, Guid tenantId) => $"connector-error:{connector}:{tenantId}";
}

/// <summary>
/// WP17.1 reusable connector base: encapsulates health recording + a journal-and-retry outbound
/// wrapper so a new connector inherits monitoring, retry and journalling for free (the DoD's
/// "&lt;50 lines" bar). Woo is the first consumer of the health seam; new connectors subclass this.
/// </summary>
public abstract class ConnectorBase
{
    protected IConnectorHealth Health { get; }
    protected ConnectorBase(IConnectorHealth health) => Health = health;

    /// <summary>The connector's stable code name (matches the ConnectorRegistry key).</summary>
    public abstract string Connector { get; }

    protected Task RecordPollAsync(Guid tenantId, bool ok, string? error = null, CancellationToken ct = default) =>
        Safe(() => Health.RecordAsync(Connector, tenantId, ConnectorActivity.Poll, ok, error, ct));

    protected Task RecordWebhookAsync(Guid tenantId, bool ok, string? error = null, CancellationToken ct = default) =>
        Safe(() => Health.RecordAsync(Connector, tenantId, ConnectorActivity.Webhook, ok, error, ct));

    /// <summary>
    /// Run an outbound send with bounded retry, recording health and journalling the outcome. On
    /// success (possibly after retries) records ok + journals sent; after the last failure records
    /// the error + journals failed. The journal callback is the connector's own sink (Woo's DB log,
    /// a queue, …) — the base doesn't dictate storage, only the shape.
    /// </summary>
    protected async Task<bool> SendWithRetryAsync(
        Guid tenantId, Func<CancellationToken, Task> send, Func<bool, string?, Task>? journal,
        int maxAttempts = 3, CancellationToken ct = default)
    {
        string? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await send(ct);
                await Safe(() => Health.RecordAsync(Connector, tenantId, ConnectorActivity.Outbound, true, null, ct));
                if (journal != null) await journal(true, null);
                return true;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                if (attempt < maxAttempts) continue; // retry
            }
        }
        await Safe(() => Health.RecordAsync(Connector, tenantId, ConnectorActivity.Outbound, false, lastError, ct));
        if (journal != null) await journal(false, lastError);
        return false;
    }

    private static async Task Safe(Func<Task> op) { try { await op(); } catch { /* health is best-effort */ } }
}
