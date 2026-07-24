using System;
using System.Security.Cryptography;
using System.Text;

namespace Plutus.SharedKernel;

/// <summary>Crockford base32 (no I, L, O, U) — for human-keyable enrolment codes.</summary>
public static class Crockford32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A random code of <paramref name="length"/> Crockford base32 chars (uppercase).</summary>
    public static string NewCode(int length = 8)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[bytes[i] & 0x1f];
        return new string(chars);
    }

    /// <summary>Normalise for hashing/lookup: uppercase and fold the visually ambiguous
    /// characters a user might type (I/L→1, O→0) to the canonical set.</summary>
    public static string Normalise(string code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant()
            .Replace('I', '1').Replace('L', '1').Replace('O', '0').Replace('U', 'V');
}

/// <summary>PBKDF2 hashing matching the legacy till/ClientUI password helper
/// (Rfc2898DeriveBytes, 101010 iterations, 64-byte output, SHA-1 default). Shared here so the
/// Tenancy module can hash device secrets without referencing the Identity module.</summary>
public static class Pbkdf2
{
    public const int Iterations = 101010;
    public const int HashBytes = 64;
    public const int SaltBytes = 32;

    public static (byte[] hash, byte[] salt) Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return (Derive(secret, salt), salt);
    }

    public static bool Verify(string secret, byte[] salt, byte[] expectedHash)
    {
        var computed = Derive(secret, salt);
        return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
    }

    private static byte[] Derive(string secret, byte[] salt)
    {
#pragma warning disable SYSLIB0041 // params fixed to match the legacy hashes stored by the till
        using var kdf = new Rfc2898DeriveBytes(secret, salt) { IterationCount = Iterations };
#pragma warning restore SYSLIB0041
        return kdf.GetBytes(HashBytes);
    }
}

/// <summary>Compact HMAC-SHA256 token: <c>base64url(payloadJson).base64url(sig)</c> — the same
/// wire shape as the Identity module's TestTokenAuth, kept here so any module can issue/verify
/// device tokens against a shared secret.</summary>
public static class CompactToken
{
    public static string Issue(string payloadJson, string secret)
    {
        var body = B64Url(Encoding.UTF8.GetBytes(payloadJson));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
        return $"{body}.{sig}";
    }

    /// <returns>The payload JSON when the signature is valid; otherwise null. Expiry is the
    /// caller's concern (parse the payload).</returns>
    public static string? Validate(string token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
        byte[] provided;
        try { provided = FromB64Url(parts[1]); } catch { return null; }
        if (!CryptographicOperations.FixedTimeEquals(expected, provided)) return null;

        try { return Encoding.UTF8.GetString(FromB64Url(parts[0])); }
        catch { return null; }
    }

    public static byte[] Sha256(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    private static string B64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64Url(string s)
    {
        var p = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '='));
    }
}
