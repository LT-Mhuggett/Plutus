using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>
    /// WP6.2 refunds (gap closed 2026-07-27): apply an order's Woo refunds as
    /// <see cref="SaleAdjustment"/> rows against the deterministic sale. Rules:
    ///  - only when the ORIGINAL SALE EXISTS in Plutus (a historical order refunded before we
    ///    ever ingested it has nothing to adjust — and is never ingested just to be refunded);
    ///  - idempotent per Woo refund id: the adjustment's Id is derived from it, so webhook
    ///    re-deliveries and poll overlap can never double-adjust;
    ///  - amounts are stored as positive magnitudes (Woo sends negatives), Type = Refund.
    /// Called from every inbound path (webhook, poll, retry) after routing.
    /// </summary>
    public static class WebstoreRefunds
    {
        public static Guid AdjustmentIdFor(Guid deviceId, long wooRefundId) =>
            DeterministicGuid.ForName("plutus:webstore-refund", deviceId.ToString("D"), wooRefundId.ToString());

        /// <summary>Apply any refunds on the order. Returns how many NEW adjustments were written.</summary>
        public static async Task<int> ApplyAsync(
            MySqlDbContext db, WebstoreConnectionContext ctx, WooOrder order, CancellationToken ct = default)
        {
            if (order.Refunds.Count == 0) return 0;

            var saleId = WooOrderMapper.SaleIdFor(ctx.DeviceId, order.Id);
            // No sale, nothing to adjust (pre-connector history, or an order we skipped).
            if (!await db.SalesV2.IgnoreQueryFilters().AsNoTracking().AnyAsync(s => s.Id == saleId, ct))
                return 0;

            var written = 0;
            foreach (var r in order.Refunds)
            {
                if (r.Id == 0) continue;
                var adjustmentId = AdjustmentIdFor(ctx.DeviceId, r.Id);
                if (await db.SaleAdjustments.IgnoreQueryFilters().AsNoTracking().AnyAsync(a => a.Id == adjustmentId, ct))
                    continue;   // already applied (redelivery / poll overlap)

                long amount;
                try { amount = Math.Abs(WebstoreMoney.ParsePence(r.Total)); }
                catch (FormatException) { continue; }
                if (amount == 0) continue;

                db.SaleAdjustments.Add(new SaleAdjustment
                {
                    Id = adjustmentId, TenantId = ctx.TenantId, Type = AdjustmentType.Refund,
                    OriginalSaleId = saleId, AmountPence = amount,
                    Reason = string.IsNullOrWhiteSpace(r.Reason) ? $"woo-refund #{r.Id}" : $"woo-refund #{r.Id}: {r.Reason}",
                    CreatedAtUtc = DateTime.UtcNow,
                });
                written++;
            }
            if (written > 0) await db.SaveChangesAsync(ct);
            return written;
        }
    }
}
