using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml;
using Plutus.Frontend.AppClient.Platforms.Windows.Services.POS;

namespace Plutus.Frontend.AppClient.Platforms.Windows;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class WindowsApp : MauiWinUIApplication
{
    private static POSManager _posManager;

    /// <summary>
    /// Preserves the original Plutus.Frontend.AppClient.UWP.App.POSManager singleton accessor that
    /// POSCommunicationPlatform calls into.
    /// </summary>
    internal static POSManager POSManager => _posManager ??= new POSManager();

    public WindowsApp()
    {
        this.InitializeComponent();

        // ⚠ WinUI DOES NOT route UI-thread exceptions through AppDomain.CurrentDomain.
        // UnhandledException. The handler installed in App() catches background and un-awaited-task
        // faults; the ones that actually kill this app — a XamlParseException building a page, a
        // binding blowing up, a click handler throwing — arrive HERE and nowhere else.
        //
        // Found the hard way on 2026-08-08: a crash was reported, the log had been added, and the
        // log contained nothing but two clean start-ups. A crash logger that misses UI crashes is
        // worse than none, because it makes the fault look like it did not happen.
        this.UnhandledException += (_, e) =>
        {
            // global:: because MauiWinUIApplication already has a `Services` property (the DI
            // container), which shadows this app's Services namespace here.
            global::Plutus.Frontend.AppClient.Services.Analytics.CrashLog.Write(
                "WinUI.UnhandledException", e.Exception, e.Message);
            // Deliberately NOT marked handled: swallowing it would leave the app alive in an unknown
            // state, which is how a till ends up taking money it cannot account for. Let it die —
            // now that the reason is on disk.
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
