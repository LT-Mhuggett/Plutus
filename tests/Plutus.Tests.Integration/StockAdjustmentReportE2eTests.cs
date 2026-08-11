using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// The stock ADJUSTMENTS report — who changed a count, by how much, and why.
///
/// ⚠ Matt, 2026-08-11: *"Writing off stock, where is this captured? I need a report on the portal
/// (that will then be reflected in all tills) that shows stock adjustments."*
///
/// ⚠ IT WAS ALREADY CAPTURED. Every write-off has been a `StockMovement` row carrying its reason,
/// its actor and its timestamp since WP5.1. Nothing was ever lost — what did not exist was a way to
/// READ it as a report. `/api/v1/stock/movements` is an item DRILL: it wants an `itemIdOne`, has no
/// date range, and returns raw GUIDs. *"Who has been writing stock off this month?"* is not a
/// question it can answer, and that is the question shrinkage is found by.
///
/// ⚠ THIS IS THE FIRST INTEGRATION COVERAGE ANY STOCK ENDPOINT HAS HAD. `/api/v1/stock/levels`,
/// `/movements` and `/locations` are referenced by no test in this suite. That is worth saying out
/// loud rather than quietly fixing for one endpoint: the ones left uncovered are still uncovered.
///
/// ⚠ EVERY TEST HERE WORKS IN A DATED WINDOW OF ITS OWN (March 2025), because the integration host
/// shares one database across every class. Asserting "there are 3 rows" against a live-ish tenant
/// is how a test starts depending on which tests ran first — and this suite has already been bitten
/// by exactly that (see `DrawerVarianceE2eTests`, where a platform-admin token read cross-tenant).
/// </summary>
public class StockAdjustmentReportE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public StockAdjustmentReportE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>The window this class owns. Nothing else in the suite writes here.</summary>
    private static readonly DateTime Window = new(2025, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private async Task<JsonElement> GetAsync(string url)
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
        using var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<(Guid LocationId, Guid ActorId, string ItemIdOne)> SeedAsync(
        StockMovementType type, DateTime atUtc, int qtyDelta, string reason, bool withActor = true)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "stock-adjust-e2e-seed";

        // ⚠ Seeds its own store rather than assuming one exists. The integration host starts with no
        // Kapow store at all, and `Stores.FirstAsync()` threw "Sequence contains no elements" —
        // which reads as a bug in the report and is nothing of the kind.
        var location = await db.StockLocations.FirstOrDefaultAsync();
        if (location == null)
        {
            var store = await db.Stores.FirstOrDefaultAsync();
            if (store == null)
            {
                var businessId = Guid.NewGuid();
                db.Business.Add(new Business
                {
                    Id = businessId, Name = "Stock adjust e2e", NameAbbr = "SAE", VatIN = "GB0",
                });
                // ⚠ Store ids are database-assigned, so seed, save, then read back.
                store = new Store
                {
                    BusinessId = businessId, ContactNumber = "-", AdLine1 = "-",
                    AdLine2 = "", City = "-", PostCode = "-", Country = "-",
                };
                db.Stores.Add(store);
                await db.SaveChangesAsync();
            }

            location = new StockLocation
            {
                Id = Uuid7.New(), TenantId = Kapow, StoreId = store.Id,
                Type = StockLocationType.Store, Name = "Shop floor",
            };
            db.StockLocations.Add(location);
        }

        var actorId = Guid.Empty;
        if (withActor)
        {
            var person = await db.People.FirstOrDefaultAsync();
            actorId = person?.Id ?? Guid.Empty;
        }

        var itemIdOne = "5010" + Random.Shared.Next(100000, 999999);

        db.StockMovements.Add(new StockMovement
        {
            Id = Uuid7.New(), TenantId = Kapow, StockLocationId = location.Id,
            ItemIdOne = itemIdOne, ItemId = Uuid7.New(), Type = type,
            QtyDelta = qtyDelta, Reason = reason,
            ActorUserId = withActor && actorId != Guid.Empty ? actorId : null,
            AtUtc = atUtc,
        });

        await db.SaveChangesAsync();
        return (location.Id, actorId, itemIdOne);
    }

    /// <summary>
    /// A write-off appears, with its REASON and WHO did it.
    ///
    /// ⚠ The reason and the actor are the entire point. A count that changed by −3 with no reason
    /// and no name is not a report about stock; it is a number that disagrees with the shelf.
    /// </summary>
    [Fact]
    public async Task A_write_off_is_reported_with_its_reason_and_its_actor()
    {
        var (_, _, itemIdOne) = await SeedAsync(
            StockMovementType.WriteOff, Window, -3, "water damage");

        var report = await GetAsync("/api/v1/stock/adjustments?from=2025-03-01&to=2025-03-31");
        var row = report.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("itemIdOne").GetString() == itemIdOne);

        Assert.Equal("WriteOff", row.GetProperty("type").GetString());
        Assert.Equal(-3, row.GetProperty("qtyDelta").GetInt32());
        Assert.Equal("water damage", row.GetProperty("reason").GetString());

        // ⚠ A NAME, NOT A GUID. Nobody recognises a person by their UUID, and a report about
        // accountability that cannot say who is not a report about accountability.
        var actor = row.GetProperty("actor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(actor));
        Assert.NotEqual(Guid.Empty.ToString(), actor);

        // Same for the location.
        Assert.NotEqual("?", row.GetProperty("location").GetString());
    }

    /// <summary>
    /// ⚠ MANUAL MOVEMENTS ONLY. A sale is a stock movement too, and a shop makes hundreds a day —
    /// including them would bury the handful of rows that represent somebody DECIDING to change a
    /// number. A report that lists every sale is a sales report, and there is already one of those.
    /// </summary>
    [Fact]
    public async Task Sales_and_receipts_are_not_stock_adjustments()
    {
        var sold = await SeedAsync(StockMovementType.Sale, Window.AddDays(1), -1, "sale");
        var received = await SeedAsync(StockMovementType.Receipt, Window.AddDays(1), 12, "goods in");
        var adjusted = await SeedAsync(StockMovementType.Adjustment, Window.AddDays(1), 2, "stock take");

        var report = await GetAsync("/api/v1/stock/adjustments?from=2025-03-01&to=2025-03-31");
        var ids = report.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("itemIdOne").GetString()).ToList();

        Assert.Contains(adjusted.ItemIdOne, ids);
        Assert.DoesNotContain(sold.ItemIdOne, ids);
        Assert.DoesNotContain(received.ItemIdOne, ids);
    }

    /// <summary>
    /// ⚠⚠ TODAY'S WRITE-OFF MUST APPEAR IN A RANGE ENDING TODAY, and this is the bug the half-open
    /// upper bound exists to prevent.
    ///
    /// `AtUtc` is a TIMESTAMP and the filter is by DAY. A naive `AtUtc &lt;= to` compares against
    /// MIDNIGHT at the start of the last day, so every movement written during that day vanishes —
    /// and the last day of the range is almost always TODAY, which is the day somebody is most
    /// likely to be asking about. The report would look correct, be quietly incomplete, and the
    /// missing rows would be the newest ones.
    /// </summary>
    [Fact]
    public async Task A_write_off_made_today_appears_in_a_range_that_ends_today()
    {
        var now = DateTime.UtcNow;
        var (_, _, itemIdOne) = await SeedAsync(
            StockMovementType.WriteOff, now, -1, "damaged in transit");

        var today = DateOnly.FromDateTime(now).ToString("yyyy-MM-dd");
        var report = await GetAsync($"/api/v1/stock/adjustments?from={today}&to={today}");

        Assert.Contains(report.GetProperty("rows").EnumerateArray(),
            r => r.GetProperty("itemIdOne").GetString() == itemIdOne);
    }

    /// <summary>
    /// ⚠ AND A WRITE-OFF OUTSIDE THE RANGE STAYS OUT. Without this the test above would pass just as
    /// happily against a filter that had no upper bound at all.
    /// </summary>
    [Fact]
    public async Task A_write_off_outside_the_range_is_not_reported()
    {
        var (_, _, itemIdOne) = await SeedAsync(
            StockMovementType.WriteOff, new DateTime(2025, 1, 5, 12, 0, 0, DateTimeKind.Utc),
            -4, "January write-off");

        var report = await GetAsync("/api/v1/stock/adjustments?from=2025-03-01&to=2025-03-31");

        Assert.DoesNotContain(report.GetProperty("rows").EnumerateArray(),
            r => r.GetProperty("itemIdOne").GetString() == itemIdOne);
    }

    /// <summary>
    /// ⚠ AN UNKNOWN ACTOR IS SHOWN AS "unknown", NEVER DROPPED. Movements folded in from the
    /// pipeline carry no user, and rows written before the operator stamp landed have none either.
    /// Filtering them out would quietly shrink the very report somebody is using to account for
    /// stock — the rows with no name are the ones most worth seeing.
    /// </summary>
    [Fact]
    public async Task A_movement_with_no_actor_is_still_reported_as_unknown()
    {
        var (_, _, itemIdOne) = await SeedAsync(
            StockMovementType.Adjustment, Window.AddDays(2), -2, "no actor recorded", withActor: false);

        var report = await GetAsync("/api/v1/stock/adjustments?from=2025-03-01&to=2025-03-31");
        var row = report.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("itemIdOne").GetString() == itemIdOne);

        Assert.Equal("unknown", row.GetProperty("actor").GetString());
    }

    /// <summary>
    /// ⚠ A CAPPED PAGE SAYS SO. A silently truncated list of write-offs reads as "that is all of
    /// them", which is the one conclusion this report must never let somebody reach by accident.
    /// </summary>
    [Fact]
    public async Task A_capped_page_says_it_was_capped()
    {
        await SeedAsync(StockMovementType.WriteOff, Window.AddDays(3), -1, "one");
        await SeedAsync(StockMovementType.WriteOff, Window.AddDays(3), -1, "two");

        var capped = await GetAsync("/api/v1/stock/adjustments?from=2025-03-01&to=2025-03-31&take=1");
        Assert.True(capped.GetProperty("truncated").GetBoolean());
        Assert.Single(capped.GetProperty("rows").EnumerateArray());

        var whole = await GetAsync("/api/v1/stock/adjustments?from=2025-03-01&to=2025-03-31&take=1000");
        Assert.False(whole.GetProperty("truncated").GetBoolean());
    }

    /// <summary>
    /// ⚠ Reading stock adjustments is a REPORTING permission, and a till operator holding only
    /// `pos.sell` is not entitled to a list of who has been writing stock off.
    /// </summary>
    [Fact]
    public async Task The_report_needs_a_reporting_permission()
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stock/adjustments");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", PlutusAppFactory.OperatorToken("pos.sell", Kapow));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
    }

    /// <summary>⚠ A backwards range is a 400, not an empty list. Silence would read as "no stock
    /// has been written off", which is a very different answer from "you asked me nothing".</summary>
    [Fact]
    public async Task A_backwards_range_is_refused_rather_than_answered_empty()
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/stock/adjustments?from=2025-03-31&to=2025-03-01");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
    }
}
