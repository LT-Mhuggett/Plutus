using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.Controls
{
    /// <summary>
    /// The MAUI half of `table-standard.md` — orderable, searchable, page-sized, paginated.
    ///
    /// ⚠⚠ MAUI IS THE FOURTH SURFACE AND HAD NONE OF THIS. The standard was written for three, all
    /// React, all sharing a byte-identical `DataTable.tsx`; this till claimed to follow it while
    /// having no sortable, searchable or paged table anywhere. Matt, 2026-08-16, choosing how the
    /// reporting rebuild should look: **"Tables only."**
    ///
    /// ⚠ BUILT IN CODE, not XAML, like Cash and Statistics — MAUI bindings fail SILENTLY, and a
    /// blank cell on a takings table is money nobody can account for. In code, a typo is a compile
    /// error.
    ///
    /// ⚠ EVERY DECISION LIVES IN <see cref="TableView{T}"/>, which is testable without a UI host.
    /// This class only draws what that says — so what is untestable here is only the drawing.
    /// </summary>
    public sealed class TillTable<T> : Grid
    {
        private IReadOnlyList<TableColumn<T>> _columns;
        private TableView<T> _view;
        private readonly Func<T, string> _search;
        private readonly string _emptyText;

        private readonly Grid _header = new() { ColumnSpacing = 8 };
        private readonly VerticalStackLayout _body = new() { Spacing = 2 };
        private readonly Label _counter = new() { VerticalOptions = LayoutOptions.Center };
        private readonly Button _prev = new() { Text = "‹ Prev" };
        private readonly Button _next = new() { Text = "Next ›" };

        /// <summary>
        /// Tap a row to open it — null for a table that is only there to be read.
        ///
        /// ⚠ OPT-IN, so the four tables that existed before drill-down get no gesture and cannot
        /// change behaviour. See `RowFor`.
        /// </summary>
        private readonly Action<T> _onRowTap;

        /// <param name="onRowTap">Optional. What to do when a row is tapped — see
        /// <see cref="_onRowTap"/>. ⚠ It must decide for itself whether the row it was handed is
        /// actually openable; the table knows nothing about what a row means.</param>
        public TillTable(
            IReadOnlyList<TableColumn<T>> columns,
            Func<T, string> search = null,
            string emptyText = "No rows.",
            Action<T> onRowTap = null)
        {
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            _search = search;
            _view = new TableView<T>(columns, search);
            _emptyText = emptyText;
            _onRowTap = onRowTap;

            RowSpacing = 4;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // search + page size
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });   // rows
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // pager

            this.Add(BuildControls(search != null), 0, 0);
            this.Add(_header, 0, 1);
            // ⚠ A ScrollView with a * row, never a StackLayout — MAUI's StackLayout measures to
            // DESIRED height and does not distribute what is left, so a scrollable list inside one
            // collapses to a few rows. That mistake already shipped on the item list.
            this.Add(new ScrollView { Content = _body }, 0, 2);
            this.Add(BuildPager(), 0, 3);

            BuildHeader();
        }

        public void SetRows(IReadOnlyList<T> rows)
        {
            _view.SetRows(rows);
            Redraw();
        }

        /// <summary>
        /// Replace the columns — for a screen that renders more than one shape of data, like the
        /// generic reports page.
        ///
        /// ⚠⚠ THE SORT AND THE PAGE ARE DELIBERATELY RESET. They refer to a column INDEX, and the
        /// new report's column 3 is a different question from the old one's — carrying them over
        /// would silently order the new report by whatever happened to sit in that position, which
        /// looks like the table sorting itself at random.
        ///
        /// ⚠ Idempotent-ish by design: calling it with the same columns still resets, because this
        /// is called when the DATA changes shape and that is exactly when stale state is wrong.
        /// </summary>
        public void SetColumns(IReadOnlyList<TableColumn<T>> columns)
        {
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            _view = new TableView<T>(_columns, _search);
            BuildHeader();
            Redraw();
        }

        private View BuildControls(bool searchable)
        {
            var bar = new Grid { ColumnSpacing = 6 };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (searchable)
            {
                var box = new SearchBar { Placeholder = "Search…" };

                // ⚠ As you type. The web till filters on every keystroke, and a till that only
                // filtered on Enter would feel broken beside it.
                box.TextChanged += (_, e) => { _view.SetQuery(e.NewTextValue); Redraw(); };
                bar.Add(box, 0, 0);
            }

            // ⚠ 25 / 50 / 100 from the SHARED constant, so the two surfaces agree about what is on
            // page one rather than agreeing by coincidence.
            var sizes = new Picker { Title = "Show" };
            foreach (var size in TableSort.PageSizes) sizes.Items.Add(size.ToString());
            sizes.SelectedIndex = 0;
            sizes.SelectedIndexChanged += (_, _) =>
            {
                if (sizes.SelectedIndex >= 0)
                {
                    _view.SetPageSize(TableSort.PageSizes[sizes.SelectedIndex]);
                    Redraw();
                }
            };
            bar.Add(sizes, 1, 0);

            return bar;
        }

        private View BuildPager()
        {
            var pager = new Grid { ColumnSpacing = 6 };
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _prev.Clicked += (_, _) => { _view.PreviousPage(); Redraw(); };
            _next.Clicked += (_, _) => { _view.NextPage(); Redraw(); };

            pager.Add(_prev, 0, 0);
            pager.Add(_counter, 1, 0);
            pager.Add(_next, 2, 0);
            return pager;
        }

        /// <summary>
        /// The header row — one tappable label per column.
        ///
        /// ⚠ A `Label` with a tap gesture rather than a `Button`: a row of buttons reads as a
        /// toolbar, and the standard's affordance is the ▲/▼/⇅ marker, not a raised control.
        /// </summary>
        private void BuildHeader()
        {
            _header.Clear();
            _header.ColumnDefinitions.Clear();

            for (var i = 0; i < _columns.Count; i++)
            {
                _header.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = _columns[i].Numeric ? GridLength.Auto : GridLength.Star,
                });

                var index = i;
                var label = new Label
                {
                    Text = _columns[i].Label + _view.SortMarkerFor(i),
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = _columns[i].Numeric ? TextAlignment.End : TextAlignment.Start,
                };

                if (_columns[i].Sortable)
                {
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (_, _) => { _view.ToggleSort(index); Redraw(); };
                    label.GestureRecognizers.Add(tap);
                }

                _header.Add(label, i, 0);
            }
        }

        private void Redraw()
        {
            BuildHeader();
            _body.Clear();

            var rows = _view.VisibleRows();

            if (rows.Count == 0)
            {
                // ⚠ SAY SO. An empty table with no words reads as "still loading" or "broken"; the
                // standard's `emptyText` exists because the difference matters to whoever is waiting.
                _body.Add(new Label { Text = _emptyText, Opacity = 0.6 });
            }
            else
            {
                foreach (var row in rows) _body.Add(RowFor(row));
            }

            _counter.Text = _view.RangeLabel();
            _counter.HorizontalTextAlignment = TextAlignment.Center;

            // ⚠ DISABLED, not hidden. A pager whose buttons vanish at the ends makes the row jump
            // about; greying them keeps the layout still and says why they cannot be pressed.
            _prev.IsEnabled = _view.HasPreviousPage;
            _next.IsEnabled = _view.HasNextPage;
        }

        private View RowFor(T row)
        {
            var grid = new Grid { ColumnSpacing = 8 };

            for (var i = 0; i < _columns.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = _columns[i].Numeric ? GridLength.Auto : GridLength.Star,
                });

                grid.Add(new Label
                {
                    Text = _columns[i].Render(row),
                    // ⚠ Numeric cells RIGHT-ALIGN — the standard's `num` class. A column of money
                    // that does not line up on the decimal point cannot be scanned by eye.
                    HorizontalTextAlignment = _columns[i].Numeric ? TextAlignment.End : TextAlignment.Start,
                    VerticalOptions = LayoutOptions.Center,
                }, i, 0);
            }

            // ⚠⚠ TAP TO OPEN, only when a screen asked for it. `_onRowTap` defaults to null, so the
            // four tables written before drill-down existed (Cash, Statistics, Inventory, Loyalty)
            // get no gesture at all and behave exactly as they did — the reason this is opt-in rather
            // than a behaviour every table suddenly gained.
            if (_onRowTap != null)
            {
                var tap = new TapGestureRecognizer();

                // ⚠ THE HANDLER CANNOT THROW. It runs on the UI thread from a gesture, so an escape
                // goes to the dispatcher unhandled — the same shape as every `async void` crash this
                // app has had. A table is not the right place to lose a till from.
                tap.Tapped += (_, _) =>
                {
                    try
                    {
                        _onRowTap(row);
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("TillTable.RowTap", ex);
                    }
                };

                grid.GestureRecognizers.Add(tap);

                // ⚠ A TAP TARGET NEEDS TO BE THE WHOLE ROW, not just the glyph. `Grid` only receives
                // gestures where it has been painted, so a row with a transparent background swallows
                // taps in the gaps between its labels — which reads as "it works sometimes".
                grid.BackgroundColor = Colors.Transparent;
                grid.InputTransparent = false;
            }

            return grid;
        }
    }
}
