using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.Entities.Tenancy;
using Plutus.SharedKernel;
using Xunit;

namespace Plutus.Tests.Integration;

/// <summary>
/// WP17.3 messaging seam: a fake adapter round-trips send → delivery webhook → event row, with the
/// tenant's own sending identity and tenant attribution preserved end to end. Proves the seam is
/// usable before any concrete provider exists (the default sender is the null one).
/// </summary>
public class MessagingSeamE2eTests : IClassFixture<PlutusAppFactory>
{
    private readonly PlutusAppFactory _f;
    public MessagingSeamE2eTests(PlutusAppFactory f) => _f = f;

    // A stand-in mail adapter: "accepts" the message and returns a provider id (no I/O).
    private sealed class FakeMessageSender : IMessageSender
    {
        public OutboundMessage Last;
        public Task<MessageSendResult> SendAsync(OutboundMessage message, CancellationToken ct = default)
        {
            Last = message;
            return Task.FromResult(new MessageSendResult(true, "prov-" + message.To, "queued"));
        }
    }

    [Fact]
    public async Task Sender_sends_nothing_until_a_provider_is_configured()
    {
        // The registered sender is the config-driven dispatcher; with no provider selected for a
        // channel it accepts nothing (the seam's "nothing sends until configured" guarantee).
        var sender = _f.Services.GetRequiredService<IMessageSender>();
        var result = await sender.SendAsync(new OutboundMessage(Guid.NewGuid(), MessageChannel.Sms, "07000000000", "x", "s", "b"));
        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task Fake_adapter_round_trips_send_then_bounce_with_tenant_attribution()
    {
        var tenant = Guid.NewGuid();
        var identityFrom = "no-reply@shop.example";

        using var scope = _f.Services.CreateScope();
        await using var db = new MySqlDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MySqlDbContext>>(), new FixedTenantContext(Guid.Empty))
        { CurrentUser = "messaging-e2e" };

        // per-tenant sending identity (model from day one)
        db.TenantSendingIdentities.Add(new TenantSendingIdentity
        {
            Id = Uuid7.New(), TenantId = tenant, Channel = (byte)MessageChannel.Email,
            FromAddress = identityFrom, Domain = "shop.example", Verified = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // send via the fake adapter using the tenant's own from-identity
        var sender = new FakeMessageSender();
        var msg = new OutboundMessage(tenant, MessageChannel.Email, "buyer@example.com", identityFrom, "Receipt", "Thanks!");
        var result = await sender.SendAsync(msg);
        Assert.True(result.Accepted);
        Assert.False(string.IsNullOrEmpty(result.ProviderMessageId));

        // record the send, then apply a bounce webhook keyed on the provider id
        MessageEventStore.RecordSent(db, tenant, MessageChannel.Email, msg.To, msg.From, result.ProviderMessageId!, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var updated = await MessageEventStore.ApplyWebhookAsync(db, result.ProviderMessageId!, MessageDeliveryStatus.Bounced, "mailbox full", DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.NotNull(updated);
        var row = await db.MessageEvents.FirstAsync(e => e.ProviderMessageId == result.ProviderMessageId);
        Assert.Equal((byte)MessageDeliveryStatus.Bounced, row.Status); // webhook advanced the lifecycle
        Assert.Equal(tenant, row.TenantId);                            // tenant attribution preserved
        Assert.Equal(identityFrom, row.FromAddress);                   // sent as the tenant's identity
    }
}
