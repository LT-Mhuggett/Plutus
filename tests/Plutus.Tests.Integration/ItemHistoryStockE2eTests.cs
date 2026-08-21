using System;
using System.Collections.Generic;
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
/// An item's History must show what happened to its STOCK, not only to its price.
///
/// ⚠⚠ MATT'S CONDITION, 2026-08-21. He agreed to an "Add/edit stock" permission that can be switched
/// on for individuals *"so long as all edits to stock items are tracked for each item (History)"*.
///
/// The tracking already existed — every movement has always been a `StockMovement` row carrying its
/// item, a signed quantity, a reason, an actor and a timestamp — but `ItemHistoryController` read
/// **`AuditLogs` only**, so the History screen showed price and barcode edits and **not one stock
/// movement**. One item, two trails, and only one of them visible to the person asking the question.
///
/// ⚠ These tests are the condition, expressed as a test. If the merge is ever removed, the permission
/// it was granted alongside becomes ungoverned.
/// </summary>
public class ItemHistoryStockE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public ItemHistoryStockE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Tenant = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>An item, a stock location, and one movement of each kind that a PERSON causes.</summary>
    private async Task<(string ItemId, Guid ActorId, string Token)> SeedAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "item-history-stock-e2e";

        var businessId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var itemId = "HIST-STOCK-" + Guid.NewGuid().ToString("N")[..6];
        var locationId = Uuid7.New();
        var actorId = Uuid7.New();

        db.Business.Add(new Business { Id = businessId, Name = "Hist E2E", NameAbbr = "HE2E", VatIN = "GB0" });

        // ⚠ A StockMovement FKs to a StockLocation, which FKs to a Store — so both must exist or
        // SaveChanges answers a bare "FOREIGN KEY constraint failed" that names nothing. ⚠ Store ids
        // are database-assigned, so seed, save, then read the id back. Same shape as
        // `StockAdjustmentReportE2eTests`.
        var store = new Store
        {
            BusinessId = businessId, ContactNumber = "-", AdLine1 = "-",
            AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        db.StockLocations.Add(new StockLocation
        {
            Id = locationId, TenantId = Tenant, StoreId = store.Id,
            Type = StockLocationType.Store, Name = "Shop floor",
        });
        db.Taxes.Add(new Tax { IdOne = 1, IdTwo = businessId, Name = "20%", Rate = 1.2 });
        db.Category.Add(new Category { IdOne = catId, IdTwo = businessId, Name = "Books", Description = "seed" });
        db.Items.Add(new Item
        {
            IdOne = itemId, IdTwo = businessId, Name = "Tracked", Brand = "-", Desc = "",
            Cost = 1m, ExPrice = 10m, Price = 12m, TaxId = 1, CatId = catId,
        });

        // The person whose name must appear against each row.
        // ⚠ Every one of these is REQUIRED by the legacy entity — a partial Employee fails at
        // SaveChanges with "The Email field is required", which reads like a code fault and is a
        // seed fault. Same shape as `TillOperatorsE2eTests`.
        db.Employees.Add(new Employee
        {
            Id = actorId, BusinessId = businessId, FName = "Sam", LName = "Stockman",
            Email = $"sam.{Guid.NewGuid():N}@example.test", Active = true, StoreId = store.Id,
            Mobile = "-", NIN = "-", AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });

        // ⚠ One of each kind a HUMAN causes, plus a Sale — which must NOT appear. Sales are trading,
        // not editing, and folding them in would bury the rows somebody is actually looking for.
        var when = DateTime.UtcNow.AddMinutes(-30);
        db.StockMovements.AddRange(
            new StockMovement
            {
                Id = Uuid7.New(), TenantId = Tenant, StockLocationId = locationId, ItemIdOne = itemId,
                Type = StockMovementType.Receipt, QtyDelta = 12, Reason = "Delivery 4471",
                ActorUserId = actorId, AtUtc = when,
            },
            new StockMovement
            {
                Id = Uuid7.New(), TenantId = Tenant, StockLocationId = locationId, ItemIdOne = itemId,
                Type = StockMovementType.WriteOff, QtyDelta = -3, Reason = "Water damage",
                ActorUserId = actorId, AtUtc = when.AddMinutes(5),
            },
            new StockMovement
            {
                Id = Uuid7.New(), TenantId = Tenant, StockLocationId = locationId, ItemIdOne = itemId,
                Type = StockMovementType.Adjustment, QtyDelta = 1, Reason = "Miscount",
                ActorUserId = actorId, AtUtc = when.AddMinutes(10),
            },
            new StockMovement
            {
                Id = Uuid7.New(), TenantId = Tenant, StockLocationId = locationId, ItemIdOne = itemId,
                Type = StockMovementType.Sale, QtyDelta = -1, Reason = null,
                ActorUserId = actorId, AtUtc = when.AddMinutes(15),
            });

        await db.SaveChangesAsync();

        // The history endpoint is gated `portal.reports.view` — naming who changed a price is a
        // supervisory record, not a stock task.
        await Plutus.Identity.RbacSeeder.EnsureBuiltInRolesAsync(db, Tenant);
        var role = await db.RbacRoles.FirstAsync(r => r.Name == "Store Manager" && r.TenantId == Tenant);
        var readerId = Uuid7.New();
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = Tenant, UserId = readerId, RoleId = role.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return (itemId, actorId, PlutusAppFactory.OperatorTokenFor(readerId, "pos.sell", Tenant));
    }

    private async Task<JsonElement> HistoryAsync(string itemId, string token)
    {
        var client = _f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/items/{itemId}/history");
        req.Headers.Authorization = new("Bearer", token);
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    private static List<(string Type, string Detail, string By)> RowsOf(JsonElement body)
    {
        var arr = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("rows");
        return arr.EnumerateArray().Select(r => (
            r.GetProperty("type").GetString() ?? "",
            r.GetProperty("detail").GetString() ?? "",
            r.TryGetProperty("by", out var b) ? b.GetString() ?? "" : "")).ToList();
    }

    /// <summary>
    /// ⚠⚠ THE CONDITION ITSELF. Every hand-made stock change appears against the item, signed, with its
    /// reason and the person who made it.
    /// </summary>
    [Fact]
    public async Task Every_hand_made_stock_change_appears_in_the_items_history()
    {
        var (itemId, _, token) = await SeedAsync();
        var rows = RowsOf(await HistoryAsync(itemId, token));

        var received = Assert.Single(rows.Where(r => r.Type == "Stock received"));
        Assert.Contains("+12", received.Detail);
        Assert.Contains("Delivery 4471", received.Detail);

        var written = Assert.Single(rows.Where(r => r.Type == "Stock written off"));
        Assert.Contains("Water damage", written.Detail);

        var adjusted = Assert.Single(rows.Where(r => r.Type == "Stock adjusted"));
        Assert.Contains("Miscount", adjusted.Detail);
    }

    /// <summary>
    /// ⚠⚠ A NEGATIVE IS SHOWN AS A NEGATIVE. "−3" reads as what happened; a bare "3" beside "written
    /// off" makes the reader do the arithmetic, and on a stock trail that is how a shortfall gets
    /// mis-read as a delivery.
    /// </summary>
    [Fact]
    public async Task A_write_off_is_signed_so_it_cannot_be_read_as_an_increase()
    {
        var (itemId, _, token) = await SeedAsync();
        var rows = RowsOf(await HistoryAsync(itemId, token));

        var written = Assert.Single(rows.Where(r => r.Type == "Stock written off"));
        Assert.StartsWith("-3", written.Detail);
        Assert.DoesNotContain("+3", written.Detail);
    }

    /// <summary>
    /// ⚠⚠ SALES ARE NOT EDITS, AND THIS IS THE LINE THAT KEEPS THE SCREEN USEFUL. A busy item has
    /// thousands of sale movements; folding them in would bury the handful of rows a person came to
    /// find. The audit question is *"who changed this by hand, and why"*.
    /// </summary>
    [Fact]
    public async Task Selling_the_item_does_NOT_appear_as_a_stock_edit()
    {
        var (itemId, _, token) = await SeedAsync();
        var rows = RowsOf(await HistoryAsync(itemId, token));

        Assert.DoesNotContain(rows, r => r.Type.Contains("Sale", StringComparison.OrdinalIgnoreCase));
        // Three human movements were seeded; the fourth was a sale.
        Assert.Equal(3, rows.Count(r => r.Type.StartsWith("Stock ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// ⚠ A Guid answers nobody's question. The point of a history is the name of the person, and
    /// `ItemHistoryController` already resolved actors for audit rows — this proves stock rows go
    /// through the same resolution rather than arriving as raw ids.
    /// </summary>
    [Fact]
    public async Task The_stock_rows_name_the_person_not_a_guid()
    {
        var (itemId, _, token) = await SeedAsync();
        var rows = RowsOf(await HistoryAsync(itemId, token));

        var stock = rows.Where(r => r.Type.StartsWith("Stock ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(stock);
        Assert.All(stock, r => Assert.Contains("Sam Stockman", r.By));
    }

    /// <summary>
    /// ⚠ The two trails are MERGED, not concatenated — the screen reads newest-first across both, so a
    /// price change and a write-off an hour apart appear in the order they happened. Concatenating
    /// would put every stock row after every audit row whatever the clock said.
    /// </summary>
    [Fact]
    public async Task The_two_trails_are_ordered_together_newest_first()
    {
        var (itemId, _, token) = await SeedAsync();
        var body = await HistoryAsync(itemId, token);
        var arr = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("rows");

        var times = arr.EnumerateArray().Select(r => r.GetProperty("atUtc").GetDateTime()).ToList();
        Assert.Equal(times.OrderByDescending(t => t).ToList(), times);
    }
}
