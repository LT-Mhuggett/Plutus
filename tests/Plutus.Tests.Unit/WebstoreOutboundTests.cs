using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP6.3 outbound: dry-run journals exactly what WOULD be sent (with the oversell
/// buffer applied) and sends NOTHING; live mode PUTs and updates the cache (idempotent repeat);
/// the kill switch halts a batch; WP6.5 draft scan journals new items once. Fast lane consumer
/// pushes on SaleRecorded.</summary>
public class WebstoreOutboundTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Biz = Guid.NewGuid();

    private sealed class CountingHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string Body)> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":9001}") };
        }
    }

    private static MySqlDbContext Ctx(DbContextOptions<MySqlDbContext> o) =>
        new(o, new FixedTenantContext(Tenant)) { CurrentUser = "outbound-test" };

    private static (SqliteConnection conn, DbContextOptions<MySqlDbContext> o, WebStoreDetails ws, Guid locationId) Seed(string mode, int buffer = 0)
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var o = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        var ws = new WebStoreDetails
        {
            Id = Uuid7.New(), Name = "Web", Url = "https://example.test", Enabled = true, StoreId = 1,
            TillId = Uuid7.New(), DeviceId = Uuid7.New(), OversellBuffer = buffer, OutboundMode = mode,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var locationId = Uuid7.New();
        using var db = Ctx(o);
        db.Database.EnsureCreated();
        db.WebStores.Add(ws);
        db.StockLocations.Add(new StockLocation { Id = locationId, TenantId = Tenant, StoreId = 1, Type = StockLocationType.Store, Name = "Store 1" });
        // Web says 5 in stock; Plutus says 3 → target (buffer 0) is 3.
        db.WebstoreProducts.Add(new WebstoreProduct
        {
            Id = Uuid7.New(), TenantId = Tenant, WebStoreId = ws.Id, WooProductId = 5106,
            Sku = "5011921156993", Name = "Orks: Lootas", PricePence = 2079, StockQuantity = 5,
            StockStatus = "instock", Status = "publish", LastSeenUtc = DateTime.UtcNow,
        });
        db.StockLevels.Add(new StockLevel { TenantId = Tenant, StockLocationId = locationId, ItemIdOne = "5011921156993", Quantity = 3 });
        db.SaveChanges();
        return (conn, o, ws, locationId);
    }

    [Fact]
    public async Task Dry_run_journals_the_exact_delta_and_sends_nothing()
    {
        var (conn, o, ws, _) = Seed("dry-run");
        using var _c = conn;
        var handler = new CountingHandler();
        using var db = Ctx(o);

        var n = await WebstoreOutbound.PushStockAsync(
            db, new WooRestClient(new HttpClient(handler), ws.Url!, new("ck", "cs")), ws,
            new[] { "5011921156993" }, "fast");

        Assert.Equal(1, n);
        Assert.Empty(handler.Requests);                              // NOTHING sent
        var log = Assert.Single(db.WebstoreOutboundLogs.AsNoTracking().ToList());
        Assert.Equal("stock", log.Kind);
        Assert.Equal("5", log.FromValue);                            // web said 5
        Assert.Equal("3", log.ToValue);                              // would set 3
        Assert.Equal("dry-run", log.Mode);
        Assert.Equal("logged", log.Result);
        Assert.Null(log.SentAtUtc);
    }

    [Fact]
    public async Task Oversell_buffer_reduces_the_listed_quantity()
    {
        var (conn, o, ws, _) = Seed("dry-run", buffer: 2);           // level 3 − buffer 2 = list 1
        using var _c = conn;
        using var db = Ctx(o);
        await WebstoreOutbound.PushStockAsync(
            db, new WooRestClient(new HttpClient(new CountingHandler()), ws.Url!, new("ck", "cs")), ws,
            new[] { "5011921156993" }, "fast");
        Assert.Equal("1", db.WebstoreOutboundLogs.AsNoTracking().Single().ToValue);
    }

    [Fact]
    public async Task Live_mode_puts_to_woo_updates_the_cache_and_is_idempotent()
    {
        var (conn, o, ws, _) = Seed("live");
        using var _c = conn;
        var handler = new CountingHandler();
        using var db = Ctx(o);
        var client = new WooRestClient(new HttpClient(handler), ws.Url!, new("ck", "cs"));

        var n1 = await WebstoreOutbound.PushStockAsync(db, client, ws, new[] { "5011921156993" }, "fast");
        Assert.Equal(1, n1);
        var put = Assert.Single(handler.Requests);
        Assert.Equal("PUT", put.Method);
        Assert.Contains("/products/5106", put.Url);
        Assert.Contains("\"stock_quantity\":3", put.Body);
        var log = Assert.Single(db.WebstoreOutboundLogs.AsNoTracking().ToList());
        Assert.Equal("sent", log.Result);
        Assert.NotNull(log.SentAtUtc);

        // Cache followed the write → a repeat push has nothing to do (idempotent).
        var n2 = await WebstoreOutbound.PushStockAsync(db, client, ws, new[] { "5011921156993" }, "fast");
        Assert.Equal(0, n2);
        Assert.Single(handler.Requests);                             // still exactly one PUT
    }

    [Fact]
    public async Task Kill_switch_off_means_no_journal_no_send()
    {
        var (conn, o, ws, _) = Seed("off");
        using var _c = conn;
        var handler = new CountingHandler();
        using var db = Ctx(o);
        var n = await WebstoreOutbound.PushStockAsync(
            db, new WooRestClient(new HttpClient(handler), ws.Url!, new("ck", "cs")), ws,
            new[] { "5011921156993" }, "fast");
        Assert.Equal(0, n);
        Assert.Empty(handler.Requests);
        Assert.Empty(db.WebstoreOutboundLogs.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Draft_scan_journals_a_new_item_once_and_skips_items_already_on_the_web()
    {
        var (conn, o, ws, _) = Seed("dry-run");
        using var _c = conn;
        using (var db = Ctx(o))
        {
            // A brand-new Plutus item NOT on the web + one that IS (5011921156993 is cached).
            db.Items.Add(new Item
            {
                IdOne = "9999999999999", IdTwo = Biz, Name = "New Comic", Brand = "-", Desc = "",
                Cost = 0, ExPrice = 4.99m, Price = 4.99m, TaxId = 1, CatId = Guid.NewGuid(),
            });
            db.Items.Add(new Item
            {
                IdOne = "5011921156993", IdTwo = Biz, Name = "Orks: Lootas", Brand = "-", Desc = "",
                Cost = 0, ExPrice = 20.79m, Price = 20.79m, TaxId = 1, CatId = Guid.NewGuid(),
            });
            db.SaveChanges();
        }
        var handler = new CountingHandler();
        using var db2 = Ctx(o);
        var client = new WooRestClient(new HttpClient(handler), ws.Url!, new("ck", "cs"));

        var n1 = await WebstoreOutbound.PushNewItemDraftsAsync(db2, client, ws, TimeSpan.FromHours(48));
        Assert.Equal(1, n1);                                          // only the new item
        Assert.Empty(handler.Requests);                               // dry-run: no POST
        var log = Assert.Single(db2.WebstoreOutboundLogs.AsNoTracking().Where(l => l.Kind == "draft-product").ToList());
        Assert.Equal("9999999999999", log.ItemIdOne);
        Assert.Contains("New Comic", log.ToValue);
        Assert.Contains("draft", log.ToValue!);

        // Second scan: already journaled → nothing new.
        var n2 = await WebstoreOutbound.PushNewItemDraftsAsync(db2, client, ws, TimeSpan.FromHours(48));
        Assert.Equal(0, n2);
    }

    [Fact]
    public async Task Fast_lane_consumer_pushes_on_SaleRecorded()
    {
        var (conn, o, ws, _) = Seed("dry-run");
        using var _c = conn;
        var saleId = Uuid7.New();
        using (var db = Ctx(o))
        {
            db.SaleLines.Add(new SaleLine
            {
                Id = Uuid7.New(), TenantId = Tenant, SaleId = saleId, LineNo = 1,
                ItemId = Uuid7.New(), ItemIdOne = "5011921156993", Qty = 1,
                UnitPricePence = 2079, LineGrossPence = 2079, VatRateBp = 0, VatAmountPence = 0,
            });
            db.SaveChanges();
        }

        var factory = new StubHttpFactory();
        using var db2 = Ctx(o);
        var consumer = new WebstoreStockOutboundConsumer(db2, new CredSecrets(), factory);
        await consumer.HandleAsync(new SaleRecorded(Uuid7.New(), Tenant, DateTime.UtcNow, saleId, ws.DeviceId, 1,
            DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        var log = Assert.Single(db2.WebstoreOutboundLogs.AsNoTracking().ToList());
        Assert.Equal("fast", log.Lane);
        Assert.Equal("3", log.ToValue);
    }

    [Fact]
    public async Task Slow_lane_never_touches_web_products_without_a_matching_catalogue_item()
    {
        // Regression (found by the first LIVE dry-run): the cached product has NO Plutus item —
        // the slow-lane diff must exclude it entirely (going live would have zeroed its stock).
        var (conn, o, ws, _) = Seed("dry-run");
        using var _c = conn;
        // Note: Seed() cached SKU 5011921156993 but did NOT create an Items row for it, and adds
        // a second cached product with an over-long composite SKU.
        using (var db = Ctx(o))
        {
            db.WebstoreProducts.Add(new WebstoreProduct
            {
                Id = Uuid7.New(), TenantId = Tenant, WebStoreId = ws.Id, WooProductId = 777,
                Sku = "00001641650000114593746001", Name = "Composite", PricePence = 100,
                StockQuantity = 9, Status = "publish", LastSeenUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
        }
        var handler = new ProductsEmptyHandler();
        var reconciler = new WebstoreReconciler(
            o, new WebstoreWebhookPipelineFactory(o, (db, c) => new NullSink()),
            new CredSecrets(), new HttpClient(handler), new WebstoreOptions());
        await reconciler.RunOnceAsync();

        using var check = Ctx(o);
        Assert.Empty(check.WebstoreOutboundLogs.AsNoTracking().Where(l => l.Lane == "slow").ToList());
    }

    private sealed class NullSink : IWebstoreSaleSink
    {
        public Task<bool> SubmitAsync(SaleV2 sale, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class ProductsEmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            resp.Headers.Add("X-WP-TotalPages", "1");
            return Task.FromResult(resp);
        }
    }

    private sealed class CredSecrets : IWebstoreSecretProvider
    {
        public string? GetWebhookSecret(Guid id) => "x";
        public WebstoreRestCredentials? GetRestCredentials(Guid id) => new("ck", "cs");
    }

    private sealed class StubHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new CountingHandler());
    }
}
