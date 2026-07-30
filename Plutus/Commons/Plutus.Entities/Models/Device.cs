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
    }
}
