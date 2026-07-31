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
/// Regression (Matt, 2026-07-31): editing an item 400'd with
/// "The Cat field is required. The Tax field is required. The Refunds field is required.
///  The Business field is required… The CreatedBy field is required…"
///
/// The legacy CRUD controllers bind the EF ENTITY straight from the body, and Plutus.Entities is
/// nullable-enabled — so MVC inferred [Required] on every non-nullable reference property,
/// including EF navigation properties, navigation collections, and the audit stamps the SERVER
/// fills in. The caller cannot supply any of them, so an ordinary edit was unfixable from the
/// client side.
/// </summary>
public class LegacyItemEditE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public LegacyItemEditE2eTests(PlutusAppFactory f) => _f = f;

    /// <summary>Business + 20% band + a category + one item, as the live catalogue looks.</summary>
    private async Task<(Guid BusinessId, Guid CatId, string ItemId)> SeedAsync()
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "legacy-item-e2e";

        var businessId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        const string itemId = "LEGACY-EDIT-1";

        db.Business.Add(new Business { Id = businessId, Name = "Edit E2E", NameAbbr = "EE2E", VatIN = "GB0" });
        db.Taxes.Add(new Tax { IdOne = 1, IdTwo = businessId, Name = "20%", Rate = 1.2 });
        db.Category.Add(new Category { IdOne = catId, IdTwo = businessId, Name = "Books", Description = "seed" });
        db.Items.Add(new Item
        {
            IdOne = itemId, IdTwo = businessId, Name = "Before", Brand = "-", Desc = "",
            Cost = 1m, ExPrice = 10m, Price = 12m, TaxId = 1, CatId = catId,
        });
        await db.SaveChangesAsync();
        return (businessId, catId, itemId);
    }

    /// <summary>The body the portal/till actually send — ids, no navigation objects, no audit stamps.</summary>
    private static object ItemBody(Guid businessId, Guid catId, string itemId, string name, decimal ex, decimal inc) => new
    {
        id = itemId, idOne = itemId, idTwo = businessId,
        name, brand = "-", desc = "",
        cost = 1m, exPrice = ex, price = inc,
        image = (byte[]?)null, amount = 0, taxId = 1, catId, businessId,
        stockUntracked = false, binnedAtUtc = (string?)null,
    };

    private async Task<(HttpStatusCode Status, string Body)> PutAsync(
        HttpClient client, string token, Guid businessId, string itemId, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/Item/{itemId}");
        req.Headers.Authorization = new("Bearer", token);
        req.Headers.Add("BusinessId", businessId.ToString());
        req.Content = JsonContent.Create(body);
        var resp = await client.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Editing_an_item_does_not_demand_navigation_properties_or_audit_stamps()
    {
        var client = _f.CreateClient();
        var (businessId, catId, itemId) = await SeedAsync();
        var token = PlutusAppFactory.OperatorToken("pos.sell");

        var (status, body) = await PutAsync(client, token, businessId, itemId,
            ItemBody(businessId, catId, itemId, "After", 10m, 12m));

        // the exact failure being fixed — every one of these was demanded of the caller
        foreach (var field in new[] { "Cat", "Tax", "Refunds", "Business", "DisItems", "CreatedBy", "ModifiedBy", "Transactions", "CheckoutItemChanges" })
            Assert.DoesNotContain($"The {field} field is required", body);
        Assert.True(status is HttpStatusCode.OK or HttpStatusCode.NoContent, $"PUT → {(int)status}: {body}");

        // and the edit actually landed
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var saved = await db.Items.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.IdOne == itemId && i.IdTwo == businessId);
        Assert.Equal("After", saved.Name);
    }

    /// <summary>
    /// ⚠ The other half: dropping the INFERRED requirements must not drop the REAL ones. The VAT
    /// guardrail (an ex-price that doesn't match the band) still has to refuse — that guardrail
    /// exists because free-typed ex-prices once corrupted 47 live items and every VAT figure with
    /// them.
    /// </summary>
    [Fact]
    public async Task Real_validation_still_fires_after_the_fix()
    {
        var client = _f.CreateClient();
        var (businessId, catId, itemId) = await SeedAsync();
        var token = PlutusAppFactory.OperatorToken("pos.sell");

        // £12.00 inc at the 20% band means £10.00 ex — £99 is nonsense and must be refused
        var (status, body) = await PutAsync(client, token, businessId, itemId,
            ItemBody(businessId, catId, itemId, "Bad prices", 99m, 12m));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("does not match", body);
    }
}
