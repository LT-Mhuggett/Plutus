using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Mopups.Hosting;
using Syncfusion.Maui.Core.Hosting;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.Frontend.AppClient.Services.IOHandeling.Picker;
using Plutus.Frontend.AppClient.Services.Loading;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Plutus.Frontend.AppClient.Services.ThirdPartyTransfer;
using Plutus.Frontend.AppClient.Services.UIHandeling;

#if ANDROID
using Plutus.Frontend.AppClient.Platforms.Android.Implementations.Services;
#elif IOS
using Plutus.Frontend.AppClient.Platforms.iOS.Implementations.Services;
#elif WINDOWS
using Plutus.Frontend.AppClient.Platforms.Windows.Implementations.Services;
#endif

namespace Plutus.Frontend.AppClient;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
#if DEBUG
        // Loads a local, gitignored .env file (e.g. NEW_RELIC_LICENSE_KEY=...) for local dev, so
        // secrets don't need a real machine-wide environment variable set. TraversePath() walks up
        // from the working directory (the build output folder when running unpackaged) looking for
        // .env, so it's found whether it's placed next to the .csproj or higher up in the repo.
        // Values already present in the real environment are never overwritten (DotNetEnv's default).
        // Missing/unreadable .env is not an error - env vars set the normal way still work without one.
        try
        {
            DotNetEnv.Env.Load(options: DotNetEnv.Env.TraversePath());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Plutus] No .env file loaded: {ex.Message}");
        }
#endif

        var builder = MauiApp.CreateBuilder();
        builder.ConfigureSyncfusionCore();
        builder
            .UseMauiApp<App>()
            .ConfigureMopups()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("materialicons.ttf", "MaterialIconsRegular");
            });

        // Same-assembly services that were DependencyService.Register<T>()-ed in App.xaml.cs.
        builder.Services.AddSingleton<IAppState, AppState>();
        builder.Services.AddSingleton<Plutus.Frontend.AppClient.Services.Analytics.ILogger, Logger>();

        // LoadingIndicatorView is just a translucent ContentPage, so one shared modal
        // push/pop implementation replaces what used to be three native-renderer hacks.
        builder.Services.AddSingleton<ILoadingViewService, LoadingViewService>();

        // Platform-specific DependencyService implementations, now registered explicitly per head.
        builder.Services.AddSingleton<IText, TextPlatform>();
        builder.Services.AddSingleton<IFolderPicker, FolderPickerPlatform>();

#if WINDOWS
        // IFile, ICopperTransfer and IPOSCommunication only ever had a Windows (UWP) implementation.
        builder.Services.AddSingleton<IFile, FilePlatform>();
        builder.Services.AddSingleton<ICopperTransfer, CopperTransferPlatform>();
        builder.Services.AddSingleton<IPOSCommunication, POSCommunicationPlatform>();
#endif

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: OtelSettings.ServiceName, serviceVersion: Microsoft.Maui.ApplicationModel.AppInfo.VersionString)
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment",
#if DEBUG
                    "Development"
#else
                    "Production"
#endif
                ),
            });

        TracerProvider tracerProvider = null;

        if (OtelSettings.IsEnabled)
        {
            builder.Logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);
                options.IncludeFormattedMessage = true;
                options.ParseStateValues = true;
                options.AddOtlpExporter(otlp =>
                {
                    // The OTLP HTTP/protobuf exporter does NOT append the signal path itself once
                    // Endpoint is set explicitly - New Relic's docs give the bare host:port, but each
                    // signal needs its own path appended, or every export 404s at the root.
                    otlp.Endpoint = new Uri($"{OtelSettings.OtlpEndpoint}/v1/logs");
                    otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
                    otlp.Headers = $"api-key={OtelSettings.LicenseKey}";
                });
#if DEBUG
                options.AddConsoleExporter();
#endif
            });

            tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(resourceBuilder)
                .AddSource("Plutus")
                .AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri($"{OtelSettings.OtlpEndpoint}/v1/traces");
                    otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
                    otlp.Headers = $"api-key={OtelSettings.LicenseKey}";
                })
#if DEBUG
                .AddConsoleExporter()
#endif
                .Build();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[Plutus] NEW_RELIC_LICENSE_KEY not set - telemetry disabled for this run.");
        }

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        Observability.Bootstrap(app.Services.GetRequiredService<ILoggerFactory>(), tracerProvider);
        return app;
    }
}
