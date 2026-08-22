using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// **The Bin's own client call — the header it cannot work without.**
///
/// ⚠⚠ THIS SHIPPED BROKEN IN TILL 1.117.0 AND THE INTEGRATION TEST DID NOT NOTICE.
/// `/api/Item/Index` is the legacy composite endpoint: `Index([FromHeader] TId2 businessId, …)`. With
/// no `businessId` header it answers a plain-text **400 "Business ID not provided"** — which
/// `GetBinnedItemsAsync` saw only as `!IsSuccessStatusCode`, returned `null` for, and the till
/// reported to the operator as *"The bin couldn't be read... Try again when the till is back
/// online"*. A connectivity message, on a till that was online the whole time, for a request that
/// never had a hope of succeeding.
///
/// ⚠⚠ AND THE LESSON WAS ALREADY WRITTEN DOWN. `BinRestoreE2eTests` carries this exact trap in its
/// own comment, because it hit the 400 and had to add the header to pass. **But it calls the
/// ENDPOINT, not the client** — so it proved the server works and said nothing about whether
/// anything on the till asks it correctly. That is the gap these tests close: they drive
/// `PlutusApiClient` itself, which is the thing the till actually uses.
///
/// ⚠ The wider shape is worth naming: an E2E test against an endpoint does not test the client of
/// that endpoint, and "built and wired to nothing" is a pattern this project has hit repeatedly.
/// </summary>
public class BinClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Sent { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static PlutusApiClient Api(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://plutus.test") });
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static readonly Guid Business = Guid.Parse("0192b8a0-1a6f-7000-8000-0000000000aa");

    /// <summary>⚠ THE REGRESSION. Without this header the request is a 400, every time.</summary>
    [Fact]
    public async Task The_bin_request_carries_the_businessId_header()
    {
        var api = Api(_ => Json("[]"), out var handler);

        await api.GetBinnedItemsAsync(Business);

        var sent = Assert.Single(handler.Sent);
        Assert.True(sent.Headers.TryGetValues("businessId", out var values),
            "No `businessId` header. /api/Item/Index binds it [FromHeader] and answers a plain-text 400 "
            + "without it — which the till shows as \"the bin couldn't be read, try again when back online\".");
        Assert.Equal(Business.ToString("D"), values!.Single());
    }

    /// <summary>⚠ And it must actually ask for the BIN, not the catalogue.</summary>
    [Fact]
    public async Task The_bin_request_asks_for_withdrawn_items()
    {
        var api = Api(_ => Json("[]"), out var handler);

        await api.GetBinnedItemsAsync(Business);

        var url = Assert.Single(handler.Sent).RequestUri!.ToString();
        Assert.Contains("Binned=true", url);
    }

    /// <summary>
    /// ⚠⚠ NULL AND EMPTY MEAN OPPOSITE THINGS AND THE DIALOG RENDERS THEM DIFFERENTLY. An empty bin
    /// is a fact; an unreadable bin is a warning. Collapsing them is how "nothing is withdrawn"
    /// becomes indistinguishable from "I could not ask".
    /// </summary>
    [Fact]
    public async Task An_empty_bin_is_an_empty_list_not_null()
    {
        var api = Api(_ => Json("[]"), out _);

        var items = await api.GetBinnedItemsAsync(Business);

        Assert.NotNull(items);
        Assert.Empty(items!);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]       // the missing-header 400 this bug WAS
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_failed_read_is_null_so_the_till_can_say_it_could_not_ask(HttpStatusCode code)
    {
        var api = Api(_ => new HttpResponseMessage(code)
        {
            Content = new StringContent("Business ID not provided", System.Text.Encoding.UTF8, "text/plain"),
        }, out _);

        Assert.Null(await api.GetBinnedItemsAsync(Business));
    }

    /// <summary>⚠ A rows-and-count envelope would deserialise to nothing and look like an empty bin —
    /// the `CustomerClientTests` fault. This pins the shape actually returned.</summary>
    [Fact]
    public async Task Withdrawn_items_are_read_from_a_bare_array()
    {
        var api = Api(_ => Json(
            """[{"idOne":"COMIC-1","name":"Batman #1","binnedAtUtc":"2026-08-20T10:00:00Z"}]"""), out _);

        var items = await api.GetBinnedItemsAsync(Business);

        Assert.NotNull(items);
        var one = Assert.Single(items!);
        Assert.Equal("COMIC-1", one.IdOne);
    }
}
