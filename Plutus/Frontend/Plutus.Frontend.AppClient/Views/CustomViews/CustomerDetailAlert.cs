using System;
using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Frontend.AppClient.Controls;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// One customer, everything about them — the portal's customer dialog, on a till (WP-L1, §5d).
    ///
    /// ⚠⚠ MATT, 2026-08-18, with a screenshot of the portal: *"I need to be able to see all the
    /// information you see in the portal on both MAUI and the webtill … I also need the 'Credit
    /// History' to be ALL history … This needs to be scroll and searchable as old accounts will have a
    /// LOT of history and needs to be usable."*
    ///
    /// ⚠⚠ **ONE SCROLLER, AND THE LAYOUT IS BUILT AROUND THAT.** A `TillTable` contains its own
    /// `ScrollView`; putting it inside another one is MAUI's classic nesting trap — the inner list
    /// collapses to a few rows or stops scrolling altogether, which is the fault that already shipped
    /// on the item list. So this is a **Grid**: the facts sit in an `Auto` row and the history table in
    /// a `Star` row, and the only thing that scrolls is the table.
    ///
    /// ⚠ THE HISTORY IS A `TillTable`, which is what makes it *"usable"* — it sorts, searches and pages
    /// for free, all of it already tested in `TableView`. Writing a list here by hand would have been a
    /// second, worse table.
    ///
    /// ⚠ BUILT IN CODE, like every other table on this till: MAUI bindings fail SILENTLY, and every
    /// figure here is somebody's money.
    ///
    /// ⚠ READ-ONLY EXCEPT FOR THE TWO BUTTONS. Editing details and granting credit are their own
    /// separately-gated actions, handed in by the caller — this dialog decides nothing about who may
    /// do what.
    /// </summary>
    public class CustomerDetailAlert : ContentView
    {
        private readonly Grid _root = new() { Padding = 10, RowSpacing = 6 };

        /// <summary>Closed — wired to the ✕ AND the Close button, so both exits behave alike (D4).</summary>
        public event EventHandler CloseRequested;

        /// <param name="row">The customer, as the loyalty list already knows them — so the facts show
        /// instantly rather than after a second round trip.</param>
        /// <param name="history">Their history, or null when it could not be read.</param>
        /// <param name="onEdit">Edit details — null when the operator may not (`customers.manage`).</param>
        /// <param name="onGrantCredit">Grant credit — null when the operator may not. ⚠ Supervisor and
        /// above by design: `RbacSeeder` gives a Cashier neither.</param>
        /// <param name="onPrintCard">Print their membership card. ⚠⚠ Deliberately NOT gated on
        /// `customers.manage` — handing a customer their own card is counter work, and a Cashier is
        /// who is standing there.</param>
        public CustomerDetailAlert(
            Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row,
            Plutus.Client.Core.PlutusApiClient.CustomerHistoryPage history,
            Action onEdit,
            Action onGrantCredit,
            Action onPrintCard)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header + facts
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });   // the history
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // close

            _root.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            _root.Add(Facts(row, onEdit, onGrantCredit, onPrintCard), 0, 0);
            _root.Add(History(history), 0, 1);

            var close = new Button { Text = "Close", Margin = new Thickness(0, 6, 0, 0) };
            close.Clicked += (_, e) => CloseRequested?.Invoke(this, e);
            _root.Add(close, 0, 2);

            Content = _root;
        }

        private View Facts(
            Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row,
            Action onEdit, Action onGrantCredit, Action onPrintCard)
        {
            var stack = new VerticalStackLayout { Spacing = 3 };

            stack.Children.Add(global::CustomViews.DialogHeader.For(
                string.IsNullOrWhiteSpace(row.Name) ? "Customer" : row.Name,
                (_, e) => CloseRequested?.Invoke(this, e)));

            // ⚠ THE PORTAL'S OWN FIELDS, in its order — that is the whole point of the row.
            stack.Children.Add(Fact("Member no.", string.IsNullOrWhiteSpace(row.MemberNo) ? "—" : row.MemberNo));
            stack.Children.Add(Fact("Email", string.IsNullOrWhiteSpace(row.Email) ? "—" : row.Email));
            stack.Children.Add(Fact("Phone", string.IsNullOrWhiteSpace(row.Phone) ? "—" : row.Phone));

            stack.Children.Add(Fact("Store credit",
                row.CreditBalancePence == 0
                    ? "—"
                    : (row.CreditBalancePence / 100m).ToString("C2", CultureInfo.CurrentCulture),
                bold: true));

            // ⚠ TIER · RATE · RENEWS on one line, as the portal shows it — three facts about one thing.
            // ⚠ An expired membership SAYS SO: "Gold" beside a lapsed member is an operator promising a
            // discount the till will not give.
            stack.Children.Add(Fact("Membership", MembershipLine(row)));

            var buttons = new HorizontalStackLayout { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };

            // ⚠ ABSENT, NOT DISABLED, when the operator may not. A control that refuses everybody who
            // can see it is worse than one that is not there — and the web till renders neither.
            // ⚠⚠ EACH ACTION RECORDS ITSELF **AND THEN CLOSES**. Edit and Grant credit each raise their
            // own `InputAlert`, and MAUI cannot stack two Mopups pages sensibly — the second lands
            // behind the first, which reads as a till that has frozen. So this dialog gets out of the
            // way and the caller opens the next one.
            if (onEdit != null)
            {
                var edit = new Button { Text = "Edit details" };
                edit.Clicked += (_, e) => { onEdit(); CloseRequested?.Invoke(this, e); };
                buttons.Children.Add(edit);
            }

            if (onGrantCredit != null)
            {
                var grant = new Button { Text = "Grant credit" };
                grant.Clicked += (_, e) => { onGrantCredit(); CloseRequested?.Invoke(this, e); };
                buttons.Children.Add(grant);
            }

            // ⚠⚠ PRINT CARD IS NOT GATED ON `customers.manage`, unlike the two above. Handing somebody
            // their own membership card is counter work — a Cashier is who is standing in front of
            // them, and making them fetch a supervisor to reprint a lost card would be absurd.
            //
            // ⚠ Hidden with no membership number, because there would be nothing to put in the
            // barcode — the button would print a card that scans as nothing.
            if (onPrintCard != null && !string.IsNullOrWhiteSpace(row.MemberNo))
            {
                var print = new Button { Text = "Print card" };
                print.Clicked += (_, e) => { onPrintCard(); CloseRequested?.Invoke(this, e); };
                buttons.Children.Add(print);
            }

            if (buttons.Children.Count > 0) stack.Children.Add(buttons);

            return stack;
        }

        private static string MembershipLine(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row)
        {
            if (string.IsNullOrWhiteSpace(row.Tier)) return "—";

            var line = row.Tier;

            if (row.AutoDiscountRate is decimal rate && rate > 0m)
                line += $" · {Math.Round(rate * 100m)}%";

            if (!string.IsNullOrWhiteSpace(row.RenewalDay))
                line += row.Expired ? $" · EXPIRED {row.RenewalDay}" : $" · renews {row.RenewalDay}";

            return line;
        }

        /// <summary>
        /// The history table.
        ///
        /// ⚠⚠ NULL IS NOT EMPTY. A history that could not be read must say so — rendering "no history"
        /// for a customer who has traded for years is a confident wrong statement, and the operator
        /// would go on to grant credit believing none had ever been given.
        ///
        /// ⚠ THE SERVER'S CAP IS SAID OUT LOUD when it bites, the same rule as every capped report
        /// here: `Total` counts what matched and `Rows` is what fitted.
        /// </summary>
        private static View History(Plutus.Client.Core.PlutusApiClient.CustomerHistoryPage history)
        {
            var stack = new VerticalStackLayout { Spacing = 4 };
            stack.Children.Add(Heading("History"));

            if (history is null)
            {
                stack.Children.Add(Muted(
                    "This customer's history couldn't be read. You may not have permission to see it, or the till is offline."));
                return stack;
            }

            if (history.Total > history.Rows.Count)
                stack.Children.Add(Muted(
                    $"⚠ Showing the most recent {history.Rows.Count:N0} of {history.Total:N0} entries — search to narrow it."));

            var table = new TillTable<Plutus.Client.Core.PlutusApiClient.CustomerHistoryRow>(
                Columns(),
                // ⚠ SEARCHABLE, which Matt asked for by name. It filters what has been fetched; the
                // note above says when that is not everything, so the two together never mislead.
                search: r => $"{r.Type} {r.Detail}",
                emptyText: "Nothing has happened to this customer yet.");

            table.SetRows(history.Rows);
            stack.Children.Add(table);
            return stack;
        }

        private static TableColumn<Plutus.Client.Core.PlutusApiClient.CustomerHistoryRow>[] Columns() => new[]
        {
            // ⚠ LOCAL TIME for a person to read, from a UTC instant — and the time as well as the
            // date, because two changes on one day are an ordinary thing to be looking at.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.CustomerHistoryRow>(
                "When",
                r => r.AtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture),
                SortText: r => r.AtUtc.ToString("O"),
                Width: 3),

            new TableColumn<Plutus.Client.Core.PlutusApiClient.CustomerHistoryRow>(
                "What", r => r.Type ?? "", Width: 3),

            // ⚠ THE REASON, and it is the column the whole feature is for: a manager reading months
            // later needs to know WHY the shop owes this money.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.CustomerHistoryRow>(
                "Detail", r => r.Detail ?? "", Width: 6),

            // ⚠ SIGNED AND ONLY ON MONEY. A rename has no amount, and rendering it as £0.00 would put
            // a transaction on the record that never happened.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.CustomerHistoryRow>(
                "Amount",
                r => r.AmountPence is long p
                    ? (p / 100m).ToString("C2", CultureInfo.CurrentCulture)
                    : "—",
                Numeric: true,
                SortNumber: r => r.AmountPence ?? 0,
                Width: 2),
        };

        /// <summary>⚠ Width is a request, height a MAXIMUM — and the cap goes on the ROOT, which is
        /// the grid, because the table inside it is what scrolls.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0) _root.WidthRequest = Math.Max(480, width * 0.75);
            if (height > 0) _root.MaximumHeightRequest = height * 0.9;
        }

        private static View Fact(string label, string value, bool bold = false)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });

            grid.Add(new Label { Text = label, Opacity = 0.7 }, 0, 0);
            grid.Add(new Label
            {
                Text = value,
                FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            }, 1, 0);

            return grid;
        }

        private static Label Heading(string text) => new()
        {
            Text = text,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 10, 0, 2),
        };

        /// <summary>⚠ Opacity, never a hard-coded grey — Store Information shipped unreadable because
        /// somebody typed a light grey that a dark scheme could not survive (1.74.0).</summary>
        private static Label Muted(string text) => new() { Text = text, Opacity = 0.7 };
    }
}
