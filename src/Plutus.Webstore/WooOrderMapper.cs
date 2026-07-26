using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>Resolves a Woo product SKU to a Plutus catalogue ItemId (SKU ⇔ Items.IdOne).
    /// Returns null when the SKU is not in the catalogue — the mapper then routes the order to the
    /// SKU-mapping review queue (WP6.2) instead of ingesting it.</summary>
    public interface IWebstoreSkuResolver
    {
        Guid? Resolve(string sku);
    }

    /// <summary>Per-connection identity the mapper stamps onto the sale: the tenant, and the
    /// virtual webstore till/device that carries WebStore-channel sales (WP6.1).</summary>
    public sealed class WebstoreConnectionContext
    {
        public Guid WebStoreId { get; init; }
        public Guid TenantId { get; init; }
        public Guid TillId { get; init; }
        public Guid DeviceId { get; init; }
    }

    /// <summary>Outcome of mapping one Woo order: a validated sale, a needs-mapping list (unknown
    /// SKUs → WP6.2 review queue), or a quarantine reason (money didn't reconcile / bad data).</summary>
    public sealed class WooMapResult
    {
        public SaleV2? Sale { get; init; }
        public IReadOnlyList<string> UnmatchedSkus { get; init; } = Array.Empty<string>();
        public string? QuarantineReason { get; init; }
        public bool NeedsMapping => UnmatchedSkus.Count > 0;
        public bool IsQuarantined => Sale is null && !NeedsMapping;
        public bool Ok => Sale is not null;

        public static WooMapResult Recorded(SaleV2 s) => new() { Sale = s };
        public static WooMapResult Unmatched(IReadOnlyList<string> skus) => new() { UnmatchedSkus = skus };
        public static WooMapResult Quarantine(string reason) => new() { QuarantineReason = reason };
    }

    /// <summary>
    /// WP6.2 core: map a WooCommerce order to a v1 <see cref="SaleV2"/> (channel WebStore), enforcing
    /// the T1.3 money invariants via <see cref="SaleV2.Create"/> — so a mis-map quarantines rather
    /// than writing a wrong sale. Pure, offline-testable (see WebstoreMapperTests against real
    /// fixtures). Woo returns NET line totals + separate tax; the platform stores VAT-INCLUSIVE unit
    /// prices with VAT tracked alongside, so we recombine. The saleId is DETERMINISTIC from the Woo
    /// order id, so a duplicate webhook delivery dedupes through the idempotent ingest.
    /// </summary>
    public static class WooOrderMapper
    {
        // Sentinel ItemIds for non-catalogue lines. ItemIdOne stays null so the items-sold report
        // excludes them (shipping/fees are not items sold), while the money still reconciles.
        public static readonly Guid ShippingItemId = new Guid("0000da7a-5417-7000-8000-000000000010");
        public static readonly Guid FeeItemId = new Guid("0000da7a-5417-7000-8000-000000000011");

        /// <summary>Deterministic saleId for a Woo order — stable across re-deliveries and polls,
        /// namespaced by the connection's device so two connections' order #1 never collide.</summary>
        public static Guid SaleIdFor(Guid deviceId, long wooOrderId)
            => DeterministicGuid.ForName("plutus:webstore-sale", deviceId.ToString("D"), "woo", wooOrderId.ToString());

        public static WooMapResult MapOrder(WooOrder order, WebstoreConnectionContext ctx, IWebstoreSkuResolver resolver)
        {
            try
            {
                if (order is null) return WooMapResult.Quarantine("null order.");
                if (!string.Equals(order.Currency, "GBP", StringComparison.OrdinalIgnoreCase))
                    return WooMapResult.Quarantine($"order {order.Id}: non-GBP currency '{order.Currency}' (GBP-only ingest).");

                var saleId = SaleIdFor(ctx.DeviceId, order.Id);
                var occurredAtUtc = WebstoreMoney.ParseGmt(order.DatePaidGmt ?? order.DateCreatedGmt
                    ?? throw new FormatException("no order date."));
                var businessDay = DateOnly.FromDateTime(occurredAtUtc);

                // rate_id → basis points from the order-level tax lines (20.0% → 2000).
                var rateBpById = order.TaxLines
                    .GroupBy(t => t.RateId)
                    .ToDictionary(g => g.Key, g => (int)Math.Round(g.First().RatePercent * 100));

                var lines = new List<SaleLine>();
                var unmatched = new List<string>();
                int lineNo = 0;

                foreach (var li in order.LineItems)
                {
                    var sku = li.Sku?.Trim();
                    if (string.IsNullOrEmpty(sku)) { unmatched.Add($"(no SKU) {li.Name}"); continue; }
                    var itemId = resolver.Resolve(sku);
                    if (itemId is null) { unmatched.Add(sku); continue; }

                    var netTotal = WebstoreMoney.ParsePence(li.Total);        // ex-VAT, post line-discount
                    var vatTotal = WebstoreMoney.ParsePence(li.TotalTax);
                    var netSub = WebstoreMoney.ParsePence(li.Subtotal);       // ex-VAT, pre line-discount
                    var vatSub = WebstoreMoney.ParsePence(li.SubtotalTax);

                    var lineGrossInc = netTotal + vatTotal;                   // inc-VAT, post-discount → LineGross
                    var grossBeforeDiscInc = netSub + vatSub;                 // inc-VAT, pre-discount
                    var qty = li.Quantity <= 0 ? 1 : li.Quantity;
                    // Unit price rounded UP so Unit*Qty ≥ pre-discount gross; the residual + the real
                    // discount both live in DiscountPence, keeping it ≥ 0 and invariant-1 exact.
                    var unitInc = (grossBeforeDiscInc + qty - 1) / qty;
                    var discount = unitInc * qty - lineGrossInc;

                    lines.Add(new SaleLine
                    {
                        Id = Uuid7.New(), TenantId = ctx.TenantId, SaleId = saleId, LineNo = ++lineNo,
                        ItemId = itemId.Value, ItemIdOne = sku, Qty = qty,
                        UnitPricePence = unitInc, DiscountPence = discount, LineGrossPence = lineGrossInc,
                        VatRateBp = RateBpForLine(li, rateBpById, netTotal, vatTotal), VatAmountPence = vatTotal,
                        DiscountsJson = JsonSerializer.Serialize(new { itemIdOne = sku }),
                    });
                }

                if (unmatched.Count > 0) return WooMapResult.Unmatched(unmatched);
                if (lines.Count == 0) return WooMapResult.Quarantine($"order {order.Id}: no catalogue lines.");

                // Shipping + fees as non-catalogue lines so Σ gross == order total == tender.
                AddChargeLine(lines, ref lineNo, ctx.TenantId, saleId, ShippingItemId,
                    WebstoreMoney.ParsePence(order.ShippingTotal), WebstoreMoney.ParsePence(order.ShippingTax));
                foreach (var fee in order.FeeLines)
                    AddChargeLine(lines, ref lineNo, ctx.TenantId, saleId, FeeItemId,
                        WebstoreMoney.ParsePence(fee.Total), WebstoreMoney.ParsePence(fee.TotalTax));

                var gross = lines.Sum(l => l.LineGrossPence);
                var vat = lines.Sum(l => l.VatAmountPence);

                // Safety net: our reconstructed gross must equal the amount Woo says was charged.
                var declared = WebstoreMoney.ParsePence(order.Total);
                if (gross != declared)
                    return WooMapResult.Quarantine($"order {order.Id}: Σ line gross {gross}p != order total {declared}p (Δ {declared - gross}p).");

                var tender = new SaleTender
                {
                    Id = Uuid7.New(), TenantId = ctx.TenantId, SaleId = saleId,
                    TenderType = TenderFor(order.PaymentMethod), AmountPence = gross, ChangePence = 0,
                    ProviderRef = order.TransactionId,
                };

                var sale = SaleV2.Create(
                    saleId, ctx.TenantId, ctx.TillId, ctx.DeviceId, order.Id,
                    SaleChannel.WebStore, businessDay, occurredAtUtc, DateTime.UtcNow,
                    gross, vat, lines, new[] { tender },
                    note: $"woo-order #{order.Number ?? order.Id.ToString()}");

                return WooMapResult.Recorded(sale);
            }
            catch (InvalidSaleException ex) { return WooMapResult.Quarantine($"order {order?.Id}: {ex.Message}"); }
            catch (FormatException ex) { return WooMapResult.Quarantine($"order {order?.Id}: parse failure — {ex.Message}"); }
        }

        /// <summary>Map a Woo refund to a platform <see cref="SaleAdjustment"/> against the original
        /// order's (deterministic) sale. Kapow's refunds are whole-order amount refunds.</summary>
        public static SaleAdjustment MapRefund(WooRefund refund, WooOrder parentOrder, WebstoreConnectionContext ctx)
            => new SaleAdjustment
            {
                Id = Uuid7.New(), TenantId = ctx.TenantId, Type = AdjustmentType.Refund,
                OriginalSaleId = SaleIdFor(ctx.DeviceId, parentOrder.Id),
                AmountPence = WebstoreMoney.ParsePence(refund.Amount),
                Reason = string.IsNullOrWhiteSpace(refund.Reason) ? "woo refund" : refund.Reason,
                CreatedAtUtc = refund.DateCreatedGmt is { } d ? WebstoreMoney.ParseGmt(d) : DateTime.UtcNow,
            };

        private static void AddChargeLine(
            List<SaleLine> lines, ref int lineNo, Guid tenantId, Guid saleId, Guid itemId, long net, long vat)
        {
            var gross = net + vat;
            if (gross == 0) return;
            lines.Add(new SaleLine
            {
                Id = Uuid7.New(), TenantId = tenantId, SaleId = saleId, LineNo = ++lineNo,
                ItemId = itemId, ItemIdOne = null, Qty = 1,
                UnitPricePence = gross, DiscountPence = 0, LineGrossPence = gross,
                VatRateBp = net > 0 ? (int)Math.Round(vat * 10000m / net) : 0, VatAmountPence = vat,
            });
        }

        private static int RateBpForLine(WooLineItem li, IReadOnlyDictionary<long, int> rateBpById, long net, long vat)
        {
            foreach (var t in li.Taxes)
                if (rateBpById.TryGetValue(t.Id, out var bp)) return bp;
            return net > 0 ? (int)Math.Round(vat * 10000m / net) : 0;   // fallback: derive from the line
        }

        private static TenderType TenderFor(string? paymentMethod)
        {
            var m = (paymentMethod ?? string.Empty).ToLowerInvariant();
            if (m.Contains("cod") || m.Contains("cash") || m.Contains("cheque")) return TenderType.Cash;
            // paypal / stripe / ppcp / klarna / card gateways → an online/card tender. Web orders are
            // never physical cash unless COD, so default to Online.
            return TenderType.Online;
        }
    }
}
