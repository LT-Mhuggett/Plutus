using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Controls;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.Reporting;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Reports
{
    /// <summary>
    /// The reports screen — ONE screen for every report in <see cref="ReportCatalogue"/>.
    ///
    /// ⚠⚠ IT KNOWS NOTHING ABOUT ANY PARTICULAR REPORT. The picker is filled from the catalogue, the
    /// columns come from whatever the chosen definition returns, and `TillTable` sorts, searches and
    /// pages it. Adding a report is one catalogue entry — no screen, no viewmodel, no XAML.
    ///
    /// ⚠ NOTHING HERE READS LOCAL SQLITE. The old Statistics viewmodels did, which is why that tab
    /// has shown ZERO for everything sold since cutover step 11 — sales stopped being written there.
    /// This is its replacement (step 26, and [L4]).
    /// </summary>
    public class ReportsViewModel : BaseViewModel
    {
        private readonly Picker _picker;
        private readonly DatePicker _from;
        private readonly DatePicker _to;
        private readonly Label _totals;
        private readonly Label _note;
        private readonly TillTable<ReportRow> _table;
        private readonly ContentView _tableHost;

        private ReportTable _current = ReportTable.Empty(string.Empty);

        public ReportsViewModel(
            Picker picker, DatePicker from, DatePicker to,
            Label totals, Label note, ContentView tableHost)
        {
            _picker = picker;
            _from = from;
            _to = to;
            _totals = totals;
            _note = note;
            _tableHost = tableHost;

            Title = "Reports";
            Icon = "insights";

            foreach (var report in ReportCatalogue.All) _picker.Items.Add(report.Title);
            _picker.SelectedIndex = 0;
            _picker.SelectedIndexChanged += (_, _) => Refresh();

            // ⚠ The last 7 days INCLUDING today. A default of "today" on a reporting screen shows an
            // empty table first thing in the morning, which reads as broken rather than as early.
            var today = SharedKernel.BusinessDay.Today();
            _to.Date = today.ToDateTime(TimeOnly.MinValue);
            _from.Date = today.AddDays(-6).ToDateTime(TimeOnly.MinValue);

            _from.DateSelected += (_, _) => Refresh();
            _to.DateSelected += (_, _) => Refresh();

            // ⚠ The table is built ONCE with placeholder columns and rebuilt per report — see
            // `Rebuild`. A `TillTable` per refresh would drop the operator's sort and page every time
            // they changed a date.
            _table = new TillTable<ReportRow>(
                Array.Empty<TableColumn<ReportRow>>(),
                search: r => r.SearchText,
                emptyText: "Nothing in this range.");

            _tableHost.Content = _table;
        }

        private Command _refreshCommand;
        public Command RefreshCommand => _refreshCommand ??= new Command(Refresh);

        public void Refresh() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            var index = _picker.SelectedIndex;
            if (index < 0 || index >= ReportCatalogue.All.Count) return;

            var report = ReportCatalogue.All[index];

            Say(string.Empty, "Loading…");

            try
            {
                var api = await Services.Storage.TillPlacement.TryCreateApiAsync().ConfigureAwait(false);
                if (api is null)
                {
                    // ⚠ SAY IT, do not show an empty table. "No rows" on a reporting screen is a
                    // statement about the shop's trading, and making it when we simply could not ask
                    // is a confident lie.
                    Show(ReportTable.Empty(
                        "Reports come from Plutus, so the till has to be online. Nothing is shown rather than a wrong figure."));
                    return;
                }

                var storeId = await Services.Storage.TillPlacement.StoreIdAsync().ConfigureAwait(false) ?? 0;

                // ⚠ `DatePicker.Date` is nullable in .NET 10 MAUI; today is the safe fallback and
                // never a silent empty range.
                var from = DateOnly.FromDateTime(_from.Date ?? DateTime.Today);
                var to = DateOnly.FromDateTime(_to.Date ?? DateTime.Today);

                // ⚠ A backwards range asks the server for nothing and gets an empty answer that looks
                // like "no trade". Say which way round it should be instead.
                if (to < from)
                {
                    Show(ReportTable.Empty("The 'to' date is before the 'from' date."));
                    return;
                }

                var table = await report.LoadAsync(api, new ReportQuery(from, to, storeId), default)
                    .ConfigureAwait(false);

                Show(table);
            }
            catch (Exception ex)
            {
                // ⚠ A reporting tab that crashes the till is worse than one that says it has nothing.
                CrashLog.Write("ReportsViewModel.RefreshAsync", ex);
                Show(ReportTable.Empty("That report couldn't be loaded."));
            }
        }

        private void Say(string totals, string note) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _totals.Text = totals;
                _note.Text = note;
            });

        /// <summary>
        /// ⚠ ON THE UI THREAD. The load runs off it, and touching a Layout's children from a pool
        /// thread is the same crash class as A4's dialog — WinUI refuses, from a place no `catch` on
        /// this path would see.
        /// </summary>
        private void Show(ReportTable table)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _current = table;
                _table.SetColumns(ColumnsFor(table));
                _table.SetRows(table.Rows);

                _totals.Text = table.Totals ?? string.Empty;
                _note.Text = table.Note ?? string.Empty;
            });
        }

        /// <summary>
        /// Turn a report's declared headers into table columns.
        ///
        /// ⚠ THE SORT KEY COMES OFF THE CELL, not its text. Rows are erased to strings so one screen
        /// can render any report — and text sorts "£100" before "£9", so a numeric column reads the
        /// number the cell was built from.
        /// </summary>
        private static TableColumn<ReportRow>[] ColumnsFor(ReportTable table)
        {
            var columns = new TableColumn<ReportRow>[table.Headers.Count];

            for (var i = 0; i < table.Headers.Count; i++)
            {
                var index = i;
                var numeric = table.NumericColumns[i];

                columns[i] = new TableColumn<ReportRow>(
                    table.Headers[i],
                    r => r.Cell(index).Text,
                    Numeric: numeric,
                    SortNumber: numeric ? r => r.Cell(index).SortNumber : null);
            }

            return columns;
        }
    }
}
