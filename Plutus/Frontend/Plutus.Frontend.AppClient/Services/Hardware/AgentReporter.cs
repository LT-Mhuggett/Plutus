using System;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;

namespace Plutus.Frontend.AppClient.Services.Hardware
{
    /// <summary>
    /// FE3.0 — tell the platform what this till's hardware agent is, so the portal's Locations page
    /// can see the fleet's printers.
    ///
    /// ⚠⚠ THE WEB TILL HAS DONE THIS SINCE FE3.0 AND MAUI NEVER DID — an unrecorded parity gap found
    /// 2026-08-17. MAUI already talks to the agent on the printing path (`TillAgentPrinting`), so it
    /// knew the version all along and simply never said. The visible symptom was a portal fleet list
    /// where every MAUI till read *"agent unknown"* while the browser tills beside them reported
    /// properly — which looks like the agent is missing rather than the reporting.
    ///
    /// ⚠ WHEN to send is `Client.Core.AgentReporting`, shared with the web till rather than reasoned
    /// out again here — see its header, and till-design.md C2.
    /// </summary>
    internal static class AgentReporter
    {
        private static AgentSnapshot _lastSent;
        private static DateTime? _lastSentAtUtc;
        private static readonly object Lock = new();

        /// <summary>
        /// Poll the agent and report if the shared rule says so. Called from the 60s cadence.
        /// Never throws.
        /// </summary>
        public static async Task ReportAsync(PlutusApiClient api, Guid deviceId, CancellationToken ct = default)
        {
            try
            {
                // ⚠ `ResolveAsync` returns null when the till is not PAIRED with an agent, which is a
                // different thing from there being no agent — but from the platform's point of view
                // both mean "this till has no working agent", and that is the fact worth recording.
                var status = await Printing.TillAgentPrinting.ResolveAsync().ConfigureAwait(false);

                var snapshot = status is null
                    ? AgentSnapshot.None
                    : new AgentSnapshot(status.AgentVersion, status.PrinterName, status.PrinterOnline);

                AgentSnapshot lastSent;
                DateTime? lastAt;
                lock (Lock) { lastSent = _lastSent; lastAt = _lastSentAtUtc; }

                var now = DateTime.UtcNow;
                if (!AgentReporting.ShouldSend(snapshot, lastSent, lastAt, now)) return;

                var ok = await api.ReportAgentStatusAsync(
                    deviceId, snapshot.AgentVersion, snapshot.PrinterName, snapshot.PrinterOnline, ct)
                    .ConfigureAwait(false);

                // ⚠⚠ ONLY ON SUCCESS. Recording a failed send would suppress the retry and lose the
                // reading for six hours — so a till whose printer went offline during an outage would
                // still be shown as healthy for the rest of the morning.
                if (!ok) return;

                lock (Lock) { _lastSent = snapshot; _lastSentAtUtc = now; }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // ⚠ Telemetry never disturbs a till. A reporter that cannot reach the platform is a
                // stale row on a portal page, not a shop that cannot sell.
                Analytics.CrashLog.Write("AgentReporter.ReportAsync", ex);
            }
        }

        /// <summary>⚠ Test seam and re-enrolment reset — a till that becomes a different device must
        /// not suppress the first report for its new identity.</summary>
        public static void Reset()
        {
            lock (Lock) { _lastSent = null; _lastSentAtUtc = null; }
        }
    }
}
