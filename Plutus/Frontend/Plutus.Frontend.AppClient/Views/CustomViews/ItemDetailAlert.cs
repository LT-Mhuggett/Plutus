using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// An item's ADDITIONAL BARCODES and its CHANGE HISTORY — WP10, 2026-08-21.
    ///
    /// ⚠⚠ THE TWO A0 ROWS THIS CLOSES were ⬜ on MAUI while the portal and the web till both had them
    /// since 2026-08-19/20. Six places in `MAUI-retrofit.md` justified that with *"MAUI has no item
    /// editor at all"*, which conflated two code paths: `AddEditView` is dead, but
    /// `ViewAllViewModel`'s tap-menu — Add to basket · Edit item · Adjust stock… · Move to the Bin… —
    /// is alive and always was. **So this is a section on an editor that exists.**
    ///
    /// ⚠⚠ IT IS `CustomerDetailAlert`, DELIBERATELY — same Grid, same three rows, same `TillTable`
    /// history, same "an action closes this dialog and the CALLER opens the next one" rule. An operator
    /// who has used one has used the other, which is the 2026-08-19 look-and-feel ruling.
    ///
    /// ⚠⚠ ONE SCROLLER. A `TillTable` contains its own `ScrollView`, and nesting one inside another is
    /// MAUI's classic trap — the inner list collapses to nothing or scrolls the wrong thing. So this is
    /// a **Grid**: the facts and barcodes sit in an `Auto` row, the history in a `Star` row.
    ///
    /// ⚠⚠ AND EVERY ACTION CLOSES BEFORE THE NEXT DIALOG OPENS. Adding or correcting a barcode needs an
    /// `InputAlert`, and MAUI cannot stack two Mopups pages sensibly — the second lands *behind* the
    /// first, which reads as a till that has frozen. So each button records what was asked for and
    /// closes; the caller prompts, writes, and reopens this. Learned in `CustomerDetailAlert`.
    ///
    /// ⚠ D4: a visible ✕, Escape cancels, and the caller must handle "backed out" — `Closed` is a real
    /// outcome, not an absence.
    /// </summary>
    public class ItemDetailAlert : ContentView
    {
        /// <summary>What the operator asked for. ⚠ `Closed` means they left — a real answer.</summary>
        public enum Kind { Closed = 0, AddBarcode = 1, EditBarcode = 2, RemoveBarcode = 3 }

        private readonly Grid _root = new() { Padding = 10, RowSpacing = 6 };

        /// <summary>Closed — wired to the ✕ AND the Close button, so both exits behave alike (D4).</summary>
        public event EventHandler CloseRequested;

        /// <summary>What was asked for, and which code it was about. ⚠ Read by the helper after the
        /// dialog closes, never mid-flight.</summary>
        public Kind Asked { get; private set; } = Kind.Closed;

        /// <summary>The barcode the action was about — null for <see cref="Kind.AddBarcode"/>.</summary>
        public string AskedCode { get; private set; }

        /// <param name="itemIdOne">⚠⚠ THE ITEM'S OWN CODE, and it is shown FIRST and marked as such.
        /// It is the identity — it seeds the deterministic item GUID and is on every historical sale
        /// line — and it is NOT a removable alias. An operator who cannot tell it apart from the
        /// aliases will try to "correct" it.</param>
        /// <param name="itemName">For the header, so the dialog says which item.</param>
        /// <param name="barcodes">The additional codes, in code order. May be empty.</param>
        /// <param name="history">Its history, or null when it could not be read.</param>
        /// <param name="mayManage">Whether this operator may change barcodes. ⚠ When false the buttons
        /// are ABSENT, not disabled — a control that refuses everybody who can see it is worse than one
        /// that is not there, and the web till renders neither.</param>
        public ItemDetailAlert(
            string itemIdOne,
            string itemName,
            IReadOnlyList<Plutus.Client.Core.PlutusApiClient.ItemBarcodeDto> barcodes,
            Plutus.Client.Core.PlutusApiClient.ItemHistoryPage history,
            bool mayManage)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header + barcodes
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });   // the history
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // close

            _root.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            _root.Add(Top(itemIdOne, itemName, barcodes, mayManage), 0, 0);
            _root.Add(History(history), 0, 1);

            var close = new Button { Text = "Close".Translate(), Margin = new Thickness(0, 6, 0, 0) };
            close.Clicked += (_, e) => CloseRequested?.Invoke(this, e);
            _root.Add(close, 0, 2);

            Content = _root;
        }

        private View Top(
            string itemIdOne,
            string itemName,
            IReadOnlyList<Plutus.Client.Core.PlutusApiClient.ItemBarcodeDto> barcodes,
            bool mayManage)
        {
            var stack = new VerticalStackLayout { Spacing = 3 };

            stack.Children.Add(global::CustomViews.DialogHeader.For(
                string.IsNullOrWhiteSpace(itemName) ? "Item" : itemName,
                (_, e) => CloseRequested?.Invoke(this, e)));

            stack.Children.Add(Heading("Barcodes"));

            // ⚠⚠ THE ITEM'S OWN CODE FIRST, AND LABELLED. It cannot be removed or corrected here — the
            // server refuses that — so saying which one it is prevents the attempt rather than
            // explaining it afterwards.
            stack.Children.Add(Row(itemIdOne, "the item's own code", null, null));

            if (barcodes == null || barcodes.Count == 0)
            {
                stack.Children.Add(Muted("No additional barcodes. This item scans under its own code only."));
            }
            else
            {
                foreach (var b in barcodes.Where(b => !string.IsNullOrWhiteSpace(b.Code)))
                {
                    var code = b.Code;

                    // ⚠⚠ EVERY ROW IS LOCKED — the 2026-08-20 ruling, and the reason is real: a barcode
                    // is the string a scanner matches on, so a stray keystroke in an always-live box is
                    // an item that silently stops scanning. The action opens a prompt; it never edits
                    // in place.
                    stack.Children.Add(Row(
                        code,
                        null,
                        mayManage ? new Action(() => Ask(Kind.EditBarcode, code)) : null,
                        mayManage ? new Action(() => Ask(Kind.RemoveBarcode, code)) : null));
                }
            }

            if (mayManage)
            {
                var add = new Button { Text = "＋ Add another barcode", Margin = new Thickness(0, 6, 0, 0) };
                add.Clicked += (_, _) => Ask(Kind.AddBarcode, null);
                stack.Children.Add(add);
            }

            return stack;
        }

        /// <summary>Record what was asked and get out of the way — see the class remarks.</summary>
        private void Ask(Kind kind, string code)
        {
            Asked = kind;
            AskedCode = code;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One barcode, with its actions. ⚠ `note` marks the item's own code.</summary>
        private static View Row(string code, string note, Action onEdit, Action onRemove)
        {
            var grid = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new Label { VerticalOptions = LayoutOptions.Center, FontSize = 14 };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInk");
            label.Text = note == null ? code : $"{code}   ({note})";
            grid.Add(label, 0, 0);

            if (onEdit != null)
            {
                // ⚠ The padlock is the whole affordance: it says "this will not change under your
                // fingers" before the operator touches anything.
                var edit = new Button { Text = "🔒 Edit", FontSize = 12, Padding = new Thickness(8, 2) };
                edit.Clicked += (_, _) => onEdit();
                grid.Add(edit, 1, 0);
            }

            if (onRemove != null)
            {
                var remove = new Button { Text = "Remove", FontSize = 12, Padding = new Thickness(8, 2) };
                remove.Clicked += (_, _) => onRemove();
                grid.Add(remove, 2, 0);
            }

            return grid;
        }

        /// <summary>
        /// Everything that has happened to this item.
        ///
        /// ⚠⚠ A FAILED READ SAYS SO. Rendering an empty table for a history that could not be fetched
        /// would tell an operator this item has never been touched — and the next thing they do is
        /// change a price believing nobody else has.
        ///
        /// ⚠ THE SERVER'S CAP IS SAID OUT LOUD when it bites: `Total` counts what exists and `Rows` is
        /// what fitted. Same rule as every capped report here.
        /// </summary>
        private static View History(Plutus.Client.Core.PlutusApiClient.ItemHistoryPage history)
        {
            var stack = new VerticalStackLayout { Spacing = 4 };
            stack.Children.Add(Heading("History"));

            if (history is null)
            {
                stack.Children.Add(Muted(
                    "This item's history couldn't be read. You may not have permission to see it, "
                    + "or the till is offline."));
                return stack;
            }

            if (history.Total > history.Rows.Count)
            {
                stack.Children.Add(Muted(
                    $"⚠ Showing the most recent {history.Rows.Count:N0} of {history.Total:N0} entries."));
            }

            var table = new TillTable<Plutus.Client.Core.PlutusApiClient.ItemHistoryRow>(
                Columns(),
                // ⚠ Searchable over what has been fetched; the note above says when that is not
                // everything, so the two together never mislead.
                search: r => $"{r.Type} {r.Detail} {r.By}",
                emptyText: "Nothing has been recorded against this item yet.");

            table.SetRows(history.Rows);
            stack.Children.Add(table);
            return stack;
        }

        private static TableColumn<Plutus.Client.Core.PlutusApiClient.ItemHistoryRow>[] Columns() => new[]
        {
            // ⚠ LOCAL TIME for a person to read, from a UTC instant — and the time as well as the date,
            // because two changes on one day are an ordinary thing to be looking at. ⚠ Sorted on the
            // round-trip UTC string so the order is the instant's, not the rendered text's.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.ItemHistoryRow>(
                "When",
                r => r.AtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture),
                SortText: r => r.AtUtc.ToString("O"),
                Width: 3),

            // The server's own words — "Details changed", "Barcode added", "Stock written off" — so
            // both tills say the same thing about the same event.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.ItemHistoryRow>(
                "What", r => r.Type ?? "", Width: 3),

            // ⚠ THE COLUMN THE WHOLE FEATURE IS FOR: what actually changed, or the reason a count
            // moved. A history that says "Stock adjusted" and not why answers nothing.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.ItemHistoryRow>(
                "Detail", r => r.Detail ?? "", Width: 6),

            // ⚠ A NAME, never a Guid — and never blank. Staff leave, and a trail that renders
            // "(unknown)" for them answers less than one that says who it was.
            new TableColumn<Plutus.Client.Core.PlutusApiClient.ItemHistoryRow>(
                "Who", r => string.IsNullOrWhiteSpace(r.By) ? "—" : r.By, Width: 3),
        };

        private static Label Heading(string text)
        {
            var label = new Label
            {
                Text = text,
                FontAttributes = FontAttributes.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0),
            };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInk");
            return label;
        }

        private static Label Muted(string text)
        {
            var label = new Label { Text = text, FontSize = 12 };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            return label;
        }
    }
}
