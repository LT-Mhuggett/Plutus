using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Cash;

namespace Plutus.Frontend.AppClient.Views.MainTill.Cash
{
    public partial class CashView : ContentPage
    {
        private readonly CashViewModel _vm;

        /// <summary>
        /// ⚠ KEPT IN A FIELD so it lives as long as the page does.
        ///
        /// ⚠⚠ This screen is where the staleness fault was FIRST reported and first fixed. Matt,
        /// 2026-08-11: *"The open float was 'Waiting' and never updated. I navigated away and back
        /// onto the cash tab and it had updated."* The drain had worked; only the screen was stale.
        /// *(waiting to send)* is the one label on this till EXPECTED to change on its own, and it
        /// was the one thing that never redrew.
        ///
        /// ⚠ It was fixed here by hand, correctly, and **that is exactly why item 7 happened**: the
        /// fix taught the next screen nothing, so the same complaint came back twice more as a
        /// general one. `LiveScreen` is that hand-rolled pair (`OnAppearing` + `Ticked`, unsubscribed
        /// on the way out, marshalled onto the UI thread) turned into the thing every screen shares.
        /// ⚠ It must not raise the global loading overlay from here — that is a modal push into a
        /// live navigation transition, which is what corrupted the Inventory layout (pitfall 11).
        /// </summary>
        private readonly Services.Sync.LiveScreen _live;

        public CashView()
        {
            InitializeComponent();
            BindingContext = _vm = new CashViewModel(Summary, Actions, History);
            _live = new Services.Sync.LiveScreen(this, _vm.Refresh);
        }
    }
}
