using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>
    /// WP6.1 one-click onboarding — the no-plugin flow:
    ///  1. CreateConnection: rows (WebStores + virtual Till/TillDetails/Device) + a minted webhook
    ///     secret, returning WooCommerce's own <c>/wc-auth/v1/authorize</c> URL. The shopkeeper
    ///     logs into THEIR WordPress and clicks Approve — auth happens inside WordPress.
    ///  2. HandleCallback: Woo POSTs the generated API keys server-to-server (keys never shown to
    ///     a human); we store them (file store), then auto-create the order webhooks with our
    ///     secret — whose activation pings hit our own (already-live) receiver.
    ///  3. Disconnect: delete the site's webhooks that point at us, disable the connection, kill
    ///     outbound — the WP6.1 DoD's clean revoke (also how a test connection is safely retired).
    /// </summary>
    public static class WebstoreOnboarding
    {
        public sealed record CreateResult(Guid Id, string AuthorizeUrl);

        public static async Task<CreateResult> CreateConnectionAsync(
            MySqlDbContext db, IWebstoreSecretStore secrets, Guid tenantId,
            string name, string siteUrl, int storeId, string publicBaseUrl, string returnUrl,
            CancellationToken ct = default)
        {
            siteUrl = siteUrl.TrimEnd('/');
            if (!siteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The webstore URL must be https:// (wc-auth refuses insecure callbacks).");

            var id = Uuid7.New();
            var tillId = Uuid7.New();
            var deviceId = Uuid7.New();

            db.Till.Add(new Till { Id = tillId, StoreId = storeId, CashFloat = 0, LastOnline = DateTime.UtcNow });
            db.TillDetails.Add(new TillDetails { TillId = tillId, TenantId = tenantId, Name = $"{name} (web)" });
            db.Devices.Add(new Device
            {
                Id = deviceId, TenantId = tenantId, TillId = tillId,
                SecretHash = RandomNumberGenerator.GetBytes(64), SecretSalt = RandomNumberGenerator.GetBytes(32),
                Status = DeviceStatus.Active, CreatedAtUtc = DateTime.UtcNow,
            });
            db.WebStores.Add(new WebStoreDetails
            {
                Id = id, TenantId = tenantId, Name = name, Url = siteUrl, Provider = "woocommerce",
                StoreId = storeId, Enabled = true, TillId = tillId, DeviceId = deviceId,
                OutboundMode = "off", CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);   // unique (TenantId, Name) surfaces here as a conflict

            secrets.SetWebhookSecret(id, "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());

            var callback = Uri.EscapeDataString($"{publicBaseUrl.TrimEnd('/')}/api/v1/webstores/wc-auth/callback");
            var ret = Uri.EscapeDataString(returnUrl);
            var authorizeUrl = $"{siteUrl}/wc-auth/v1/authorize?app_name=Plutus&scope=read_write" +
                               $"&user_id={id:D}&return_url={ret}&callback_url={callback}";
            return new CreateResult(id, authorizeUrl);
        }

        /// <summary>Woo's server-to-server key delivery. One-shot: refuses to overwrite an already
        /// -provisioned connection (a replayed/forged callback can't rotate a live key).</summary>
        public static async Task<(int Status, string Detail)> HandleCallbackAsync(
            MySqlDbContext db, IWebstoreSecretProvider provider, IWebstoreSecretStore store,
            HttpClient http, Guid webStoreId, string consumerKey, string consumerSecret,
            string publicBaseUrl, CancellationToken ct = default)
        {
            var ws = await db.WebStores.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == webStoreId, ct);
            if (ws is null || string.IsNullOrWhiteSpace(ws.Url)) return (404, "unknown webstore.");
            if (provider.GetRestCredentials(webStoreId) is not null)
                return (409, "this webstore is already provisioned.");
            if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret))
                return (400, "consumer key/secret missing.");

            store.SetRestCredentials(webStoreId, consumerKey.Trim(), consumerSecret.Trim());

            var webhookSecret = provider.GetWebhookSecret(webStoreId);
            if (string.IsNullOrEmpty(webhookSecret)) return (500, "webhook secret missing — recreate the connection.");

            var deliveryUrl = $"{publicBaseUrl.TrimEnd('/')}/api/v1/webstores/{webStoreId:D}/webhook";
            var client = new WooRestClient(http, ws.Url!, new WebstoreRestCredentials(consumerKey.Trim(), consumerSecret.Trim()));
            await client.CreateWebhookAsync("Plutus order sync (created)", "order.created", deliveryUrl, webhookSecret, ct);
            await client.CreateWebhookAsync("Plutus order sync (updated)", "order.updated", deliveryUrl, webhookSecret, ct);
            return (200, "provisioned");
        }

        /// <summary>Clean revoke: remove OUR webhooks from the site, then disable the connection
        /// (rows kept for audit; sales already ingested are untouched).</summary>
        public static async Task<(int Status, string Detail)> DisconnectAsync(
            MySqlDbContext db, IWebstoreSecretProvider provider, HttpClient http, Guid webStoreId,
            CancellationToken ct = default)
        {
            var ws = await db.WebStores.FirstOrDefaultAsync(w => w.Id == webStoreId, ct);
            if (ws is null) return (404, "unknown webstore.");

            var removed = 0;
            var creds = provider.GetRestCredentials(webStoreId);
            if (creds is not null && !string.IsNullOrWhiteSpace(ws.Url))
            {
                var client = new WooRestClient(http, ws.Url!, creds);
                try
                {
                    foreach (var (hookId, delivery) in await client.ListWebhooksAsync(ct))
                        if (delivery.Contains(webStoreId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                        {
                            await client.DeleteWebhookAsync(hookId, ct);
                            removed++;
                        }
                }
                catch (HttpRequestException) { /* site unreachable — still disable our side */ }
            }
            ws.Enabled = false;
            ws.OutboundMode = "off";
            await db.SaveChangesAsync(ct);
            return (200, $"disconnected; {removed} webhook(s) removed from the site.");
        }
    }
}
