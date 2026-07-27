using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Plutus.Entities.Models;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP6.2 core: the WooCommerce order → v1 sale mapper, exercised against REAL
/// PII-scrubbed fixtures captured from the live Kapow store (tests/Fixtures/Woo). Proves the
/// money model (Woo net + separate VAT → platform VAT-inclusive), the T1.3 invariants, guest
/// handling, deterministic idempotent saleIds, and the unmatched-SKU review path.</summary>
public class WebstoreMapperTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Till = Guid.NewGuid();
    private static readonly Guid Device = Guid.NewGuid();
    private static readonly WebstoreConnectionContext Ctx =
        new() { TenantId = Tenant, TillId = Till, DeviceId = Device };

    private static readonly JsonSerializerOptions J = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo");
    private static WooOrder Order(string file) =>
        JsonSerializer.Deserialize<WooOrder>(File.ReadAllText(Path.Combine(FixtureDir, file)), J)!;
    private static List<WooRefund> Refunds(string file) =>
        JsonSerializer.Deserialize<List<WooRefund>>(File.ReadAllText(Path.Combine(FixtureDir, file)), J)!;

    /// <summary>Resolver that accepts a fixed set of SKUs (deterministic ItemId per SKU).</summary>
    private sealed class StubResolver : IWebstoreSkuResolver
    {
        private readonly HashSet<string> _known;
        public StubResolver(IEnumerable<string> known) => _known = new(known);
        public Guid? Resolve(string sku) =>
            _known.Contains(sku) ? Plutus.SharedKernel.DeterministicGuid.ForName("test:item", sku) : (Guid?)null;
    }

    // Accept every SKU present on the order (so mapping reaches the money path).
    private static StubResolver ResolverForAllSkus(WooOrder o) =>
        new(o.LineItems.Where(l => !string.IsNullOrWhiteSpace(l.Sku)).Select(l => l.Sku!.Trim()));

    [Fact]
    public void Maps_a_multiline_order_with_penny_exact_totals()
    {
        var o = Order("order-7127.json");
        var r = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o));

        Assert.True(r.Ok, r.QuarantineReason ?? string.Join(",", r.UnmatchedSkus));
        var sale = r.Sale!;
        Assert.Equal(SaleChannel.WebStore, sale.Channel);
        Assert.Equal(4, sale.Lines.Count(l => l.ItemIdOne != null));  // 4 catalogue lines
        // Order 7127: net 85.98 + VAT 17.18 = gross 103.16.
        Assert.Equal(10316, sale.GrossPence);
        Assert.Equal(1718, sale.VatPence);
        // A single online tender that nets to the gross (invariant 4 held → Create didn't throw).
        var t = Assert.Single(sale.Tenders);
        Assert.Equal(TenderType.Online, t.TenderType);      // paypal
        Assert.Equal(10316, t.AmountPence);
        // Every catalogue line carries its SKU as the barcode + in DiscountsJson.
        Assert.All(sale.Lines.Where(l => l.ItemIdOne != null),
            l => Assert.Contains(l.ItemIdOne!, l.DiscountsJson!));
        // 20% VAT band.
        Assert.All(sale.Lines.Where(l => l.ItemIdOne != null), l => Assert.Equal(2000, l.VatRateBp));
    }

    [Fact]
    public void Line_invariant_holds_for_every_line()
    {
        var o = Order("order-7127.json");
        var sale = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o)).Sale!;
        foreach (var l in sale.Lines)
            Assert.Equal(l.UnitPricePence * l.Qty - l.DiscountPence, l.LineGrossPence);
        Assert.Equal(sale.GrossPence, sale.Lines.Sum(l => l.LineGrossPence));
        Assert.Equal(sale.VatPence, sale.Lines.Sum(l => l.VatAmountPence));
    }

    [Fact]
    public void SaleId_is_deterministic_across_redeliveries()
    {
        var o = Order("order-7127.json");
        var a = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o)).Sale!;
        var b = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o)).Sale!;
        Assert.Equal(a.Id, b.Id);                                   // stable → idempotent ingest dedupes
        Assert.Equal(WooOrderMapper.SaleIdFor(Device, o.Id), a.Id);
        Assert.Equal(o.Id, a.DeviceSeq);
    }

    [Fact]
    public void Guest_order_maps_without_a_customer()
    {
        var o = Order("order-guest-8505.json");
        Assert.Equal(0, o.CustomerId);                             // guest checkout
        var r = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o));
        Assert.True(r.Ok, r.QuarantineReason ?? string.Join(",", r.UnmatchedSkus));
        Assert.Equal(WebstoreMoneyGross(o), r.Sale!.GrossPence);
    }

    [Fact]
    public void Unknown_sku_routes_to_the_mapping_queue_not_a_sale()
    {
        var o = Order("order-7127.json");
        // Resolver knows nothing → every line is unmatched.
        var r = WooOrderMapper.MapOrder(o, Ctx, new StubResolver(Array.Empty<string>()));
        Assert.False(r.Ok);
        Assert.True(r.NeedsMapping);
        Assert.Equal(o.LineItems.Count, r.UnmatchedSkus.Count);
        Assert.Null(r.Sale);
    }

    [Fact]
    public void Non_gbp_order_quarantines()
    {
        var o = Order("order-7127.json");
        o.Currency = "USD";
        var r = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o));
        Assert.True(r.IsQuarantined);
        Assert.Contains("GBP-only", r.QuarantineReason);
    }

    [Fact]
    public void Refund_maps_to_an_adjustment_against_the_same_sale()
    {
        var parent = Order("order-8347.json");
        var refund = Refunds("refunds-8347.json").Single();
        var adj = WooOrderMapper.MapRefund(refund, parent, Ctx);
        Assert.Equal(AdjustmentType.Refund, adj.Type);
        Assert.Equal(WooOrderMapper.SaleIdFor(Device, parent.Id), adj.OriginalSaleId);
        Assert.Equal(6149, adj.AmountPence);                       // refund amount 61.49
    }

    [Fact]
    public void All_completed_fixtures_reconcile_to_their_declared_total()
    {
        foreach (var file in new[] { "order-7127.json", "order-7137.json", "order-8502.json", "order-guest-8505.json" })
        {
            var o = Order(file);
            var r = WooOrderMapper.MapOrder(o, Ctx, ResolverForAllSkus(o));
            Assert.True(r.Ok, $"{file}: {r.QuarantineReason ?? string.Join(",", r.UnmatchedSkus)}");
            Assert.Equal(WebstoreMoneyGross(o), r.Sale!.GrossPence);
        }
    }

    // Expected gross (inc VAT) in pence straight from the order's declared total.
    private static long WebstoreMoneyGross(WooOrder o) =>
        (long)Math.Round(decimal.Parse(o.Total!, System.Globalization.CultureInfo.InvariantCulture) * 100m);
}
