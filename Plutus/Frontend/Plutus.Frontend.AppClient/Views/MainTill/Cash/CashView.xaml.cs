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
        }
    }
}
