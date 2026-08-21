using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Controls
{
    /// <summary>
    /// One collapsible group on the Settings screen — **the MAUI twin of the web till's
    /// `SettingsSection`** in `SettingsPage.tsx`.
    ///
    /// ⚠⚠ MATT, 2026-08-21: *"Can the settings screen in MAUI be made to look like the webtill please,
    /// so its consistent."* The web till renders `&lt;details class="settings-section"&gt;` with the title
    /// and a one-line description in the `&lt;summary&gt;`, **closed by default** — its own comment says
    /// *"so the page reads as a table of contents"*. MAUI had a flat, always-open, TWO-COLUMN list of
    /// bare bold headings, so the two screens shared their section names (aligned 2026-08-18) and
    /// nothing else.
    ///
    /// ⚠ .NET MAUI HAS NO `Expander` — it lives in the Community Toolkit, which this project does not
    /// reference. Adding a package for one control would be the expensive way to do this; a header
    /// button that flips one `IsVisible` is the cheap one.
    ///
    /// ⚠⚠ THE HEADER IS A `Button`, NOT A TAPPABLE `Grid`, AND THAT IS DELIBERATE. A `Grid` with a
    /// `TapGestureRecognizer` is not focusable, so on a till with a keyboard the whole screen becomes
    /// unreachable — and the shop PCs this runs on are keyboard-and-scanner machines. The web till gets
    /// this free because `&lt;summary&gt;` is focusable by construction; MAUI does not.
    ///
    /// ⚠ The description sits BELOW the header rather than inside it, because a MAUI `Button` renders
    /// one line of text. It stays visible when the section is closed — which is the point of it: the
    /// closed page has to say what each section is for, or a table of contents is just a list of words.
    ///
    /// ⚠ Colours are theme ROLES, never literals — `ThemeInk`, `ThemeInkMuted`, `ThemeLine`. Hardcoding
    /// grey here is the fault that made Store Information unreadable in 1.74.0 and the section headings
    /// nearly invisible under the stock light scheme.
    /// </summary>
    public sealed class SettingsSection : VerticalStackLayout
    {
        private readonly Button _header;
        private readonly VerticalStackLayout _body;
        private readonly string _title;
        private bool _open;

        /// <param name="title">The section name. ⚠ Use the WEB TILL'S name — that is the whole point.</param>
        /// <param name="description">One line saying what is in here, shown whether open or closed.</param>
        /// <param name="startOpen">⚠ Default FALSE, as on the web till. A page that opens with every
        /// section expanded is the flat list this replaced.</param>
        public SettingsSection(string title, string description, bool startOpen = false)
        {
            _title = title ?? string.Empty;
            _open = startOpen;

            Spacing = 2;
            Margin = new Thickness(0, 6, 0, 0);

            _header = new Button
            {
                // ⚠ A chevron, not a +/−: it says "there is more below" rather than "add something".
                Text = Chevron(),
                HorizontalOptions = LayoutOptions.Fill,
                // ⚠ Left-aligned so the list reads as headings down the page, not as a column of
                // centred buttons — which is what a default MAUI Button looks like and is why the old
                // screen read as "a list of buttons" (§5c item 8).
                ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 0),
                FontAttributes = FontAttributes.Bold,
            };
            _header.Clicked += (_, _) => Toggle();
            Children.Add(_header);

            if (!string.IsNullOrWhiteSpace(description))
            {
                var desc = new Label
                {
                    Text = description,
                    FontSize = 12,
                    Margin = new Thickness(4, 0, 0, 2),
                };
                desc.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
                Children.Add(desc);
            }

            _body = new VerticalStackLayout
            {
                Spacing = 4,
                Margin = new Thickness(8, 2, 0, 6),
                IsVisible = _open,
            };
            Children.Add(_body);

            var rule = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 4, 0, 0) };
            rule.SetDynamicResource(BoxView.ColorProperty, "ThemeLine");
            Children.Add(rule);
        }

        /// <summary>Put a control inside this section. ⚠ Adds to the BODY, not to the section — adding
        /// to `Children` directly would put it below the divider, outside the group it belongs to.</summary>
        public new void Add(IView child)
        {
            if (child is View v) _body.Children.Add(v);
        }

        /// <summary>Is anything in here? ⚠ An empty section is a heading that lies about having
        /// content — the caller checks this rather than rendering one.</summary>
        public bool IsEmpty => _body.Children.Count == 0;

        private void Toggle()
        {
            _open = !_open;
            _body.IsVisible = _open;
            _header.Text = Chevron();
        }

        private string Chevron() => (_open ? "▾  " : "▸  ") + _title;
    }
}
