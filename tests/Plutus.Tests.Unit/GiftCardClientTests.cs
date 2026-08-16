using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The shared gift-card client (WP13 / step 27).
///
/// ⚠ `Plutus.Frontend.WebApp/src/api.ts:428–480` is the reference (binding default 10), so these
/// pin the URL and PAYLOAD shapes against it — a client that talks to a slightly different URL fails
/// at a counter, not in a compiler.
///
/// ⚠⚠ AND THEY PIN THE REFUSAL WORDING HARDER THAN USUAL, because gift cards are the one place this
/// client deliberately surfaces the SERVER's own sentence. *"That card only has £12.50 left"* is
/// something a cashier acts on; "409" is not.
/// </summary>
public class GiftCardClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Sent { get; } = new();
        public string? LastBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent.Add(request);
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

    private static readonly Guid Sale = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Entry = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ── the URLs ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lookup_asks_the_endpoint_the_web_till_uses()
    {
        var api = Api(_ => Json("{}"), out var h);

        await api.LookupGiftCardAsync("K7QP2M9W");

        Assert.Equal("https://plutus.test/api/v1/giftcards/K7QP2M9W/lookup", h.Sent[0].RequestUri!.AbsoluteUri);
    }

    /// <summary>
    /// ⚠ A CODE IS URL-ENCODED. Gift-card codes come off a scanner and a human keyboard, and a stray
    /// `&` or `/` in one would otherwise truncate the path and look up a different card — or none.
    /// ⚠ `AbsoluteUri`, not `ToString()`: `Uri.ToString()` UNESCAPES, so it cannot tell an encoded
    /// path from a raw one and the assertion would pass either way.
    /// </summary>
    [Fact]
    public async Task A_code_with_awkward_characters_is_encoded_rather_than_breaking_the_path()
    {
        var api = Api(_ => Json("{}"), out var h);

        await api.LookupGiftCardAsync("A/B&C");

        Assert.Equal("https://plutus.test/api/v1/giftcards/A%2FB%26C/lookup", h.Sent[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Activate_and_redeem_post_to_their_own_actions()
    {
        var api = Api(_ => Json("""{"entryId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","balancePence":500}"""), out var h);

        await api.ActivateGiftCardAsync("CODE", 2000, Sale, Entry);
        await api.RedeemGiftCardAsync("CODE", 500, Sale, Entry);

        Assert.Equal("https://plutus.test/api/v1/giftcards/CODE/activate", h.Sent[0].RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, h.Sent[0].Method);
        Assert.Equal("https://plutus.test/api/v1/giftcards/CODE/redeem", h.Sent[1].RequestUri!.AbsoluteUri);
    }

    /// <summary>⚠ The entry id is what makes these idempotent — a queued-then-drained sale must draw
    /// the balance down once. It has to actually reach the wire.</summary>
    [Fact]
    public async Task The_payload_carries_the_amount_the_sale_and_the_idempotency_key()
    {
        var api = Api(_ => Json("""{"balancePence":0}"""), out var h);

        await api.RedeemGiftCardAsync("CODE", 1250, Sale, Entry);

        Assert.Contains("1250", h.LastBody);
        Assert.Contains(Sale.ToString(), h.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Entry.ToString(), h.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    // ── refusals: the sentence is the point ───────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ THE SERVER'S OWN WORDS REACH THE CASHIER. This client swallows the detail everywhere else;
    /// gift cards are the deliberate exception, because *"That card only has £12.50 left"* tells an
    /// operator what to do next and "409" does not.
    /// </summary>
    [Fact]
    public async Task A_refusal_surfaces_the_servers_detail_not_a_status_code()
    {
        var api = Api(_ => Json(
            """{"type":"about:blank","title":"Conflict","status":409,"detail":"That card only has 12.50 left."}""",
            HttpStatusCode.Conflict), out _);

        var (ok, _, problem) = await api.RedeemGiftCardAsync("CODE", 5000, Sale, Entry);

        Assert.False(ok);
        Assert.Equal("That card only has 12.50 left.", problem);
    }

    /// <summary>⚠ An unexpected body is still more use than "something went wrong", so the raw text
    /// is the fallback rather than a generic sentence.</summary>
    [Fact]
    public async Task A_refusal_that_is_not_problem_details_still_says_something_specific()
    {
        var api = Api(_ => Json("card is void", HttpStatusCode.Conflict), out _);

        var (ok, _, problem) = await api.RedeemGiftCardAsync("CODE", 100, Sale, Entry);

        Assert.False(ok);
        Assert.Contains("void", problem);
    }

    /// <summary>⚠ And an empty body still names the action and the code, so a log line is diagnosable.</summary>
    [Fact]
    public async Task An_empty_refusal_body_still_names_the_action()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.Conflict), out _);

        var (ok, _, problem) = await api.ActivateGiftCardAsync("CODE", 100, Sale, Entry);

        Assert.False(ok);
        Assert.Contains("activate", problem);
        Assert.Contains("409", problem);
    }

    /// <summary>
    /// ⚠⚠ UNREACHABLE MUST READ AS "NOT DONE", never as "probably fine". These are called BEFORE the
    /// sale is recorded precisely so a refusal can abort it; an exception treated as success would
    /// record a sale paid with a card that was never actually debited.
    /// </summary>
    [Fact]
    public async Task A_network_failure_is_a_refusal_rather_than_an_exception()
    {
        var api = Api(_ => throw new HttpRequestException("no route to host"), out _);

        var (ok, _, problem) = await api.RedeemGiftCardAsync("CODE", 100, Sale, Entry);

        Assert.False(ok);
        Assert.Contains("couldn't be reached", problem);
    }

    [Fact]
    public async Task An_empty_code_is_refused_before_a_request_is_made()
    {
        var api = Api(_ => Json("{}"), out var h);

        var (ok, _, _) = await api.RedeemGiftCardAsync("   ", 100, Sale, Entry);

        Assert.False(ok);
        Assert.Empty(h.Sent);
    }

    // ── the lookup's verdict ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ `unsold` IS THE TRAP. A card on the rack has a real code, looks entirely genuine and
    /// scans — and taking it as payment would hand over goods against value nobody ever bought.
    /// Only an ACTIVE card with money on it can be spent.
    /// </summary>
    [Theory]
    [InlineData("active", 500, true)]
    [InlineData("active", 0, false)]
    [InlineData("unsold", 5000, false)]
    [InlineData("spent", 0, false)]
    [InlineData("expired", 500, false)]
    [InlineData("void", 500, false)]
    public async Task Only_an_active_card_with_a_balance_can_be_spent(string status, long balance, bool spendable)
    {
        var api = Api(_ => Json($$"""{"status":"{{status}}","balancePence":{{balance}}}"""), out _);

        var card = await api.LookupGiftCardAsync("CODE");

        Assert.Equal(spendable, card!.IsSpendable);
    }

    /// <summary>⚠ Status casing is the server's business, not a reason to refuse a real card.</summary>
    [Fact]
    public async Task The_status_check_does_not_care_about_casing()
    {
        var api = Api(_ => Json("""{"status":"Active","balancePence":500}"""), out _);

        Assert.True((await api.LookupGiftCardAsync("CODE"))!.IsSpendable);
    }

    /// <summary>
    /// ⚠ The VAT treatment round-trips, because it decides WHEN the card's VAT falls due — "multi"
    /// means no VAT at the sale of the card and VAT off the goods when it is spent; "single" means
    /// the reverse. Guessing it puts a wrong number on a VAT return, which is why the server refuses
    /// to default it.
    /// </summary>
    [Fact]
    public async Task The_vat_treatment_and_the_activation_row_survive_the_read()
    {
        var api = Api(_ => Json(
            $$"""{"status":"active","balancePence":2000,"vatTreatment":"multi","itemIdOne":"{{GiftCards.ItemIdOne}}"}"""),
            out _);

        var card = await api.LookupGiftCardAsync("CODE");

        Assert.Equal("multi", card!.VatTreatment);
        Assert.True(GiftCards.IsActivation(card.ItemIdOne));
    }

    [Fact]
    public async Task An_unknown_code_reads_back_as_nothing_rather_than_throwing()
    {
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.NotFound), out _);

        Assert.Null(await api.LookupGiftCardAsync("NOPE"));
    }
}
