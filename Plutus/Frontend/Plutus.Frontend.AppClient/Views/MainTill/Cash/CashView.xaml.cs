using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Cash;

namespace Plutus.Frontend.AppClient.Views.MainTill.Cash
{
    public partial class CashView : ContentPage
    {
        private readonly CashViewModel _vm;

        public CashView()
        {
            InitializeComponent();
            BindingContext = _vm = new CashViewModel(Summary, Actions, History);
        }

        /// <summary>
        /// ⚠ Refreshed on APPEARING, not only in the constructor. The drawer changes because of
        /// sales rung up on the Till tab, so a cash screen built once and never revisited shows
        /// yesterday's story. ⚠ It must not raise the global loading overlay from here — that is a
        /// modal push into a live navigation transition, which is what corrupted the Inventory
        /// layout (repo-runbook pitfall 11).
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _vm.Refresh();

            // ⚠ AND REDRAW ON EVERY TICK WHILE THIS PAGE IS UP. Matt, 2026-08-11: *"The open float
            // was 'Waiting' and never updated. I navigated away and back onto the cash tab and it
            // had updated."* The drain worked; only the screen was stale. `(waiting to send)` is
            // the one label on this till that is EXPECTED to change on its own, and it was the one
            // thing that never redrew.
            Services.Sync.TillCadence.Ticked += OnTicked;
        }

        /// <summary>
        /// ⚠ UNSUBSCRIBE, ALWAYS. `Ticked` is a STATIC event: a page that subscribes and never
        /// detaches is held alive for the life of the process, and every visit to this tab adds
        /// another. Sixty seconds later they all redraw at once.
        /// </summary>
        protected override void OnDisappearing()
        {
            Services.Sync.TillCadence.Ticked -= OnTicked;
            base.OnDisappearing();
        }

        /// <summary>
        /// ⚠ ARRIVES ON THE BACKGROUND LOOP'S THREAD, so it marshals itself — the viewmodel's
        /// refresh writes an ObservableCollection the list is bound to, and doing that off the UI
        /// thread is the kind of fault that works in testing and throws on a shop floor.
        /// ⚠ `Refresh` is fire-and-forget by design and swallows its own failures; a stale cash
        /// screen must never be able to disturb the cadence that feeds it.
        /// </summary>
        private void OnTicked() =>
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => _vm.Refresh());
    }
}
