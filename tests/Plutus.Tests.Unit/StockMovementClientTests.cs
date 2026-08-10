using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Client.Core;
using Plutus.Identity;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// Adjusting stock from a till (WP10 / cutover step 25).
///
/// ⚠⚠ THE ONE THING THAT MATTERS: `qty` IS A DELTA, NOT A COUNT. Stock is an append-only ledger and
/// the server does `level.Quantity += qtyDelta`. Sending the number an operator typed into a box
/// labelled "quantity" ADDS their count to the existing one — 7 on the shelf, operator counts 7,
/// stock becomes 14 — and nothing errors, nothing logs, and nobody finds out until a stock take.
///
/// The only "set it to N" surface is `POST /api/v1/stock/takes`, which this client deliberately
/// does not expose: it sits under a controller-wide portal gate that also covers inter-store
/// transfers, so reaching it from a till would grant transfers by accident.
/// </summary>
public class StockMovementClientTests
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

    private static HttpResponseMessage Ok() => new(HttpStatusCode.Created);

    [Fact]
    public async Task A_write_off_goes_out_NEGATIVE_and_typed_as_a_WriteOff()
    {
        // ⚠ The server REFUSES a positive write-off. The sign comes from the operator's choice of
        // direction, never from something they type — asking somebody to get a minus sign right on
        // a stock ledger at a counter is asking for the wrong answer.
        var api = Api(_ => Ok(), out var handler);

        await api.PostStockMovementAsync("BAT001", "WriteOff", -2, "damaged in transit", storeId: 4);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("WriteOff", sent.RootElement.GetProperty("type").GetString());
        Assert.Equal(-2, sent.RootElement.GetProperty("qty").GetInt32());
        Assert.Equal("damaged in transit", sent.RootElement.GetProperty("reason").GetString());
        Assert.Equal(4, sent.RootElement.GetProperty("storeId").GetInt32());
    }

    [Fact]
    public async Task A_positive_adjustment_goes_out_as_an_Adjustment()
    {
        var api = Api(_ => Ok(), out var handler);

        await api.PostStockMovementAsync("BAT001", "Adjustment", 3, "found behind the counter");

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("Adjustment", sent.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, sent.RootElement.GetProperty("qty").GetInt32());
    }

    [Fact]
    public async Task It_posts_to_the_MOVEMENTS_endpoint_and_never_to_takes()
    {
        // ⚠ `/stock/takes` takes an ABSOLUTE count and lives behind a controller-wide portal gate
        // that also covers inter-store transfers. A till that reached it would be granted transfers
        // by accident — and would be sending a total where the ledger expects a change.
        var api = Api(_ => Ok(), out var handler);

        await api.PostStockMovementAsync("BAT001", "WriteOff", -1, "damaged");

        var url = handler.Last!.RequestUri!.ToString();
        Assert.Equal("https://plutus.test/api/v1/stock/movements", url);
        Assert.DoesNotContain("/takes", url);
    }

    [Fact]
    public async Task The_servers_rule_reaches_the_operator_when_it_refuses()
    {
        // ⚠ "A write-off must have a negative qty" and "reason is required" are the actionable
        // sentences. Replacing them with "couldn't do that" throws the useful part away.
        var api = Api(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"detail":"reason is required for adjustments and write-offs."}"""),
        }, out _);

        var (ok, problem) = await api.PostStockMovementAsync("BAT001", "Adjustment", 3, "");

        Assert.False(ok);
        Assert.Contains("reason is required", problem!);
    }

    [Fact]
    public async Task An_offline_till_is_told_it_did_NOT_record_the_change()
    {
        // ⚠ A stock change is NOT queued like a sale, and the message has to say so. A sale is
        // queued because the money moved whether or not the platform heard; a stock correction is a
        // DECISION, and replaying one against a count that has since changed writes a wrong number.
        // An operator who assumes it queued will not redo it.
        var api = Api(_ => throw new HttpRequestException("no route to host"), out _);

        var (ok, problem) = await api.PostStockMovementAsync("BAT001", "WriteOff", -2, "damaged");

        Assert.False(ok);
        Assert.Contains("NOT been recorded", problem!, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Who may adjust stock from a till.
///
/// ⚠ Matt's decision, 2026-08-11: a **Supervisor** must be able to write off a damaged box at the
/// counter. They hold no portal permission at all, so under `portal.stock.adjust` alone it would
/// have waited for a manager — and stock figures nobody trusts are how that ends.
///
/// ⚠ A **Cashier** must not. The person who can alter a count and the person minding the shelf have
/// to differ, or shrinkage stops being visible.
/// </summary>
public class PosStockAdjustSeedTests
{
    private static IReadOnlyList<string> GrantsFor(string role)
    {
        // ⚠ Reads the SEED TEMPLATE, which is what actually reaches a tenant — `EnsureBuiltInRolesAsync`
        // backfills any grant a built-in role is missing. Asserting against a hand-written list here
        // would pin the test to itself.
        var method = typeof(RbacSeeder).GetMethod("BuiltInRoles",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var roles = (System.Collections.IEnumerable)method!.Invoke(null, null)!;
        foreach (var entry in roles)
        {
            // ⚠ ValueTuple exposes Item1/Item2 as FIELDS, not properties — `GetProperty` returns
            // null and every assertion then fails for a reason that has nothing to do with the seed.
            var name = (string)entry!.GetType().GetField("Item1")!.GetValue(entry)!;
            if (!string.Equals(name, role, StringComparison.Ordinal)) continue;

            var grants = (System.Collections.IEnumerable)entry.GetType().GetField("Item2")!.GetValue(entry)!;
            return grants.Cast<object>()
                .Select(g => (string)g.GetType().GetProperty("Code")!.GetValue(g)!)
                .ToList();
        }

        throw new Xunit.Sdk.XunitException($"No built-in role named '{role}'");
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Company Admin")]
    [InlineData("Store Manager")]
    [InlineData("Supervisor")]
    public void The_roles_that_mind_a_shop_floor_can_adjust_stock(string role)
    {
        Assert.Contains(PermissionCatalogue.PosStockAdjust, GrantsFor(role));
    }

    [Fact]
    public void A_CASHIER_cannot()
    {
        // ⚠ The load-bearing half. If this ever passes, shrinkage has become invisible.
        Assert.DoesNotContain(PermissionCatalogue.PosStockAdjust, GrantsFor("Cashier"));
    }

    [Fact]
    public void The_code_is_in_the_catalogue_or_every_grant_of_it_is_rejected_at_write_time()
    {
        // ⚠ Unknown codes are refused when a grant is saved, so a permission missing from `All` is
        // one that cannot be granted at all — and the seeder would fail silently on that role.
        Assert.Contains(PermissionCatalogue.PosStockAdjust, PermissionCatalogue.All);
    }

    [Fact]
    public void It_is_NOT_ceiling_capable()
    {
        // A stock adjustment has no money amount, so a pence ceiling on it would be meaningless —
        // and a meaningless ceiling in the UI invites somebody to set one and believe it.
        Assert.DoesNotContain(PermissionCatalogue.PosStockAdjust, PermissionCatalogue.CeilingCapable);
    }
}
