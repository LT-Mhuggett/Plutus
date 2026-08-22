using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Controls
{
    /// <summary>
    /// **A row of pill tabs — the web till's `nav.subtabs`.**
    ///
    /// ⚠⚠ IT REPLACES A DROP-DOWN, AND THAT IS THE POINT. Matt, 2026-08-22: *"Instead of the drop
    /// down in reports, can it not be like the webtill? Where they are tabs?"* The web till has always
    /// shown its eight reports as tabs; MAUI hid the same eight behind a `Picker`, so choosing one
    /// took two taps and a list, and **which reports exist at all was invisible until you opened it**.
    /// An operator moving between the two tills mid-shift met a different shape of screen for the same
    /// job — the 2026-08-19 look-and-feel ruling.
    ///
    /// ⚠ AND IT TOOK THE WHITE LIST WITH IT. The `Picker`'s open list is a WinUI popup that cannot
    /// inherit a MAUI style (see `NativePopupTheme`), so the most-used drop-down on the till was also
    /// the worst offender. Tabs are ordinary views and simply follow the scheme.
    ///
    /// ⚠ REAL BUTTONS, NOT TAPPABLE LABELS. A `Grid` with a `TapGestureRecognizer` is not focusable,
    /// and this till is driven with a keyboard and a scanner — the same finding that made
    /// `SettingsSection`'s headers buttons.
    ///
    /// ⚠ SCROLLS HORIZONTALLY. Eight reports do not fit a narrow window, and a tab row that wraps
    /// changes the height of everything under it as the window resizes.
    /// </summary>
    public class SubTabBar : ScrollView
    {
        private readonly HorizontalStackLayout _row = new() { Spacing = 4, Padding = new Thickness(0, 2) };
        private readonly List<Button> _buttons = new();

        /// <summary>Raised with the index of the tab the operator chose.</summary>
        public event EventHandler<int> Selected;

        /// <summary>The chosen tab, or -1 when there are none.</summary>
        public int SelectedIndex { get; private set; } = -1;

        public SubTabBar()
        {
            Orientation = ScrollOrientation.Horizontal;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never;
            Content = _row;
        }

        /// <summary>
        /// Draw the tabs. ⚠ Rebuilt wholesale rather than diffed — the list changes only when the
        /// operator's permissions or the portal's publish list change, which is not a hot path.
        /// </summary>
        public void SetTabs(IEnumerable<string> titles, int select = 0)
        {
            _row.Clear();
            _buttons.Clear();

            var list = (titles ?? Enumerable.Empty<string>()).ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                var b = new Button
                {
                    Text = list[i],
                    FontSize = 13,
                    Padding = new Thickness(14, 4),
                    CornerRadius = 16,
                    BorderWidth = 1,
                    BackgroundColor = Colors.Transparent,
                };
                b.Clicked += (_, _) => Choose(index);
                _row.Add(b);
                _buttons.Add(b);
            }

            // ⚠ NO SELECTION WHEN THERE ARE NO TABS, rather than 0. An operator permitted to read
            // nothing must not have the first report they cannot see selected on their behalf.
            SelectedIndex = list.Count == 0 ? -1 : Math.Clamp(select, 0, list.Count - 1);
            Paint();
        }

        private void Choose(int index)
        {
            if (index == SelectedIndex) return;   // ⚠ Re-running a report on its own tab is churn.
            SelectedIndex = index;
            Paint();
            Selected?.Invoke(this, index);
        }

        /// <summary>
        /// ⚠ THE ACTIVE TAB IS FILLED, THE REST ARE OUTLINED — the web till's `.subtab` / `.subtab
        /// .active` exactly. ⚠ Colours come from the scheme, never literals: a hard-coded accent here
        /// is the unreadable-label fault of 1.74.0 on somebody's pale brand colour.
        /// </summary>
        private void Paint()
        {
            for (var i = 0; i < _buttons.Count; i++)
            {
                var active = i == SelectedIndex;
                var b = _buttons[i];

                if (active)
                {
                    b.SetDynamicResource(Button.BackgroundColorProperty, "ThemeAccent");
                    b.SetDynamicResource(Button.TextColorProperty, "ThemeAccentInk");
                    b.SetDynamicResource(Button.BorderColorProperty, "ThemeAccent");
                }
                else
                {
                    b.BackgroundColor = Colors.Transparent;
                    b.SetDynamicResource(Button.TextColorProperty, "ThemeInk");
                    b.SetDynamicResource(Button.BorderColorProperty, "ThemeLine");
                }
            }
        }
    }
}
