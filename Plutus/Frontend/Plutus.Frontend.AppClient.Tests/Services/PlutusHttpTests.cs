using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Services.Connectivity;
using Xunit;

namespace Plutus.Frontend.AppClient.Tests.Services
{
    /// <summary>
    /// The HttpClient the till talks to Plutus through.
    ///
    /// ⚠ WHY THIS FILE EXISTS. Enrolment shipped broken: pressing "Enrol this till" reported
    /// *"This instance has already started one or more requests. Properties can only be modified
    /// before sending the first request."* — a .NET plumbing message, on a shop floor.
    ///
    /// The cause is a genuine trap. <c>HttpClient.BaseAddress</c> is immutable once the instance has
    /// sent a request, and the obvious defence — "only assign it if it changed" — DOES NOT WORK,
    /// because assigning <c>new Uri("https://host")</c> stores it normalised as
    /// <c>https://host/</c>. The next comparison therefore always says "changed", reassigns, and
    /// throws. The connection check ran first and sent a request; everything after it died.
    ///
    /// These tests pin the shape that avoids it: one client per address, never mutated.
    /// </summary>
    public class PlutusHttpTests
    {
        [Fact]
        public void The_same_address_returns_the_same_client()
        {
            // Not just tidiness: a client per call exhausts sockets on a till that runs all day,
            // which is what the mutation was (wrongly) avoiding in the first place.
            var a = PlutusHttp.TryFor("https://plutus.example.test");
            var b = PlutusHttp.TryFor("https://plutus.example.test");

            Assert.NotNull(a);
            Assert.Same(a, b);
        }

        [Fact]
        public async Task Asking_again_AFTER_a_request_has_been_sent_does_not_throw()
        {
            // ⚠ THE REGRESSION. This is the exact sequence that broke enrolment: check the
            // connection (sends a request), then enrol (asks for a client again).
            var client = PlutusHttp.TryFor("https://plutus-after-request.example.test");
            Assert.NotNull(client);

            try { await client!.GetAsync("/api/v1/ping", CancellationToken.None); }
            catch (HttpRequestException) { /* no such host — the point is that a request was SENT */ }

            var again = PlutusHttp.TryFor("https://plutus-after-request.example.test");

            Assert.Same(client, again);
            Assert.Equal("https://plutus-after-request.example.test/", again!.BaseAddress!.AbsoluteUri);
        }

        [Fact]
        public void A_trailing_slash_is_the_same_address()
        {
            // The normalisation that made the old "has it changed?" guard lie. Pinned so nobody
            // reintroduces a comparison against the un-normalised string.
            Assert.Same(
                PlutusHttp.TryFor("https://plutus-slash.example.test"),
                PlutusHttp.TryFor("https://plutus-slash.example.test/"));
        }

        [Fact]
        public void Different_addresses_get_different_clients()
        {
            Assert.NotSame(
                PlutusHttp.TryFor("https://one.example.test"),
                PlutusHttp.TryFor("https://two.example.test"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a url")]
        [InlineData("ftp://files.example.test")]
        public void An_unusable_address_returns_null_rather_than_throwing(string? url)
        {
            // The caller says "that address doesn't look right" in plain English. Throwing here is
            // how a typo in a settings box became a stack trace on a till.
            Assert.Null(PlutusHttp.TryFor(url));
        }

        [Fact]
        public void A_bare_host_is_assumed_to_be_https()
        {
            // What people actually type. ⚠ https rather than http: this connection carries a device
            // credential, so the insecure default would be the wrong kindness.
            var client = PlutusHttp.TryFor("plutus-bare.example.test");

            Assert.NotNull(client);
            Assert.Equal(Uri.UriSchemeHttps, client!.BaseAddress!.Scheme);
        }
    }
}
