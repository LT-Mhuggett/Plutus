using System;
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
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>
/// WP5.4 (DoD): the 9-case policy matrix — {CENTRAL, CENTRAL_WITH_OVERRIDE, LOCAL} ×
/// {HQ price change, store override, HQ force-reset} — plus the effective-date boundary
/// (a scheduled reprice activates exactly at its EffectiveFromUtc).
/// </summary>
public class PricingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private const string ItemId = "GAME-9";
    private const int StoreA = 1;

    private static MySqlDbContext Ctx(SqliteConnection conn)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(Tenant)) { CurrentUser = "pricing-test" };

    private static PricesController Controller(MySqlDbContext db)
    {
        var c = new PricesController(db, new FixedTenantContext(Tenant))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()) }, "test")),
                },
            },
        };
        return c;
    }

    private static SqliteConnection Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using var ctx = Ctx(conn);
        ctx.Database.EnsureCreated();
        ctx.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
        ctx.Stores.Add(new Store
        {
            Id = StoreA, BusinessId = BusinessId, ContactNumber = "-",
            AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
        });
        ctx.Items.Add(new Item
        {
            IdOne = ItemId, IdTwo = BusinessId, Name = "Game", Brand = "-", Desc = "",
            Cost = 1, ExPrice = 6.26m, Price = 7.50m, TaxId = 1, CatId = Guid.NewGuid(),
        });
        ctx.SaveChanges();
        return conn;
    }

    private static async Task SetPolicy(SqliteConnection conn, PricePolicy policy)
    {
        using var db = Ctx(conn);
        Assert.IsType<NoContentResult>(await Controller(db).SetPolicy(ItemId, new PolicyBody(policy.ToString())));
    }

    private static async Task<long> Price(SqliteConnection conn, int store = StoreA)
    {
        using var db = Ctx(conn);
        return (await new PricingService(db).ResolveAsync(ItemId, store, DateTime.UtcNow)).PricePence;
    }

    private static async Task HqPrice(SqliteConnection conn, long pence)
    {
        using var db = Ctx(conn);
        Assert.IsType<CreatedResult>(await Controller(db).SetCentral(ItemId, new CentralPriceBody(pence, pence * 5 / 6, null)));
    }

    private static async Task<IActionResult> StoreOverride(SqliteConnection conn, long pence)
    {
        using var db = Ctx(conn);
        return await Controller(db).SetOverride(ItemId, new OverridePriceBody(StoreA, pence, pence * 5 / 6, null));
    }

    private static async Task<IActionResult> ForceReset(SqliteConnection conn)
    {
        using var db = Ctx(conn);
        return await Controller(db).ForceReset(ItemId, new ForceResetBody(null));
    }

    // ── the 9-case matrix ──

    [Fact]
    public async Task Central_hq_change_applies_override_rejected_reset_noop()
    {
        using var conn = Open();
        await SetPolicy(conn, PricePolicy.Central);
        Assert.Equal(750, await Price(conn)); // legacy baseline before any entry

        await HqPrice(conn, 800);                                        // 1. HQ change → applies
        Assert.Equal(800, await Price(conn));

        Assert.IsType<ConflictObjectResult>(await StoreOverride(conn, 700)); // 2. override → 409
        Assert.Equal(800, await Price(conn));

        var reset = Assert.IsType<OkObjectResult>(await ForceReset(conn));   // 3. reset → harmless no-op
        Assert.Equal(0, (int)reset.Value!.GetType().GetProperty("revoked")!.GetValue(reset.Value)!);
        Assert.Equal(800, await Price(conn));
    }

    [Fact]
    public async Task CentralWithOverride_override_survives_hq_change_until_force_reset()
    {
        using var conn = Open();
        await SetPolicy(conn, PricePolicy.CentralWithOverride);

        await HqPrice(conn, 800);                                        // 4. HQ change → default
        Assert.Equal(800, await Price(conn));

        Assert.IsType<CreatedResult>(await StoreOverride(conn, 700));    // 5. override wins…
        Assert.Equal(700, await Price(conn));
        await HqPrice(conn, 900);                                        //    …and SURVIVES a reprice
        Assert.Equal(700, await Price(conn));

        var reset = Assert.IsType<OkObjectResult>(await ForceReset(conn)); // 6. force-reset → central
        Assert.Equal(1, (int)reset.Value!.GetType().GetProperty("revoked")!.GetValue(reset.Value)!);
        Assert.Equal(900, await Price(conn));
    }

    [Fact]
    public async Task Local_store_owns_price_hq_is_baseline_only_and_cannot_reset()
    {
        using var conn = Open();
        await SetPolicy(conn, PricePolicy.Local);

        await HqPrice(conn, 800);                                        // 7. HQ change = baseline only
        Assert.Equal(800, await Price(conn));                            //    (no local price yet)

        Assert.IsType<CreatedResult>(await StoreOverride(conn, 650));    // 8. store sets ITS price
        Assert.Equal(650, await Price(conn));
        await HqPrice(conn, 999);                                        //    HQ reprice changes nothing
        Assert.Equal(650, await Price(conn));

        Assert.IsType<ConflictObjectResult>(await ForceReset(conn));     // 9. HQ cannot reset LOCAL
        Assert.Equal(650, await Price(conn));
    }

    // ── effective-date boundary + variance ──

    [Fact]
    public async Task Scheduled_reprice_activates_exactly_at_the_boundary()
    {
        using var conn = Open();
        var boundary = DateTime.UtcNow.AddHours(1); // "Sunday night"

        using (var db = Ctx(conn))
        {
            Assert.IsType<CreatedResult>(await Controller(db).SetCentral(ItemId, new CentralPriceBody(800, 667, null)));
            Assert.IsType<CreatedResult>(await Controller(db).SetCentral(ItemId, new CentralPriceBody(850, 708, boundary)));
        }

        using (var db = Ctx(conn))
        {
            var service = new PricingService(db);
            Assert.Equal(800, (await service.ResolveAsync(ItemId, StoreA, boundary.AddTicks(-1))).PricePence);
            Assert.Equal(850, (await service.ResolveAsync(ItemId, StoreA, boundary)).PricePence); // AT the boundary
            Assert.Equal(850, (await service.ResolveAsync(ItemId, StoreA, boundary.AddMinutes(5))).PricePence);
        }
    }

    [Fact]
    public async Task Variance_reports_stores_that_differ_from_hq()
    {
        using var conn = Open();
        await SetPolicy(conn, PricePolicy.CentralWithOverride);
        await HqPrice(conn, 800);
        await StoreOverride(conn, 700);

        using var db = Ctx(conn);
        var res = Assert.IsType<OkObjectResult>(await Controller(db).Variance(null));
        var row = Assert.Single(((System.Collections.IEnumerable)res.Value!).Cast<object>());
        Assert.Equal(-100L, (long)row.GetType().GetProperty("deltaPence")!.GetValue(row)!);
    }
}
