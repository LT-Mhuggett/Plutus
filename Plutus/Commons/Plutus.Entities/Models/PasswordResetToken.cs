using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// FE9.1 password-reset (and first-time "set your password" invite) token.
    ///
    /// The token is a random secret emailed to the user; only its SHA-256 <see cref="TokenHash"/>
    /// is stored, so a database or backup leak cannot be turned into account takeovers — the same
    /// discipline as till enrolment codes. Single-use: <see cref="UsedAtUtc"/> is stamped on
    /// completion, and issuing/consuming one invalidates that user's other outstanding tokens.
    /// </summary>
    public class PasswordResetToken
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>The Employee the token resets.</summary>
        public Guid UserId { get; set; }
        /// <summary>The email it was sent to (the WebCredential key at issue time).</summary>
        public string Email { get; set; }
        public byte[] TokenHash { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        /// <summary>True when this was an invite ("set your password") rather than a reset — the
        /// same machinery, different email wording.</summary>
        public bool IsInvite { get; set; }
        /// <summary>Who asked for it: the admin's user id, or null for self-service.</summary>
        public Guid? RequestedBy { get; set; }
    }
}
