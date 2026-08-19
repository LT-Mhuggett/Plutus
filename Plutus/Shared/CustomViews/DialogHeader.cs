using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CustomViews
{
    /// <summary>
    /// The title row every dialog gets: the title on the left, a ✕ on the right.
    ///
    /// ⚠⚠ WHY IT EXISTS. Matt, 2026-08-18: *"the box it pops has no X to close the box. I know you
    /// can click outside of the box to close it but its not intuative"*, then *"add x's to all
    /// relevant boxes… so that it is not missed in future"*. Backing out already worked on every
    /// dialog — `AlertDialogBase.OnBackButtonPressed` always cancels and `OnBackgroundClicked`
    /// cancels when interuptable — so this is about **advertising** the exit, not creating one.
    ///
    /// ⚠ ONE HELPER, SO A NEW DIALOG CANNOT QUIETLY SHIP WITHOUT ONE. Every dialog in this project
    /// built its own title `Label` and inserted it at the top of a StackLayout; three copies of the
    /// same six lines, and nothing to notice when a fourth arrived without the ✕. The rule is written
    /// down in `Build/till-design.md` **Part D4**.
    ///
    /// ⚠ THE ✕ IS WIRED TO **CANCEL**, NOT TO THE RAW DISMISS, AND THAT IS A MONEY DECISION. The two
    /// are not equivalent: `InputAlert`'s Cancel blanks the entries and returns a dictionary of nulls,
    /// which every caller's `if (value != null)` guard already handles, while the raw background /
    /// Escape dismiss returns **null** — and 17 call sites dereference that and take the till down
    /// (`MAUI-retrofit.md` §0.3b). Wiring the ✕ to Cancel gives the operator a visible exit **without
    /// widening the reach of a crash that is still open.**
    /// </summary>
    public static class DialogHeader
    {
        /// <summary>⚠ A glyph, not an image: no asset to go missing, and it renders in every font the
        /// till has ever been run with. Same character the web till uses.</summary>
        public const string CloseGlyph = "✕";

        /// <summary>
        /// Build the row. <paramref name="title"/> may be null — the ✕ still appears, because a
        /// dialog with no title is exactly the sort a person cannot work out how to leave.
        /// </summary>
        public static View For(string title, EventHandler onClose)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                ColumnSpacing = 8,
            };

            var label = new Label
            {
                Text = title ?? string.Empty,
                FontSize = new Label().FontSize,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start,
            };
            grid.Add(label);
            Grid.SetColumn(label, 0);

            // ⚠ A Button, not a TapGestureRecognizer on a Label: a 44pt target that a finger can hit
            // on a touch till, and one that announces itself to accessibility as a button.
            var close = new Button
            {
                Text = CloseGlyph,
                // ⚠ Ghost styling on purpose — the ✕ must be findable without competing with the
                // Confirm button for the operator's eye.
                BackgroundColor = Colors.Transparent,
                FontAttributes = FontAttributes.Bold,
                WidthRequest = 44,
                HeightRequest = 44,
                Padding = 0,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
            };

            // ⚠⚠ `ThemeInk`, NOT `Colors.Black` (2026-08-19). Every dialog that carries this header
            // sits on `ThemeSurface`, and under a dark scheme a hardcoded black ✕ measures 1.23:1
            // against it — the close control till-design D4 MANDATES was invisible on the very
            // dialogs it exists for. ⚠ A local value beats the implicit Button style, which is why
            // hardcoding it here defeated the theme silently; the background stays a local
            // Transparent for exactly that reason — the ghost look is deliberate.
            close.SetDynamicResource(Button.TextColorProperty, "ThemeInk");
            // ⚠ Named for a screen reader — the glyph alone reads as nothing useful.
            Microsoft.Maui.Controls.SemanticProperties.SetDescription(close, "Close");
            if (onClose != null) close.Clicked += onClose;
            grid.Add(close);
            Grid.SetColumn(close, 1);

            return grid;
        }
    }
}
