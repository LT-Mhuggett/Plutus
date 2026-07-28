#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plutus.Entities;
using Plutus.Entities.Models;
using Plutus.SharedKernel;

namespace Plutus.Infrastructure.Notifications
{
    /// <summary>
    /// Notification framework (17.3 config layer) dispatcher. Reads the operator-configured
    /// <see cref="NotificationSettings"/> for the message's channel and routes to the registered
    /// concrete <see cref="INotificationProvider"/> whose Key matches. If the channel is disabled or
    /// no provider is selected, it sends nothing (returns not-accepted). If a provider is selected
    /// but no adapter is wired yet, it runs in SIMULATED mode — so the operator can configure and
    /// exercise the full flow (test-send, delivery ledger) before an SDK exists. Every attempt is
    /// recorded in MessageEvents. Best-effort + inert on the SQLite dev host.
    /// </summary>
    public sealed class ConfiguredMessageSender : IMessageSender
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IEnumerable<INotificationProvider> _providers;
        private readonly ILogger<ConfiguredMessageSender> _logger;

        public ConfiguredMessageSender(IServiceScopeFactory scopes, IEnumerable<INotificationProvider> providers, ILogger<ConfiguredMessageSender> logger)
        {
            _scopes = scopes;
            _providers = providers;
            _logger = logger;
        }

        public async Task<MessageSendResult> SendAsync(OutboundMessage message, CancellationToken ct = default)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetService<RepositoryContext>() as MySqlDbContext;
            if (db == null) return new MessageSendResult(false, null, "No database (dev host).");

            var channel = (byte)message.Channel;
            var settings = await db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Channel == channel, ct);
            if (settings == null || !settings.Enabled || string.IsNullOrEmpty(settings.Provider) || settings.Provider == "none")
                return new MessageSendResult(false, null, "Notifications are disabled for this channel.");

            var config = ParseConfig(settings.ConfigJson);
            var provider = _providers.FirstOrDefault(p => string.Equals(p.Key, settings.Provider, StringComparison.OrdinalIgnoreCase));

            MessageSendResult result;
            try
            {
                result = provider != null
                    ? await provider.SendAsync(message, config, ct)
                    : new MessageSendResult(true, "sim-" + Uuid7.New().ToString("N"),
                        $"SIMULATED via '{settings.Provider}' — no adapter wired yet.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification provider '{Provider}' send failed.", settings.Provider);
                result = new MessageSendResult(false, null, $"Provider error: {ex.Message}");
            }

            // Record the attempt in the ledger (tenant-attributed).
            try
            {
                db.CurrentUser = "notification-sender";
                if (result.Accepted && !string.IsNullOrEmpty(result.ProviderMessageId))
                    MessageEventStore.RecordSent(db, message.TenantId, message.Channel, message.To, message.From, result.ProviderMessageId, DateTime.UtcNow);
                else
                    db.MessageEvents.Add(new MessageEvent
                    {
                        Id = Uuid7.New(), TenantId = message.TenantId, Channel = channel,
                        ToAddress = message.To, FromAddress = message.From,
                        Status = (byte)MessageDeliveryStatus.Failed, Detail = result.Detail,
                        AtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                    });
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Recording the message event failed (non-fatal)."); }

            return result;
        }

        private static IReadOnlyDictionary<string, string> ParseConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return d ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }
    }

    public static class NotificationRegistration
    {
        public static IServiceCollection AddPlutusNotifications(this IServiceCollection services)
        {
            // The dispatcher IS the IMessageSender. Concrete INotificationProvider adapters register
            // themselves here when they exist; with none, selected providers run SIMULATED.
            services.AddSingleton<IMessageSender, ConfiguredMessageSender>();
            return services;
        }
    }
}
