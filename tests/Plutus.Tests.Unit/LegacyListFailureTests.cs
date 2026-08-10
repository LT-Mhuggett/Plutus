using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// "There are none" and "I could not ask" are DIFFERENT ANSWERS.
///
/// ⚠ WHY THIS IS A TESTED RULE. Matt, 2026-08-10: *"Maui edit items is missing category and tax
/// e.g. 20%."* The item editor's tax and category prompts were skipped whenever the list came back
/// empty — and the client returned an empty list for a 403, a 500, an unreachable server and an
/// unparseable body alike. So a network fault presented as a fact about the shop, the prompt
/// vanished without a word, and a capability that had been built looked like one that never had.
///
/// The rule: a client may not flatten a FAILURE into a legitimate EMPTY RESULT. "There are no tax
/// bands" is a fact about the tenant; "Plutus refused" is a fact about the till, and only one of
/// them is somebody's job to fix.
/// </summary>
public class LegacyListFailureTests
{
    private static readonly Guid Business = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? Last { get; private set; }
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
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

    [Fact]
    public async Task A_tenant_with_no_bands_gets_an_empty_list_and_NO_problem()
    {
        // ⚠ The honest empty case must stay silent. If it reported a problem, every shop that
        // genuinely has no categories would be told something is broken.
        var api = Api(_ => Json("[]"), out _);

        var (bands, problem) = await api.GetTaxBandsAsync(Business);

        Assert.NotNull(bands);
        Assert.Empty(bands!);
        Assert.Null(problem);
    }

    [Fact]
    public async Task A_real_list_comes_back_parsed()
    {
        var api = Api(_ => Json("""[{"idOne":1,"name":"20%","rate":1.2}]"""), out _);

        var (bands, problem) = await api.GetTaxBandsAsync(Business);

        Assert.Null(problem);
        var band = Assert.Single(bands!);
        Assert.Equal(1, band.IdOne);
        Assert.Equal(1.2m, band.Rate);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Every_refusal_says_so_instead_of_pretending_the_shop_has_none(HttpStatusCode code)
    {
        var api = Api(_ => new HttpResponseMessage(code), out _);

        var (bands, problem) = await api.GetTaxBandsAsync(Business);

        Assert.Null(bands);
        Assert.False(string.IsNullOrWhiteSpace(problem), $"{(int)code} must carry a reason");
    }

    [Fact]
    public async Task A_five_hundred_points_at_the_sign_in_because_that_is_what_causes_it()
    {
        // ⚠ The legacy base controller's CONSTRUCTOR does `.First()` on the `objectidentifier`
        // claim, before model binding and before the action. A DEVICE token doesn't merely fail the
        // policy — it 500s. So "nobody is signed in" is the actionable reading of a 500 here, and
        // a generic "server error" would send somebody to the wrong place entirely.
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), out _);

        var (_, problem) = await api.GetTaxBandsAsync(Business);

        Assert.Contains("signed in", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unrecognised_SHAPE_is_reported_as_a_shape_problem()
    {
        // ⚠ These endpoints return a BARE ARRAY today — `PagedList<T>` derives from `List<T>` and
        // the paging metadata rides in the `X-Pagination` header. This fires only if something
        // starts wrapping the body, and reporting "none" would send somebody hunting through the
        // portal's data for a fault that is in the wire.
        var api = Api(_ => Json("""{"items":[{"idOne":1,"name":"20%","rate":1.2}],"totalCount":1}"""), out _);

        var (bands, problem) = await api.GetTaxBandsAsync(Business);

        Assert.Null(bands);
        Assert.Contains("shape", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_and_does_not_throw()
    {
        // ⚠ It must not throw: the caller is an `async void` command handler, where an escape is a
        // closed till rather than a failed button.
        var api = Api(_ => throw new HttpRequestException("no route to host"), out _);

        var (bands, problem) = await api.GetTaxBandsAsync(Business);

        Assert.Null(bands);
        Assert.Contains("reach", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_till_that_has_not_learnt_its_business_says_THAT_rather_than_calling()
    {
        // ⚠ `businessId` is a required HEADER and an empty one is a 400. Asking anyway would turn
        // a knowable local fault into a server round trip and a less useful message.
        var api = Api(_ => Json("[]"), out var handler);

        var (bands, problem) = await api.GetCategoriesAsync(Guid.Empty);

        Assert.Null(bands);
        Assert.Contains("business", problem!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.Last);   // it never went to the wire
    }

    [Fact]
    public async Task The_business_id_travels_as_a_HEADER_the_way_the_web_till_sends_it()
    {
        // ⚠ Not a query parameter, and not the tenant id. The wrong identifier here returns another
        // tenant's rows or none at all — the quietest possible way to be wrong.
        var api = Api(_ => Json("[]"), out var handler);

        await api.GetCategoriesAsync(Business);

        Assert.Equal(Business.ToString("D"), Assert.Single(handler.Last!.Headers.GetValues("businessId")));
        Assert.DoesNotContain("businessId=", handler.Last.RequestUri!.Query);
    }
}
