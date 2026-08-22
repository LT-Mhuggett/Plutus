using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// **The Bin — withdrawn items, and putting one back. WP10 #4, 2026-08-22.**
    ///
    /// ⚠⚠ THIS IS A SCREEN AND NOT A WIRING JOB, AND THE ESTIMATE SAID OTHERWISE. WP10 costed this
    /// at ~½d on the assumption a binned item was locally visible and just needed a button. **It is
    /// not visible at all.** `CatalogueChangesController` sends `Removed: r.BinnedAtUtc != null`, so
    /// a binned item reaches a till as a **tombstone** and the till DELETES it — deliberately, so a
    /// withdrawn product stops scanning even on a till that has been offline since. MAUI's item list
    /// is a capped read of that local SQLite, so there was nothing to restore FROM.
    ///
    /// ⚠ Hence a server-backed list, and hence **online-only**.
    ///
    /// ⚠⚠ AN EMPTY BIN AND AN UNREACHABLE SERVER MUST NOT LOOK THE SAME. One means "nothing has been
    /// withdrawn", the other means "ask again later", and a shop acting on the first when the second
    /// is true will re-create a product that already exists — with a new id, splitting its sales
    /// history. `GetBinnedItemsAsync` answers null rather than empty for exactly this reason, and
    /// this dialog renders the two differently.
    ///
    /// ⚠ `AlertDialogBase` supplies only the scrim; the CONTENT's own background is the dialog. A
    /// `ContentView` defaults to transparent and stretches — the "transparent screen" fault Matt
    /// reported on the sale-detail dialog (2026-08-18). ⚠ `SetDynamicResource`, never a fetched
    /// colour: a value read once here would be frozen at construction.
    ///
    /// ⚠ D4: a visible ✕, Escape cancels, and backing out is a real answer.
    /// </summary>
    public class BinAlert : ContentView
    {
        /// <summary>What the operator asked for. ⚠ `Closed` means they left — a real answer.</summary>
        public enum Kind { Closed = 0, Restore = 1 }

        private readonly Grid _root = new() { Padding = 10, RowSpacing = 6 };

        /// <summary>Closed — wired to the ✕ AND the Close button, so both exits behave alike (D4).</summary>
        public event EventHandler CloseRequested;

        public Kind Asked { get; private set; } = Kind.Closed;

        /// <summary>The item the action was about. ⚠ Null for <see cref="Kind.Closed"/>.</summary>
        public string AskedIdOne { get; private set; }

        /// <param name="items">What the server said is in the Bin, or NULL when it could not be
        /// read. ⚠ The two are rendered differently — see the class remarks.</param>
        /// <param name="mayRestore">Whether this operator holds `inventory.bulk`. ⚠ When false the
        /// buttons are ABSENT, not disabled: a control that refuses everybody who can see it is
        /// worse than one that is not there, and the Bin is still worth READING — knowing a product
        /// was withdrawn answers "why can I not scan this" without being able to undo it.</param>
        public BinAlert(IReadOnlyList<BinnedItemDto> items, bool mayRestore)
        {
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header + note
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });   // the table
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // close

            _root.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            _root.Add(Top(items, mayRestore), 0, 0);
            _root.Add(Table(items, mayRestore), 0, 1);

            var close = new Button { Text = "Close".Translate(), Margin = new Thickness(0, 6, 0, 0) };
            close.Clicked += (_, e) => CloseRequested?.Invoke(this, e);
            _root.Add(close, 0, 2);

            Content = _root;
        }

        private View Top(IReadOnlyList<BinnedItemDto> items, bool mayRestore)
        {
            var stack = new VerticalStackLayout { Spacing = 3 };

            stack.Children.Add(global::CustomViews.DialogHeader.For(
                "The Bin", (_, e) => CloseRequested?.Invoke(this, e)));

            // ⚠⚠ THE THREE STATES ARE SAID IN WORDS, and they are genuinely different answers.
            if (items is null)
            {
                stack.Children.Add(Muted(
                    "The Bin couldn't be read. It lives on Plutus — a withdrawn item is removed from "
                    + "this till's catalogue, so there is no local copy to show. Try again when the "
                    + "till is back online."));
            }
            else if (items.Count == 0)
            {
                stack.Children.Add(Muted("Nothing has been withdrawn from sale."));
            }
            else
            {
                stack.Children.Add(Muted(mayRestore
                    ? "Withdrawn from sale. Putting one back makes it scannable on every till again — "
                      + "it keeps its id, so its sales history stays in one piece."
                    : "Withdrawn from sale. Putting an item back is a manager's action."));
            }

            return stack;
        }

        /// <summary>
        /// ⚠ A `TillTable`, like every other list on this till — it brings search, sort and paging,
        /// and a Bin on a shop that has tidied its catalogue can run to hundreds of rows.
        /// </summary>
        private View Table(IReadOnlyList<BinnedItemDto> items, bool mayRestore)
        {
            var columns = new List<TableColumn<BinnedItemDto>>
            {
                new("Item", r => string.IsNullOrWhiteSpace(r.Name) ? r.IdOne : r.Name),
                new("Brand", r => string.IsNullOrWhiteSpace(r.Brand) || r.Brand == "-" ? "" : r.Brand),

                // ⚠ The price is what tells two similar-looking products apart on a Bin list.
                new("Price", r => r.Price.ToString("C2", CultureInfo.CurrentCulture),
                    Numeric: true, SortNumber: r => (long)Math.Round(r.Price * 100m)),

                // ⚠⚠ WHEN, IN LOCAL TIME, THROUGH `ApiTime` (C2). A bare `DateTime` off this wire is
                // `Kind=Unspecified` and `.ToLocalTime()` on one does NOTHING — the hour-out bug of
                // 2026-08-21, which was found on exactly this kind of column.
                new("Withdrawn", r => r.BinnedAtUtc is DateTime b
                        ? SharedKernel.ApiTime.AsLocal(b).ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
                        : "—",
                    SortText: r => r.BinnedAtUtc?.ToString("O") ?? ""),
            };

            var table = new TillTable<BinnedItemDto>(
                columns,
                search: r => $"{r.IdOne} {r.Name} {r.Brand}",
                emptyText: items is null
                    ? "Couldn't read the Bin."
                    : "Nothing has been withdrawn from sale.",

                // ⚠⚠ THE ROW IS THE ACTION, and it CLOSES the dialog. MAUI cannot stack two Mopups
                // pages — a confirmation raised over this one would land BEHIND it and read as a
                // frozen till. So the row records what was asked and the caller confirms, restores
                // and reopens. Same rule as `ItemDetailAlert`, learned in `CustomerDetailAlert`.
                onRowTap: mayRestore ? Ask : null);

            table.SetRows(items ?? Array.Empty<BinnedItemDto>());
            return table;
        }

        /// <summary>Record what was asked and get out of the way — see the class remarks.</summary>
        private void Ask(BinnedItemDto item)
        {
            if (item is null) return;

            Asked = Kind.Restore;
            AskedIdOne = item.IdOne;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private static Label Muted(string text)
        {
            var label = new Label { Text = text, FontSize = 12 };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            return label;
        }

        /// <summary>
        /// ⚠ WIDTH IS A REQUEST, HEIGHT IS A MAXIMUM — copied from `ItemDetailAlert` rather than
        /// invented. ⚠⚠ AND WITHOUT IT THE STAR ROW RESOLVES TO ZERO: a `TillTable` in a Mopup with
        /// no bounded height renders as nothing at all, which is exactly how the item history
        /// shipped invisible in 1.113.0 and was reported as *"I cannot see an Item history in MAUI"*.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0) _root.WidthRequest = Math.Max(480, width * 0.75);
            if (height > 0) _root.MaximumHeightRequest = height * 0.9;
        }
    }
}
