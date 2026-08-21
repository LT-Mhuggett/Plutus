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

    /// <summary>
    /// An operator who may change an item, and one who may not.
    ///
    /// ⚠⚠ THESE TESTS USED `OperatorToken("pos.sell")` AND PASSED, because until 2026-08-21 the
    /// item write endpoints inherited a bare `[Authorize]` from the legacy CRUD base — **any signed-in
    /// user could create or edit any item.** Matt: *"editing of items to be a supervisor and above
    /// permission across all tills."* Now gated `perm:portal.prices.manage,pos.items.manage`.
    ///
    /// ⚠ `perm:*` resolves from **RbacRoleAssignments**, never from the token's Scope claim — which is
    /// why a scope-only token 403s however generous the scope string looks.
    /// </summary>
    private async Task<string> TokenForRoleAsync(string roleName)
    {
        using var scope = _f.Services.CreateScope();
        var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        db.CurrentUser = "legacy-item-e2e";
        var tenantId = Plutus.Entities.Tenancy.KnownTenants.Kapow;
        await Plutus.Identity.RbacSeeder.EnsureBuiltInRolesAsync(db, tenantId);
        var role = await db.RbacRoles.FirstAsync(r => r.Name == roleName && r.TenantId == tenantId);
        var userId = Uuid7.New();
        db.RbacRoleAssignments.Add(new RbacRoleAssignment
        {
            Id = Uuid7.New(), TenantId = tenantId, UserId = userId, RoleId = role.Id,
            ScopeType = RbacScopeType.Tenant, ScopeId = "", CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        // � The TENANT must be on the token, or the principal falls back to the default one and the
        // assignment just made is invisible.
        return PlutusAppFactory.OperatorTokenFor(userId, "pos.sell", tenantId);
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
        var token = await TokenForRoleAsync("Supervisor");

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
        var token = await TokenForRoleAsync("Supervisor");

        // £12.00 inc at the 20% band means £10.00 ex — £99 is nonsense and must be refused
        var (status, body) = await PutAsync(client, token, businessId, itemId,
            ItemBody(businessId, catId, itemId, "Bad prices", 99m, 12m));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("does not match", body);
    }

    /// <summary>
    /// ⚠⚠ THE RULE MATT ASKED FOR, PROVED FROM THE OTHER SIDE. *"Editing of items to be a supervisor
    /// and above permission across all tills."* — so a **Cashier must be refused**, on the SERVER,
    /// whatever the client offers.
    ///
    /// ⚠ This is the test that would have failed before 2026-08-21, and it is the point of the whole
    /// change: the endpoints inherited a bare `[Authorize]`, so a cashier's token edited items happily.
    /// Both tills gated it in their own UI — MAUI really did, the web till not at all — and a
    /// client-side gate is a suggestion.
    ///
    /// ⚠ A price is what the customer is charged. A cashier changing one unsupervised is a discount
    /// with no reason, no ceiling and no audit row.
    /// </summary>
    [Fact]
    public async Task A_cashier_cannot_edit_an_item_and_a_supervisor_can()
    {
        var client = _f.CreateClient();
        var (businessId, catId, itemId) = await SeedAsync();

        var cashier = await TokenForRoleAsync("Cashier");
        var (refused, _) = await PutAsync(client, cashier, businessId, itemId,
            ItemBody(businessId, catId, itemId, "Cashier edit", 10m, 12m));
        Assert.Equal(HttpStatusCode.Forbidden, refused);

        // ⚠ And the refusal WROTE NOTHING — a 403 that still saved would be the worst outcome.
        using (var scope = _f.Services.CreateScope())
        {
            var db = (MySqlDbContext)scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var untouched = await db.Items.IgnoreQueryFilters().AsNoTracking()
                .FirstAsync(i => i.IdOne == itemId && i.IdTwo == businessId);
            Assert.Equal("Before", untouched.Name);
        }

        // The same edit, by a supervisor, lands — so the gate is a gate and not a wall.
        var supervisor = await TokenForRoleAsync("Supervisor");
        var (allowed, body) = await PutAsync(client, supervisor, businessId, itemId,
            ItemBody(businessId, catId, itemId, "Supervisor edit", 10m, 12m));
        Assert.True(allowed is HttpStatusCode.OK or HttpStatusCode.NoContent, $"PUT → {(int)allowed}: {body}");
    }

    /// <summary>⚠ Creation is gated the same way, and by the same reasoning — a new catalogue row is
    /// not a smaller act than editing one. ⚠ This is the change with an operational cost: a cashier
    /// scanning a new delivery can no longer add it (`TillPage` now says who to ask).</summary>
    [Fact]
    public async Task A_cashier_cannot_CREATE_an_item_either()
    {
        var client = _f.CreateClient();
        var (businessId, catId, _) = await SeedAsync();
        var cashier = await TokenForRoleAsync("Cashier");

        // ⚠ `Bearer` and `BusinessId`, exactly as `PutAsync` above does it. Built by hand first with
        // an "Authorization: PlutusToken …" header, which answered **401 not 403** — an authentication
        // failure wearing the costume of an authorisation one, and it would have "passed" a sloppier
        // assertion while proving nothing about the gate.
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/Item");
        req.Headers.Authorization = new("Bearer", cashier);
        req.Headers.Add("BusinessId", businessId.ToString());
        req.Content = JsonContent.Create(ItemBody(businessId, catId, "CASHIER-NEW-1", "Nope", 10m, 12m));

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
