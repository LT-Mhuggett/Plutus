using System;
using System.Collections.Generic;
using System.Linq;
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

// ── Notification provider framework (17.3 configuration layer) ──

/// <summary>One configurable field of a provider (rendered as a form input in the operator
/// dashboard). Secret fields are write-only — never returned on read.</summary>
public sealed record ProviderField(string Name, string Label, bool Secret, bool Required);

/// <summary>A selectable notification provider: its key, display label, channel, and the config
/// fields the operator fills in. This is CONFIG METADATA only — no SDK is referenced here; the
/// concrete adapter is registered separately when credentials exist.</summary>
public sealed record NotificationProviderInfo(string Key, string Label, MessageChannel Channel, IReadOnlyList<ProviderField> Fields);

/// <summary>
/// Code-defined catalogue of the providers the operator can choose from (like the permission /
/// metric catalogues). Adding a provider here makes it selectable + configurable in the dashboard;
/// wiring its concrete adapter (an <see cref="INotificationProvider"/> with the matching Key) is a
/// separate, later step. Until then a selected-but-unwired provider runs in SIMULATED mode.
/// </summary>
public static class NotificationProviderCatalogue
{
    private static ProviderField Text(string n, string l, bool req = true) => new(n, l, false, req);
    private static ProviderField Secret(string n, string l, bool req = true) => new(n, l, true, req);
    private static readonly ProviderField From = Text("fromDefault", "Default from address");

    public static readonly IReadOnlyList<NotificationProviderInfo> All = new List<NotificationProviderInfo>
    {
        new("none", "None (disabled)", MessageChannel.Email, System.Array.Empty<ProviderField>()),
        new("smtp", "SMTP", MessageChannel.Email, new[] { Text("host", "SMTP host"), Text("port", "Port"), Text("username", "Username", false), Secret("password", "Password", false), From }),
        new("postmark", "Postmark", MessageChannel.Email, new[] { Secret("serverToken", "Server API token"), From }),
        new("ses", "Amazon SES", MessageChannel.Email, new[] { Text("region", "AWS region"), Text("accessKeyId", "Access key id"), Secret("secretAccessKey", "Secret access key"), From }),
        new("sendgrid", "SendGrid", MessageChannel.Email, new[] { Secret("apiKey", "API key"), From }),
        new("mailgun", "Mailgun", MessageChannel.Email, new[] { Text("domain", "Sending domain"), Secret("apiKey", "API key"), From }),
        // SMS-ready (email is the first wired channel; these make the framework channel-agnostic).
        new("twilio", "Twilio SMS", MessageChannel.Sms, new[] { Text("accountSid", "Account SID"), Secret("authToken", "Auth token"), Text("fromNumber", "From number") }),
    };

    public static NotificationProviderInfo? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A concrete notification adapter. Implementations are registered under their catalogue
/// <see cref="Key"/>; the dispatcher routes to the one matching the selected provider. None ship in
/// core (the arch test enforces that) — they arrive as adapters when an account/keys exist.
/// </summary>
public interface INotificationProvider
{
    string Key { get; }
    Task<MessageSendResult> SendAsync(OutboundMessage message, IReadOnlyDictionary<string, string> config, CancellationToken ct = default);
}
