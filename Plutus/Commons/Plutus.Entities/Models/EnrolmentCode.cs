using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// A single-use till enrolment code (T1.2). Server-side only (mapped on MySqlDbContext),
    /// and deliberately UNSCOPED by tenant query filter: the anonymous enrol endpoint looks a
    /// code up by its hash before any tenant is known, then learns the tenant from the row.
    /// The plaintext code is never stored — only its SHA-256 hash.
    /// </summary>
    public class EnrolmentCode
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid TillId { get; set; }
        /// <summary>SHA-256 of the Crockford base32 code (32 bytes).</summary>
        public byte[] CodeHash { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
