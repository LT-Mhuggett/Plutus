using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>Phase 6 DB layer: the webstore config + SKU-map rows persist, are tenant-isolated by
/// the global query filter, and the catalogue SKU resolver resolves barcodes to the web-POS
/// deterministic ItemId (or null for an unknown SKU).</summary>
public class WebstoreDbTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();

    private static MySqlDbContext Ctx(SqliteConnection conn, Guid tenant)
        => new MySqlDbContext(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
                              new FixedTenantContext(tenant)) { CurrentUser = "webstore-test" };

    [Fact]
    public void Config_and_map_persist_and_are_tenant_isolated()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var storeId = Uuid7.New();
        using (var db = Ctx(conn, TenantA))
        {
            db.Database.EnsureCreated();
            db.WebStores.Add(new WebStoreDetails
            {
                Id = storeId, Name = "Kapow Web", Url = "https://www.kapow-comics.co.uk",
                Enabled = true, TillId = Uuid7.New(), DeviceId = Uuid7.New(), CreatedAtUtc = DateTime.UtcNow,
            });
            db.WebstoreSkuMaps.Add(new WebstoreSkuMap
            {
                Id = Uuid7.New(), WebStoreId = storeId, Sku = "9781561631698", Status = "Pending",
                SeenCount = 1, FirstSeenUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        // Tenant A sees its rows (TenantId auto-stamped by the guard).
        using (var a = Ctx(conn, TenantA))
        {
            var ws = Assert.Single(a.WebStores.AsNoTracking().ToList());
            Assert.Equal("Kapow Web", ws.Name);
            Assert.Equal(TenantA, ws.TenantId);
            Assert.Single(a.WebstoreSkuMaps.AsNoTracking().Where(m => m.Status == "Pending").ToList());
        }
        // Tenant B sees nothing (global query filter).
        using (var b = Ctx(conn, TenantB))
        {
            Assert.Empty(b.WebStores.AsNoTracking().ToList());
            Assert.Empty(b.WebstoreSkuMaps.AsNoTracking().ToList());
        }
    }

    [Fact]
    public void Resolver_returns_deterministic_itemid_for_known_sku_and_null_for_unknown()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using (var db = Ctx(conn, TenantA))
        {
            db.Database.EnsureCreated();
            db.Business.Add(new Business { Id = BusinessId, Name = "Testco", NameAbbr = "TST", VatIN = "GB0" });
            db.Stores.Add(new Store
            {
                Id = 1, BusinessId = BusinessId, ContactNumber = "-",
                AdLine1 = "-", AdLine2 = "", City = "-", PostCode = "-", Country = "-",
            });
            db.Items.Add(new Item
            {
                IdOne = "5011921156993", IdTwo = BusinessId, Name = "Orks: Lootas", Brand = "GW",
                Desc = "", Cost = 1, ExPrice = 1, Price = 20.79m, TaxId = 1, CatId = Guid.NewGuid(),
            });
            db.SaveChanges();
        }

        using var ctx = Ctx(conn, TenantA);
        var resolver = new CatalogueSkuResolver(ctx);

        Assert.Equal(DeterministicGuid.ForItem(BusinessId, "5011921156993"), resolver.Resolve("5011921156993"));
        Assert.Equal(DeterministicGuid.ForItem(BusinessId, "5011921156993"), resolver.Resolve("  5011921156993 ")); // trims
        Assert.Null(resolver.Resolve("0000000000000"));  // unknown SKU → review queue
        Assert.Null(resolver.Resolve(""));                 // empty → null
    }

    [Fact]
    public async System.Threading.Tasks.Task Queue_upserts_pending_rows_and_bumps_seen_count()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var webStoreId = Uuid7.New();
        var ctx = new WebstoreConnectionContext { WebStoreId = webStoreId, TenantId = TenantA };

        using (var db = Ctx(conn, TenantA)) db.Database.EnsureCreated();

        using (var db = Ctx(conn, TenantA))
            await new WebstoreSkuMapQueue(db).EnqueueAsync(ctx, 8347, new[] { "9781561631698", "1901618013" });
        // Second delivery re-sees one SKU + adds a new one.
        using (var db = Ctx(conn, TenantA))
            await new WebstoreSkuMapQueue(db).EnqueueAsync(ctx, 8348, new[] { "9781561631698", "NEW-SKU" });

        using (var check = Ctx(conn, TenantA))
        {
            var rows = check.WebstoreSkuMaps.AsNoTracking().OrderBy(m => m.Sku).ToList();
            Assert.Equal(3, rows.Count);                                   // 2 + 1 new, not 4
            var reseen = rows.Single(m => m.Sku == "9781561631698");
            Assert.Equal(2, reseen.SeenCount);                             // bumped, not duplicated
            Assert.Equal("Pending", reseen.Status);
            Assert.All(rows, m => Assert.Equal(TenantA, m.TenantId));
        }
    }
}
