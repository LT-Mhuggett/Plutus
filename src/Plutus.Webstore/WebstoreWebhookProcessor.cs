using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Entities.Models;

namespace Plutus.Webstore
{
    /// <summary>Idempotently persists a mapped webstore sale — the connector's port over the
    /// platform's sale ingest (implemented in the host against <c>SalesIngestService</c>, so the
    /// connector never references the Sales module). Returns whether the sale was newly recorded
    /// (false = a duplicate delivery that deduped on the deterministic saleId).</summary>
    public interface IWebstoreSaleSink
    {
        Task<bool> SubmitAsync(SaleV2 sale, CancellationToken ct = default);
    }

    /// <summary>Parks an order whose line SKUs aren't in the catalogue into the WP6.2 review queue
    /// (bind SKU→item / ignore / create item). Host-implemented over the WebstoreSkuMap table.</summary>
    public interface IWebstoreSkuMapQueue
    {
        Task EnqueueAsync(WebstoreConnectionContext ctx, long wooOrderId, IReadOnlyList<string> unmatchedSkus, CancellationToken ct = default);
    }

    public enum WebstoreInboundStatus { Recorded, Duplicate, NeedsMapping, Quarantined, Rejected, Skipped }

    public sealed class WebstoreInboundResult
    {
        public WebstoreInboundStatus Status { get; init; }
        public Guid? SaleId { get; init; }
        public string? Detail { get; init; }
        /// <summary>The Woo order id, once the payload parsed — lets the caller derive the
        /// deterministic saleId (e.g. to park a quarantine idempotently). Null for Rejected.</summary>
        public long? WooOrderId { get; init; }
        /// <summary>The parsed order (callers use it for quarantine payloads + the pick-from-floor
        /// notification). Null for Rejected.</summary>
        public WooOrder? Order { get; set; }
        public IReadOnlyList<string> UnmatchedSkus { get; init; } = Array.Empty<string>();

        public static WebstoreInboundResult Recorded(Guid id, long orderId) =>
            new() { Status = WebstoreInboundStatus.Recorded, SaleId = id, WooOrderId = orderId };
        public static WebstoreInboundResult Duplicate(Guid id, long orderId) =>
            new() { Status = WebstoreInboundStatus.Duplicate, SaleId = id, WooOrderId = orderId };
        public static WebstoreInboundResult NeedsMapping(IReadOnlyList<string> skus, long orderId) =>
            new() { Status = WebstoreInboundStatus.NeedsMapping, UnmatchedSkus = skus, WooOrderId = orderId };
        public static WebstoreInboundResult Quarantined(string reason, long orderId) =>
            new() { Status = WebstoreInboundStatus.Quarantined, Detail = reason, WooOrderId = orderId };
        public static WebstoreInboundResult Rejected(string reason) => new() { Status = WebstoreInboundStatus.Rejected, Detail = reason };
        public static WebstoreInboundResult Skipped(string reason, long orderId) =>
            new() { Status = WebstoreInboundStatus.Skipped, Detail = reason, WooOrderId = orderId };
    }

    /// <summary>
    /// WP6.2 inbound pipeline: verify the webhook HMAC → parse the order → map it (WooOrderMapper) →
    /// route the outcome — recorded/duplicate via the sale sink, unknown SKUs to the review queue,
    /// unreconcilable money to quarantine, a forged/garbled delivery rejected. Pure orchestration
    /// over injected ports, so it unit-tests end-to-end with fakes and no HTTP/DB.
    /// </summary>
    public sealed class WebstoreWebhookProcessor
    {
        private readonly IWebstoreSaleSink _sink;
        private readonly IWebstoreSkuMapQueue _queue;

        public WebstoreWebhookProcessor(IWebstoreSaleSink sink, IWebstoreSkuMapQueue queue)
        {
            _sink = sink;
            _queue = queue;
        }

        public async Task<WebstoreInboundResult> ProcessOrderWebhookAsync(
            string rawBody, string? signatureHeader, string secret,
            WebstoreConnectionContext ctx, IWebstoreSkuResolver resolver, CancellationToken ct = default)
        {
            if (!WooWebhookVerifier.Verify(rawBody, signatureHeader, secret))
                return WebstoreInboundResult.Rejected("invalid or missing webhook signature.");

            WooOrder? order;
            try { order = JsonSerializer.Deserialize<WooOrder>(rawBody, WooJson.Options); }
            catch (JsonException ex) { return WebstoreInboundResult.Rejected($"unparseable order payload — {ex.Message}"); }

            return await RouteOrderAsync(order, ctx, resolver, ct);
        }

        /// <summary>The shared routing core — used by the webhook path (above, after HMAC+parse)
        /// AND by the reconciliation poll (which gets orders from the authenticated REST pull, so
        /// no signature step). One path = one set of safeguards.</summary>
        public async Task<WebstoreInboundResult> RouteOrderAsync(
            WooOrder? order, WebstoreConnectionContext ctx, IWebstoreSkuResolver resolver, CancellationToken ct = default)
        {
            if (order is null || order.Id == 0)
                return WebstoreInboundResult.Rejected("order payload missing an id.");

            // Status gate: only PAID orders become sales. order.created fires for pending/unpaid
            // baskets, and failed/cancelled orders must never ingest (this store has hundreds of
            // failed orders). Refunded orders' money is handled via the refund path, not re-ingest.
            var status = (order.Status ?? string.Empty).ToLowerInvariant();
            if (status is not ("processing" or "completed"))
                return WebstoreInboundResult.Skipped($"order status '{order.Status}' is not ingestable.", order.Id);

            var mapped = WooOrderMapper.MapOrder(order, ctx, resolver);

            WebstoreInboundResult result;
            if (mapped.NeedsMapping)
            {
                await _queue.EnqueueAsync(ctx, order.Id, mapped.UnmatchedSkus, ct);
                result = WebstoreInboundResult.NeedsMapping(mapped.UnmatchedSkus, order.Id);
            }
            else if (mapped.IsQuarantined)
            {
                result = WebstoreInboundResult.Quarantined(mapped.QuarantineReason!, order.Id);
            }
            else
            {
                var wasNew = await _sink.SubmitAsync(mapped.Sale!, ct);
                result = wasNew
                    ? WebstoreInboundResult.Recorded(mapped.Sale!.Id, order.Id)
                    : WebstoreInboundResult.Duplicate(mapped.Sale!.Id, order.Id);
            }
            result.Order = order;
            return result;
        }
    }
}
