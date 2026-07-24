using System.Security.Cryptography;

namespace Plutus.SharedKernel;

/// <summary>
/// UUIDv7 (RFC 9562): 48-bit Unix-ms timestamp, version 7, variant 10, remainder
/// crypto-random. A monotonic guard advances the timestamp when called faster than the
/// clock ticks, so IDs minted in a burst stay strictly ordered by their leading bytes
/// (natural DB clustering + sortable sale IDs, per D4). Big-endian byte layout so the
/// timestamp is the *leading* bytes of the canonical form.
/// </summary>
public static class Uuid7
{
    private static readonly object Gate = new();
    private static long _lastMs;

    public static Guid New()
    {
        long ms;
        lock (Gate)
        {
            ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ms <= _lastMs) ms = _lastMs + 1;
            _lastMs = ms;
        }

        Span<byte> b = stackalloc byte[16];
        b[0] = (byte)(ms >> 40);
        b[1] = (byte)(ms >> 32);
        b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16);
        b[4] = (byte)(ms >> 8);
        b[5] = (byte)ms;
        RandomNumberGenerator.Fill(b[6..]);
        b[6] = (byte)((b[6] & 0x0F) | 0x70); // version 7
        b[8] = (byte)((b[8] & 0x3F) | 0x80); // variant 10

        return new Guid(b, bigEndian: true);
    }
}
