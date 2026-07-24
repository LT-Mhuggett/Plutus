using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Plutus.Identity
{
    /// <summary>
    /// TEST-ENVIRONMENT token scheme (paired with <see cref="DevAuthBypassEvaluator"/>):
    /// compact HMAC-SHA256-signed tokens issued by the login endpoint and required by
    /// every [Authorize] endpoint while TEST_TOKEN_SECRET is configured. Replaced by real
    /// B2C/OIDC JWTs once the tenant question (architecture §11) is resolved — this class is
    /// the swappable seam.
    /// </summary>
    public static class TestTokenAuth
    {
        public class TokenPayload
        {
            public Guid EmployeeId { get; set; }
            public string Name { get; set; }
            public long Exp { get; set; } // unix seconds
        }

        private static string B64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromB64Url(string s)
        {
            var p = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '='));
        }

        public static string Issue(TokenPayload payload, string secret)
        {
            var body = B64Url(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
            return $"{body}.{sig}";
        }

        /// <returns>The payload when the token is valid and unexpired; otherwise null.</returns>
        public static TokenPayload Validate(string token, string secret)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            var parts = token.Split('.');
            if (parts.Length != 2) return null;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
            byte[] provided;
            try { provided = FromB64Url(parts[1]); } catch { return null; }
            if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return null;

            try
            {
                var payload = JsonConvert.DeserializeObject<TokenPayload>(Encoding.UTF8.GetString(FromB64Url(parts[0])));
                if (payload == null || payload.Exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
                return payload;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>PBKDF2 check matching the NatApp/ClientUI Password helper
        /// (Rfc2898DeriveBytes, 101010 iterations, 64-byte hash, SHA-1 default).</summary>
        public static bool VerifyPassword(string password, byte[] salt, byte[] expectedHash)
        {
#pragma warning disable SYSLIB0041 // matches the legacy hashes stored by the till
            using var kdf = new Rfc2898DeriveBytes(password, salt);
#pragma warning restore SYSLIB0041
            kdf.IterationCount = 101010;
            var computed = kdf.GetBytes(64);
            return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
        }
    }
}
