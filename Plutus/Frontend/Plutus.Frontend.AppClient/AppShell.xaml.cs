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

            // ⚠ THE ORDER AND THE NAMES MATCH THE WEB TILL (Matt, 2026-08-11: *"Can the tabs be
            // renamed to match the webtill please."*). Its `App.tsx` reads:
            //
            //     Till · Cash · Inventory Management · Reporting · Loyalty · Store Information · Settings
            //
            // ⚠ Two tills whose tabs read differently are two products to be trained on, and the
            // same person uses both. The renames also fix "Inventory Managment", misspelt in the
            // tab bar since it was written, and retitle Statistics to **Reporting** — which is what
            // the screen will actually be once cutover step 26 lands.
            //
            // ⚠ **Loyalty is absent, deliberately** — cutover step 27. An empty tab is worse than a
            // missing one: it promises a capability and then explains that it does not exist.
            // ⚠ **Plutus is EXTRA**, and stays last. It is enrolment and diagnostics — the screen
            // somebody opens when the till is NOT working — and the web till has no equivalent
            // because a browser till cannot be un-enrolled from itself.
            var tabBar = new TabBar();
            tabBar.Items.Add(Tab(new Views.MainTill.Till.TillView()));
            // WP9 cash (cutover step 23). ⚠ Second, as on the web till: a shop cannot OPEN or CLOSE
            // without it, so it must be reachable without knowing where to look.
            tabBar.Items.Add(Tab(new Views.MainTill.Cash.CashView()));
            tabBar.Items.Add(Tab(new Views.MainTill.Inventory.InventoryView()));
            tabBar.Items.Add(Tab(new Views.MainTill.Statistics.StatisticsView()));
            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
            {
                tabBar.Items.Add(Tab(new Views.MainTill.StoreOptions.StoreOptionsView()));
            }
            tabBar.Items.Add(Tab(new Views.MainTill.Settings.SettingsView()));
            // MAUI retrofit: enrolment + platform diagnostics. Its own tab rather than a section of
            // Settings because it is the screen someone opens when the till is NOT working, and it
            // has to be findable without knowing where to look.
            tabBar.Items.Add(Tab(new Views.Platform.ConnectionView()));
            Items.Add(tabBar);

            App.SetLoading(false);
        }

        /// <summary>
        /// Wrap a page as a tab, CARRYING ITS TITLE AND ICON ACROSS.
        ///
        /// ⚠ Shell renders <c>ShellContent.Title</c> and <c>ShellContent.Icon</c>. It does NOT take
        /// them from the content page, so `new ShellContent { Content = page }` produces a tab bar
        /// of blank, unlabelled, identical buttons — every page still knew its own title (the one
        /// at the top of the screen was correct all along), and none of it reached the tabs.
        ///
        /// Every page here declares <c>Title="{Binding Title}"</c> and
        /// <c>IconImageSource="{Binding Icon, ...}"</c> against a BindingContext set in its XAML,
        /// so both are resolved by the time <c>InitializeComponent</c> has returned and can simply
        /// be copied. Keeping the pages as the single source of the strings also keeps them
        /// translated — the titles come from <c>I18N_L10N</c>, and hard-coding them here would
        /// quietly ship an English-only tab bar.
        /// </summary>
        private static ShellContent Tab(Page page) => new()
        {
            Title = page.Title,
            Icon = page.IconImageSource,
            Content = page,
        };
    }
}
