using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// **WP10 #4 — the Bin, and putting an item back.**
///
/// ⚠⚠ THE FINDING THAT MADE THIS A SCREEN: a binned item reaches a till as a **tombstone**.
/// `CatalogueChangesController` sends `Removed: r.BinnedAtUtc != null` and the till DELETES it —
/// deliberately, so a withdrawn product stops scanning even on a till that has been offline since.
/// MAUI's item list is a capped read of that local SQLite, so there was **nothing to restore from**
/// and the Bin had to become a server-backed view.
///
/// ⚠ These pin the two server behaviours MAUI now depends on: the Bin LISTS only binned items, and
/// `restore` puts one back. Both already existed; neither had a MAUI caller, which is the
/// "built and wired to nothing" pattern this project has hit repeatedly.
/// </summary>
public class BinRestoreE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public BinRestoreE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = KnownTenants.Kapow;

    private const string Live = "BIN-E2E-LIVE";
    private const string Binned = "BIN-E2E-BINNED";

    /// <summary>Set by the seed — the `Index` endpoint scopes on it.</summary>
    private static Guid BusinessId;

    private HttpRequestMessage R(HttpMethod m, string url, string scope = PlutusPolicies.PlatformAdmin, object body = null)
    {
        var req = new HttpRequestMessage(m, url) { Content = body == null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(scope, Kapow));

        // ⚠⚠ THE LEGACY `Index` ENDPOINT TAKES ITS SCOPE FROM A **HEADER**, not the token:
        // `CompositeApiControllerBaseR.Index([FromHeader] TId2 businessId, …)` answers a plain-text
        // 400 "Business ID not provided" without it. That 400 is what a JSON parser meets as
        // 'B' is an invalid start of a value — which is how this surfaced.
        if (BusinessId != Guid.Empty) req.Headers.Add("BusinessId", BusinessId.ToString());
        return req;
    }

    /// <summary>One item on sale and one withdrawn, so "the Bin shows only the Bin" is a real
    /// assertion rather than a tautology over an empty catalogue.</summary>
    private async Task SeedAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "bin-e2e-seed";

        // ⚠ THE SCOPE IS SET BEFORE THE SHORT-CIRCUIT. The seed runs once, but every test calls
        // it — and a second call returning early without setting `BusinessId` would leave the
        // header off and answer a plain-text 400.
        BusinessId = await db.Business.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync();

        // ⚠⚠ RE-BINNED ON EVERY SEED, NOT ONLY ON FIRST CREATION. These tests share one
        // fixture database and one of them RESTORES the binned item — so a seed that short-circuited
        // left every later test looking at an empty Bin. It read as "the Bin view is broken" and
        // was an order dependency. Making the seed assert the state it needs costs one update.
        var existing = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.IdOne == Binned);
        if (existing is not null)
        {
            existing.BinnedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
            return;
        }

        var bizId = await db.Business.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync();
        if (bizId == Guid.Empty)
        {
            bizId = Guid.NewGuid();
            db.Business.Add(new Business { Id = bizId, Name = "Bin E2E", NameAbbr = "BINE", VatIN = "GB0" });
        }

        // ⚠ SET AGAIN HERE, because the read above answers Guid.Empty on a FRESH database and
        // this branch is what creates the row. Without this the first test to run seeds a
        // business and then sends no header — a plain-text 400 that a JSON parser meets as
        // 'B' is an invalid start of a value.
        BusinessId = bizId;

        if (!await db.Taxes.IgnoreQueryFilters().AnyAsync(t => t.IdOne == 1 && t.IdTwo == bizId))
            db.Taxes.Add(new Tax { IdOne = 1, IdTwo = bizId, Name = "VAT", Rate = 1.2 });

        // ⚠⚠ `Item.CatId` IS A REAL FOREIGN KEY. Omitting it leaves Guid.Empty, which no
        // Category row has — SQLite answers "FOREIGN KEY constraint failed" and the seed dies
        // before a single assertion runs. Caught on the first execution of this file.
        var catId = Guid.NewGuid();
        db.Category.Add(new Category { IdOne = catId, IdTwo = bizId, Name = "Bin E2E", Description = "seed" });

        db.Items.Add(new Item
        {
            IdOne = Live, IdTwo = bizId, Name = "Still on sale", Brand = "-", Desc = "",
            Cost = 1m, ExPrice = 1m, Price = 1m, TaxId = 1, CatId = catId,
        });

        db.Items.Add(new Item
        {
            IdOne = Binned, IdTwo = bizId, Name = "Withdrawn thing", Brand = "-", Desc = "",
            Cost = 2m, ExPrice = 2m, Price = 2m, TaxId = 1, CatId = catId,
            BinnedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
        });

        await db.SaveChangesAsync();
    }

    private async Task<DateTime?> BinnedAtAsync(string idOne)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return await db.Items.IgnoreQueryFilters().Where(i => i.IdOne == idOne)
            .Select(i => i.BinnedAtUtc).FirstAsync();
    }

    /// <summary>
    /// ⚠⚠ `Binned=true` SHOWS THE BIN AND ONLY THE BIN. This is the list MAUI's Bin screen is built
    /// on, and if it leaked live items an operator would be offered "put it back" for something that
    /// was never withdrawn.
    /// </summary>
    [Fact]
    public async Task The_bin_view_lists_binned_items_and_not_live_ones()
    {
        await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Get, "/api/Item/Index?PageNumber=1&PageSize=200&Binned=true"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var ids = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Select(i => i.GetProperty("idOne").GetString()).ToList();

        Assert.Contains(Binned, ids);
        Assert.DoesNotContain(Live, ids);
    }

    /// <summary>⚠ And the DEFAULT list is the mirror image — a withdrawn item must not reappear on
    /// the ordinary catalogue view, which is what "withdrawn from sale" means.</summary>
    [Fact]
    public async Task The_default_list_excludes_the_bin()
    {
        await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Get, "/api/Item/Index?PageNumber=1&PageSize=200"));

        var ids = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Select(i => i.GetProperty("idOne").GetString()).ToList();

        Assert.Contains(Live, ids);
        Assert.DoesNotContain(Binned, ids);
    }

    /// <summary>
    /// ⚠⚠ THE ROUND TRIP. Restore clears `BinnedAtUtc`, which is what makes the item sellable again
    /// on every till — including offline ones, because the next catalogue delta sends it as an
    /// upsert rather than a tombstone.
    ///
    /// ⚠ THE ID IS UNCHANGED, and that is the point of binning rather than deleting: past sale
    /// lines still resolve and the item's history stays in one piece.
    /// </summary>
    [Fact]
    public async Task Restoring_puts_it_back_and_keeps_its_id()
    {
        await SeedAsync();
        Assert.NotNull(await BinnedAtAsync(Binned));

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, "/api/v1/items/bulk", body: new { action = "restore", ids = new[] { Binned } }));
        Assert.True(resp.IsSuccessStatusCode, $"restore answered {(int)resp.StatusCode}");

        Assert.Null(await BinnedAtAsync(Binned));

        // ⚠ Back on the ordinary list, under the SAME id.
        using var list = await _f.CreateClient().SendAsync(
            R(HttpMethod.Get, "/api/Item/Index?PageNumber=1&PageSize=200"));
        var ids = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement
            .EnumerateArray().Select(i => i.GetProperty("idOne").GetString()).ToList();

        Assert.Contains(Binned, ids);
    }

    /// <summary>
    /// ⚠⚠ RESTORING IS `inventory.bulk`, SYMMETRIC WITH BINNING. One call here changes what every
    /// till in the estate sells; MAUI's *Move to the Bin…* is manager-and-above for exactly that
    /// reason, and the way back must not be easier than the way in.
    /// </summary>
    [Fact]
    public async Task A_till_operator_cannot_restore()
    {
        await SeedAsync();

        using var resp = await _f.CreateClient().SendAsync(
            R(HttpMethod.Post, "/api/v1/items/bulk", scope: "pos.sell",
              body: new { action = "restore", ids = new[] { Binned } }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

}
