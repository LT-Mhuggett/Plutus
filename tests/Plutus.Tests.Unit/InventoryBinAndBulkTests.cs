using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Catalogue;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Repository.QueryParameters;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// FE5.3/5.4/5.5 (DoD): the Bin hides items from every selling surface without deleting anything;
/// bulk edits work by tick-list AND by filter criteria, are capped, and record prior values;
/// untracked items post no stock movement but are still sold and reported.
/// </summary>
public class InventoryBinAndBulkTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "bulk-test" };

    private static InventoryBulkController Controller(MySqlDbContext db) =>
        new InventoryBulkController(db, new FixedTenantContext(Tenant))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "test")),
                },
            },
        };

    private static SqliteConnection Open(out Guid businessId, out Guid catA, out Guid catB)
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        var b = new Business { Id = Uuid7.New(), Name = "Co", NameAbbr = "CO", VatIN = "-" };
        ctx.Business.Add(b);
        var a = new Category { IdOne = Uuid7.New(), IdTwo = b.Id, Name = "Comics", Description = "-" };
        var c2 = new Category { IdOne = Uuid7.New(), IdTwo = b.Id, Name = "Toys", Description = "-" };
        ctx.Category.AddRange(a, c2);
        ctx.Taxes.Add(new Tax { IdOne = 1, IdTwo = b.Id, Name = "Standard", Rate = 1.2 });
        ctx.SaveChanges();
        businessId = b.Id; catA = a.IdOne; catB = c2.IdOne;
        return conn;
    }

    private static void AddItem(SqliteConnection conn, Guid businessId, Guid catId, string id, string name,
                                string brand = "Acme", bool binned = false, bool untracked = false)
    {
        using var db = Ctx(conn);
        db.Items.Add(new Item
        {
            IdOne = id, IdTwo = businessId, Name = name, Brand = brand, Desc = "",
            Cost = 1, ExPrice = 1, Price = 1.2m, TaxId = 1, CatId = catId,
            BinnedAtUtc = binned ? DateTime.UtcNow : null, StockUntracked = untracked,
        });
        db.SaveChanges();
    }

    private static List<Item> Filtered(MySqlDbContext db, ItemParameters p) =>
        db.Items.Where(p.GetExpression()).ToList();

    // ── FE5.4 the Bin ──

    [Fact]
    public void Binned_items_are_hidden_by_the_shared_item_filter()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "LIVE-1", "Live item");
        AddItem(conn, b, catA, "BIN-1", "Binned item", binned: true);

        using var db = Ctx(conn);
        // default: no criteria at all — "no filters" must STILL exclude the bin
        var visible = Filtered(db, new ItemParameters());
        Assert.Single(visible);
        Assert.Equal("LIVE-1", visible[0].IdOne);

        // the Bin view asks for them explicitly
        var binned = Filtered(db, new ItemParameters { Binned = true });
        Assert.Single(binned);
        Assert.Equal("BIN-1", binned[0].IdOne);
    }

    [Fact]
    public void Binned_items_are_hidden_from_search_and_category_filters_too()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "LIVE-2", "Batman Year One");
        AddItem(conn, b, catA, "BIN-2", "Batman Binned", binned: true);

        using var db = Ctx(conn);
        Assert.Single(Filtered(db, new ItemParameters { Search = "batman", MatchAllWords = true }));
        Assert.Single(Filtered(db, new ItemParameters { CatId = catA }));
    }

    [Fact]
    public void Nothing_is_deleted_when_an_item_is_binned()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "KEEP-1", "Still here");

        using (var db = Ctx(conn))
            Assert.IsType<OkObjectResult>(Controller(db).Bulk(
                new BulkBody("bin", null, null, new List<string> { "KEEP-1" }, null), default).Result);

        using (var db = Ctx(conn))
        {
            // row intact — a historic sale line referencing it still resolves
            var item = db.Items.IgnoreQueryFilters().Single(i => i.IdOne == "KEEP-1");
            Assert.NotNull(item.BinnedAtUtc);
            Assert.Equal("Still here", item.Name);
        }
    }

    [Fact]
    public async Task Restore_takes_an_item_back_out_of_the_bin()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "BIN-3", "Restore me", binned: true);

        using (var db = Ctx(conn))
            Assert.IsType<OkObjectResult>(await Controller(db).Bulk(
                new BulkBody("restore", null, null, new List<string> { "BIN-3" }, null), default));

        using (var db = Ctx(conn))
        {
            Assert.Single(Filtered(db, new ItemParameters()));          // visible again
            Assert.Empty(Filtered(db, new ItemParameters { Binned = true }));
        }
    }

    // ── FE5.3 bulk edit ──

    [Fact]
    public async Task Bulk_set_category_by_tick_list()
    {
        using var conn = Open(out var b, out var catA, out var catB);
        AddItem(conn, b, catA, "A-1", "One");
        AddItem(conn, b, catA, "A-2", "Two");
        AddItem(conn, b, catA, "A-3", "Untouched");

        using (var db = Ctx(conn))
        {
            var res = await Controller(db).Bulk(
                new BulkBody("set-category", null, catB, new List<string> { "A-1", "A-2" }, null), default);
            var ok = Assert.IsType<OkObjectResult>(res);
            Assert.Equal(2, ok.Value!.GetType().GetProperty("affected")!.GetValue(ok.Value));
        }
        using (var db = Ctx(conn))
        {
            Assert.Equal(catB, db.Items.Single(i => i.IdOne == "A-1").CatId);
            Assert.Equal(catB, db.Items.Single(i => i.IdOne == "A-2").CatId);
            Assert.Equal(catA, db.Items.Single(i => i.IdOne == "A-3").CatId);   // not in the list
        }
    }

    /// <summary>The "select everything matching this filter" mode — the client never loaded the rows.</summary>
    [Fact]
    public async Task Bulk_by_criteria_applies_to_every_match_not_just_a_page()
    {
        using var conn = Open(out var b, out var catA, out var catB);
        for (var i = 0; i < 30; i++) AddItem(conn, b, catA, $"C-{i}", $"Batman {i}");
        AddItem(conn, b, catA, "OTHER-1", "Spider-Man");

        using (var db = Ctx(conn))
        {
            var res = await Controller(db).Bulk(
                new BulkBody("set-brand", "DC", null, null, new BulkCriteria("batman", true, null, false)), default);
            var ok = Assert.IsType<OkObjectResult>(res);
            Assert.Equal(30, ok.Value!.GetType().GetProperty("affected")!.GetValue(ok.Value));
        }
        using (var db = Ctx(conn))
        {
            Assert.Equal(30, db.Items.Count(i => i.Brand == "DC"));
            Assert.Equal("Acme", db.Items.Single(i => i.IdOne == "OTHER-1").Brand);  // untouched
        }
    }

    [Fact]
    public async Task Criteria_count_matches_what_the_bulk_would_affect()
    {
        using var conn = Open(out var b, out var catA, out _);
        for (var i = 0; i < 7; i++) AddItem(conn, b, catA, $"K-{i}", $"Batman {i}");
        AddItem(conn, b, catA, "Z-1", "Something else");

        using var db = Ctx(conn);
        var res = await Controller(db).Count(new BulkCriteria("batman", true, null, false), default);
        var ok = Assert.IsType<OkObjectResult>(res);
        Assert.Equal(7, ok.Value!.GetType().GetProperty("count")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task Ids_and_criteria_together_are_refused()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "X-1", "One");
        using var db = Ctx(conn);
        var res = await Controller(db).Bulk(
            new BulkBody("bin", null, null, new List<string> { "X-1" }, new BulkCriteria(null, false, null, false)), default);
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task Clear_category_moves_items_to_Uncategorised_because_the_column_is_required()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "U-1", "One");

        using (var db = Ctx(conn))
            Assert.IsType<OkObjectResult>(await Controller(db).Bulk(
                new BulkBody("clear-category", null, null, new List<string> { "U-1" }, null), default));

        using (var db = Ctx(conn))
        {
            var item = db.Items.Single(i => i.IdOne == "U-1");
            Assert.NotEqual(catA, item.CatId);
            var cat = db.Category.Single(c => c.IdOne == item.CatId);
            Assert.Equal(InventoryBulkController.UncategorisedName, cat.Name);
        }
    }

    [Fact]
    public async Task An_unknown_category_is_refused()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "V-1", "One");
        using var db = Ctx(conn);
        Assert.IsType<BadRequestObjectResult>(await Controller(db).Bulk(
            new BulkBody("set-category", null, Guid.NewGuid(), new List<string> { "V-1" }, null), default));
    }

    [Fact]
    public async Task The_bulk_audit_records_the_prior_values_so_a_mistake_can_be_unpicked()
    {
        using var conn = Open(out var b, out var catA, out var catB);
        AddItem(conn, b, catA, "AU-1", "One", brand: "OldBrand");

        using (var db = Ctx(conn))
            await Controller(db).Bulk(new BulkBody("set-brand", "NewBrand", null, new List<string> { "AU-1" }, null), default);

        using (var db = Ctx(conn))
        {
            var audit = db.AuditLogs.Single(a => a.Action == "inventory.bulk.set-brand");
            Assert.Contains("OldBrand", audit.DetailJson);   // recoverable
            Assert.Contains("AU-1", audit.DetailJson);
        }
    }

    // ── FE5.5 untracked stock ──

    [Fact]
    public async Task Marking_an_item_untracked_is_a_bulk_action()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "BAG-1", "Carrier bag");

        using (var db = Ctx(conn))
            Assert.IsType<OkObjectResult>(await Controller(db).Bulk(
                new BulkBody("set-untracked", null, null, new List<string> { "BAG-1" }, null), default));

        using (var db = Ctx(conn))
            Assert.True(db.Items.Single(i => i.IdOne == "BAG-1").StockUntracked);
    }

    [Fact]
    public void An_untracked_item_is_still_a_normal_sellable_catalogue_item()
    {
        using var conn = Open(out var b, out var catA, out _);
        AddItem(conn, b, catA, "BAG-2", "Carrier bag", untracked: true);
        using var db = Ctx(conn);
        // untracked must NOT imply hidden — it still scans, sells and reports
        var visible = Filtered(db, new ItemParameters());
        Assert.Single(visible);
        Assert.True(visible[0].StockUntracked);
    }
}
