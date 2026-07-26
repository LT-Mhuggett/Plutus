using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Plutus.Webstore
{
    /// <summary>Write side of secret storage — the wc-auth callback lands keys at runtime, so a
    /// config-only provider isn't enough for self-serve onboarding.</summary>
    public interface IWebstoreSecretStore
    {
        void SetWebhookSecret(Guid webStoreId, string secret);
        void SetRestCredentials(Guid webStoreId, string consumerKey, string consumerSecret);
    }

    /// <summary>
    /// WP6.1 secret storage: CONFIG first (the pm2-env pattern used by the hand-provisioned Kapow
    /// connection), then a server-side JSON file that the wc-auth callback writes
    /// (<c>{ "&lt;id&gt;": { "webhook": "…", "restKey": "ck|cs" } }</c>). The file lives outside
    /// the repo/deploy dir (e.g. ~/PLUTUS/secrets/webstore-secrets.json) and survives deploys.
    /// </summary>
    public sealed class FileWebstoreSecretProvider : IWebstoreSecretProvider, IWebstoreSecretStore
    {
        private sealed class Entry
        {
            public string? Webhook { get; set; }
            public string? RestKey { get; set; }
        }

        private static readonly object Gate = new();
        private readonly IConfiguration _config;
        private readonly string _filePath;

        public FileWebstoreSecretProvider(IConfiguration config, string filePath)
        {
            _config = config;
            _filePath = filePath;
        }

        public string? GetWebhookSecret(Guid webStoreId) =>
            _config[$"Webstore:Secrets:{webStoreId:D}"] ?? Load().GetValueOrDefault(Key(webStoreId))?.Webhook;

        public WebstoreRestCredentials? GetRestCredentials(Guid webStoreId)
        {
            var raw = _config[$"Webstore:RestKeys:{webStoreId:D}"] ?? Load().GetValueOrDefault(Key(webStoreId))?.RestKey;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Split('|');
            return parts.Length == 2 ? new WebstoreRestCredentials(parts[0].Trim(), parts[1].Trim()) : null;
        }

        public void SetWebhookSecret(Guid webStoreId, string secret) =>
            Mutate(webStoreId, e => e.Webhook = secret);

        public void SetRestCredentials(Guid webStoreId, string consumerKey, string consumerSecret) =>
            Mutate(webStoreId, e => e.RestKey = $"{consumerKey}|{consumerSecret}");

        private static string Key(Guid id) => id.ToString("D");

        private Dictionary<string, Entry> Load()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(_filePath)) return new Dictionary<string, Entry>();
                    return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_filePath))
                           ?? new Dictionary<string, Entry>();
                }
                catch (JsonException) { return new Dictionary<string, Entry>(); }
            }
        }

        private void Mutate(Guid id, Action<Entry> apply)
        {
            lock (Gate)
            {
                var all = Load();
                if (!all.TryGetValue(Key(id), out var entry)) all[Key(id)] = entry = new Entry();
                apply(entry);
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
}
