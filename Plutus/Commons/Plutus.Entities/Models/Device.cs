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
    }
}
