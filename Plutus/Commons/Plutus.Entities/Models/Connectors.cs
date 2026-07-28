using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.SharedKernel;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP17.1 connector health — one row per (Connector, TenantId): when it last polled, took a
    /// webhook, pushed outbound, and its current error streak. GLOBAL (TenantId as data) so the
    /// health recorder can write from any context. This is the generalisation of the Woo-specific
    /// heartbeats: the same row feeds the WP13.3 alerter (silence / error-streak → operator alert)
    /// for every connector, and both the operator and tenant portals read it.
    /// </summary>
    public class ConnectorRun
    {
        public Guid Id { get; set; }
        public string Connector { get; set; }     // "woo", "dummy", …
        public Guid TenantId { get; set; }
        public DateTime? LastPollAtUtc { get; set; }
        public DateTime? LastWebhookAtUtc { get; set; }
        public DateTime? LastOutboundAtUtc { get; set; }
        public int ErrorStreak { get; set; }       // consecutive failures; reset to 0 on any success
        public string LastError { get; set; }
        public DateTime? LastErrorAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        /// <summary>The most recent activity of any kind — what "silence" is measured against.</summary>
        public DateTime? LastActivityAtUtc
        {
            get
            {
                DateTime? m = LastPollAtUtc;
                if (LastWebhookAtUtc > (m ?? DateTime.MinValue)) m = LastWebhookAtUtc;
                if (LastOutboundAtUtc > (m ?? DateTime.MinValue)) m = LastOutboundAtUtc;
                return m;
            }
        }
    }

    /// <summary>Keyed upsert for ConnectorRuns (global table). Stamps the activity's timestamp;
    /// a success resets the error streak, a failure bumps it and records the message. Caller saves.</summary>
    public static class ConnectorRunStore
    {
        public static async Task RecordAsync(
            MySqlDbContext db, string connector, Guid tenantId, ConnectorActivity activity, bool ok, string error, DateTime nowUtc, CancellationToken ct = default)
        {
            var row = await db.ConnectorRuns.FirstOrDefaultAsync(r => r.Connector == connector && r.TenantId == tenantId, ct);
            if (row == null)
                db.ConnectorRuns.Add(row = new ConnectorRun { Id = Uuid7.New(), Connector = connector, TenantId = tenantId });

            switch (activity)
            {
                case ConnectorActivity.Poll: row.LastPollAtUtc = nowUtc; break;
                case ConnectorActivity.Webhook: row.LastWebhookAtUtc = nowUtc; break;
                case ConnectorActivity.Outbound: row.LastOutboundAtUtc = nowUtc; break;
            }
            if (ok) row.ErrorStreak = 0;
            else { row.ErrorStreak++; row.LastError = Trunc(error, 500); row.LastErrorAtUtc = nowUtc; }
            row.UpdatedAtUtc = nowUtc;
        }

        private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
    }
}
