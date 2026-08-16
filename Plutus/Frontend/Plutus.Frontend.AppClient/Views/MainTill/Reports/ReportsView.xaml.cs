using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.ViewModels.MainTill.Reports;

namespace Plutus.Frontend.AppClient.Views.MainTill.Reports
{
    public partial class ReportsView : ContentPage
    {
        private readonly ReportsViewModel _vm;

        public ReportsView()
        {
            InitializeComponent();
            BindingContext = _vm = new ReportsViewModel(
                ReportPicker, FromPicker, ToPicker, TotalsLabel, NoteLabel, TableHost);
        }

        /// <summary>
        /// ⚠ REFRESHED ON APPEARING. `AppShell` builds every tab up front, so a viewmodel constructor
        /// runs ONCE — at sign-in — and a report loaded there would be frozen for the whole session
        /// (runbook pitfall 17). Takings change all day, and a stale figure looks exactly like a
        /// correct one.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _vm.Refresh();
        }
    }
}
