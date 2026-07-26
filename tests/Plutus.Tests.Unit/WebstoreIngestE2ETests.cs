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
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>Phase 6 end-to-end: a signed Woo order webhook flows through the real pipeline —
/// verify → map → the REAL <see cref="SalesIngestService"/> → the SalesV2 tables + outbox — and a
/// duplicate delivery dedupes on the deterministic saleId (idempotent). This exercises the exact
/// components the host will wire, minus HTTP; the host's sink adapter mirrors the test sink here.</summary>
public class WebstoreIngestE2ETests
{
    private const string Secret = "whsec_e2e";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly WebstoreConnectionContext Ctx =
        new() { TenantId = Tenant, TillId = Guid.NewGuid(), DeviceId = Guid.NewGuid() };

    private static MySqlDbContext NewCtx(SqliteConnection conn) =>
        new(new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options,
            new FixedTenantContext(Tenant)) { CurrentUser = "webstore" };

    private static string Body(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo", file));

    private sealed class OkResolver : IWebstoreSkuResolver
    {
        public Guid? Resolve(string sku) => Plutus.SharedKernel.DeterministicGuid.ForName("test:item", sku);
    }
    private sealed class NoopQueue : IWebstoreSkuMapQueue
    {
        public Task EnqueueAsync(WebstoreConnectionContext ctx, long o, System.Collections.Generic.IReadOnlyList<string> s, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>The sink the HOST will implement: convert the mapped SaleV2 to the ingest DTO and
    /// submit through the platform's idempotent ingest. New sale → true; duplicate → false.</summary>
    private sealed class IngestSink : IWebstoreSaleSink
    {
        private readonly SqliteConnection _conn;
        public IngestSink(SqliteConnection conn) => _conn = conn;
        public async Task<bool> SubmitAsync(SaleV2 sale, CancellationToken ct = default)
        {
            await using var db = NewCtx(_conn);
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
            var outcome = await new SalesIngestService(db).IngestAsync(req, Tenant, sale.DeviceId, "webstore");
            return outcome.Status == 201;   // 201 new, 200 duplicate
        }
    }

    [Fact]
    public async Task Signed_webhook_ingests_a_sale_then_dedupes_on_redelivery()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        using (var db = NewCtx(conn)) db.Database.EnsureCreated();

        var body = Body("order-7127.json");
        var sig = WooWebhookVerifier.Sign(body, Secret);
        var proc = new WebstoreWebhookProcessor(new IngestSink(conn), new NoopQueue());

        // First delivery → recorded.
        var r1 = await proc.ProcessOrderWebhookAsync(body, sig, Secret, Ctx, new OkResolver());
        Assert.Equal(WebstoreInboundStatus.Recorded, r1.Status);

        using (var check = NewCtx(conn))
        {
            var sale = Assert.Single(check.SalesV2.Include(s => s.Lines).Include(s => s.Tenders).ToList());
            Assert.Equal(SaleChannel.WebStore, sale.Channel);
            Assert.Equal(10316, sale.GrossPence);
            Assert.Equal(1718, sale.VatPence);
            Assert.Equal(r1.SaleId, sale.Id);
            // Catalogue lines carry the barcode (extracted from DiscountsJson by the ingest).
            Assert.Equal(4, sale.Lines.Count(l => l.ItemIdOne != null));
            Assert.Single(check.OutboxEvents.Where(e => e.EventType == "SaleRecorded").ToList());
        }

        // Duplicate delivery (same deterministic saleId) → deduped, still one sale.
        var r2 = await proc.ProcessOrderWebhookAsync(body, sig, Secret, Ctx, new OkResolver());
        Assert.Equal(WebstoreInboundStatus.Duplicate, r2.Status);
        using (var check = NewCtx(conn))
            Assert.Single(check.SalesV2.ToList());
    }
}
