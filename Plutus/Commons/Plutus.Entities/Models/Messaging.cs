using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.SharedKernel;

namespace Plutus.Entities.Models
{
    /// <summary>
    /// WP17.3 per-tenant sending identity — the from-address/domain a tenant's messages go out as,
    /// in the model FROM DAY ONE so a shared domain can never be poisoned by one tenant's sending.
    /// GLOBAL (operator-managed), keyed one per (TenantId, Channel).
    /// </summary>
    public class TenantSendingIdentity
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public byte Channel { get; set; }          // MessageChannel
        public string FromAddress { get; set; }     // e.g. no-reply@shop.example
        public string Domain { get; set; }          // the sending domain (SPF/DKIM scope)
        public bool Verified { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// WP17.3 message event ledger — one row per send, advanced by a provider delivery webhook
    /// (sent → bounced / complained). Tenant-attributed so deliverability can be reported per tenant
    /// when a mailer lands. Keyed for webhook lookup by ProviderMessageId.
    /// </summary>
    public class MessageEvent
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public byte Channel { get; set; }           // MessageChannel
        public string ToAddress { get; set; }
        public string FromAddress { get; set; }
        public byte Status { get; set; }            // MessageDeliveryStatus
        public string ProviderMessageId { get; set; }
        public string Detail { get; set; }
        public DateTime AtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// Notification framework (17.3 config layer): the selected provider + its config for one
    /// channel (email/sms). GLOBAL, one row per channel (Channel is the key). ConfigJson holds the
    /// provider's fields (API key, sending domain, …) as a JSON object; fields the provider
    /// catalogue marks secret are never returned to the dashboard (write-only, redacted on read).
    /// Provider "none" = notifications off. NOTE: on the test env secrets live in this column; a
    /// production hardening moves them to the same file/secret store the webstore secrets use.
    /// </summary>
    public class NotificationSettings
    {
        public byte Channel { get; set; }          // MessageChannel
        public string Provider { get; set; } = "none";
        public string ConfigJson { get; set; }      // provider field values (JSON object)
        public bool Enabled { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string UpdatedBy { get; set; }
    }

    /// <summary>Records a send and maps a later delivery webhook onto it (by ProviderMessageId),
    /// preserving tenant attribution. Caller saves. GLOBAL table.</summary>
    public static class MessageEventStore
    {
        public static MessageEvent RecordSent(
            MySqlDbContext db, Guid tenantId, MessageChannel channel, string to, string from, string providerMessageId, DateTime nowUtc)
        {
            var row = new MessageEvent
            {
                Id = Uuid7.New(), TenantId = tenantId, Channel = (byte)channel, ToAddress = to, FromAddress = from,
                Status = (byte)MessageDeliveryStatus.Sent, ProviderMessageId = providerMessageId,
                AtUtc = nowUtc, UpdatedAtUtc = nowUtc,
            };
            db.MessageEvents.Add(row);
            return row;
        }

        /// <summary>Apply a provider delivery webhook (bounce/complaint/etc.) to the recorded send.
        /// Returns the updated row, or null if the provider id is unknown.</summary>
        public static async Task<MessageEvent> ApplyWebhookAsync(
            MySqlDbContext db, string providerMessageId, MessageDeliveryStatus status, string detail, DateTime nowUtc, CancellationToken ct = default)
        {
            var row = await db.MessageEvents.FirstOrDefaultAsync(e => e.ProviderMessageId == providerMessageId, ct);
            if (row == null) return null;
            row.Status = (byte)status;
            row.Detail = detail;
            row.UpdatedAtUtc = nowUtc;
            return row;
        }
    }
}
