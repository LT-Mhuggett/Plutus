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
    /// <summary>
    /// WP5.3 cross-channel identity, FIRST SLICE — link-only, by email (D3: never auto-create).
    /// After a webstore order is recorded, if its buyer email matches an existing loyalty
    /// <see cref="Customer"/> (case-insensitive), record/refresh a <see cref="CustomerExternalRef"/>
    /// so the portal can show "linked to Woo". No match → nothing at all. Idempotent per
    /// (tenant, provider, externalId). The DbContext is the tenant-fixed pipeline context, so
    /// Customers and refs are already scoped to this connection's tenant.
    /// </summary>
    public static class WebstoreCustomerLink
    {
        public static async Task ApplyAsync(
            MySqlDbContext db, WebstoreConnectionContext ctx, WooOrder? order, CancellationToken ct = default)
        {
            var email = order?.Billing?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email)) return;

            // The external key: the Woo customer id for a registered buyer, else the order id for a
            // guest checkout (customer_id = 0) so guest orders don't all collide on "c0".
            var externalId = order!.CustomerId > 0 ? $"c{order.CustomerId}" : $"o{order.Id}";
            var lower = email.ToLowerInvariant();

            var customerId = await db.Customers
                .Where(c => c.Active && c.Email != null && c.Email.ToLower() == lower)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (customerId is null) return; // report-only: no matching customer, create nothing

            db.CurrentUser = "webstore-link"; // system-initiated write (no interactive actor)
            var existing = await db.CustomerExternalRefs
                .FirstOrDefaultAsync(r => r.Provider == "woo" && r.ExternalId == externalId, ct);
            if (existing is null)
            {
                db.CustomerExternalRefs.Add(new CustomerExternalRef
                {
                    Id = Uuid7.New(), TenantId = ctx.TenantId, CustomerId = customerId.Value,
                    Provider = "woo", ExternalId = externalId, Email = email,
                    CreatedAtUtc = DateTime.UtcNow, LastSeenAtUtc = DateTime.UtcNow,
                });
            }
            else
            {
                existing.CustomerId = customerId.Value; // re-point if the email now maps elsewhere
                existing.Email = email;
                existing.LastSeenAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
