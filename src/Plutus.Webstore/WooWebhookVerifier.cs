using System;
using System.Security.Cryptography;
using System.Text;

namespace Plutus.Webstore
{
    /// <summary>
    /// WP6.2 security boundary: verify a WooCommerce webhook's <c>X-WC-Webhook-Signature</c> header
    /// — base64(HMAC-SHA256(rawBody, secret)) — against the per-connection secret. The comparison is
    /// constant-time. An unsigned, mis-signed, or malformed delivery is rejected before any parsing,
    /// so a forged POST can never reach the ingest path.
    /// </summary>
    public static class WooWebhookVerifier
    {
        /// <summary>True iff <paramref name="signatureHeaderBase64"/> is the valid HMAC of the exact
        /// raw request body under <paramref name="secret"/>. Verify the RAW bytes as received —
        /// never a re-serialised object (whitespace/ordering would change the hash).</summary>
        public static bool Verify(string rawBody, string? signatureHeaderBase64, string? secret)
        {
            if (string.IsNullOrEmpty(signatureHeaderBase64) || string.IsNullOrEmpty(secret) || rawBody is null)
                return false;

            byte[] provided;
            try { provided = Convert.FromBase64String(signatureHeaderBase64); }
            catch (FormatException) { return false; }

            var computed = Compute(rawBody, secret);
            return CryptographicOperations.FixedTimeEquals(computed, provided);
        }

        /// <summary>The raw HMAC-SHA256 of the body under the secret (16→32 bytes). Exposed so tests
        /// (and any outbound signing) can produce the header WooCommerce would send:
        /// <c>Convert.ToBase64String(Compute(body, secret))</c>.</summary>
        public static byte[] Compute(string rawBody, string secret)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return h.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        }

        /// <summary>The base64 signature header WooCommerce would send for this body+secret.</summary>
        public static string Sign(string rawBody, string secret) => Convert.ToBase64String(Compute(rawBody, secret));
    }
}
