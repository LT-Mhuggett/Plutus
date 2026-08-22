using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;
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
        private readonly Controls.SubTabBar _tabs;
        private readonly DatePicker _from;
        private readonly DatePicker _to;
        private readonly Label _totals;
        private readonly Label _note;
        private readonly TillTable<ReportRow> _table;
        private readonly ContentView _tableHost;

        private ReportTable _current = ReportTable.Empty(string.Empty);

        /// <summary>The reports this operator may actually read — what the picker lists (5b).
        /// ⚠⚠ The picker's index refers to THIS list, never to `ReportCatalogue.All`.</summary>
        private System.Collections.Generic.List<ReportDefinition> _visible;

        public ReportsViewModel(
            Controls.SubTabBar tabs, DatePicker from, DatePicker to,
            Label totals, Label note, ContentView tableHost)
        {
            _tabs = tabs;
            _from = from;
            _to = to;
            _totals = totals;
            _note = note;
            _tableHost = tableHost;

            Title = "Reports";
            Icon = "insights";

            // ⚠⚠ ONLY THE REPORTS THIS OPERATOR MAY READ — ruling 5b, 2026-08-18: *"Separate permissions
            // need to be created for viewing them."*
            //
            // ⚠ NOT LISTED, rather than listed-and-refused. Gating the endpoints alone would show eight
            // reports and refuse seven of them; worse, a greyed-out row **leaks what other roles can
            // see**. The publish decides the menu, the permission decides the door — and a door nobody
            // may open is not drawn.
            //
            // ⚠⚠ THROUGH `TillGate`, NOT a raw code list. The gate also applies the staleness tier and
            // the permission window, so a supervisor whose roster is a fortnight old, or who is only
            // authorised on Saturdays, is answered correctly. `ReportPermissions.CodesThatOpen` owns
            // WHICH codes open a report; the gate owns whether this person holds one right now.
            // ⚠⚠ AND ONLY THE REPORTS THE PORTAL HAS PUBLISHED TO THIS TILL — ruling 5b(a), 2026-08-19:
            // *"Portal shows which reports a till can show."* Two independent filters, and BOTH must pass.
            // `PublishedReports.Keys` reads a cache, never the network, so drawing this menu cannot block
            // on a server — and when this till has never had an answer it returns the whole catalogue.
            _visible = BuildVisible();

            _tabs.SetTabs(_visible.Select(r => r.Title));

            // ⚠ AN OPERATOR MAY BE ALLOWED NONE. `SelectedIndex = 0` on an empty picker throws, and
            // `RefreshAsync` must not then index into nothing — see its own guard.


            _tabs.Selected += (_, _) => Refresh();

            // ⚠ The last 7 days INCLUDING today. A default of "today" on a reporting screen shows an
            // empty table first thing in the morning, which reads as broken rather than as early.
            var today = SharedKernel.BusinessDay.Today();
            _to.Date = today.ToDateTime(TimeOnly.MinValue);
            _from.Date = today.AddDays(-6).ToDateTime(TimeOnly.MinValue);

            // ⚠ GUARDED. `DatePicker.DateSelected` fires on a PROGRAMMATIC write too, so
            // "Today's sales" — which sets both — would otherwise run the report twice, the first
            // time against a half-applied range (new From, old To). The constructor's own seeding
            // above is safe only because it happens before these handlers exist.
            _from.DateSelected += (_, _) => { if (!_settingRange) Refresh(); };
            _to.DateSelected += (_, _) => { if (!_settingRange) Refresh(); };

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

        /// <summary>
        /// The reports to LIST: published to this till (5b(a)) **and** readable by this operator (5b(b)).
        ///
        /// ⚠⚠ TWO INDEPENDENT FILTERS AND BOTH MUST PASS. They answer different questions — "does this
        /// shop want this report on this till" and "may this person read it" — and neither implies the
        /// other. A report published but unreadable is NOT LISTED, never listed-and-refused: a greyed row
        /// leaks what other roles can see.
        ///
        /// ⚠ Published first because it is a cheap set lookup; the gate check hits the roster.
        /// </summary>
        private System.Collections.Generic.List<ReportDefinition> BuildVisible()
        {
            var published = Services.Reporting.PublishedReports.Keys;

            return ReportCatalogue.All
                .Where(r => published.Contains(r.Key, StringComparer.Ordinal))
                .Where(r => Services.Security.TillGate.CheckAny(
                    App.GetViewModel().SignedInOperator, null,
                    SharedKernel.ReportPermissions.CodesThatOpen(r.Key)).Allowed)
                .ToList();
        }

        /// <summary>
        /// Ask the server what is published and rebuild the picker if it changed.
        ///
        /// ⚠⚠ THIS IS WHY IT IS NOT ONLY DONE IN THE CONSTRUCTOR. `AppShell` builds every tab up front,
        /// so a viewmodel constructor runs ONCE at sign-in (runbook pitfall 17) — an owner turning a
        /// report off in the portal would otherwise not reach the till until the operator signed out and
        /// in again, which is exactly the "nothing updates unless you navigate away and back" complaint
        /// (§5c item 7).
        ///
        /// ⚠ REBUILT ONLY WHEN IT CHANGED. Re-filling the picker resets `SelectedIndex`, so doing it on
        /// every appear would throw the operator back to the first report each time they returned to the
        /// tab. `RefreshAsync` returns false when nothing moved.
        ///
        /// ⚠ The operator's CURRENT report is kept if it survived the change; otherwise the picker falls
        /// back to the first one, because the alternative is a screen showing a report that is no longer
        /// on its own menu.
        /// </summary>
        private async Task ApplyPublicationAsync()
        {
            try
            {
                if (!await Services.Reporting.PublishedReports.RefreshAsync().ConfigureAwait(false)) return;

                var chosen = _tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _visible.Count
                    ? _visible[_tabs.SelectedIndex].Key
                    : null;

                var rebuilt = BuildVisible();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _visible = rebuilt;

                    // ⚠ THE OPERATOR'S CHOSEN REPORT SURVIVES A REPUBLISH where it still exists —
                    // redrawing the row would otherwise throw them back to the first tab mid-read.
                    var keep = chosen is null ? -1 : _visible.FindIndex(r => r.Key == chosen);
                    _tabs.SetTabs(_visible.Select(r => r.Title), keep >= 0 ? keep : 0);
                });
            }
            catch (Exception ex)
            {
                // ⚠ A menu that could not be re-checked keeps the one it has. Never throws: this is on
                // the appearing path, and an escape from here would be an `async void` kill.
                CrashLog.Write("ReportsViewModel.ApplyPublication", ex);
            }
        }

        // ⚠⚠ THERE IS NO "REPRINT A RECEIPT" BUTTON HERE, AND THAT IS DELIBERATE (2026-08-21).
        //
        // One lived here for a few hours. Reprint had become unreachable when the Statistics tab was
        // dropped on 2026-08-16 — `ReceiptReprint.PickAndReprintAsync` was left with one caller, on a
        // screen no operator could open — and a toolbar button restored the capability. Matt: *"Why is
        // there a button there in MAUI and not in the webtill? Reprinting receipts needs to be done
        // from reports and looking at the specific sales in a day."*
        //
        // ⚠ THE FIX WAS A PARITY BREACH IN THE OTHER DIRECTION. The web till has no such button; it
        // reprints from `SaleDetailDialog`, opened off a report row. A toolbar picker of "recent sales"
        // is a SECOND way to find a sale, sitting next to the **Sales** report that already lists them
        // — two finders for one job, only one of which the other till has.
        //
        // ⚠ The capability is not lost: it moved to `SaleDetailAlert`, which the **Sales** report's
        // rows already open (`drill:` in `ReportCatalogue`). `PickAndReprintAsync` and its two pickers
        // went with the button — see `ReceiptReprint`.

        private Command _refreshCommand;
        public Command RefreshCommand => _refreshCommand ??= new Command(Refresh);

        /// <summary>Set while both date pickers are being written at once — see the guarded
        /// `DateSelected` handlers. Not thread-shared: every write is on the UI thread.</summary>
        private bool _settingRange;

        Command _todaysSalesCommand;
        /// <summary>
        /// Both pickers to today, and run the report — Matt, 2026-08-22.
        ///
        /// ⚠ `BusinessDay.Today()`, never `DateTime.Today`: the business day is the shop's, and it is
        /// the same primitive the sale itself was stamped with, so the filter and the data agree.
        /// The web surfaces call `businessToday()` in `apiTime.ts` for the same reason.
        /// </summary>
        public Command TodaysSalesCommand => _todaysSalesCommand ??= new Command(() =>
        {
            var today = SharedKernel.BusinessDay.Today().ToDateTime(TimeOnly.MinValue);

            _settingRange = true;
            try
            {
                // ⚠ `From` first. A DatePicker can carry a MinimumDate/MaximumDate pinned to its
                // sibling, and writing To below the old From would be rejected outright.
                _from.Date = today;
                _to.Date = today;
            }
            finally { _settingRange = false; }

            Refresh();
        });

        public void Refresh() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            // ⚠ THE MENU BEFORE THE DATA. If the portal has withdrawn a report, the operator must not
            // watch it load and then vanish — and if it withdrew the one they were looking at, the
            // selection has to move before anything is fetched for it.
            await ApplyPublicationAsync();

            // ⚠⚠ INDEXED INTO `_visible`, NEVER INTO `ReportCatalogue.All`. The tab row lists only the
            // reports this operator may read (5b), so position 1 in the row is not position 1 in the
            // catalogue — reading from `All` here would run whatever report happened to sit at that
            // index, which for a narrow role means **running a report they are not allowed**.
            var index = _tabs.SelectedIndex;
            if (index < 0 || index >= _visible.Count)
            {
                // ⚠ An operator allowed no reports at all lands here. Say so, rather than leaving an
                // empty screen that reads as "loading" for ever.
                if (_visible.Count == 0)
                    Say(string.Empty, "You don't have permission to view any reports on this till.");
                return;
            }

            var report = _visible[index];

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
