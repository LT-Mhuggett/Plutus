using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;

namespace Plutus.Frontend.AppClient.Views.CustomViews
{
    /// <summary>
    /// **Pick one of these — the themed replacement for `DisplayActionSheet`.**
    ///
    /// ⚠⚠ MATT, 2026-08-21, with a screenshot of the item tap-menu: *"the pop windows doesnt appear to
    /// follow the theme?"* It does not, and it cannot. `DisplayActionSheet` is drawn by **WinUI**, not
    /// by MAUI: it is a `ContentDialog` in the platform's own light/dark scheme, and it ignores
    /// `Styles.xaml`, `ThemeSurface`, `ThemeInk` and every colour the portal pushes to this till. A
    /// shop on a dark scheme got a white box in the middle of a dark till, thirty-three times over.
    ///
    /// ⚠ `till-design.md` D4 used to say native dialogs were *"out of scope — the platform draws
    /// those"*. That was true and is no longer good enough: the 2026-08-19 ruling widened parity to
    /// **look and feel**, and a dialog that ignores the shop's colours fails that test whoever draws
    /// it. D4 now says to use this.
    ///
    /// ⚠⚠ THE SIGNATURE IS `DisplayActionSheet`'s ON PURPOSE — title, cancel, destruction, buttons —
    /// so a call site changes by one word and nothing else. Thirty-three call sites is too many to
    /// re-think one at a time, and a migration that also redesigns each flow is a migration that stops
    /// half way. `ChoiceHelper.AskAsync` is the front door.
    ///
    /// ⚠ IT RETURNS THE LABEL, exactly as the native one does — including `null` for a back-out, which
    /// every existing caller already handles. ⚠ **`Cancel` comes back as its own label, not as null**,
    /// because that is what `DisplayActionSheet` does and callers test for it by name.
    ///
    /// ⚠ SCROLLS. A native sheet scrolls a long list for free; a `VerticalStackLayout` of forty staff
    /// members would run off the bottom of the screen with no way to reach the end. The list is in a
    /// `ScrollView` with the height cap on the SCROLLER — the `InputAlert` rule, whose header records
    /// what capping the inner stack did to the item editor.
    ///
    /// ⚠ D4: a visible ✕ (`DialogHeader`), Escape cancels, and backing out is a real answer.
    /// </summary>
    public class ChoiceAlert : ContentView
    {
        private readonly VerticalStackLayout _main = new() { Padding = 10, Spacing = 4 };
        private readonly ScrollView _scroller;

        /// <summary>Raised when the operator has answered — including by backing out.</summary>
        public event EventHandler CloseRequested;

        /// <summary>What they picked. ⚠ `null` means they backed out, which is a real answer and not
        /// an absence — D4. The cancel label comes back as itself.</summary>
        public string Picked { get; private set; }

        /// <param name="title">What is being asked. ⚠ May be blank — some callers have no question.</param>
        /// <param name="cancel">The back-out label, or null for none. Rendered apart from the choices.</param>
        /// <param name="destruction">The dangerous one, if there is one — rendered in the danger colour
        /// and placed LAST, away from the others. ⚠ `DisplayActionSheet` puts it first on iOS and last
        /// on Windows; last is right on a till, where the operator's thumb rests near the top.</param>
        /// <param name="choices">The options, in the order given. ⚠ Order is the caller's — several
        /// flows index back into their own list by position.</param>
        public ChoiceAlert(string title, string cancel, string destruction, IReadOnlyList<string> choices)
        {
            // ⚠⚠ A SOLID BACKGROUND AND A CENTRED BOX — without both, a `ContentView` in a Mopup is
            // transparent and full-width, which is the "transparent screen" fault Matt reported on the
            // sale-detail dialog (2026-08-18). ⚠ `SetDynamicResource`, never a fetched colour: a value
            // read once here would be frozen at construction and would not follow a scheme applied
            // afterwards by the portal.
            _main.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            _scroller = new ScrollView
            {
                Content = _main,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            _scroller.SetDynamicResource(VisualElement.BackgroundColorProperty, "ThemeSurface");

            // ⚠ The ✕ is wired to the same exit as Cancel, so both back-outs behave identically (D4).
            _main.Children.Add(global::CustomViews.DialogHeader.For(
                string.IsNullOrWhiteSpace(title) ? " " : title, (_, _) => Answer(null)));

            foreach (var choice in choices ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(choice)) continue;

                var button = new Button { Text = choice, Margin = new Thickness(0, 2, 0, 0) };
                var captured = choice;
                button.Clicked += (_, _) => Answer(captured);
                _main.Children.Add(button);
            }

            if (!string.IsNullOrWhiteSpace(destruction))
            {
                var danger = new Button { Text = destruction, Margin = new Thickness(0, 8, 0, 0) };

                // ⚠ `ThemeDanger` AS THE INK, NOT AS A FILL. It is the only danger colour this app
                // has and there is no paired "ink on danger" key — a red fill under whatever text
                // colour the button style happens to carry is how a control ends up unreadable on one
                // scheme (the 1.74.0 fault). ⚠ It is also NOT a portal slot, deliberately (WP-T1 T1.3):
                // a shop must not be able to restyle "this one is dangerous" into something calm.
                danger.SetDynamicResource(Button.TextColorProperty, "ThemeDanger");

                var capturedDanger = destruction;
                danger.Clicked += (_, _) => Answer(capturedDanger);
                _main.Children.Add(danger);
            }

            if (!string.IsNullOrWhiteSpace(cancel))
            {
                // ⚠ RETURNED AS ITS OWN LABEL, not as null — `DisplayActionSheet` does that and several
                // callers compare against the string. Changing it would break them silently.
                var back = new Button { Text = cancel, Margin = new Thickness(0, 8, 0, 0) };
                var capturedCancel = cancel;
                back.Clicked += (_, _) => Answer(capturedCancel);
                _main.Children.Add(back);
            }

            Content = _scroller;
        }

        private void Answer(string picked)
        {
            Picked = picked;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// ⚠ WIDTH IS A REQUEST, HEIGHT IS A MAXIMUM, AND THE CAP GOES ON THE SCROLLER — copied from
        /// `InputAlert` rather than invented. Capping the inner stack caps the CONTENT, leaving the
        /// scroller nothing to scroll and clipping the list exactly as before; that is how the item
        /// editor lost five of its eight fields and was reported as *"I can ONLY change the tax"*.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            if (width > 0) _main.WidthRequest = Math.Max(320, Math.Min(560, width * 0.6));
            if (height > 0) _scroller.MaximumHeightRequest = height * 0.85;
        }
    }
}
