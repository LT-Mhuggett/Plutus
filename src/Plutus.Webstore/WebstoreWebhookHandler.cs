using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Webstore
{
    /// <summary>Woo REST API credentials (consumer key/secret) for one connection.</summary>
    public sealed record WebstoreRestCredentials(string ConsumerKey, string ConsumerSecret);

    /// <summary>Per-connection secrets, resolved from server config keyed by the webstore id —
    /// NEVER from the DB row or the repo (WP6.2a step 2). Host impl reads configuration
    /// (<c>Webstore:Secrets:{id}</c> for the webhook HMAC, <c>Webstore:RestKeys:{id}</c> as
    /// "ck|cs" for the reconciliation poll's REST pull); tests use fakes.</summary>
    public interface IWebstoreSecretProvider
    {
        string? GetWebhookSecret(Guid webStoreId);
        WebstoreRestCredentials? GetRestCredentials(Guid webStoreId);
    }

    /// <summary>What the thin HTTP controller returns: status code + response body.</summary>
    public sealed record WebhookHttpResult(int Status, object Body);

    /// <summary>
    /// WP6.2a — the anonymous-webhook flow, framework-free so it unit-tests offline:
    ///  1. connection lookup by URL id — the ONE deliberate unscoped read (`IgnoreQueryFilters`;
    ///     anonymous request means the ambient claims-derived tenant is wrong); unknown → 404,
    ///     disabled / unentitled → 410 Gone (tells Woo to stop retrying and auto-disable).
    ///  2. Woo's activation ping (form-encoded <c>webhook_id=N</c>, sent UNSIGNED on webhook
    ///     creation) → 200 with zero side effects — creation fails unless the ping gets a 2xx.
    ///  3. HMAC verification of the RAW body before any parsing; bad/missing signature → 401.
    ///  4. everything after runs on the tenant-fixed pipeline (WebstoreWebhookPipelineFactory).
    ///  5. outcome → HTTP chosen for Woo's retry/auto-disable behaviour: anything durably
    ///     recorded or parked is 2xx; quarantines are PARKED into SaleQuarantine idempotently.
    /// </summary>
    public sealed class WebstoreWebhookHandler
    {
        private static readonly Regex PingBody = new(@"^\s*webhook_id=\d+\s*$", RegexOptions.Compiled);

        private readonly MySqlDbContext _db;                 // ambient — used ONLY for the unscoped lookup
        private readonly IEntitlementService _entitlements;
        private readonly IWebstoreSecretProvider _secrets;
        private readonly WebstoreWebhookPipelineFactory _pipelines;

        public WebstoreWebhookHandler(
            MySqlDbContext db, IEntitlementService entitlements,
            IWebstoreSecretProvider secrets, WebstoreWebhookPipelineFactory pipelines)
        {
            _db = db;
            _entitlements = entitlements;
            _secrets = secrets;
            _pipelines = pipelines;
        }

        public async Task<WebhookHttpResult> HandleOrderWebhookAsync(
            Guid webStoreId, string rawBody, string? signatureHeader, CancellationToken ct = default)
        {
            // 1. Connection lookup — unscoped by design; the UUIDv7 id is unguessable and the HMAC
            //    below still gates every payload.
            var row = await _db.WebStores.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == webStoreId, ct);
            if (row is null) return new(404, new { detail = "Unknown webstore." });
            if (!row.Enabled) return new(410, new { detail = "Webstore connection is disabled." });
            if (!await _entitlements.IsEnabledAsync(row.TenantId, Entitlements.WooConnector, ct))
                return new(410, new { detail = "The woo-connector feature is not enabled for this tenant." });

            // 2. Activation ping (Woo sends it unsigned; webhook creation fails without a 2xx).
            if (rawBody != null && PingBody.IsMatch(rawBody))
                return new(200, new { status = "pong" });

            // 3. Secret + HMAC before any parsing.
            var secret = _secrets.GetWebhookSecret(webStoreId);
            if (string.IsNullOrEmpty(secret))
                return new(500, new { detail = "Webhook secret is not configured for this webstore." });
            if (!WooWebhookVerifier.Verify(rawBody ?? string.Empty, signatureHeader, secret))
                return new(401, new { detail = "Invalid webhook signature." });

            // 4. Tenant-fixed pipeline for everything that touches tenant data.
            var ctx = new WebstoreConnectionContext
            {
                WebStoreId = row.Id, TenantId = row.TenantId, TillId = row.TillId, DeviceId = row.DeviceId,
            };
            using var pipeline = _pipelines.Create(ctx);
            var r = await pipeline.Processor.ProcessOrderWebhookAsync(
                rawBody!, signatureHeader, secret, ctx, pipeline.Resolver, ct);

            // 5. Outcome → HTTP. Durably recorded/parked = 2xx (Woo auto-disables webhooks that
            //    keep failing); non-2xx is reserved for cases where a retry or a stop is right.
            switch (r.Status)
            {
                case WebstoreInboundStatus.Recorded:
                    return new(200, new { status = "recorded", saleId = r.SaleId });
                case WebstoreInboundStatus.Duplicate:
                    return new(200, new { status = "duplicate", saleId = r.SaleId });
                case WebstoreInboundStatus.NeedsMapping:
                    return new(202, new { status = "needs-mapping", skus = r.UnmatchedSkus });
                case WebstoreInboundStatus.Quarantined:
                    await WebstoreQuarantine.ParkAsync(pipeline.Db, ctx, r, rawBody!, ct);
                    return new(202, new { status = "quarantined", detail = r.Detail });
                case WebstoreInboundStatus.Skipped:
                    // Not-ingestable status (pending/failed/cancelled/refunded…) — acknowledged,
                    // deliberately no sale. order.updated re-delivers when it becomes paid.
                    return new(200, new { status = "skipped", detail = r.Detail });
                default:
                    return new(400, new { detail = r.Detail });
            }
        }

    }
}
