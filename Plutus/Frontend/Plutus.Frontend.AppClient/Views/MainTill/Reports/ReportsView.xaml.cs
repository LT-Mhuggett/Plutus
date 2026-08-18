using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Reports;

namespace Plutus.Frontend.AppClient.Views.MainTill.Reports
{
    public partial class ReportsView : ContentPage
    {
        private readonly ReportsViewModel _vm;

        /// <summary>
        /// ⚠ REFRESHED ON APPEARING. `AppShell` builds every tab up front, so a viewmodel constructor
        /// runs ONCE — at sign-in — and a report loaded there would be frozen for the whole session
        /// (pitfall 17). Takings change all day, and a stale figure looks exactly like a correct one.
        ///
        /// ⚠ **`onCadence: false`.** A report is a question the operator asked — a date range and a
        /// report type they chose — and re-answering it every 60 seconds would rebuild the table and
        /// lose their place in it. ⚠ It is the *appearing* half that matters here anyway: a report run
        /// before a sale and read after it must not still be the old one.
        /// </summary>
        private readonly Services.Sync.LiveScreen _live;

        public ReportsView()
        {
            InitializeComponent();
            BindingContext = _vm = new ReportsViewModel(
                ReportPicker, FromPicker, ToPicker, TotalsLabel, NoteLabel, TableHost);
            _live = new Services.Sync.LiveScreen(this, _vm.Refresh, onCadence: false);
        }
    }
}
