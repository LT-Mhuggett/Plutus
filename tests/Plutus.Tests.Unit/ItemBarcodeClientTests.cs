using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP10 — the client half of MAUI's barcode and history sections, 2026-08-21.
///
/// ⚠⚠ WHAT THESE ARE FOR, AND WHAT THEY CANNOT BE. The UI is a Mopups dialog and cannot be rendered
/// here, so these pin the part a machine can reach: the URL, the verb, the body, and — the one that
/// matters — **that the server's own refusal sentence reaches the caller intact**. The screen itself
/// is §G74, by hand.
///
/// ⚠ Every refusal on this path is written to be shown to a person verbatim ("That is the shape of a
/// membership card…"), and the reserved shapes live ONLY in `SharedKernel.ItemBarcodeRules`. A client
/// that flattened these to true/false would have to invent its own wording — the exact C2 fault of
/// copying an identity rule into a client.
/// </summary>
public class ItemBarcodeClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(ct);
            return _respond(request);
        }
    }

    private static PlutusApiClient Api(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://plutus.test") });
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    // ── reading ──

    /// <summary>
    /// ⚠ The server's list endpoint answers with the WHOLE TENANT'S codes — that is deliberate, so the
    /// portal and web till can answer "is this taken" with no round trip per keystroke. Filtering to
    /// one item belongs in the client so three callers do not each re-derive it.
    /// </summary>
    [Fact]
    public async Task Barcodes_are_filtered_to_the_item_and_ordered()
    {
        var api = Api(_ => Json(
            """[{"code":"ZZZ","itemIdOne":"OTHER"},{"code":"BBB","itemIdOne":"MINE"},{"code":"AAA","itemIdOne":"MINE"}]"""),
            out _);

        var rows = await api.GetItemBarcodesAsync("MINE");

        Assert.Equal(new[] { "AAA", "BBB" }, rows.ConvertAll(r => r.Code));
    }

    /// <summary>⚠ Case-insensitive: the wire is a string and an item id that differs only in case is
    /// the same item. Matching case-sensitively would hide an operator's own alias from them.</summary>
    [Fact]
    public async Task The_item_match_ignores_case()
    {
        var api = Api(_ => Json("""[{"code":"AAA","itemIdOne":"mine"}]"""), out _);

        Assert.Single(await api.GetItemBarcodesAsync("MINE"));
    }

    /// <summary>
    /// ⚠⚠ AN EMPTY LIST, NEVER NULL. A caller that had to null-check would eventually forget, and the
    /// failure is a crash on a screen an operator opened to read something.
    /// </summary>
    [Fact]
    public async Task A_failed_read_is_an_empty_list_not_a_null()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);

        Assert.Empty(await api.GetItemBarcodesAsync("MINE"));
    }

    [Fact]
    public async Task History_asks_the_right_url_with_its_cap()
    {
        var api = Api(_ => Json("""{"total":2,"rows":[{"atUtc":"2026-08-21T09:00:00Z","type":"Stock adjusted","detail":"+1 — Miscount","by":"Sam Stockman"}]}"""), out var h);

        var page = await api.GetItemHistoryAsync("ITEM-1", take: 25);

        Assert.Equal("/api/v1/items/ITEM-1/history?take=25", h.Last!.RequestUri!.PathAndQuery);
        Assert.Equal(2, page!.Total);
        var row = Assert.Single(page.Rows);
        Assert.Equal("Stock adjusted", row.Type);
        Assert.Equal("Sam Stockman", row.By);
    }

    // ── writing ──

    [Fact]
    public async Task Adding_posts_the_code_to_the_items_barcodes()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Created), out var h);

        var result = await api.AddItemBarcodeAsync("ITEM-1", "5012345678900");

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Post, h.Last!.Method);
        Assert.Equal("/api/v1/items/ITEM-1/barcodes", h.Last.RequestUri!.PathAndQuery);
        Assert.Contains("5012345678900", h.LastBody);
    }

    /// <summary>
    /// ⚠⚠ ONE `PUT`, NEVER DELETE-THEN-ADD. Two calls can fail between them and leave the item with
    /// NEITHER code — and for a barcode that means an item that silently stops scanning. This pins the
    /// single call, with the OLD code in the path and the NEW one in the body.
    /// </summary>
    [Fact]
    public async Task Correcting_is_ONE_put_with_the_old_code_in_the_path()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var h);

        var result = await api.RenameItemBarcodeAsync("ITEM-1", "OLD1", "NEW9");

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Put, h.Last!.Method);
        Assert.Equal("/api/v1/items/ITEM-1/barcodes/OLD1", h.Last.RequestUri!.PathAndQuery);
        Assert.Contains("NEW9", h.LastBody);
    }

    [Fact]
    public async Task Removing_deletes_the_code_and_sends_no_body()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var h);

        var result = await api.RemoveItemBarcodeAsync("ITEM-1", "OLD1");

        Assert.True(result.Ok);
        Assert.Equal(HttpMethod.Delete, h.Last!.Method);
        Assert.Equal("/api/v1/items/ITEM-1/barcodes/OLD1", h.Last.RequestUri!.PathAndQuery);
        Assert.Null(h.LastBody);
    }

    /// <summary>⚠ A code with a slash or a space must not break the URL — it is operator input.</summary>
    [Fact]
    public async Task Codes_are_escaped_into_the_path()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var h);

        await api.RemoveItemBarcodeAsync("ITEM/1", "A B");

        Assert.Equal("/api/v1/items/ITEM%2F1/barcodes/A%20B", h.Last!.RequestUri!.PathAndQuery);
    }

    /// <summary>
    /// ⚠⚠ THE LOAD-BEARING ONE. The server's sentence reaches the caller INTACT, so the till can show
    /// it verbatim and no client has to own a copy of the identity rules.
    /// </summary>
    [Fact]
    public async Task A_refusal_carries_the_servers_own_sentence()
    {
        const string detail = "That is the shape of a membership card, so it can't be an item barcode.";
        var api = Api(_ => Json($"{{\"detail\":\"{detail}\"}}", HttpStatusCode.BadRequest), out _);

        var result = await api.AddItemBarcodeAsync("ITEM-1", "C1234567");

        Assert.False(result.Ok);
        Assert.Equal(detail, result.Problem);
    }

    /// <summary>⚠ A 409 names the item that already owns the code — the useful answer, and it must
    /// survive the trip.</summary>
    [Fact]
    public async Task A_clash_names_the_other_item()
    {
        var api = Api(_ => Json("""{"detail":"That code belongs to Batman Year One."}""", HttpStatusCode.Conflict), out _);

        var result = await api.AddItemBarcodeAsync("ITEM-1", "5012345678900");

        Assert.False(result.Ok);
        Assert.Contains("Batman Year One", result.Problem);
    }

    /// <summary>
    /// ⚠⚠ A REFUSAL AND A DROPPED CONNECTION ARE DIFFERENT THINGS, and the difference is what the
    /// operator does next. `Problem == null` means the request never landed, so the till says "check
    /// the connection" rather than "Plutus refused that" — which would send somebody hunting for a
    /// rule that was never applied.
    /// </summary>
    [Fact]
    public async Task A_dropped_request_is_distinguishable_from_a_refusal()
    {
        var api = Api(_ => throw new HttpRequestException("no route to host"), out _);

        var result = await api.AddItemBarcodeAsync("ITEM-1", "5012345678900");

        Assert.False(result.Ok);
        Assert.Null(result.Problem);
    }

    /// <summary>⚠ A refusal with no readable body still gets an honest sentence rather than a status
    /// code nobody at a counter can act on.</summary>
    [Fact]
    public async Task A_refusal_with_no_problem_details_still_says_something_usable()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html>nope</html>"),
        }, out _);

        var result = await api.AddItemBarcodeAsync("ITEM-1", "X");

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
    }
}
