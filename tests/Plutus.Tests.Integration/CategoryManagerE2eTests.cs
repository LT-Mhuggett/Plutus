using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
/// WP4.4 category manager. Reads are gated on portal.reports.view; the destructive guard is the
/// point of the endpoint: DELETE refuses (409) while items reference the category, /reassign moves
/// them, and then the delete succeeds — so a category delete can never cascade-take items with it.
/// </summary>
public class CategoryManagerE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public CategoryManagerE2eTests(PlutusAppFactory f) => _f = f;

    private static readonly Guid Kapow = Plutus.Entities.Tenancy.KnownTenants.Kapow;

    /// <summary>Seeds a Business + two categories (A, C) + one item in A. Ambient tenant falls back
    /// to Kapow in a bare scope, so the shadow TenantId auto-stamps and the rows are Kapow-visible.</summary>
    private async Task<(Guid catA, Guid catC)> SeedCatalogueAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "cat-e2e-seed";

        var bizId = Guid.NewGuid();
        db.Business.Add(new Business { Id = bizId, Name = "Cat E2E", NameAbbr = "CE2E", VatIN = "GB0" });
        // Item.TaxId → Tax is a composite FK (TaxId, IdTwo) → (Tax.IdOne, Tax.IdTwo); seed the band.
        db.Taxes.Add(new Tax { IdOne = 1, IdTwo = bizId, Name = "VAT", Rate = 1.2 });

        var catA = Guid.NewGuid();
        var catC = Guid.NewGuid();
        db.Category.Add(new Category { IdOne = catA, IdTwo = bizId, Name = "Zed Occupied", Description = "seed" });
        db.Category.Add(new Category { IdOne = catC, IdTwo = bizId, Name = "Zed Empty", Description = "seed" });
        db.Items.Add(new Item
        {
            IdOne = "CAT-E2E-1", IdTwo = bizId, Name = "Guarded item", Brand = "-", Desc = "",
            Cost = 1m, ExPrice = 1m, Price = 1m, TaxId = 1, CatId = catA,
        });
        await db.SaveChangesAsync();
        return (catA, catC);
    }

    [Fact]
    public async Task List_is_gated_on_reports_view()
    {
        var client = _f.CreateClient();
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken("pos.sell", Kapow));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories"))
        {
            req.Headers.Authorization = new("Bearer", PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
        }
    }

    [Fact]
    public async Task Delete_is_blocked_while_items_reference_it_then_reassign_unblocks()
    {
        var (catA, catC) = await SeedCatalogueAsync();
        var client = _f.CreateClient();
        var admin = PlutusAppFactory.OperatorToken(PlutusPolicies.PlatformAdmin, Kapow);

        // the occupied category reports its item count
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            var occupied = false;
            foreach (var c in body.EnumerateArray())
                if (c.GetProperty("id").GetGuid() == catA)
                    occupied = c.GetProperty("itemCount").GetInt32() == 1;
            Assert.True(occupied, "category A should report itemCount 1");
        }

        // create via the API exercises BusinessIdAsync + the id mint (needs the seeded Business)
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/categories"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { name = "Created Via Api" });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(req)).StatusCode);
        }

        // delete refuses while the item is still in it
        using (var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/categories/{catA}"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(req)).StatusCode);
        }

        // reassign the item to C
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/categories/{catA}/reassign"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            req.Content = JsonContent.Create(new { toId = catC });
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var moved = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("moved").GetInt32();
            Assert.Equal(1, moved);
        }

        // now the empty category deletes cleanly
        using (var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/categories/{catA}"))
        {
            req.Headers.Authorization = new("Bearer", admin);
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(req)).StatusCode);
        }
    }
}
