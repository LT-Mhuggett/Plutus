using System;
using System.IO;
using System.Linq;
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

/// <summary>Web refunds (Phase 6 gap closed 2026-07-27): a refunded order adjusts its EXISTING
/// sale idempotently; a refund on a never-ingested order is a no-op; a partial refund coexists
/// with the recorded sale. Uses the real refunded fixture (order 8347, −£61.49).</summary>
public class WebstoreRefundTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Biz = Guid.NewGuid();

    private static MySqlDbContext Ctx(DbContextOptions<MySqlDbContext> o) =>
        new(o, new FixedTenantContext(Tenant)) { CurrentUser = "refund-test" };

    private static string Fixture(string f) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo", f));

    private sealed class OkResolver : IWebstoreSkuResolver
    {
        public Guid? Resolve(string sku) => DeterministicGuid.ForName("test:item", sku);
    }
    private sealed class NoopQueue : IWebstoreSkuMapQueue
    {
        public Task EnqueueAsync(WebstoreConnectionContext c, long o, System.Collections.Generic.IReadOnlyList<string> s, CancellationToken ct = default) => Task.CompletedTask;
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
            var status = (await new SalesIngestService(_db).IngestAsync(req, _ctx.TenantId, _ctx.DeviceId, "t")).Status;
            // ⚠ Mirrors the real sink — 202 is NotRecorded, never AlreadyRecorded.
            return status == 201 ? SaleSinkOutcome.Recorded : status == 200 ? SaleSinkOutcome.AlreadyRecorded : SaleSinkOutcome.NotRecorded;
        }
    }

    private static (SqliteConnection conn, DbContextOptions<MySqlDbContext> o, WebstoreConnectionContext ctx) Open()
    {
        var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();
        var o = new DbContextOptionsBuilder<MySqlDbContext>().UseSqlite(conn).Options;
        var ctx = new WebstoreConnectionContext
        { WebStoreId = Uuid7.New(), TenantId = Tenant, TillId = Uuid7.New(), DeviceId = Uuid7.New() };
        using var db = Ctx(o);
        db.Database.EnsureCreated();
        return (conn, o, ctx);
    }

    private static WooOrder Order8347() =>
        JsonSerializer.Deserialize<WooOrder>(Fixture("order-8347.json"), WooJson.Options)!;

    [Fact]
    public async Task Refunded_order_with_no_ingested_sale_is_a_noop()
    {
        var (conn, o, ctx) = Open();
        using var _ = conn;
        using var db = Ctx(o);
        var order = Order8347();
        Assert.Equal("refunded", order.Status);
        Assert.Single(order.Refunds);

        // Route it end-to-end like the handler does: skipped as a sale, then refunds applied.
        var proc = new WebstoreWebhookProcessor(new IngestSink(db, ctx), new NoopQueue());
        var r = await proc.RouteOrderAsync(order, ctx, new OkResolver());
        Assert.Equal(WebstoreInboundStatus.Skipped, r.Status);
        Assert.NotNull(r.Order);                                   // order rides along for refunds
        var applied = await WebstoreRefunds.ApplyAsync(db, ctx, r.Order!, CancellationToken.None);

        Assert.Equal(0, applied);                                  // no sale → nothing to adjust
        Assert.Empty(db.SaleAdjustments.IgnoreQueryFilters().ToList());
    }

    /// <summary>The sale as it would exist had the order been ingested when paid — its Id IS the
    /// deterministic saleId, which is all the refund path keys on.</summary>
    private static void SeedSaleFor(MySqlDbContext db, WebstoreConnectionContext ctx, long wooOrderId, long grossPence)
    {
        var saleId = WooOrderMapper.SaleIdFor(ctx.DeviceId, wooOrderId);
        var line = new SaleLine
        {
            Id = Uuid7.New(), TenantId = ctx.TenantId, SaleId = saleId, LineNo = 1,
            ItemId = Uuid7.New(), ItemIdOne = "0000000000000", Qty = 1,
            UnitPricePence = grossPence, LineGrossPence = grossPence, VatRateBp = 0, VatAmountPence = 0,
        };
        var tender = new SaleTender
        {
            Id = Uuid7.New(), TenantId = ctx.TenantId, SaleId = saleId,
            TenderType = TenderType.Online, AmountPence = grossPence, ChangePence = 0,
        };
        db.SalesV2.Add(SaleV2.Create(saleId, ctx.TenantId, ctx.TillId, ctx.DeviceId, wooOrderId,
            SaleChannel.WebStore, DateOnly.FromDateTime(DateTime.UtcNow), DateTime.UtcNow, DateTime.UtcNow,
            grossPence, 0, new[] { line }, new[] { tender }));
        db.SaveChanges();
    }

    [Fact]
    public async Task Refund_on_an_existing_sale_writes_one_adjustment_idempotently()
    {
        var (conn, o, ctx) = Open();
        using var _ = conn;
        using var db = Ctx(o);
        var proc = new WebstoreWebhookProcessor(new IngestSink(db, ctx), new NoopQueue());

        // 1. The sale exists (as if ingested when the order was paid).
        SeedSaleFor(db, ctx, 8347, 6149);

        // 2. Later, order.updated arrives with status refunded + the refund entry.
        var refunded = Order8347();
        var r2 = await proc.RouteOrderAsync(refunded, ctx, new OkResolver());
        Assert.Equal(WebstoreInboundStatus.Skipped, r2.Status);    // never re-ingested
        var n1 = await WebstoreRefunds.ApplyAsync(db, ctx, refunded, CancellationToken.None);
        Assert.Equal(1, n1);

        var adj = Assert.Single(db.SaleAdjustments.IgnoreQueryFilters().ToList());
        Assert.Equal(AdjustmentType.Refund, adj.Type);
        Assert.Equal(6149, adj.AmountPence);                       // |−61.49| in pence
        Assert.Equal(WooOrderMapper.SaleIdFor(ctx.DeviceId, refunded.Id), adj.OriginalSaleId);
        Assert.Contains("woo-refund #8348", adj.Reason);
        Assert.Equal(WebstoreRefunds.AdjustmentIdFor(ctx.DeviceId, 8348), adj.Id);

        // 3. Redelivery (webhook retry / poll overlap) → no second adjustment.
        var n2 = await WebstoreRefunds.ApplyAsync(db, ctx, Order8347(), CancellationToken.None);
        Assert.Equal(0, n2);
        Assert.Single(db.SaleAdjustments.IgnoreQueryFilters().ToList());
        // And the sale is still exactly one row (Skipped never re-ingests).
        Assert.Single(db.SalesV2.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task Partial_refund_coexists_with_the_recorded_sale()
    {
        var (conn, o, ctx) = Open();
        using var _ = conn;
        using var db = Ctx(o);

        // The sale exists; a COMPLETED order arrives carrying a partial refund entry
        // (Woo keeps the status completed for partial refunds).
        SeedSaleFor(db, ctx, 8347, 6149);
        var order = Order8347();
        order.Status = "completed";
        order.Refunds[0].Total = "-10.00";
        var n = await WebstoreRefunds.ApplyAsync(db, ctx, order, CancellationToken.None);

        Assert.Equal(1, n);
        Assert.Single(db.SalesV2.IgnoreQueryFilters().ToList());   // the sale stays
        var adj = Assert.Single(db.SaleAdjustments.IgnoreQueryFilters().ToList());
        Assert.Equal(1000, adj.AmountPence);                       // £10 partial
    }
}
