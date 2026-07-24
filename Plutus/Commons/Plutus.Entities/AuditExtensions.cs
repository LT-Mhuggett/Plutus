using System;
using System.Text.Json;
using Plutus.Entities.Models;

namespace Plutus.Entities
{
    /// <summary>WP3.2: one-line audit writes for admin mutations. The row is ADDED to the
    /// change tracker, not saved — callers save it in the SAME SaveChanges as the mutation,
    /// so the trail can never disagree with the data.</summary>
    public static class AuditExtensions
    {
        public static void Audit(this MySqlDbContext db, Guid tenantId, Guid actorUserId,
            string action, string entityType, string entityId, object detail = null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId ?? "",
                DetailJson = detail == null ? null : JsonSerializer.Serialize(detail),
                AtUtc = DateTime.UtcNow,
            });
        }
    }
}
