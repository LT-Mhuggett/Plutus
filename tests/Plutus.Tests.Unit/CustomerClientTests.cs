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
/// The shared customer client (WP12 / step 27).
///
/// ⚠ IT DID NOT EXIST UNTIL 2026-08-13, and that absence is why the MAUI till has no customer attach
/// and why a Gold member is charged 10% more on it than on the web till for the same basket.
/// `Plutus.Frontend.WebApp/src/api.ts:309–418` is the reference (binding default 10), so these tests
/// pin the URL SHAPES and the PAYLOAD SHAPES against it — a client that talks to a slightly different
/// URL fails at a counter, not in a compiler.
///
/// ⚠ They also pin the REFUSAL WORDING, because a cashier reading "Forbidden" mid-queue learns
/// nothing about what to do next, and the next thing to do is ask a supervisor.
/// </summary>
public class CustomerClientTests
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

    private static HttpResponseMessage Text(string body, HttpStatusCode code)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain") };

    // ── search: the URL the web till uses, character for character ──

    [Fact]
    public async Task Search_asks_for_take_10()
    {
        var api = Api(_ => Json("[]"), out var h);
        await api.SearchCustomersAsync("ada");

        var url = h.Sent.Single().RequestUri!.AbsoluteUri;
        Assert.Contains("/api/v1/customers?take=10", url);
        Assert.Contains("search=ada", url);
    }

    /// <summary>
    /// ⚠⚠ THE ENCODING TEST HAS TO USE AN AMPERSAND, AND FINDING THAT OUT COST A MUTANT. A space is
    /// no test at all: `new HttpRequestMessage(..., url)` builds a <c>Uri</c>, which escapes a space
    /// to <c>%20</c> **by itself** — so removing <c>Uri.EscapeDataString</c> entirely left the space
    /// case passing and the mutation SURVIVED.
    ///
    /// <c>&amp;</c> is the character that actually matters, because <c>Uri</c> leaves it alone: raw,
    /// <c>search=a&amp;b</c> reaches the server as <c>search=a</c> plus a stray parameter, so a
    /// customer called "Marks &amp; Spencer" silently searches for "Marks". Encoded it is
    /// <c>search=a%26b</c> and means what the operator typed.
    ///
    /// ⚠ AbsoluteUri, NOT ToString() — <c>Uri.ToString()</c> unescapes, so it cannot tell an encoded
    /// query from a raw one.
    /// </summary>
    [Fact]
    public async Task Search_encodes_a_term_that_would_otherwise_split_the_query()
    {
        var api = Api(_ => Json("[]"), out var h);
        await api.SearchCustomersAsync("Marks & Spencer");

        var url = h.Sent.Single().RequestUri!.AbsoluteUri;
        Assert.Contains("search=Marks%20%26%20Spencer", url);
        // The raw form would end the search parameter at the ampersand.
        Assert.DoesNotContain("search=Marks%20&", url);
        Assert.Single(h.Sent.Single().RequestUri!.Query.Split('&').Where(p => p.StartsWith("search=")));
        Assert.Equal(2, h.Sent.Single().RequestUri!.Query.Split('&').Length);   // take + search, nothing more
    }

    [Fact]
    public async Task Search_with_no_term_sends_no_search_parameter_at_all()
    {
        var api = Api(_ => Json("[]"), out var h);
        await api.SearchCustomersAsync("   ");

        var url = h.Sent.Single().RequestUri!.ToString();
        Assert.EndsWith("/api/v1/customers?take=10", url);
        Assert.DoesNotContain("search=", url);   // "search=" with nothing after it is not the same request
    }

    [Fact]
    public async Task Search_reads_the_member_number_the_card_carries()
    {
        var api = Api(_ => Json("""
            [{"id":"11111111-1111-1111-1111-111111111111","name":"Ada","email":null,"phone":null,"memberNo":"000482P"}]
            """), out _);

        var rows = await api.SearchCustomersAsync("482");
        Assert.Equal("000482P", rows!.Single().MemberNo);
        // ⚠ And the number the server returned must satisfy the shared rule, or the till's own
        // scan-routing would disagree with the server's search.
        Assert.True(Plutus.SharedKernel.MemberNumbers.IsValid(rows.Single().MemberNo));
    }

    // ── the detail read: what decides the money ──

    [Fact]
    public async Task The_detail_read_carries_the_tier_rate_the_balance_and_the_barcode()
    {
        var api = Api(_ => Json("""
            {"id":"11111111-1111-1111-1111-111111111111","name":"Ada","email":"a@b.c","phone":"0700",
             "memberNo":"000482P","memberBarcode":"C000482P",
             "creditAccountId":"22222222-2222-2222-2222-222222222222","creditBalancePence":440,
             "membership":{"tierId":"33333333-3333-3333-3333-333333333333","tier":"Gold",
                           "autoDiscountRate":0.10,"renewalDay":"2027-08-13","expired":false}}
            """), out var h);

        var c = await api.GetCustomerAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.Equal("/api/v1/customers/11111111-1111-1111-1111-111111111111", h.Sent.Single().RequestUri!.AbsolutePath);
        Assert.Equal("C000482P", c!.MemberBarcode);
        Assert.Equal(440, c.CreditBalancePence);
        Assert.Equal("Gold", c.Membership!.Tier);
        Assert.Equal(0.10m, c.Membership.AutoDiscountRate);
        Assert.False(c.Membership.Expired);

        // ⚠ THE POINT OF THE WHOLE READ: the rate feeds the SHARED rule, not a till's own maths.
        Assert.Equal(44, Plutus.SharedKernel.MemberDiscount.ForLine(
            440, 1, c.Membership.AutoDiscountRate,
            hasMembership: true, expired: c.Membership.Expired,
            isReturn: false, hasDiscount: false, isGiftCard: false));
    }

    /// <summary>⚠ `expired` is the SERVER's verdict and the till must carry it through, not re-derive
    /// it from renewalDay against its own clock — an offline till's clock is exactly the thing that
    /// cannot be trusted to decide whether somebody is still entitled to a discount.</summary>
    [Fact]
    public async Task An_expired_membership_arrives_as_expired_and_earns_nothing()
    {
        var api = Api(_ => Json("""
            {"id":"11111111-1111-1111-1111-111111111111","name":"Lapsed","memberNo":"000482P",
             "creditBalancePence":0,
             "membership":{"tierId":null,"tier":"Gold","autoDiscountRate":0.10,
                           "renewalDay":"2025-01-01","expired":true}}
            """), out _);

        var c = await api.GetCustomerAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.True(c!.Membership!.Expired);
        Assert.Equal(0, Plutus.SharedKernel.MemberDiscount.ForLine(
            440, 1, c.Membership.AutoDiscountRate,
            hasMembership: true, expired: true,
            isReturn: false, hasDiscount: false, isGiftCard: false));
    }

    [Fact]
    public async Task A_customer_with_no_membership_reads_as_null_not_as_a_zero_tier()
    {
        var api = Api(_ => Json("""
            {"id":"11111111-1111-1111-1111-111111111111","name":"Walk-in","memberNo":"0000011",
             "creditBalancePence":0,"membership":null}
            """), out _);

        var c = await api.GetCustomerAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Null(c!.Membership);   // a till must not have to distinguish "no tier" from "0% tier"
    }

    // ── create: the write a cashier may make ──

    [Fact]
    public async Task Create_posts_name_email_phone_and_returns_the_issued_member_number()
    {
        var api = Api(_ => Json("""
            {"id":"11111111-1111-1111-1111-111111111111","memberNo":"000482P"}
            """, HttpStatusCode.Created), out var h);

        var (ok, id, memberNo, problem) = await api.CreateCustomerAsync(" Ada Lovelace ", " a@b.c ", " 0700 ");

        Assert.True(ok);
        Assert.Null(problem);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), id);
        Assert.Equal("000482P", memberNo);
        Assert.Equal(HttpMethod.Post, h.Sent.Single().Method);
        Assert.Equal("/api/v1/customers", h.Sent.Single().RequestUri!.AbsolutePath);
        // ⚠ Trimmed — a trailing space in a name is invisible on a receipt and breaks an exact search.
        Assert.Contains("\"name\":\"Ada Lovelace\"", h.LastBody);
        Assert.Contains("\"email\":\"a@b.c\"", h.LastBody);
    }

    [Fact]
    public async Task Create_sends_null_rather_than_an_empty_string_for_a_blank_contact()
    {
        var api = Api(_ => Json("""{"id":"11111111-1111-1111-1111-111111111111","memberNo":"000482P"}""",
            HttpStatusCode.Created), out var h);

        await api.CreateCustomerAsync("Ada", email: "", phone: "   ");

        // ⚠ "" would be STORED as an email — it then matches nothing, looks like a typo'd address in
        // the portal, and cannot be distinguished from "we never asked".
        Assert.Contains("\"email\":null", h.LastBody);
        Assert.Contains("\"phone\":null", h.LastBody);
    }

    [Fact]
    public async Task Create_refuses_a_blank_name_without_troubling_the_server()
    {
        var api = Api(_ => Json("{}", HttpStatusCode.Created), out var h);
        var (ok, _, _, problem) = await api.CreateCustomerAsync("   ");

        Assert.False(ok);
        Assert.Contains("name is required", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(h.Sent);   // a round trip that can only 400 is a round trip worth not making
    }

    /// <summary>⚠ A 403 IS THE ONE A CASHIER WILL ACTUALLY HIT, on a till whose operator holds
    /// neither code — or on a device token, which carries no userId for `perm:*` to resolve. The
    /// message has to name the way forward, because "Forbidden" does not.</summary>
    [Fact]
    public async Task A_permission_refusal_tells_the_operator_to_ask_a_supervisor()
    {
        var api = Api(_ => Text("Forbidden", HttpStatusCode.Forbidden), out _);
        var (ok, _, _, problem) = await api.CreateCustomerAsync("Ada");

        Assert.False(ok);
        Assert.Contains("supervisor", problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Forbidden", problem);
    }

    /// <summary>⚠ Offline is a HARD refusal here, unlike a sale — and the message must say why rather
    /// than implying a retry helps, because membership numbers come from a tenant-wide counter and
    /// two offline tills would mint the same one.</summary>
    [Fact]
    public async Task Offline_says_the_member_was_NOT_added_and_why()
    {
        var api = Api(_ => throw new HttpRequestException("no route to host"), out _);
        var (ok, _, _, problem) = await api.CreateCustomerAsync("Ada");

        Assert.False(ok);
        Assert.Contains("NOT been added", problem);
        Assert.Contains("online", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_servers_own_words_reach_the_operator_on_a_400()
    {
        var api = Api(_ => Text("name is required.", HttpStatusCode.BadRequest), out _);
        var (ok, _, _, problem) = await api.CreateCustomerAsync("Ada");

        Assert.False(ok);
        Assert.Equal("name is required.", problem);
    }

    // ── tiers ──

    [Fact]
    public async Task SetMembership_posts_only_a_tier_id()
    {
        var api = Api(_ => Json("{}", HttpStatusCode.Created), out var h);
        var (ok, problem) = await api.SetMembershipAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));

        Assert.True(ok);
        Assert.Null(problem);
        Assert.Equal("/api/v1/customers/11111111-1111-1111-1111-111111111111/membership",
            h.Sent.Single().RequestUri!.AbsolutePath);
        // ⚠ NO name and NO rate. The tier owns both, so re-rating "Gold" in the portal moves every
        // Gold member at once instead of leaving a snapshot on whichever till assigned it.
        Assert.Contains("\"tierId\":\"33333333-3333-3333-3333-333333333333\"", h.LastBody);
        Assert.DoesNotContain("autoDiscountRate", h.LastBody);
        Assert.DoesNotContain("\"tier\":", h.LastBody);
    }

    [Fact]
    public async Task Setting_a_tier_without_permission_names_the_supervisor()
    {
        var api = Api(_ => Text("Forbidden", HttpStatusCode.Forbidden), out _);
        var (ok, problem) = await api.SetMembershipAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("supervisor", problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deactivating a tier stops new assignments while leaving existing members working, so
    /// the server's 400 is the sentence that explains it.</summary>
    [Fact]
    public async Task An_inactive_tier_refusal_carries_the_servers_reason()
    {
        var api = Api(_ => Text("That tier is not active.", HttpStatusCode.BadRequest), out _);
        var (ok, problem) = await api.SetMembershipAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal("That tier is not active.", problem);
    }

    [Fact]
    public async Task The_tier_catalogue_reads_the_rate_the_discount_is_computed_from()
    {
        var api = Api(_ => Json("""
            [{"id":"33333333-3333-3333-3333-333333333333","name":"Gold","autoDiscountRate":0.10,
              "durationMonths":12,"active":true,"sortOrder":1,"memberCount":42}]
            """), out var h);

        var tiers = await api.GetLoyaltyTiersAsync();
        Assert.Equal("/api/v1/loyalty/tiers", h.Sent.Single().RequestUri!.AbsolutePath);
        var gold = tiers!.Single();
        Assert.Equal("Gold", gold.Name);
        Assert.Equal(0.10m, gold.AutoDiscountRate);
        // ⚠ The label both tills must print for it — shared, so a receipt reads the same either side.
        Assert.Equal("Gold 10%", Plutus.SharedKernel.MemberDiscount.Label(gold.Name!, gold.AutoDiscountRate));
    }
}
