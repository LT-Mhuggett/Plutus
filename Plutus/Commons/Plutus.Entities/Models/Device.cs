using System;

namespace Plutus.Entities.Models
{
    public enum DeviceStatus : byte
    {
        Active = 0,
        Revoked = 1,
        // WP6.2: the device has asked to be un-enrolled and is awaiting portal approval. It KEEPS
        // TRADING (token issuance still allowed) until an admin approves (→ Revoked) or rejects
        // (→ Active). A plain byte value on the existing tinyint column — no schema migration.
        PendingRemoval = 2,
    }

    /// <summary>
    /// An enrolled till device (T1.2). Server-side only (mapped on MySqlDbContext) and
    /// UNSCOPED by tenant query filter: the anonymous device-token endpoint looks a device up
    /// by Id before a tenant is known, then issues a token carrying the row's TenantId.
    /// The client secret is stored PBKDF2-hashed (same KDF params as the legacy till), never
    /// in plaintext. LastSeenSeq is the per-device monotonic sale sequence used by T1.4 ingest —
    /// kept here so the shared Till entity (and the MAUI Sqlite schema) stay untouched.
    /// </summary>
    public class Device
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid TillId { get; set; }
        public byte[] SecretHash { get; set; }
        public byte[] SecretSalt { get; set; }
        public DeviceStatus Status { get; set; }
        public long LastSeenSeq { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        // FE3.0 hardware-agent telemetry (Matt, 2026-07-31): the browser till polls its local
        // "Plutus Till Agent" (localhost) and forwards what it finds, so the portal's Locations
        // page can see which till PCs run which agent version and whether the printer is up.
        // Semantics: AgentReportedAtUtc null = this device has never reported (native till, or a
        // web till older than this feature); reported with AgentVersion null = the web till looked
        // and found NO agent installed.

        /// <summary>Agent version from its /status, e.g. "1.0.2"; null = no agent found.</summary>
        public string? AgentVersion { get; set; }
        /// <summary>The printer the agent is driving, as the agent names it.</summary>
        public string? AgentPrinterName { get; set; }
        /// <summary>Whether the agent said its printer was online at the last report.</summary>
        public bool? AgentPrinterOnline { get; set; }
        /// <summary>When the till last reported (regardless of what it found).</summary>
        public DateTime? AgentReportedAtUtc { get; set; }

        /// <summary>
        /// The till SOFTWARE's version, from its heartbeat — e.g. "1.13.0+6affd2e".
        ///
        /// ⚠ PERSISTED, deliberately, unlike the rest of presence. Live presence lives in memory
        /// because a write per till per minute for data that expires in five is the busiest and
        /// least useful write path in the system — but a VERSION is not that. It changes only on a
        /// deploy, and the question it answers ("is that till on the new build?") is asked most
        /// often about tills that are switched OFF right now. Keeping it in memory meant the fleet
        /// list forgot every version whenever the backend restarted, which is exactly when someone
        /// is looking. This is the same reason the agent's version is a column.
        /// </summary>
        public string? AppVersion { get; set; }

        /// <summary>When that version was last reported. ⚠ Null with a non-null AppVersion is
        /// impossible; null for both means this device has never sent a heartbeat — which today
        /// includes every WEB till, because the web till does not heartbeat at all.</summary>
        public DateTime? AppVersionReportedAtUtc { get; set; }

        // WP5 pull signals. A till on a shop LAN behind NAT cannot be reached, so anything the
        // platform wants it to do has to be something it ASKS about on its own cadence. These two
        // columns are that mailbox: set from the portal, collected on the next heartbeat.

        /// <summary>An operator pressed "sync now". ⚠ ONE-SHOT — cleared as it is delivered, or a
        /// till would re-sync on every beat forever.</summary>
        public bool SyncNow { get; set; }

        /// <summary>Lock the till's UI to a "contact your administrator" screen.
        /// ⚠ Locks SELLING, not the outbox: queued takings keep draining and local data is
        /// untouched. A lock that stranded a day's sales would be a worse remedy than the problem
        /// it is reaching for.</summary>
        public bool Locked { get; set; }

        /// <summary>Shown on the lock screen so an operator knows who to ring. Null = a generic
        /// message; the point is that a locked till is never a mystery.</summary>
        public string? LockReason { get; set; }
    }
}
