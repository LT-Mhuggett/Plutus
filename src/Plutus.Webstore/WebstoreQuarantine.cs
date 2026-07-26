using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>Parks a mapper-level quarantine (non-GBP, total mismatch…) in SaleQuarantine so a
    /// 202 means "durably held for review" — keyed by the order's deterministic saleId so webhook
    /// re-deliveries AND poll overlap can't duplicate the parked row. Shared by the webhook
    /// handler and the reconciliation poll.</summary>
    public static class WebstoreQuarantine
    {
        public static async Task ParkAsync(
            MySqlDbContext db, WebstoreConnectionContext ctx, WebstoreInboundResult r, string payloadJson,
            CancellationToken ct = default)
        {
            if (r.WooOrderId is not { } orderId) return;
            var saleId = WooOrderMapper.SaleIdFor(ctx.DeviceId, orderId);
            // Same idempotency pattern as the ingest's quarantine path (unique (TenantId, SaleId)).
            if (await db.SaleQuarantine.IgnoreQueryFilters().AsNoTracking().AnyAsync(q => q.SaleId == saleId, ct))
                return;
            var reason = r.Detail ?? "webstore quarantine";
            db.SaleQuarantine.Add(new SaleQuarantine
            {
                Id = Uuid7.New(), TenantId = ctx.TenantId, SaleId = saleId,
                PayloadJson = payloadJson,
                Reason = reason.Length <= 500 ? reason : reason.Substring(0, 500),
                ReceivedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
