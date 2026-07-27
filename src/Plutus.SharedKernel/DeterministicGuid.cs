using System;
using System.Security.Cryptography;
using System.Text;

namespace Plutus.SharedKernel;

/// <summary>
/// Deterministic, name-derived GUIDs (WP2.1 decision, 2026-07-24). The web POS mints the
/// v1 SaleLine.ItemId for a legacy catalogue item as a hash of the item's natural key, so
/// every device derives the SAME id for the same item with no server round-trip and no
/// mapping table. When Phase 5 mints real item UUIDs, the legacy→UUID map is rebuilt by
/// computing this hash for every Items row and joining.
///
/// Algorithm (mirrored EXACTLY by itemGuid() in the web POS src/pipeline.ts):
///   bytes = SHA-256(UTF8("plutus:item:" + businessId.ToString("D").ToLowerInvariant() + ":" + itemIdOne))[0..16]
///   bytes[6] = (bytes[6] &amp; 0x0F) | 0x80   // version 8 (custom)
///   bytes[8] = (bytes[8] &amp; 0x3F) | 0x80   // RFC 4122 variant
///   guid = canonical textual form of those 16 bytes (big-endian, as hex-printed).
/// NOTE: historic Kapow rows migrated by WP1.8 used randomly-minted ids (IdRemap) instead —
/// the two populations are distinguished by SaleV2.LegacyRef and unified at Phase 5 adoption.
/// </summary>
public static class DeterministicGuid
{
    public static Guid ForItem(Guid businessId, string itemIdOne)
    {
        if (string.IsNullOrEmpty(itemIdOne)) throw new ArgumentException("itemIdOne is required.", nameof(itemIdOne));
        return FromName($"plutus:item:{businessId.ToString("D").ToLowerInvariant()}:{itemIdOne}");
    }

    /// <summary>General name-derived GUID (same version-8 algorithm as <see cref="ForItem"/>).
    /// Use for any deterministic id from a stable natural key — e.g. the Woo connector derives a
    /// sale's id from its order id so re-delivered webhooks dedupe. Parts are joined with ':'.</summary>
    public static Guid ForName(string ns, params string[] parts)
    {
        if (string.IsNullOrEmpty(ns)) throw new ArgumentException("namespace is required.", nameof(ns));
        var name = parts is { Length: > 0 } ? ns + ":" + string.Join(":", parts) : ns;
        return FromName(name);
    }

    private static Guid FromName(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));

        Span<byte> b = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(b);
        b[6] = (byte)((b[6] & 0x0F) | 0x80);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);

        // Build from the canonical (big-endian) hex string so the textual form matches the
        // JS implementation byte-for-byte (Guid(byte[]) would apply little-endian groups).
        var s = $"{b[0]:x2}{b[1]:x2}{b[2]:x2}{b[3]:x2}-{b[4]:x2}{b[5]:x2}-{b[6]:x2}{b[7]:x2}-{b[8]:x2}{b[9]:x2}-{b[10]:x2}{b[11]:x2}{b[12]:x2}{b[13]:x2}{b[14]:x2}{b[15]:x2}";
        return Guid.Parse(s);
    }
}
