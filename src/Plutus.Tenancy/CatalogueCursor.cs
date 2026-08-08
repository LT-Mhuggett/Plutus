using System;
using System.Globalization;

namespace Plutus.Tenancy
{
    /// <summary>
    /// The catalogue feed's cursor: a position in <c>(ModifiedAt, IdOne)</c> order.
    ///
    /// ⚠ WHY A KEYSET CURSOR AND NOT A PAGE NUMBER. A till pages through a catalogue that shop
    /// staff are editing at the same time. With <c>OFFSET n</c>, an item inserted or re-sorted
    /// mid-run shifts every later row — so a till either sees a row twice (harmless, it upserts) or
    /// SKIPS one entirely. A skipped row is an item whose new price never arrives on that till and
    /// never will, because the cursor has already moved past it. Nothing would ever report it.
    ///
    /// ⚠ WHY (ModifiedAt, IdOne) AND NOT ModifiedAt ALONE. A bulk edit stamps hundreds of rows with
    /// the same timestamp — <c>InventoryBulk</c> exists precisely to do that. Keyed on the timestamp
    /// only, a page boundary landing inside such a group either loses the rest of it or loops on it
    /// forever. The barcode breaks the tie, and it is unique per business.
    ///
    /// ⚠ WHY ModifiedAt AT ALL, rather than a version counter the write paths bump. The retrofit
    /// plan originally specified a bumped BIGINT. That needs every catalogue write path to remember
    /// to increment it, and a path added next year that forgets leaves every till silently stale —
    /// the exact failure mode this platform keeps meeting. <c>ModifiedAt</c> is stamped for every
    /// <c>IAuditable</c> in <c>RepositoryContext.SaveMethods()</c>, a single choke point every write
    /// already passes through. Same reasoning as <c>VatBandStamp</c>: put it where it cannot be
    /// forgotten rather than where it must be remembered.
    ///
    /// ⚠ The one hole, stated honestly: the legacy CRUD bases accept <c>?isSync=true</c>, which lets
    /// a caller supply <c>ModifiedAt</c> instead of the server stamping it. That path only ACCEPTS a
    /// value newer than the stored one, so a row's timestamp never goes backwards — but a client
    /// could supply one between "stored" and "now" and a till whose cursor is already past it would
    /// miss the change until the row is next touched. That path is the legacy NatApp sync, which
    /// this retrofit retires.
    /// </summary>
    public static class CatalogueCursor
    {
        /// <summary>Round-trip-safe: ticks, then the barcode. Opaque to clients by contract — they
        /// store it and hand it back — but debuggable by eye, which matters more than opacity when
        /// a shop reports that one item never updated.</summary>
        public static string Encode(DateTime modifiedAt, string idOne) =>
            $"{modifiedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{idOne}";

        /// <summary>Parse a cursor. An unparseable or absent one means "from the beginning" — a
        /// FULL resync, which is always correct and merely expensive. Failing closed here would
        /// leave a till with a corrupted cursor permanently stale instead, which is worse and
        /// silent.</summary>
        public static bool TryDecode(string? cursor, out DateTime modifiedAt, out string idOne)
        {
            modifiedAt = DateTime.MinValue;
            idOne = string.Empty;
            if (string.IsNullOrWhiteSpace(cursor)) return false;

            var sep = cursor.IndexOf(':');
            if (sep <= 0) return false;

            if (!long.TryParse(cursor[..sep], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                return false;
            if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) return false;

            modifiedAt = new DateTime(ticks, DateTimeKind.Utc);
            idOne = cursor[(sep + 1)..];
            return true;
        }
    }
}
