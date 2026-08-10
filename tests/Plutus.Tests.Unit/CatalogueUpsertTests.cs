using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// The catalogue feed reaching the till's own row — schema v5 (WP10 / cutover step 25).
///
/// ⚠ WHY THESE EXIST. `TillStore` has TWO hand-written upsert branches over `CatalogueItem`, and
/// keeping them in step is a manual job that has already been got wrong once: `StockUntracked` was
/// carried by the feed for weeks while the field-by-field update branch did not copy it, so an item
/// that BECAME untracked in the portal stayed tracked on every till that already held it. Only
/// brand-new items got the flag. Nothing failed, nothing logged, and the symptom — a carrier bag
/// decrementing stock — looks like a stock-count problem rather than a sync one.
///
/// So the last test here is deliberately a REFLECTION test: it fails when a field is added to
/// `CatalogueItem` and not copied on update, without anybody having to remember to write a test.
/// </summary>
public class CatalogueUpsertTests
{
    private static async Task<(TillStore Store, SqliteConnection Conn)> NewStoreAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<TillDbContext>().UseSqlite(conn).Options;
        var db = new TillDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return (new TillStore(db), conn);
    }

    private static CatalogueItemDto Dto(
        string idOne = "BAT001", string name = "Batman #1",
        string? brand = "DC", string? desc = "First appearance", long costPence = 250,
        bool untracked = false, bool removed = false) =>
        new(
            Id: DeterministicGuid.ForItem(Guid.Parse("22222222-2222-2222-2222-222222222222"), idOne),
            IdOne: idOne,
            Name: name,
            PricePence: 599,
            ExPricePence: 499,
            TaxId: 1,
            CategoryId: null,
            StockUntracked: untracked,
            Removed: removed,
            UpdatedAtUtc: new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
            Brand: brand,
            Desc: desc,
            CostPence: costPence);

    [Fact]
    public async Task A_new_item_arrives_with_its_brand_description_and_cost()
    {
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[] { Dto() }, "cursor-1");

        var item = await store.FindByBarcodeAsync("BAT001");
        Assert.NotNull(item);
        Assert.Equal("DC", item!.Brand);
        Assert.Equal("First appearance", item.Desc);
        Assert.Equal(250, item.CostPence);
    }

    [Fact]
    public async Task An_item_that_GAINS_a_brand_later_gets_it_on_a_till_that_already_held_it()
    {
        // ⚠ THIS IS THE StockUntracked BUG, ONE FIELD ALONG. The update branch is separate from the
        // insert branch, so a field copied in one and not the other reaches only items the till has
        // never seen before — and every existing item keeps the old value for ever.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[] { Dto(brand: null, desc: null, costPence: 0) }, "c1");
        await store.ApplyCatalogueAsync(new[] { Dto(brand: "Marvel", desc: "Reissue", costPence: 399) }, "c2");

        var item = await store.FindByBarcodeAsync("BAT001");
        Assert.Equal("Marvel", item!.Brand);
        Assert.Equal("Reissue", item.Desc);
        Assert.Equal(399, item.CostPence);
    }

    [Fact]
    public async Task Searching_by_BRAND_finds_the_item()
    {
        // ⚠ The parity gap this column closed. `ItemSearch` matches name, barcode and brand; the
        // till's row had no brand, so `SearchAsync` passed null and matched two fields where the
        // server and the web till matched three. "Marvel" found nothing here and everything there.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[]
        {
            Dto("SPI001", "Spider-Man #1", brand: "Marvel"),
            Dto("BAT001", "Batman #1", brand: "DC"),
        }, "c1");

        var hits = await store.SearchAsync("marvel");

        Assert.Equal("SPI001", Assert.Single(hits).IdOne);
    }

    [Fact]
    public async Task A_brand_search_still_narrows_across_words()
    {
        // Word mode ANDs the tokens — brand plus part of a name must behave like any other pair.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[]
        {
            Dto("SPI001", "Spider-Man #1", brand: "Marvel"),
            Dto("THO001", "Thor #1", brand: "Marvel"),
        }, "c1");

        var hits = await store.SearchAsync("marvel thor");

        Assert.Equal("THO001", Assert.Single(hits).IdOne);
    }

    [Fact]
    public async Task An_item_with_no_brand_is_still_found_by_name()
    {
        // ⚠ The SQL prefilter gained a brand clause; a null brand must not exclude a row from it.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[] { Dto("NOB001", "Nobrand Thing", brand: null) }, "c1");

        Assert.Single(await store.SearchAsync("nobrand"));
    }

    [Fact]
    public void EVERY_catalogue_field_is_copied_on_UPDATE_not_just_on_insert()
    {
        // ⚠ A REFLECTION TEST, on purpose. `TillStore` has two hand-written upsert branches and
        // keeping them in step is a manual job that has already been got wrong once. This fails the
        // moment a property is added to `CatalogueItem` without being copied in the update branch —
        // which is the only kind of guard that survives somebody adding a field in six months'
        // time without reading this file.
        // ⚠ Walk up to the repo root rather than hardcoding a relative depth — the test binary's
        // directory depth changes with the target framework and configuration.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;

        Assert.NotNull(dir);

        var path = System.IO.Path.Combine(dir!.FullName, "src", "Plutus.Client.Storage", "TillStore.cs");
        Assert.True(System.IO.File.Exists(path), $"Expected TillStore at {path}");

        var source = System.IO.File.ReadAllText(path);

        var fields = typeof(CatalogueItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            // Id is the key the row is FOUND by; it is never reassigned on an update.
            .Where(n => n != "Id")
            .ToList();

        Assert.NotEmpty(fields);

        // ⚠ BOTH BRANCHES, hence the count of two rather than a `Contains`. `TillStore` updates a
        // catalogue row in two places — `ApplyCatalogueAsync`'s field-by-field merge and the sync
        // loop's — and a field copied in only ONE of them is still broken for half the callers.
        // A plain "does it appear anywhere" check passes that mutation, which is exactly the shape
        // of bug this test is for.
        var missing = fields
            .Where(f => CountOf(source, $"existing.{f} = ") < 2)
            .ToList();

        Assert.True(missing.Count == 0,
            "These CatalogueItem fields are not copied in BOTH of TillStore's update branches, so "
            + "an item the till ALREADY HOLDS keeps its old value while new items get the right one "
            + "— the StockUntracked bug: " + string.Join(", ", missing));

        static int CountOf(string haystack, string needle)
        {
            var n = 0;
            for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                n++;
            return n;
        }
    }
}

