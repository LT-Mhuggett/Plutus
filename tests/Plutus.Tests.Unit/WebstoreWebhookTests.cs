using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Plutus.Entities.Models;
using Plutus.Webstore;
using Xunit;

namespace Plutus.Tests.Unit;

/// <summary>WP6.2 inbound: the webhook HMAC verifier (security boundary) and the end-to-end
/// processing pipeline (verify → parse → map → route) exercised with fake ports and a real
/// scrubbed order body.</summary>
public class WebstoreWebhookTests
{
    private const string Secret = "whsec_kapow_test_123";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly WebstoreConnectionContext Ctx =
        new() { TenantId = Tenant, TillId = Guid.NewGuid(), DeviceId = Guid.NewGuid() };

    private static string Body(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Woo", file));

    private sealed class OkResolver : IWebstoreSkuResolver
    {
        public Guid? Resolve(string sku) => Plutus.SharedKernel.DeterministicGuid.ForName("test:item", sku);
    }
    private sealed class NoneResolver : IWebstoreSkuResolver { public Guid? Resolve(string sku) => null; }

    private sealed class FakeSink : IWebstoreSaleSink
    {
        public readonly List<SaleV2> Submitted = new();
        public bool NextIsDuplicate;
        public Task<bool> SubmitAsync(SaleV2 sale, CancellationToken ct = default)
        { Submitted.Add(sale); return Task.FromResult(!NextIsDuplicate); }
    }
    private sealed class FakeQueue : IWebstoreSkuMapQueue
    {
        public readonly List<(long, IReadOnlyList<string>)> Enqueued = new();
        public Task EnqueueAsync(WebstoreConnectionContext ctx, long wooOrderId, IReadOnlyList<string> skus, CancellationToken ct = default)
        { Enqueued.Add((wooOrderId, skus)); return Task.CompletedTask; }
    }

    // ---- HMAC verifier ----

    [Fact]
    public void Valid_signature_verifies_and_a_tampered_body_does_not()
    {
        var body = Body("order-7127.json");
        var sig = WooWebhookVerifier.Sign(body, Secret);
        Assert.True(WooWebhookVerifier.Verify(body, sig, Secret));
        Assert.False(WooWebhookVerifier.Verify(body + " ", sig, Secret));      // one byte changed
        Assert.False(WooWebhookVerifier.Verify(body, sig, "wrong-secret"));
        Assert.False(WooWebhookVerifier.Verify(body, null, Secret));           // missing header
        Assert.False(WooWebhookVerifier.Verify(body, "not-base64!!", Secret)); // malformed header
    }

    // ---- pipeline routing ----

    [Fact]
    public async Task Signed_order_with_known_skus_is_recorded_via_the_sink()
    {
        var body = Body("order-7127.json");
        var (sink, queue) = (new FakeSink(), new FakeQueue());
        var proc = new WebstoreWebhookProcessor(sink, queue);

        var r = await proc.ProcessOrderWebhookAsync(body, WooWebhookVerifier.Sign(body, Secret), Secret, Ctx, new OkResolver());

        Assert.Equal(WebstoreInboundStatus.Recorded, r.Status);
        var sale = Assert.Single(sink.Submitted);
        Assert.Equal(10316, sale.GrossPence);
        Assert.Empty(queue.Enqueued);
        Assert.Equal(sale.Id, r.SaleId);
    }

    [Fact]
    public async Task Duplicate_delivery_reports_duplicate_not_a_second_sale()
    {
        var body = Body("order-7127.json");
        var sink = new FakeSink { NextIsDuplicate = true };
        var proc = new WebstoreWebhookProcessor(sink, new FakeQueue());
        var r = await proc.ProcessOrderWebhookAsync(body, WooWebhookVerifier.Sign(body, Secret), Secret, Ctx, new OkResolver());
        Assert.Equal(WebstoreInboundStatus.Duplicate, r.Status);
    }

    [Fact]
    public async Task Unknown_skus_are_parked_in_the_review_queue()
    {
        var body = Body("order-7127.json");
        var (sink, queue) = (new FakeSink(), new FakeQueue());
        var proc = new WebstoreWebhookProcessor(sink, queue);

        var r = await proc.ProcessOrderWebhookAsync(body, WooWebhookVerifier.Sign(body, Secret), Secret, Ctx, new NoneResolver());

        Assert.Equal(WebstoreInboundStatus.NeedsMapping, r.Status);
        Assert.Empty(sink.Submitted);
        var (orderId, skus) = Assert.Single(queue.Enqueued);
        Assert.Equal(7127, orderId);
        Assert.NotEmpty(skus);
    }

    [Fact]
    public async Task Forged_signature_is_rejected_before_any_side_effect()
    {
        var body = Body("order-7127.json");
        var (sink, queue) = (new FakeSink(), new FakeQueue());
        var proc = new WebstoreWebhookProcessor(sink, queue);

        var r = await proc.ProcessOrderWebhookAsync(body, "AAAA", Secret, Ctx, new OkResolver());

        Assert.Equal(WebstoreInboundStatus.Rejected, r.Status);
        Assert.Empty(sink.Submitted);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Garbled_body_with_valid_signature_is_rejected()
    {
        var body = "{ not json";
        var (sink, queue) = (new FakeSink(), new FakeQueue());
        var proc = new WebstoreWebhookProcessor(sink, queue);
        var r = await proc.ProcessOrderWebhookAsync(body, WooWebhookVerifier.Sign(body, Secret), Secret, Ctx, new OkResolver());
        Assert.Equal(WebstoreInboundStatus.Rejected, r.Status);
    }
}
