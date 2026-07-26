using System;
using System.IO;
using System.Linq;
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

/// <summary>WP6.2a — the anonymous-webhook handler: connection lookup / ping / HMAC / outcome→HTTP,
/// and the TENANT-SCOPE PROOF this design exists for: the handler's ambient context is deliberately
/// fixed to the WRONG tenant, and every row a delivery writes must still land under the webstore's
/// own tenant.</summary>
public class WebstoreWebhookHandlerTests
{
    private const string Secret = "whsec_handler_test";
    private static readonly Guid TenantA = Guid.NewGuid();   // ambient tenant (wrong on purpose)
    private static readonly Guid TenantB = Guid.NewGuid();   // the webstore's tenant
    private static readonly Guid BizB = Guid.NewGuid();
    private static readonly string[] Skus7127 = { "5011921156993", "5011921157013", "5011921142163", "5011921142156" };

    private sealed class FakeEntitlements : IEntitlementService
    {
        public bool Enabled = true;
        public Task<bool> IsEnabledAsync(Guid tenantId, string feature, CancellationToken ct = default) => Task.FromResult(Enabled);
    }

    private sealed class FakeSecrets : IWebstoreSecretProvider
    {
        public string? Secret = WebstoreWebhookHandlerTests.Secret;
        public string? GetWebhookSecret(Guid webStoreId) => Secret;
        public WebstoreRestCredentials? GetRestCredentials(Guid webStoreId) => null;
    }

    /// <summary>The host sink shape: mapped SaleV2 → ingest DTO → the REAL SalesIngestService on
    /// the pipeline's tenant-fixed context. 201 new / 200 duplicate.</summary>
    private sealed class IngestSink : IWebstoreSaleSink
    {
        private readonly MySqlDbContext _db;
        private readonly WebstoreConnectionContext _ctx;
        public IngestSink(MySqlDbContext db, WebstoreConnectionContext ctx) { _db = db; _ctx = ctx; }

        public async Task<bool> SubmitAsync(SaleV2 sale, CancellationToken ct = default)
        {
            var req = new IngestSaleRequest
            {
                SaleId = sale.Id, DeviceId = sale.DeviceId, DeviceSeq = sale.DeviceSeq,
                Channel = (byte)sale.Channel, BusinessDay = sale.BusinessDay, OccurredAtUtc = sale.OccurredAtUtc,
                GrossPence = sale.GrossPence, VatPence = sale.VatPence, Note = sale.Note, OperatorUserId = sale.OperatorUserId,
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
            var outcome = await new SalesIngestService(_db).IngestAsync(req, _ctx.TenantId, _ctx.DeviceId, "webstore");
            return outcome.Status == 201;
        }
    }

    /// <summary>One composed test world: seeded SQLite DB, fakes, and a handler whose ambient
    /// context is fixed to Tenant A while the webstore belongs to Tenant B.</summary>
    private sealed class Harness : IDisposable
    {
        public readonly SqliteConnection Conn;
        public readonly DbContextOptions<MySqlDbContext> Options;
        public readonly WebstoreWebhookHandler Handler;
        public readonly FakeEntitlements Entitlements = new();
        public readonly FakeSecrets Secrets = new();
        public readonly Guid WebStoreId = Uuid7.New();
        public readonly Guid TillId = Uuid7.New();
        public readonly Guid DeviceId = Uuid7.New();
        private readonly MySqlDbContext _ambient;

        public Harness(bool seedItems)
        {
            Conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            Conn.Open();
            Options = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(Conn).Options;

            using (var db = Ctx(TenantB))
            {
                db.Database.EnsureCreated();
                db.WebStores.Add(new WebStoreDetails
                {
                    Id = WebStoreId, Name = "Kapow Web", Url = "https://example.test", Enabled = true,
                    TillId = TillId, DeviceId = DeviceId, CreatedAtUtc = DateTime.UtcNow,
                });
                // The virtual device row — how ingest derives the sale's TillId (WP6.1 provisioning).
                db.Devices.Add(new Device
                {
                    Id = DeviceId, TenantId = TenantB, TillId = TillId,
                    SecretHash = new byte[] { 1 }, SecretSalt = new byte[] { 1 },
                    Status = DeviceStatus.Active, CreatedAtUtc = DateTime.UtcNow,
                });
                if (seedItems)
                    foreach (var sku in Skus7127)
                        db.Items.Add(new Item
                        {
                            IdOne = sku, IdTwo = BizB, Name = $"Item {sku}", Brand = "-", Desc = "",
                            Cost = 1, ExPrice = 1, Price = 1, TaxId = 1, CatId = Guid.NewGuid(),
                        });
                db.SaveChanges();
            }

            var factory = new WebstoreWebhookPipelineFactory(Options, (db, ctx) => new IngestSink(db, ctx));
            _ambient = Ctx(TenantA);   // the WRONG tenant — the handler must not care
            Handler = new WebstoreWebhookHandler(_ambient, Entitlements, Secrets, factory);
        }

        public MySqlDbContext Ctx(Guid tenant) =>
            new(Options, new FixedTenantContext(tenant)) { CurrentUser = "handler-test" };

        public void Dispose() { _ambient.Dispose(); Conn.Dispose(); }
    }

    private static string Body(string file = "order-7127.json") =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo", file));

    // ---- gate checks ----

    [Fact]
    public async Task Unknown_webstore_id_is_404()
    {
        using var h = new Harness(seedItems: false);
        var r = await h.Handler.HandleOrderWebhookAsync(Uuid7.New(), Body(), null);
        Assert.Equal(404, r.Status);
    }

