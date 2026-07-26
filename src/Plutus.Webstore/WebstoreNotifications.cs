using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>WP6.2 pick-from-floor: create the "sold online — pull it off the shelf" notification
    /// for a RECORDED webstore sale. One per Woo order (unique index; a webhook+poll double-see is
    /// a no-op). The till polls unacked rows; ack on one till clears it for all. Shared by the
    /// webhook handler and the reconciliation poll — same rule as everything else on this path.</summary>
    public static class WebstoreNotifications
    {
        public static async Task CreateForRecordedAsync(
            MySqlDbContext db, WebstoreConnectionContext ctx, WooOrder order, int? storeId, CancellationToken ct = default)
        {
            if (await db.WebstoreNotifications.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(n => n.WebStoreId == ctx.WebStoreId && n.WooOrderId == order.Id, ct))
                return;

            var items = string.Join(", ", order.LineItems
                .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                .Select(l => l.Quantity > 1 ? $"{l.Quantity}× {l.Name}" : l.Name!)
                .Take(8));
            if (items.Length > 800) items = items.Substring(0, 800) + "…";

            db.WebstoreNotifications.Add(new WebstoreNotification
            {
                Id = Uuid7.New(), TenantId = ctx.TenantId, WebStoreId = ctx.WebStoreId, StoreId = storeId,
                WooOrderId = order.Id,
                Message = $"Web order #{order.Number ?? order.Id.ToString()}: {items} sold online — check if it needs removing from the shop floor.",
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
