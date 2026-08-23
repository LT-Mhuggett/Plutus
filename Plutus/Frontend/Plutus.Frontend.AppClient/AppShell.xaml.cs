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
            // ⚠⚠ REPORTS REPLACES STATISTICS (step 26, 2026-08-16). The old tab's three viewmodels
            // read LOCAL SQLITE, so it has shown ZERO for everything sold since cutover step 11 —
            // sales stopped being written there. This one reads the platform, which is also the only
            // way a figure can include sales rung up on the OTHER till.
            // ⚠ THE STATISTICS TAB IS GONE — Matt, 2026-08-23: *"Statistics has been replaced by
            // reporting. Remove it"*. It had been commented out of this bar and therefore unreachable;
            // Reports → **Takings** answers the same question from the same source. Its reprint picker
            // went with it (L16) — reprinting lives on the sale-detail dialog, which is where Matt
            // asked for it on 2026-08-21.
            // ⚠ Loyalty landed with step 27 (2026-08-16) — the comment above said it was absent
            // deliberately "until the capability exists", and it now does: members, tiers and store
            // credit are all readable, and a tier is assigned from the Till tab against an attached
            // member. It is a LOOKUP; tiers are still created in the portal only (default 20).
            tabBar.Items.Add(Tab(new Views.MainTill.Loyalty.LoyaltyView()));
            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
            {
                tabBar.Items.Add(Tab(new Views.MainTill.StoreOptions.StoreOptionsView()));
            }
            tabBar.Items.Add(Tab(new Views.MainTill.Settings.SettingsView()));
            // ⚠⚠ THE "PLUTUS" TAB IS GONE (2026-08-18, §5c item 9). Matt: *"most of the MAUI Plutus
            // tab would move into settings"* — and the web till has no such tab either: its
            // equivalents are Settings sections called **Till device** and **Environment**.
            //
            // ⚠ THE SCREEN IS NOT DELETED, only un-tabbed. `SettingsViewModel.OpenTillDeviceCommand`
            // pushes the same `ConnectionView` modally, so the enrolment flow and the five diagnostics
            // are all still there — flattening them into a button list would have lost the thing that
            // makes them useful, which is a failure pointing at ONE layer rather than at "the network".
            //
            // ⚠ The old comment here argued it deserved a tab because it is *"the screen someone opens
            // when the till is NOT working, and it has to be findable without knowing where to look"*.
            // That is a fair instinct and it did not survive contact: this tab bar is only built AFTER
            // sign-in, so a till too broken to sign in never showed it anyway. Enrolment before
            // sign-in has its own route — `ConnectionViewModel(firstRun: true)`.
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
        private static ShellContent Tab(Page page)
        {
            // ⚠⚠ THE APP BAR GOES ON EVERY TAB (2026-08-21). Matt: *"The help needs to be in the top
            // corner of the MAUI till like the web till … It is also missing the users etc."* The web
            // till's app bar is one header above the whole shell; MAUI's equivalent is Shell's
            // `TitleView`, and Shell resolves that **per page** — there is no shell-wide one.
            //
            // ⚠ SO EACH PAGE GETS ITS OWN INSTANCE, and it has to: a `View` cannot have two parents,
            // and sharing one across seven tabs re-parents it on every tab change (it would appear on
            // whichever tab was opened last and nowhere else). `TillAppBar` is built for that — the
            // clock subscribes to ONE static tick rather than each instance owning a timer.
            Shell.SetTitleView(page, new Controls.TillAppBar(page));

            return new ShellContent
            {
                Title = page.Title,
                Icon = page.IconImageSource,
                Content = page,
            };
        }
    }
}
