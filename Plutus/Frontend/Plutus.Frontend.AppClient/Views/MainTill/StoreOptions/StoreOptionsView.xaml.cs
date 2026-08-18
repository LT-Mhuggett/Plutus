using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.MainTill.StoreOptions
{
    public partial class StoreOptionsView : ContentPage
    {
        private readonly ViewModels.MainTill.StoreOptions.StoreInformationViewModel _vm;

        /// <summary>
        /// ⚠⚠ THIS SCREEN LOADED ONCE, AT SIGN-IN, AND NEVER AGAIN — and it is very likely the
        /// concrete case behind §5c item 7 (*"Nothing updates unless you navigate away and back"*).
        /// `LoadStoreDetails` ran in the constructor, `AppShell` builds every tab up front, and this
        /// page had **no `OnAppearing` at all** — so a detail changed in the portal could not appear
        /// on this till until somebody signed out and back in. That is the same screen whose opening
        /// hours were the subject of the 2026-08-17 report; a till that cannot be shown a corrected
        /// value is indistinguishable from a portal that did not save it.
        ///
        /// ⚠ **`onCadence: true` here, unlike the tables.** Everything on this page is read-only text
        /// in cards — there is no scroll position or selection to lose — so re-reading it every 60
        /// seconds costs nothing and means a portal edit lands on the shop floor within the minute.
        /// </summary>
        private readonly Services.Sync.LiveScreen _live;

        public StoreOptionsView()
        {
            InitializeComponent();

            BindingContext = _vm = new ViewModels.MainTill.StoreOptions.StoreInformationViewModel(Body);
            _live = new Services.Sync.LiveScreen(this, _vm.Refresh);
        }

        // ⚠ `OnSizeAllocated` is GONE (2026-08-17). It moved a `RightColumn` between grid cells to
        // fake a responsive layout; the cards now wrap themselves (`FlexLayout Wrap="Wrap"`), which
        // is what the web till's CSS does and what the platform is for. Reflowing a layout from a
        // size callback also re-entered on every window resize, which is a good way to get a
        // half-applied layout that nobody can reproduce.
    }
}
