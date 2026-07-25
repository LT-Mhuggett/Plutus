using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.SharedKernel;

namespace Plutus.Tenancy
{
    /// <summary>What a billing event asks the platform to change for a tenant (entitlements /
    /// plan / lifecycle status). The webhook controller applies it in one audited SaveChanges.</summary>
    public sealed record BillingChange(Guid TenantId, string[] Entitlements, string Plan, byte? Status);

    /// <summary>
    /// WP10.1 (architecture §9): the billing provider seam. The FIRST CONCRETE ADAPTER (Stripe
    /// Billing / Paddle / Chargebee) awaits the commercial choice — everything downstream
    /// (entitlement model + enforcement, the webhook receiver, the checkout hand-off) is
    /// provider-agnostic and already runs against this interface. Core has ZERO reference to any
    /// concrete provider (arch test).
    /// </summary>
    public interface IBillingProvider
    {
        string Name { get; }
        /// <summary>Verify a webhook's signature and translate it into a <see cref="BillingChange"/>.
        /// Returns false when the signature is invalid or the event isn't one we act on.</summary>
        bool TryHandleWebhook(string payload, string signature, out BillingChange change);
        /// <summary>A hosted-checkout URL for the tenant to start/change a subscription, or "" when
        /// no provider is configured.</summary>
        string CreateCheckoutUrl(Guid tenantId, string plan);
    }

    /// <summary>
    /// Stands in until the commercial provider is chosen. It is NOT a no-op: it verifies an
    /// HMAC-SHA256 signature over the raw payload with <c>BILLING_WEBHOOK_SECRET</c>, so the whole
    /// entitlement-write path is testable end-to-end without a real provider. A real adapter
    /// replaces the signature scheme + event mapping; the controller and model are unchanged.
    /// </summary>
    public sealed class NullBillingProvider : IBillingProvider
    {
        private readonly string _secret;
        public NullBillingProvider(string secret) => _secret = secret ?? string.Empty;

        public string Name => "none";

        public string CreateCheckoutUrl(Guid tenantId, string plan) => ""; // no hosted checkout yet

        public bool TryHandleWebhook(string payload, string signature, out BillingChange change)
        {
            change = null;
            if (string.IsNullOrEmpty(_secret) || string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signature))
                return false;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
            var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var provided = signature.Trim().ToLowerInvariant();
            if (expected.Length != provided.Length ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(provided)))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (!root.TryGetProperty("tenantId", out var tid) || !Guid.TryParse(tid.GetString(), out var tenantId))
                    return false;
                var ents = root.TryGetProperty("entitlements", out var e) && e.ValueKind == JsonValueKind.Array
                    ? e.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                    : Array.Empty<string>();
                var plan = root.TryGetProperty("plan", out var p) ? p.GetString() : null;
                byte? status = root.TryGetProperty("status", out var s) && s.TryGetByte(out var b) ? b : (byte?)null;
                change = new BillingChange(tenantId, ents, plan, status);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }

    /// <summary>
    /// WP10.1 entitlement resolution: reads the tenant's <c>Entitlements</c> JSON array. Cached
    /// per request (scoped). Platform-admin (Guid.Empty) is always entitled.
    /// </summary>
    public sealed class EntitlementService : IEntitlementService
    {
        private readonly MySqlDbContext _db;
        public EntitlementService(MySqlDbContext db) => _db = db;

        public async Task<bool> IsEnabledAsync(Guid tenantId, string feature, CancellationToken ct = default)
        {
            if (tenantId == Guid.Empty) return true; // platform-admin / unscoped
            if (string.IsNullOrWhiteSpace(feature)) return false;
            var json = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId).Select(t => t.Entitlements).FirstOrDefaultAsync(ct);
            return Parse(json).Contains(feature, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The tenant's entitlement list (empty on null/parse failure).</summary>
        public static string[] Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
            try
            {
                return JsonSerializer.Deserialize<string[]>(json)?
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>();
            }
            catch (JsonException) { return Array.Empty<string>(); }
        }
    }
}
