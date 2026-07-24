using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.Storage;

namespace Plutus.Frontend.AppClient.Services.Analytics
{
    /// <summary>
    /// Non-secret OTLP config, loaded from Resources/Raw/appsettings.json (packaged as a MauiAsset and
    /// read via FileSystem.OpenAppPackageFileAsync - MAUI has no ASP.NET-style appsettings.json
    /// auto-binding, so this is done explicitly). Falls back to the hardcoded defaults below if the
    /// file is missing/unreadable, so a bad or absent config never crashes the app.
    ///
    /// The license key is a real secret and is never checked in - it's read from the
    /// NEW_RELIC_LICENSE_KEY environment variable. If it's absent, telemetry is disabled gracefully
    /// (no exception) rather than crashing a POS/retail app over a missing observability config.
    /// </summary>
    internal static class OtelSettings
    {
        private const string DefaultOtlpEndpoint = "https://otlp.eu01.nr-data.net:4318";
        private const string DefaultServiceName = "Plutus";

        private static readonly Lazy<IConfiguration> ConfigurationHolder = new(LoadConfiguration);

        internal static string OtlpEndpoint => ConfigurationHolder.Value["OpenTelemetry:OtlpEndpoint"] ?? DefaultOtlpEndpoint;

        internal static string ServiceName => ConfigurationHolder.Value["OpenTelemetry:ServiceName"] ?? DefaultServiceName;

        internal static string? LicenseKey => Environment.GetEnvironmentVariable("NEW_RELIC_LICENSE_KEY");

        internal static bool IsEnabled => !string.IsNullOrWhiteSpace(LicenseKey);

        private static IConfiguration LoadConfiguration()
        {
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
                return new ConfigurationBuilder().AddJsonStream(stream).Build();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Plutus] Could not load appsettings.json, falling back to defaults: {ex.Message}");
                return new ConfigurationBuilder().Build();
            }
        }
    }
}
