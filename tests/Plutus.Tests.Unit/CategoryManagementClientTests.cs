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
/// Category management from a till (WP10 / cutover step 25).
///
/// ⚠ THE 409 IS THE FEATURE, NOT AN ERROR, and that is the whole reason these tests exist.
/// `DELETE /api/v1/categories/{id}` refuses while any item still references the category, and says
/// how many. That refusal guards a legacy cascade: `Item → Category` is `OnDelete(Cascade)`, so
/// deleting a category on the LEGACY route takes every item in it — and their sale lines and stock
/// with them. A client that reports the 409 and stops has removed the only safe route through, and
/// MAUI had no reassign UI at all, so the refusal had nowhere to land.
///
/// The rule: **the server's own words reach the operator**, because they carry the count and the
/// count is what the next step depends on.
/// </summary>
public class CategoryManagementClientTests
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

    [Fact]
    public async Task The_list_carries_the_ITEM_COUNT_because_the_delete_flow_turns_on_it()
    {
        var api = Api(_ => Json("""
            [{"id":"11111111-1111-1111-1111-111111111111","name":"Comics","description":"","itemCount":12},
             {"id":"22222222-2222-2222-2222-222222222222","name":"Empty","description":"","itemCount":0}]
            """), out _);

        var categories = await api.GetCategoryListAsync();

        Assert.NotNull(categories);
        Assert.Equal(12, categories!.Single(c => c.Name == "Comics").ItemCount);
        Assert.Equal(0, categories.Single(c => c.Name == "Empty").ItemCount);
    }

    [Fact]
    public async Task A_delete_that_is_REFUSED_returns_the_servers_own_words_with_the_count()
    {
        // ⚠ "12 item(s) are still in this category — reassign them first" is the actionable message.
        // Replacing it with "couldn't delete that" throws away the only thing the operator can use.
        var api = Api(_ => Json(
            """{"detail":"12 item(s) are still in this category — reassign them first."}""",
            HttpStatusCode.Conflict), out _);

        var (ok, problem) = await api.DeleteCategoryAsync(Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("12 item", problem!);
        Assert.Contains("reassign", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_the_LAST_category_is_refused_and_says_why()
    {
        // ⚠ `Item.CatId` is [Required] — an item must always have a category — so the final one
        // cannot go. A generic failure here would look like a bug rather than a rule.
        var api = Api(_ => Json(
            """{"detail":"This is the last category; items must always have one. Create another first."}""",
            HttpStatusCode.Conflict), out _);

        var (ok, problem) = await api.DeleteCategoryAsync(Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("last category", problem!);
    }

    [Fact]
    public async Task A_duplicate_NAME_is_refused_with_the_servers_message()
    {
        var api = Api(_ => Json("""{"detail":"A category named 'Comics' already exists."}""",
            HttpStatusCode.Conflict), out _);

        var (ok, problem) = await api.CreateCategoryAsync("Comics");

        Assert.False(ok);
        Assert.Contains("already exists", problem!);
    }

    [Fact]
    public async Task Reassign_posts_to_the_SOURCE_category_and_names_the_target()
    {
        // ⚠ The direction matters and is easy to invert: the route id is the category items are
        // moving OUT of, and `toId` is where they land. Swapping them empties the wrong category.
        var from = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var to = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var api = Api(_ => Json("""{"moved":12}"""), out var handler);

        var (ok, _) = await api.ReassignCategoryAsync(from, to);

        Assert.True(ok);
        Assert.Equal($"https://plutus.test/api/v1/categories/{from:D}/reassign",
            handler.Sent.Single().RequestUri!.ToString());
        Assert.Contains(to.ToString("D"), handler.LastBody!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_successful_delete_reports_success_even_though_it_returns_no_body()
    {
        // 204 No Content — a client that insists on parsing a body would call this a failure.
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out _);

        var (ok, problem) = await api.DeleteCategoryAsync(Guid.NewGuid());

        Assert.True(ok);
        Assert.Null(problem);
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_and_does_not_throw()
    {
        // ⚠ The caller is an `async void` command handler; an escape is a closed till.
        var api = Api(_ => throw new HttpRequestException("no route"), out _);

        var (ok, problem) = await api.CreateCategoryAsync("Comics");

        Assert.False(ok);
        Assert.Contains("reach", problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_till_never_calls_the_LEGACY_category_delete()
    {
        // ⚠ THE CASCADE. `Item → Category` is OnDelete(Cascade), so `DELETE /api/Category/{id}`
        // silently deletes every item in the category — and their sale lines and stock with them.
        // The v1 route exists to guard exactly that, and this pins that the client only knows the
        // guarded one.
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out var handler);

        await api.DeleteCategoryAsync(Guid.NewGuid());

        var url = handler.Sent.Single().RequestUri!.ToString();
        Assert.Contains("/api/v1/categories/", url);
        Assert.DoesNotContain("/api/Category", url);
    }
}
