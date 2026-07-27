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
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