    [Fact]
    public async Task Disabled_connection_is_410()
    {
        using var h = new Harness(seedItems: false);
        using (var db = h.Ctx(TenantB))
        {
            var row = db.WebStores.Single();
            row.Enabled = false;
            db.SaveChanges();
        }
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, Body(), null);
        Assert.Equal(410, r.Status);
    }

    [Fact]
    public async Task Unentitled_tenant_is_410()
    {
        using var h = new Harness(seedItems: false);
        h.Entitlements.Enabled = false;
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, Body(), null);
        Assert.Equal(410, r.Status);
    }

    [Fact]
    public async Task Activation_ping_is_200_with_no_side_effects()
    {
        using var h = new Harness(seedItems: false);
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, "webhook_id=42", null);
        Assert.Equal(200, r.Status);
        using var check = h.Ctx(TenantB);
        Assert.Empty(check.SalesV2.IgnoreQueryFilters().ToList());
        Assert.Empty(check.WebstoreSkuMaps.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task Bad_signature_is_401_and_writes_nothing()
    {
        using var h = new Harness(seedItems: true);
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, Body(), "AAAA");
        Assert.Equal(401, r.Status);
        using var check = h.Ctx(TenantB);
        Assert.Empty(check.SalesV2.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task Missing_secret_is_500()
    {
        using var h = new Harness(seedItems: false);
        h.Secrets.Secret = null;
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, Body(), "AAAA");
        Assert.Equal(500, r.Status);
    }

    // ---- the tenant-scope proof + happy path ----

    [Fact]
    public async Task Delivery_records_under_the_webstores_tenant_despite_a_wrong_ambient_tenant()
    {
        using var h = new Harness(seedItems: true);
        var body = Body();
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, WooWebhookVerifier.Sign(body, Secret));
        Assert.Equal(200, r.Status);

        // Tenant B (the webstore's tenant) sees the sale…
        using (var b = h.Ctx(TenantB))
        {
            var sale = Assert.Single(b.SalesV2.AsNoTracking().ToList());
            Assert.Equal(TenantB, sale.TenantId);
            Assert.Equal(SaleChannel.WebStore, sale.Channel);
            Assert.Equal(10316, sale.GrossPence);
            Assert.Equal(h.TillId, sale.TillId);           // derived via the virtual Device row
            var outbox = Assert.Single(b.OutboxEvents.AsNoTracking().ToList());
            Assert.Equal(TenantB, outbox.TenantId);
        }
        // …and Tenant A (the ambient tenant the handler ran under) sees NOTHING.
        using (var a = h.Ctx(TenantA))
            Assert.Empty(a.SalesV2.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Redelivery_is_200_duplicate_with_one_sale()
    {
        using var h = new Harness(seedItems: true);
        var body = Body();
        var sig = WooWebhookVerifier.Sign(body, Secret);
        await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, sig);
        var r2 = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, sig);
        Assert.Equal(200, r2.Status);
        using var check = h.Ctx(TenantB);
        Assert.Single(check.SalesV2.ToList());
    }

    [Fact]
    public async Task Unknown_skus_are_202_and_park_mapping_rows_under_the_right_tenant()
    {
        using var h = new Harness(seedItems: false);   // no catalogue → every SKU unmatched
        var body = Body();
        var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, WooWebhookVerifier.Sign(body, Secret));
        Assert.Equal(202, r.Status);

        using (var b = h.Ctx(TenantB))
        {
            var rows = b.WebstoreSkuMaps.AsNoTracking().ToList();
            Assert.Equal(Skus7127.Length, rows.Count);
            Assert.All(rows, m => Assert.Equal(TenantB, m.TenantId));
            Assert.Empty(b.SalesV2.ToList());          // parked, not sold
        }
        using (var a = h.Ctx(TenantA))
            Assert.Empty(a.WebstoreSkuMaps.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Unpaid_or_failed_order_is_200_skipped_with_no_sale()
    {
        using var h = new Harness(seedItems: true);
        foreach (var status in new[] { "pending", "failed", "cancelled", "refunded", "on-hold" })
        {
            var body = Body().Replace("\"status\":\"completed\"", $"\"status\":\"{status}\"");
            var r = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, WooWebhookVerifier.Sign(body, Secret));
            Assert.Equal(200, r.Status);
        }
        using var check = h.Ctx(TenantB);
        Assert.Empty(check.SalesV2.IgnoreQueryFilters().ToList());     // none of them ingested
        Assert.Empty(check.SaleQuarantine.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task Non_gbp_is_202_and_parks_one_quarantine_row_idempotently()
    {
        using var h = new Harness(seedItems: true);
        var body = Body().Replace("\"currency\":\"GBP\"", "\"currency\":\"USD\"");
        var sig = WooWebhookVerifier.Sign(body, Secret);

        var r1 = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, sig);
        Assert.Equal(202, r1.Status);
        var r2 = await h.Handler.HandleOrderWebhookAsync(h.WebStoreId, body, sig);   // redelivery
        Assert.Equal(202, r2.Status);

        using var check = h.Ctx(TenantB);
        var q = Assert.Single(check.SaleQuarantine.IgnoreQueryFilters().ToList());   // ONE row, not two
        Assert.Equal(TenantB, q.TenantId);
        Assert.Contains("GBP-only", q.Reason);
        Assert.Empty(check.SalesV2.ToList());
    }
}