/// <summary>
/// A binned item must stop selling — including on a till with no network.
///
/// ⚠ THIS IS WHAT THE TOMBSTONE IS FOR, and it is the rule most likely to be quietly broken by a
/// new read path. "Not in this page of the feed" and "withdrawn from sale" are indistinguishable to
/// a client that only ever receives upserts, so the feed sends `Removed: true` rather than simply
/// omitting the row — and EVERY read on the till has to honour it. One that forgets puts a
/// withdrawn product back on sale on an offline till, indefinitely, with nothing to notice.
///
/// ⚠ The withdrawal can be a recall. That is the case worth having tests for.
/// </summary>
public class CatalogueTombstoneTests
{
    private static async Task<(TillStore Store, Microsoft.Data.Sqlite.SqliteConnection Conn)> NewStoreAsync()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<TillDbContext>().UseSqlite(conn).Options;
        var db = new TillDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return (new TillStore(db), conn);
    }

    private static CatalogueItemDto Dto(string idOne, string name, bool removed) =>
        new(
            Id: DeterministicGuid.ForItem(Guid.Parse("33333333-3333-3333-3333-333333333333"), idOne),
            IdOne: idOne, Name: name,
            PricePence: 599, ExPricePence: 499, TaxId: 1, CategoryId: null,
            StockUntracked: false, Removed: removed,
            UpdatedAtUtc: new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc),
            Brand: "DC");

    [Fact]
    public async Task A_binned_item_cannot_be_SCANNED()
    {
        // ⚠ The hot path. If a barcode still resolves, the item goes straight into a basket.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[] { Dto("BAT001", "Batman #1", removed: false) }, "c1");
        Assert.NotNull(await store.FindByBarcodeAsync("BAT001"));

        await store.ApplyCatalogueAsync(new[] { Dto("BAT001", "Batman #1", removed: true) }, "c2");

        Assert.Null(await store.FindByBarcodeAsync("BAT001"));
    }

    [Fact]
    public async Task A_binned_item_cannot_be_SEARCHED_for()
    {
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[] { Dto("BAT001", "Batman #1", removed: true) }, "c1");

        Assert.Empty(await store.SearchAsync("batman"));
        // ⚠ Brand too — the new searched field must not become a back door to a withdrawn item.
        Assert.Empty(await store.SearchAsync("DC"));
    }

    [Fact]
    public async Task A_binned_item_is_not_offered_by_the_BROWSE_list()
    {
        // ⚠ The browse list's whole purpose is tapping a row to add it to a basket, so a withdrawn
        // item appearing there is one tap from being sold.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[]
        {
            Dto("BAT001", "Batman #1", removed: true),
            Dto("SPI001", "Spider-Man #1", removed: false),
        }, "c1");

        var browsed = await store.BrowseAsync();

        Assert.Equal("SPI001", Assert.Single(browsed).IdOne);
    }

    [Fact]
    public async Task Restoring_an_item_brings_it_back()
    {
        // ⚠ The tombstone is not a delete. A product pulled and then cleared has to sell again
        // without anyone re-creating it — and its id must be the same one, or its sales history
        // splits in two.
        var (store, conn) = await NewStoreAsync();
        using var _ = conn;

        await store.ApplyCatalogueAsync(new[] { Dto("BAT001", "Batman #1", removed: true) }, "c1");
        Assert.Null(await store.FindByBarcodeAsync("BAT001"));

        await store.ApplyCatalogueAsync(new[] { Dto("BAT001", "Batman #1", removed: false) }, "c2");

        var back = await store.FindByBarcodeAsync("BAT001");
        Assert.NotNull(back);
        Assert.Equal(Dto("BAT001", "Batman #1", false).Id, back!.Id);
    }
}
