using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Loyalty;

namespace Plutus.Frontend.AppClient.Views.MainTill.Loyalty
{
    public partial class LoyaltyView : ContentPage
    {
        private readonly LoyaltyViewModel _vm;

        /// <summary>
        /// ⚠ REFRESHED ON APPEARING, not only in the constructor. `AppShell` builds every tab up
        /// front, so a viewmodel constructor runs ONCE — at sign-in — and a list loaded there would
        /// be frozen for the whole session (pitfall 17).
        ///
        /// ⚠ **`onCadence: false` — deliberately NOT every 60 seconds.** This is a paged, sortable
        /// table that the operator re-queries on purpose with the Search button, so a tick would
        /// rebuild it and send them back to page one mid-read. It also re-reads on every dialog
        /// dismissal, which is how a newly added member and a just-set tier appear without asking.
        /// </summary>
        private readonly Services.Sync.LiveScreen _live;

        public LoyaltyView()
        {
            InitializeComponent();
            BindingContext = _vm = new LoyaltyViewModel(TableHost, StatusLabel);
            _live = new Services.Sync.LiveScreen(this, _vm.Refresh, onCadence: false);
        }
    }
}
