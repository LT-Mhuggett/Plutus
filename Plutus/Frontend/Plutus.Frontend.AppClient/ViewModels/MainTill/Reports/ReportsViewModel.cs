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
                emptyText: "Nothing in this range.",
                // ⚠⚠ DRILL-DOWN (§5c item 5). Only rows that identify a sale do anything — see
                // `ReportRow.DrillSaleId`. Every other report's rows carry no id and a tap is inert,
                // which is why this can be wired once here rather than per report.
                onRowTap: OpenSale);

            _tableHost.Content = _table;
        }

        /// <summary>
        /// Open the sale a row is about — the drill-down (5c item 5).
        ///
        /// ⚠⚠ A TAP ON A ROW THAT IS NOT A SALE DOES NOTHING, SILENTLY. Six of the seven
        /// reports carry no sale id, and a tap on a VAT bucket must not raise a dialog saying "that
        /// isn't a sale" — the operator did not ask a question, they brushed a list.
        ///
        /// ⚠ NOT `async void`. This is called from a gesture, and an escaping exception from an
        /// `async void` goes to the dispatcher unhandled, which on MAUI kills the till — the shape of
        /// every crash Matt reported on 2026-08-18. `TillTable` also catches around the callback; two
        /// guards, because only one of them is a guarantee.
        /// </summary>
        private void OpenSale(ReportRow row)
        {
            if (row?.DrillSaleId is not Guid saleId) return;

            _ = OpenSaleAsync(saleId, row);
        }

        private async Task OpenSaleAsync(Guid saleId, ReportRow row)
        {
            try
            {
                // ⚠ THE OPERATOR'S CLIENT, not the till's. `GET /api/v1/sales/{id}` is `perm:`-gated
                // and RBAC resolves by the token's NameIdentifier - the device id on a device token,
                // which holds no grants. That is the fault that made this whole tab unreadable in
                // 1.75.0, and it would be just as wrong here.
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();

                if (api is null)
                {
                    Say(string.Empty, "A sale can only be opened while the till is online and somebody is signed in.");
                    return;
                }

                var sale = await api.GetSaleAsync(saleId);

                if (sale is null)
                {
                    // ⚠ NULL IS NOT "EMPTY SALE". A 403 or an unreadable answer must not render as a
                    // sale with no lines, which would read as a sale that took no money.
                    Say(string.Empty, "That sale couldn't be read. You may not have permission to see it, or the till is offline.");
                    return;
                }

                // ⚠ The till column from the ROW, because the detail endpoint does not answer one.
                await Helpers.CustomViews.SaleDetailHelper.ShowAsync(sale, row.Cell(1).Text);
            }
            catch (Exception ex)
            {
                CrashLog.Write("ReportsViewModel.OpenSale", ex);
                Say(string.Empty, "That sale couldn't be opened.");
            }
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
                // ⚠⚠ THE **OPERATOR'S** CLIENT, NOT THE TILL'S — and this line was the whole bug
                // (Matt, 2026-08-18: *"When I look at reports in MAUI, it is saying 'This report
                // couldn't be read…'"*). It used to be `TillPlacement.TryCreateApiAsync()`, which
                // authorises as the DEVICE.
                //
                // Every report endpoint is gated `perm:*`, and `PermissionAuthorizationHandler`
                // resolves RBAC by the token's `NameIdentifier`. On a device token that claim is the
                // **device id** (`PlutusTokenAuthHandler`, the `did` branch) — so the `Guid.TryParse`
                // succeeds and the lookup then asks "what may this DEVICE do", which is nothing,
                // because grants hang off users. Result: **403 on every report, for every operator,
                // whatever their role.** Not a permission that needed granting — the wrong identity
                // was asking.
                //
                // ⚠ `OperatorTokenProvider` was built for exactly this in step 19 and had **one**
                // call site (`PlutusApi.GetOperatorAsync`), which nothing on this screen used. That
                // is the fifth component in this codebase found fully built, tested and wired to
                // nothing — after `OutboxPusher.DrainAsync`, the catalogue browse,
                // `TillStore.SearchAsync` and `NoticesClient`.
                //
                // ⚠ Reports are the till speaking for a PERSON, so the device token is not merely
                // insufficient here, it is the wrong thing to send. Ingest, the heartbeat and the
                // catalogue feed keep the device client — they must work overnight with nobody
                // signed in.
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync().ConfigureAwait(false);
                if (api is null)
                {
                    // ⚠ TWO CAUSES, TWO SENTENCES. `GetOperatorAsync` returns null when nobody is
                    // signed in OR the session lapsed — neither of which is "offline", and the old
                    // message said offline for both. Saying the wrong cause sends somebody to check
                    // the network cable over an expired session.
                    Show(ReportTable.Empty(
                        "Reports are read as the signed-in operator, and this session has no live sign-in "
                        + "(nobody signed in, or the session has expired). Sign in again to see them."));
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
