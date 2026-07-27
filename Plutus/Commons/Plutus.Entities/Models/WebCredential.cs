using System;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// Web login credentials (used by the /auth login endpoint). Server-side only, mapped to the
    /// pre-existing WebCredentials table. Global/unscoped: login looks a user up by email before a
    /// tenant is known. Password is PBKDF2-hashed (base64 in HashedPassword/Salt) — never plaintext.
    /// </summary>
    public class WebCredential
    {
        public string Email { get; set; }        // PK
        public Guid EmployeeId { get; set; }
        public string HashedPassword { get; set; } // base64(PBKDF2 hash)
        public string Salt { get; set; }           // base64(salt)
    }
}
