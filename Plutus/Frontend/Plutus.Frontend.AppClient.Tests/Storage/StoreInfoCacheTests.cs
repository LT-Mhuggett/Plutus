using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Services.Storage;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Storage
{
    /// <summary>
    /// Cutover step 20 — the store's details come from the PORTAL, read-only.
    ///
    /// ⚠ WHAT THIS REPLACED. The till edited its shop's name, address, logo, phone and VAT number
    /// into the LEGACY local database. So the company's own VAT number could differ on every till
    /// in the estate, and the number printed on a receipt was whichever machine printed it — the
    /// sort of disagreement nobody finds until an inspection.
    ///
    /// `RefreshAsync` needs a device and a server; what is testable without one is the formatting,
    /// which is what actually lands on a receipt header.
    /// </summary>
    public class StoreInfoCacheTests
    {
        private static StoreInfoResult Info(
            string ad1 = null, string ad2 = null, string city = null, string post = null, string country = null) =>
            new(1, "Kapow Comics", "Kapow Comics Ltd", null, "GB123", ad1, ad2, city, post, country, "01484 000000", null);

        [Fact]
        public void An_address_is_the_populated_lines_in_order()
        {
            var address = StoreInfoCache.AddressOf(
                Info("30-31 Byram Street", null, "Huddersfield", "HD1 1ND", "UK"));

            Assert.Equal("30-31 Byram Street\nHuddersfield\nHD1 1ND\nUK", address);
        }

        /// <summary>⚠ Blank lines are DROPPED, not rendered. An address with an empty second line
        /// printed as a gap in the middle of a receipt header — and a stack of blank lines on a
        /// screen reads as a broken binding rather than as missing data.</summary>
        [Fact]
        public void Empty_lines_are_dropped_rather_than_left_as_gaps()
        {
            var address = StoreInfoCache.AddressOf(Info("30-31 Byram Street", "   ", "Huddersfield", null, null));

            Assert.Equal("30-31 Byram Street\nHuddersfield", address);
        }

        /// <summary>⚠ Null, not an empty string: the caller decides what "no address" looks like,
        /// and an empty string renders as a blank row that cannot be told from a failure.</summary>
        [Fact]
        public void A_store_with_no_address_at_all_is_null()
        {
            Assert.Null(StoreInfoCache.AddressOf(Info()));
            Assert.Null(StoreInfoCache.AddressOf(null));
        }

        [Fact]
        public void Whitespace_around_a_line_is_trimmed()
        {
            Assert.Equal("30-31 Byram Street", StoreInfoCache.AddressOf(Info("  30-31 Byram Street  ")));
        }
    }
}
