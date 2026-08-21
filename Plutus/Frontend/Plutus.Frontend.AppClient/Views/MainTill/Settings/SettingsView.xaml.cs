using Plutus.Frontend.AppClient.ViewModels.MainTill.Settings;

using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.MainTill.Settings
{
    /// <summary>
    /// ⚠⚠ THE ORIENTATION CODE IS GONE, 2026-08-21, and its absence is the change. This page used to
    /// carry an `OnSizeAllocated` override that re-parented a second column into a second row when the
    /// window went portrait — the only reason it existed was the two-column grid.
    ///
    /// Matt: *"Can the settings screen in MAUI be made to look like the webtill please, so its
    /// consistent."* The web till is one column of collapsible groups. One column has no narrow mode
    /// to fold into, so there is nothing left to swap — **a layout with no modes has no mode to get
    /// wrong**, and this one had two states that were only ever exercised by resizing the window.
    /// </summary>
    public partial class SettingsView : ContentPage
    {
        public SettingsView()
        {
            InitializeComponent();

            BindingContext = new SettingsViewModel(SectionsColumn);
        }
    }
}
