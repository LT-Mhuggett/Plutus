using System;
using System.Collections.Generic;
using System.IO;
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
using Plutus.Sales;
using Plutus.SharedKernel;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP6.2 reconciliation poll: a stubbed Woo REST API returns real fixture orders; the
/// reconciler routes them through the SAME pipeline as webhooks — recording, deduping, skipping —
/// and advances the per-webstore cursor. This is the webhook-outage healing path.</summary>
public class WebstoreReconcilerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Biz = Guid.NewGuid();
    private static readonly string[] Skus7127 = { "5011921156993", "5011921157013", "5011921142163", "5011921142156" };

    private sealed class FakeSecrets : IWebstoreSecretProvider
    {
        public string? GetWebhookSecret(Guid id) => "unused";
        public WebstoreRestCredentials? GetRestCredentials(Guid id) => new("ck_test", "cs_test");
    }

    /// <summary>Serves a fixed JSON body for any /orders request; counts calls.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string Body = "[]";
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
            resp.Headers.Add("X-WP-TotalPages", "1");
            return Task.FromResult(resp);
        }
    }

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
            var outcome = await new SalesIngestService(_db).IngestAsync(req, _ctx.TenantId, _ctx.DeviceId, "poll");
            // ⚠ MIRRORS THE REAL SINK. 202 is QUARANTINED — not in — and a double that called that
            // "recorded" would hide the bug this three-way answer exists to prevent.
            return outcome.Status == 201 ? SaleSinkOutcome.Recorded : outcome.Status == 200 ? SaleSinkOutcome.AlreadyRecorded : SaleSinkOutcome.NotRecorded;
        }
    }

    private static string Fixture(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo", file));

    [Fact]
    public async Task Poll_records_missed_orders_dedupes_reruns_and_advances_the_cursor()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var options = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        var wsId = Uuid7.New(); var tillId = Uuid7.New(); var devId = Uuid7.New();

        using (var db = new MySqlDbContext(options, new FixedTenantContext(Tenant)) { CurrentUser = "t" })
        {
            db.Database.EnsureCreated();
            db.WebStores.Add(new WebStoreDetails
            {
                Id = wsId, Name = "Web", Url = "https://example.test", Enabled = true,
                TillId = tillId, DeviceId = devId, CreatedAtUtc = DateTime.UtcNow,
                // Cursor set BEFORE the fixture order's date_modified so the advance is testable
                // (a null cursor floors at now − InitialLookback, which is after the 2025 fixture).
                OrdersCursorUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            db.Devices.Add(new Device
            {
                Id = devId, TenantId = Tenant, TillId = tillId,
                SecretHash = new byte[] { 1 }, SecretSalt = new byte[] { 1 }, Status = DeviceStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            });
            foreach (var sku in Skus7127)
                db.Items.Add(new Item
                {
                    IdOne = sku, IdTwo = Biz, Name = sku, Brand = "-", Desc = "",
                    Cost = 1, ExPrice = 1, Price = 1, TaxId = 1, CatId = Guid.NewGuid(),
                });
            db.SaveChanges();
        }

        // The "missed webhook": order 7127 (completed) + a pending copy that must be skipped.
        var completed = Fixture("order-7127.json");
        var pending = Fixture("order-7137.json").Replace("\"status\":\"completed\"", "\"status\":\"pending\"");
        var handler = new StubHandler { Body = $"[{completed},{pending}]" };

        var pipelines = new WebstoreWebhookPipelineFactory(options, (db, ctx) => new IngestSink(db, ctx));
        var reconciler = new WebstoreReconciler(
            options, pipelines, new FakeSecrets(), new HttpClient(handler),
            new WebstoreOptions { OverlapMinutes = 5, InitialLookbackHours = 24 });

        // Pass 1: records the completed order, skips the pending one.
        var s1 = await reconciler.RunOnceAsync();
        Assert.Equal(1, s1.Recorded);
        Assert.Equal(1, s1.Skipped);
        Assert.Equal(1, s1.Webstores);
        Assert.True(s1.Requests >= 1);

        using (var check = new MySqlDbContext(options, new FixedTenantContext(Tenant)) { CurrentUser = "t" })
        {
            var sale = Assert.Single(check.SalesV2.AsNoTracking().ToList());
            Assert.Equal(10316, sale.GrossPence);
            Assert.Equal(tillId, sale.TillId);
            // Cursor advanced to the LATEST date_modified_gmt seen — the pending order's
            // (2025-06-26), later than the completed one's. Skipped orders advance the cursor
            // too: when one becomes paid, order.updated bumps its date_modified past this.
            var ws = check.WebStores.AsNoTracking().Single();
            Assert.NotNull(ws.OrdersCursorUtc);
            Assert.Equal(new DateTime(2025, 6, 26, 11, 6, 55, DateTimeKind.Utc), ws.OrdersCursorUtc!.Value);
        }

        // Pass 2 (same responses — the overlap window): everything dedupes, nothing new.
        var s2 = await reconciler.RunOnceAsync();
        Assert.Equal(0, s2.Recorded);
        Assert.Equal(1, s2.Duplicates);
        using (var check = new MySqlDbContext(options, new FixedTenantContext(Tenant)) { CurrentUser = "t" })
            Assert.Single(check.SalesV2.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Poll_skips_webstores_without_rest_credentials()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var options = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        using (var db = new MySqlDbContext(options, new FixedTenantContext(Tenant)) { CurrentUser = "t" })
        {
            db.Database.EnsureCreated();
            db.WebStores.Add(new WebStoreDetails
            {
                Id = Uuid7.New(), Name = "NoCreds", Url = "https://example.test", Enabled = true,
                TillId = Uuid7.New(), DeviceId = Uuid7.New(), CreatedAtUtc = DateTime.UtcNow,
            });
            db.SaveChanges();
        }

        var noCreds = new NoCredsSecrets();
        var handler = new StubHandler();
        var reconciler = new WebstoreReconciler(
            options, new WebstoreWebhookPipelineFactory(options, (db, ctx) => new IngestSink(db, ctx)),
            noCreds, new HttpClient(handler), new WebstoreOptions());

        var s = await reconciler.RunOnceAsync();
        Assert.Equal(0, s.Webstores);      // skipped — not counted as polled
        Assert.Equal(0, handler.Calls);    // and no request hit the site
    }

    private sealed class NoCredsSecrets : IWebstoreSecretProvider
    {
        public string? GetWebhookSecret(Guid id) => null;
        public WebstoreRestCredentials? GetRestCredentials(Guid id) => null;
    }
}
