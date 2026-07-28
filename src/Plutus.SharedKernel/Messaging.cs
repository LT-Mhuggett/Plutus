using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plutus.SharedKernel;

/// <summary>WP17.3 message channel.</summary>
public enum MessageChannel { Email, Sms }

/// <summary>Delivery lifecycle of a message (a provider webhook advances it).</summary>
public enum MessageDeliveryStatus { Queued, Sent, Bounced, Complained, Failed }

/// <summary>
/// WP17.3 outbound message. Carries the <see cref="TenantId"/> and the tenant's own
/// <see cref="From"/> sending identity FROM DAY ONE — the requirements-doc isolation rule: one
/// tenant's sends must never ride, or poison, a shared domain.
/// </summary>
public sealed record OutboundMessage(Guid TenantId, MessageChannel Channel, string To, string From, string Subject, string Body);

/// <summary>Result of a send attempt. ProviderMessageId is the key a delivery webhook arrives on.</summary>
public sealed record MessageSendResult(bool Accepted, string? ProviderMessageId, string? Detail);

/// <summary>
/// WP17.3 messaging seam (mirrors the billing seam). Core depends ONLY on this; concrete providers
/// (a mailer / SMS gateway) arrive as adapters later. The default <see cref="NullMessageSender"/>
/// accepts nothing, so nothing is sent until a real adapter is chosen — and an arch test keeps any
/// concrete provider out of core.
/// </summary>
public interface IMessageSender
{
    Task<MessageSendResult> SendAsync(OutboundMessage message, CancellationToken ct = default);
}

/// <summary>Default no-op sender: accepts nothing and sends nothing (no mailer configured).</summary>
public sealed class NullMessageSender : IMessageSender
{
    public Task<MessageSendResult> SendAsync(OutboundMessage message, CancellationToken ct = default) =>
        Task.FromResult(new MessageSendResult(false, null, "No message provider configured."));
}
