using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Loyalty;

namespace Plutus.Frontend.AppClient.Views.MainTill.Loyalty
{
    public partial class LoyaltyView : ContentPage
    {
        private readonly LoyaltyViewModel _vm;

        public LoyaltyView()
        {
            InitializeComponent();
            BindingContext = _vm = new LoyaltyViewModel(TableHost, StatusLabel);
        }

        /// <summary>
        /// ⚠ REFRESHED ON APPEARING, not only in the constructor. `AppShell` builds every tab up
        /// front, so a viewmodel constructor runs ONCE — at sign-in — and a list loaded there would
        /// be frozen for the whole session (runbook pitfall 17). Balances change on the Till tab all
        /// day; a stale one looks exactly like a correct one.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _vm.Refresh();
        }
    }
}
