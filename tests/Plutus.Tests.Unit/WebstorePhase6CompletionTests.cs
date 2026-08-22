using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.Sales;
using Plutus.SharedKernel;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>Phase 6 completion paths: pick-from-floor notification on record (idempotent),
/// needs-mapping parks the payload, and the bind→reprocess loop heals a parked order into a
/// real sale. Plus the WP6.4 product sweep: upsert, incremental cursor, full-sweep deletion.</summary>
public class WebstorePhase6CompletionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Biz = Guid.NewGuid();
    private static readonly string[] Skus7127 = { "5011921156993", "5011921157013", "5011921142163", "5011921142156" };

    private sealed class IngestSink : IWebstoreSaleSink
    {
        private readonly MySqlDbContext _db; private readonly WebstoreConnectionContext _ctx;
        public IngestSink(MySqlDbContext db, WebstoreConnectionContext ctx) { _db = db; _ctx = ctx; }
        public async Task<SaleSinkOutcome> SubmitAsync(SaleV2 sale, CancellationToken ct = default)
        {
            var req = new IngestSaleRequest
            {
                SaleId = sale.Id, DeviceId = sale.DeviceId, DeviceSeq = sale.DeviceSeq,
                Channel = (byte)sale.Channel, BusinessDay = sale.BusinessDay, OccurredAtUtc = sale.OccurredAtUtc,
                GrossPence = sale.GrossPence, VatPence = sale.VatPence, Note = sale.Note,
                Lines = sale.Lines.Select(l => new IngestLine
                {
                    ItemId = l.ItemId, Qty = l.Qty, UnitPricePence = l.UnitPricePence, DiscountPence = l.DiscountPence,
                    LineGrossPence = l.LineGrossPence, VatRateBp = l.VatRateBp, VatAmountPence = l.VatAmountPence,
                    OverriddenFromPence = l.OverriddenFromPence, DiscountsJson = l.DiscountsJson,
                }).ToList(),
                Tenders = sale.Tenders.Select(t => new IngestTender
                {
                    TenderType = (byte)t.TenderType, AmountPence = t.AmountPence, ChangePence = t.ChangePence, ProviderRef = t.ProviderRef,
                }).ToList(),
            };
            var status = (await new SalesIngestService(_db).IngestAsync(req, _ctx.TenantId, _ctx.DeviceId, "t")).Status;
            // ⚠ Mirrors the real sink — 202 is NotRecorded, never AlreadyRecorded.
            return status == 201 ? SaleSinkOutcome.Recorded : status == 200 ? SaleSinkOutcome.AlreadyRecorded : SaleSinkOutcome.NotRecorded;
        }
    }

    private static MySqlDbContext Ctx(DbContextOptions<MySqlDbContext> o) =>
        new(o, new FixedTenantContext(Tenant)) { CurrentUser = "phase6-test" };

    private static string Fixture(string f) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo", f));

    private static (SqliteConnection conn, DbContextOptions<MySqlDbContext> o, WebstoreConnectionContext ctx) Seed(bool seedItems)
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var o = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        var ctx = new WebstoreConnectionContext
        { WebStoreId = Uuid7.New(), TenantId = Tenant, TillId = Uuid7.New(), DeviceId = Uuid7.New() };
        using var db = Ctx(o);
        db.Database.EnsureCreated();
        db.WebStores.Add(new WebStoreDetails
        {
            Id = ctx.WebStoreId, Name = "Web", Url = "https://example.test", Enabled = true, StoreId = 1,
            TillId = ctx.TillId, DeviceId = ctx.DeviceId, CreatedAtUtc = DateTime.UtcNow,
        });
        db.Devices.Add(new Device
        {
            Id = ctx.DeviceId, TenantId = Tenant, TillId = ctx.TillId,
            SecretHash = new byte[] { 1 }, SecretSalt = new byte[] { 1 }, Status = DeviceStatus.Active, CreatedAtUtc = DateTime.UtcNow,
        });
        if (seedItems)
            foreach (var sku in Skus7127)
                db.Items.Add(new Item
                {
                    IdOne = sku, IdTwo = Biz, Name = sku, Brand = "-", Desc = "",
                    Cost = 1, ExPrice = 1, Price = 1, TaxId = 1, CatId = Guid.NewGuid(),
                });
        db.SaveChanges();
        return (conn, o, ctx);
    }

    [Fact]
    public async Task Recorded_sale_creates_one_pick_notification_even_when_double_seen()
    {
        var (conn, o, ctx) = Seed(seedItems: true);
        using var _ = conn;
        var order = JsonSerializer.Deserialize<WooOrder>(Fixture("order-7127.json"), WooJson.Options)!;

        using (var db = Ctx(o))
        {
            await WebstoreNotifications.CreateForRecordedAsync(db, ctx, order, storeId: 1);
            await WebstoreNotifications.CreateForRecordedAsync(db, ctx, order, storeId: 1);   // double-see
        }
        using (var check = Ctx(o))
        {
            var n = Assert.Single(check.WebstoreNotifications.AsNoTracking().ToList());
            Assert.Contains("Orks: Lootas", n.Message);
            Assert.Contains("check if it needs removing from the shop floor", n.Message);
            Assert.Equal(7127, n.WooOrderId);
            Assert.Null(n.AckedAtUtc);
        }
    }

    [Fact]
    public async Task Needs_mapping_parks_payload_and_bind_plus_reprocess_heals_it_into_a_sale()
    {
        var (conn, o, ctx) = Seed(seedItems: false);   // unknown SKUs
        using var _ = conn;
        var order = JsonSerializer.Deserialize<WooOrder>(Fixture("order-7127.json"), WooJson.Options)!;
        var pipelines = new WebstoreWebhookPipelineFactory(o, (db, c) => new IngestSink(db, c));

        // 1. First pass: needs-mapping → SKU rows + parked payload (like handler/poll do).
        using (var pipeline = pipelines.Create(ctx))
        {
            var r = await pipeline.Processor.RouteOrderAsync(order, ctx, pipeline.Resolver);
            Assert.Equal(WebstoreInboundStatus.NeedsMapping, r.Status);
            await WebstoreQuarantine.ParkAsync(pipeline.Db, ctx, r, JsonSerializer.Serialize(order, WooJson.Options));
        }
        using (var check = Ctx(o))
        {
            Assert.Equal(4, check.WebstoreSkuMaps.Count());
            var q = Assert.Single(check.SaleQuarantine.IgnoreQueryFilters().ToList());
            Assert.StartsWith("needs-mapping:", q.Reason);
            Assert.Null(q.ResolvedAtUtc);
            Assert.Empty(check.SalesV2.ToList());
        }

        // 2. The shopkeeper creates the items (the WP6.5 Woo→Plutus half, simulated directly).
        using (var db = Ctx(o))
        {
            foreach (var sku in Skus7127)
                db.Items.Add(new Item
                {
                    IdOne = sku, IdTwo = Biz, Name = sku, Brand = "-", Desc = "",
                    Cost = 1, ExPrice = 1, Price = 1, TaxId = 1, CatId = Guid.NewGuid(),
                });
            db.SaveChanges();
        }

        // 3. Retry (what the controller's /retry does): reprocess the parked payload.
        using (var pipeline = pipelines.Create(ctx))
        {
            var parked = pipeline.Db.SaleQuarantine.IgnoreQueryFilters().Single(q => q.ResolvedAtUtc == null);
            var again = JsonSerializer.Deserialize<WooOrder>(parked.PayloadJson, WooJson.Options)!;
            Assert.Equal(WooOrderMapper.SaleIdFor(ctx.DeviceId, again.Id), parked.SaleId);   // ownership check
            var r = await pipeline.Processor.RouteOrderAsync(again, ctx, pipeline.Resolver);
            Assert.Equal(WebstoreInboundStatus.Recorded, r.Status);
            parked.ResolvedAtUtc = DateTime.UtcNow;
            await pipeline.Db.SaveChangesAsync();
        }
        using (var check = Ctx(o))
        {
            var sale = Assert.Single(check.SalesV2.AsNoTracking().ToList());
            Assert.Equal(10316, sale.GrossPence);
            Assert.NotNull(check.SaleQuarantine.IgnoreQueryFilters().Single().ResolvedAtUtc);
        }
    }

    // ---- WP6.4 product sweep ----

    private sealed class ProductStubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string> BodyFor = _ => "[]";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BodyFor(request)) };
            resp.Headers.Add("X-WP-TotalPages", "1");
            return Task.FromResult(resp);
        }
    }

    private sealed class CredSecrets : IWebstoreSecretProvider
    {
        public string? GetWebhookSecret(Guid id) => "x";
        public WebstoreRestCredentials? GetRestCredentials(Guid id) => new("ck", "cs");
    }

    [Fact]
    public async Task Product_sweep_upserts_the_cache_and_a_full_sweep_marks_deletions()
    {
        var (conn, o, ctx) = Seed(seedItems: false);
        using var _ = conn;

        const string p1 = "{\"id\":5106,\"sku\":\"5011921156993\",\"name\":\"Orks: Lootas\",\"price\":\"20.79\",\"regular_price\":\"25.99\",\"stock_quantity\":0,\"stock_status\":\"outofstock\",\"status\":\"publish\",\"permalink\":\"https://example.test/p/5106\",\"date_modified_gmt\":\"2026-04-30T21:57:29\"}";
        const string p2 = "{\"id\":4936,\"sku\":\"5011921142163\",\"name\":\"Vanguard\",\"price\":\"24.99\",\"regular_price\":\"\",\"stock_quantity\":3,\"stock_status\":\"instock\",\"status\":\"publish\",\"permalink\":\"https://example.test/p/4936\",\"date_modified_gmt\":\"2026-05-01T10:00:00\"}";

        var handler = new ProductStubHandler
        {
            // orders endpoint → empty; products endpoint → two products.
            BodyFor = req => req.RequestUri!.AbsolutePath.Contains("/products") ? $"[{p1},{p2}]" : "[]",
        };
        var reconciler = new WebstoreReconciler(
            o, new WebstoreWebhookPipelineFactory(o, (db, c) => new IngestSink(db, c)),
            new CredSecrets(), new HttpClient(handler), new WebstoreOptions());

        // First run: LastFullProductSweepUtc is null → FULL sweep runs immediately.
        var s1 = await reconciler.RunOnceAsync();
        Assert.Equal(2, s1.ProductsSeen);
        using (var check = Ctx(o))
        {
            var rows = check.WebstoreProducts.AsNoTracking().OrderBy(p => p.WooProductId).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(2079, rows[1].PricePence);              // 20.79 → pence
            Assert.Equal(2599, rows[1].RegularPricePence);
            Assert.Equal("outofstock", rows[1].StockStatus);
            var ws = check.WebStores.AsNoTracking().Single();
            Assert.NotNull(ws.LastFullProductSweepUtc);
            Assert.NotNull(ws.ProductsCursorUtc);
        }

        // Second run happens outside the nightly window in most test hours — force a full sweep
        // by clearing the marker, with product 4936 now MISSING from the site.
        using (var db = Ctx(o))
        {
            var ws = db.WebStores.Single();
            ws.LastFullProductSweepUtc = null;
            db.SaveChanges();
        }
        handler.BodyFor = req => req.RequestUri!.AbsolutePath.Contains("/products") ? $"[{p1}]" : "[]";
        await reconciler.RunOnceAsync();

        using (var check = Ctx(o))
        {
            var gone = check.WebstoreProducts.AsNoTracking().Single(p => p.WooProductId == 4936);
            Assert.Equal("deleted", gone.Status);                 // full sweep detected the deletion
            var kept = check.WebstoreProducts.AsNoTracking().Single(p => p.WooProductId == 5106);
            Assert.Equal("publish", kept.Status);
        }
    }
}
