using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace Plutus.Frontend.AppClient
{
    /// <summary>
    /// Replaces the old Plugin.Iconize IconTabbedPage-based MainView (Plugin.Iconize has no .NET
    /// MAUI package) with a Shell TabBar hosting the same pages. Tab icons/titles keep coming from
    /// each page's own IconImageSource/Title binding - unchanged from before.
    /// </summary>
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            var tabBar = new TabBar();
            tabBar.Items.Add(new ShellContent { Content = new Views.MainTill.Till.TillView() });
            tabBar.Items.Add(new ShellContent { Content = new Views.MainTill.Inventory.InventoryView() });
            tabBar.Items.Add(new ShellContent { Content = new Views.MainTill.Statistics.StatisticsView() });
            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
            {
                tabBar.Items.Add(new ShellContent { Content = new Views.MainTill.StoreOptions.StoreOptionsView() });
            }
            tabBar.Items.Add(new ShellContent { Content = new Views.MainTill.Settings.SettingsView() });
            Items.Add(tabBar);

            App.SetLoading(false);
        }
    }
}
