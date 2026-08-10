using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Contracts.Client;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The stock column on the inventory list (WP10 / cutover step 25).
///
/// ⚠ THE WHOLE POINT IS THAT "NO NUMBER" IS NOT ZERO, and there are two different kinds of it. An
/// item can be UNTRACKED — a carrier bag, a service, whose level is meaningless by design — or it
/// can simply never have been counted. Both arrive as a null quantity and only the flag tells them
/// apart. An operator reading "0" against an item the shop has never counted will reorder it; an
/// operator reading "0" against a carrier bag will conclude the shop is out of carrier bags.
///
/// This column was BLANK on every row until 2026-08-10, which reads as zero for everything.
/// </summary>
public class StockLevelDisplayTests
{
    private static StockLevelDto Level(string id, bool untracked = false, int? qty = null)
        => new() { ItemIdOne = id, Untracked = untracked, Quantity = qty };

    [Fact]
    public void A_counted_item_shows_its_number()
    {
        Assert.Equal("7", Level("BAT001", qty: 7).Display);
    }

    [Fact]
    public void A_genuine_zero_shows_zero_and_is_not_confused_with_no_answer()
    {
        // ⚠ "We counted, there are none" IS a number and must render as one. Collapsing it into
        // "—" would hide a genuine stock-out on the one screen somebody checks for it.
        Assert.Equal("0", Level("BAT001", qty: 0).Display);
    }

    [Fact]
    public void An_untracked_item_shows_infinity_never_a_count()
    {
        // ⚠ Even if a quantity somehow arrives, the flag wins: an untracked item's level is
        // meaningless by design, and showing a stale count invites somebody to act on it.
        Assert.Equal("∞", Level("BAG", untracked: true).Display);
        Assert.Equal("∞", Level("BAG", untracked: true, qty: 4).Display);
    }

    [Fact]
    public void An_item_that_has_never_been_counted_shows_a_dash_not_a_zero()
    {
        // ⚠ THE ONE THAT MATTERS. Null means "no stock record at all" — nothing has ever been
        // received. That is not a stock-out; it is silence, and printing 0 turns silence into a
        // reorder.
        Assert.Equal("—", Level("NEW001").Display);
    }

    [Fact]
    public void A_negative_count_is_shown_as_it_is()
    {
        // ⚠ Stock CAN go negative — the ledger allows it and there is a report for exactly that.
        // Clamping it to zero here would hide the discrepancy on the screen most likely to catch it.
        Assert.Equal("-3", Level("BAT001", qty: -3).Display);
    }
}

/// <summary>
/// Reading those levels off the platform.
///
/// ⚠ The client must never turn a FAILURE into "the shop has none" — the same rule that
/// `LegacyListFailureTests` pins for tax bands and categories, in a place where the wrong answer is
/// a number rather than a missing prompt.
/// </summary>
public class StockLevelClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            Calls++;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(ct);
            return _respond(request);
        }
    }

    private static PlutusApiClient Api(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new PlutusApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://plutus.test") });
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Levels_come_back_parsed_with_the_untracked_flag_intact()
    {
        var api = Api(_ => Json("""
            [{"itemIdOne":"BAT001","untracked":false,"quantity":7},
             {"itemIdOne":"BAG","untracked":true,"quantity":null}]
            """), out _);

        var levels = await api.GetStockLevelsAsync(new[] { "BAT001", "BAG" });

        Assert.NotNull(levels);
        Assert.Equal("7", levels!.Single(l => l.ItemIdOne == "BAT001").Display);
        Assert.Equal("∞", levels.Single(l => l.ItemIdOne == "BAG").Display);
    }

    [Fact]
    public async Task An_empty_ask_never_reaches_the_wire()
    {
        var api = Api(_ => Json("[]"), out var handler);

        Assert.Empty((await api.GetStockLevelsAsync(Array.Empty<string>()))!);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_refusal_is_NULL_so_the_column_keeps_its_dashes()
    {
        // ⚠ Null means "leave the column alone". An empty LIST would mean "every item you asked
        // about has no record", which the caller would render as a dash for each — the same
        // outcome by luck rather than by rule, and the wrong one the moment the caller changes.
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out _);

        Assert.Null(await api.GetStockLevelsAsync(new[] { "BAT001" }));
    }

    [Fact]
    public async Task An_unreachable_server_does_not_throw_into_the_screen()
    {
        var api = Api(_ => throw new HttpRequestException("no route"), out _);

        Assert.Null(await api.GetStockLevelsAsync(new[] { "BAT001" }));
    }

    [Fact]
    public async Task The_ask_is_CLAMPED_to_two_hundred_because_the_server_truncates_silently()
    {
        // ⚠ The endpoint does `if (length > 200) Take(200)` — it TRUNCATES rather than refusing.
        // Sending 500 ids would come back with 200 answers and 300 silent gaps, and every gap
        // renders as "never counted": a wrong answer that looks exactly like the honest one.
        var api = Api(_ => Json("[]"), out var handler);

        await api.GetStockLevelsAsync(Enumerable.Range(0, 500).Select(i => $"ITEM{i}").ToList());

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(200, sent.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task It_is_one_call_for_the_whole_page_not_one_per_row()
    {
        // ⚠ A per-row call over a 500-row list is 500 round trips on a counter with a queue. The
        // endpoint is a bulk POST for exactly this reason.
        var api = Api(_ => Json("[]"), out var handler);

        await api.GetStockLevelsAsync(new[] { "A", "B", "C", "D" });

        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://plutus.test/api/v1/stock/levels/bulk", handler.Last!.RequestUri!.ToString());
    }
}
