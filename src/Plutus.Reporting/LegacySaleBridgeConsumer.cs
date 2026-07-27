#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Reporting
{
    /// <summary>
    /// TRANSITIONAL Phase-2 bridge (decision 2026-07-24, see HANDOVER.md): projects each
    /// SaleRecorded event into the LEGACY tables (Sales/Trans/PaySales/Transaction_Discounts/
    /// CheckoutItemChange/Refunds + a Stocks decrement) so the existing reports, recall and
    /// stock views keep working while the client writes ONLY to POST /api/v1/sales (WP2.1).
    /// Deleted when the WP3.3 rollup projections replace the legacy read model.
    ///
    /// Projection metadata rides in SaleLine.DiscountsJson (shape owned by the web POS,
    /// src/pipeline.ts): {"itemIdOne":"...","exUnitPence":100,"discounts":[{"id":1,"rate":0.1}],
    /// "return":{"originSaleId":"..."}}. Sales without it (e.g. future MAUI tills, replayed ETL
    /// events) are skipped — they are not web-POS sales and their projection story arrives with
    /// Phase 3/4.
    ///
    /// Contract with OutboxDrainer: this consumer is registered to resolve the SAME scoped
    /// RepositoryContext the drainer saves, and it never calls SaveChanges itself — the drainer
    /// commits the projection atomically with the ProcessedEvents row and the consumer offset,
    /// which is what makes the projection effectively-once. All validation/reads happen BEFORE
    /// the first context mutation so a thrown (retried/parked) event never leaves partial state
    /// tracked in the shared context.
    /// </summary>
    public sealed class LegacySaleBridgeConsumer : IEventConsumer
    {
        public const string ConsumerName = "legacy-sale-bridge";
        private readonly MySqlDbContext _db;

        public LegacySaleBridgeConsumer(MySqlDbContext db) => _db = db;

        public string Name => ConsumerName;

        private sealed class LineMeta
        {
            [JsonPropertyName("itemIdOne")] public string ItemIdOne { get; set; }
            [JsonPropertyName("exUnitPence")] public long? ExUnitPence { get; set; }
            [JsonPropertyName("discounts")] public List<MetaDiscount> Discounts { get; set; }
            [JsonPropertyName("return")] public MetaReturn Return { get; set; }
        }
        private sealed class MetaDiscount
        {
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("rate")] public decimal Rate { get; set; }
        }
        private sealed class MetaReturn
        {
            [JsonPropertyName("originSaleId")] public Guid OriginSaleId { get; set; }
        }
        private sealed class TenderMeta
        {
            [JsonPropertyName("payId")] public int PayId { get; set; }
        }

        public async Task HandleAsync(DomainEvent e, CancellationToken ct)
        {
            if (e is not SaleRecorded recorded) return;

            // ---- phase 1: read + validate (no context mutation, safe to throw/retry) ----

            var sale = await _db.SalesV2.IgnoreQueryFilters().AsNoTracking()
                .Include(s => s.Lines).Include(s => s.Tenders)
                .FirstOrDefaultAsync(s => s.Id == recorded.SaleId, ct);
            if (sale == null)
                throw new InvalidOperationException($"SaleRecorded {recorded.SaleId}: SaleV2 row not found.");

            if (sale.LegacyRef != null) return; // migrated FROM legacy — never project back

            var metas = sale.Lines.OrderBy(l => l.LineNo)
                .Select(l => (Line: l, Meta: ParseMeta(l.DiscountsJson))).ToList();
            if (metas.All(m => string.IsNullOrEmpty(m.Meta?.ItemIdOne)))
                return; // no projection metadata — not a web-POS sale (see class comment)

            var missing = metas.FirstOrDefault(m => string.IsNullOrEmpty(m.Meta?.ItemIdOne));
            if (missing.Line != null)
                throw new InvalidOperationException(
                    $"Sale {sale.Id} line {missing.Line.LineNo}: projection metadata missing itemIdOne.");

            if (await _db.Sales.IgnoreQueryFilters().AnyAsync(s => s.Id == sale.Id, ct))
                return; // already projected (defence in depth on top of ProcessedEvents)

            var employee = sale.OperatorUserId.HasValue
                ? await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == sale.OperatorUserId.Value, ct)
                : null;

            var businessId = employee?.BusinessId
                ?? await _db.Business.IgnoreQueryFilters().AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync(ct);
            if (businessId == Guid.Empty)
                throw new InvalidOperationException($"Sale {sale.Id}: no business resolvable for projection.");

            var till = await _db.Till.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == sale.TillId, ct);
            var storeId = till?.StoreId ?? employee?.StoreId
                ?? throw new InvalidOperationException($"Sale {sale.Id}: no store resolvable (till {sale.TillId}).");

            var payMethods = await _db.PayMethods.AsNoTracking().ToListAsync(ct);

            // Stock rows to patch (read now, mutate in phase 2). Missing row = not stock-tracked.
            var stockPatches = new List<(Stock Row, int Delta)>();
            foreach (var (line, meta) in metas)
            {
                var stock = await _db.Stocks.IgnoreQueryFilters().FirstOrDefaultAsync(
                    s => s.IdOne == meta.ItemIdOne && s.IdTwo == businessId && s.IdThree == storeId, ct);
                if (stock != null) stockPatches.Add((stock, -line.Qty)); // sale −qty; return (negative qty) adds back
            }

            var legacy = BuildLegacySale(sale, metas, businessId, storeId, payMethods);

            // ---- phase 2: mutate the shared context (nothing below throws) ----

            _db.Sales.Add(legacy);
            StampTenant(legacy, sale.TenantId);
            foreach (var t in legacy.Transactions) { StampTenant(t, sale.TenantId); StampTenant(t.CheckoutItemChange, sale.TenantId); foreach (var d in t.Transaction_Discounts) StampTenant(d, sale.TenantId); }
            foreach (var p in legacy.PaySales) StampTenant(p, sale.TenantId);
            foreach (var r in legacy.Refunds) StampTenant(r, sale.TenantId);
            foreach (var (row, delta) in stockPatches) row.Quantity += delta;
        }

        private void StampTenant(object entity, Guid tenantId)
        {
            if (entity == null) return;
            _db.Entry(entity).Property("TenantId").CurrentValue = tenantId;
        }

        private static LineMeta ParseMeta(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<LineMeta>(json); }
            catch (JsonException) { return null; }
        }

        private static Sale BuildLegacySale(
            SaleV2 sale, List<(SaleLine Line, LineMeta Meta)> metas,
            Guid businessId, int storeId, List<PaymentMethod> payMethods)
        {
            var legacy = new Sale
            {
                Id = sale.Id,
                Total = sale.GrossPence / 100m,
                TotalExTax = (sale.GrossPence - sale.VatPence) / 100m,
                DateOfSale = sale.OccurredAtUtc,
                EmployeeId = sale.OperatorUserId ?? Guid.Empty,
                StoreId = storeId,
                TillId = sale.TillId,
                Transactions = new List<Transaction>(),
                PaySales = new List<PaymentMethod_Sale>(),
                Refunds = new List<Refund>(),
            };

            foreach (var (line, meta) in metas)
            {
                // per-unit ex-VAT price: carried by the client; derived (2dp) when absent
                var exUnitPence = meta.ExUnitPence
                    ?? (long)Math.Round((line.LineGrossPence - line.VatAmountPence) / (decimal)line.Qty, 0);

                if (line.Qty < 0)
                {
                    // return line (negative qty by WP2.1 convention) → legacy Refund row
                    if (meta.Return == null || meta.Return.OriginSaleId == Guid.Empty)
                        throw new InvalidOperationException(
                            $"Sale {sale.Id} line {line.LineNo}: return line without originSaleId.");
                    legacy.Refunds.Add(new Refund
                    {
                        Reason = "Till return",
                        Amount = -line.Qty,
                        ItemIdOne = meta.ItemIdOne,
                        ItemIdTwo = businessId,
                        AuthoriserId = sale.OperatorUserId ?? Guid.Empty,
                        SaleIdReturned = meta.Return.OriginSaleId,
                    });
                    continue;
                }

                var trans = new Transaction
                {
                    Amount = line.Qty,
                    ItemCostPrice = line.UnitPricePence / 100m,
                    ItemCostExPrice = exUnitPence / 100m,
                    ItemIdOne = meta.ItemIdOne,
                    ItemIdTwo = businessId,
                    TillId = sale.TillId,
                    Transaction_Discounts = (meta.Discounts ?? new List<MetaDiscount>())
                        .Select(d => new Transaction_Discount { SaleId = sale.Id, DiscountId = d.Id, DiscountRate = d.Rate })
                        .ToList(),
                    CheckoutItemChange = line.OverriddenFromPence.HasValue
                        ? new CheckoutItemChange
                        {
                            Price = line.UnitPricePence / 100m,
                            ExPrice = exUnitPence / 100m,
                            ItemIdOne = meta.ItemIdOne,
                            ItemIdTwo = businessId,
                        }
                        : null,
                };
                legacy.Transactions.Add(trans);
            }

            foreach (var tender in sale.Tenders)
            {
                legacy.PaySales.Add(new PaymentMethod_Sale
                {
                    PayId = ResolvePayId(tender, payMethods),
                    SaleId = sale.Id,
                    Amount = tender.AmountPence / 100m,
                    Change = tender.ChangePence / 100m,
                });
            }

            return legacy;
        }

        /// <summary>ProviderRef carries {"payId":N} from the web POS (interim, documented);
        /// falls back to matching the tender type against the method names.</summary>
        private static int ResolvePayId(SaleTender tender, List<PaymentMethod> methods)
        {
            if (!string.IsNullOrWhiteSpace(tender.ProviderRef))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<TenderMeta>(tender.ProviderRef);
                    if (meta != null && meta.PayId > 0 && methods.Any(m => m.Id == meta.PayId)) return meta.PayId;
                }
                catch (JsonException) { /* fall through to name match */ }
            }

            var wanted = tender.TenderType switch
            {
                TenderType.Cash => "cash",
                TenderType.Online => "online",
                TenderType.Credit => "credit",
                _ => "card",
            };
            var match = methods.FirstOrDefault(m => m.Name != null && m.Name.ToLowerInvariant().Contains(wanted))
                ?? methods.FirstOrDefault()
                ?? throw new InvalidOperationException("No payment methods exist to project a tender onto.");
            return match.Id;
        }
    }
}
