using System;
using System.Threading.Tasks;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Controls
{
    /// <summary>
    /// **The bar across the top of every tab — the web till's app bar, on MAUI.**
    ///
    /// ⚠⚠ MATT, 2026-08-21: *"The help needs to be in the top corner of the MAUI till like the web
    /// till … It is also missing the users etc."* MAUI had **no app bar at all**: Help was a section
    /// at the bottom of Settings, and Users was reachable only from the **login screen**, so a
    /// supervisor who was already signed in had to sign out to add somebody.
    ///
    /// ⚠ Both capabilities EXIST — `SettingsViewModel.HelpAndSupportCommand` and
    /// `Services.People.StaffDirectory`. This is not new function; it is the same function where the
    /// other till puts it, which is the 2026-08-19 look-and-feel ruling. `App.tsx`'s own comment says
    /// *"Help, top-right next to the users button"*.
    ///
    /// ⚠⚠ ONE INSTANCE PER PAGE, because a `View` cannot have two parents — Shell gives each tab its
    /// own `TitleView`. So the clock does **not** own a timer: they all subscribe to one static tick,
    /// and unsubscribe when they leave the tree. Seven pages with seven one-second timers is seven
    /// times the wakeups for one visible clock.
    ///
    /// ⚠ SUBSCRIBED ON `Loaded` AND DROPPED ON `Unloaded`. A static event holding a strong reference
    /// to a dead page is the classic MAUI leak, and on a till that stays open for days it is the kind
    /// that is only ever seen as "it got slow".
    /// </summary>
    public class TillAppBar : Grid
    {
        /// <summary>
        /// ⚠ ONE TIMER FOR THE WHOLE APP. Started on first use and never stopped — it costs a tick a
        /// second and stopping it would mean reference-counting subscribers for no gain.
        /// </summary>
        private static event EventHandler Tick;

        private static bool _started;

        private readonly Label _clock = new()
        {
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        private readonly EventHandler _onTick;

        /// <summary>The ❓, kept as a field because its glyph carries the unread count.</summary>
        private readonly Button _help;

        private readonly Action<int> _onUnread;

        /// <summary>The shop's name for THIS till, once the platform has been asked.</summary>
        private readonly Label _tillName = new()
        {
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0),
            IsVisible = false,
        };

        /// <summary>The door to the portal — hidden until we know this operator may open it.</summary>
        private readonly Button _portal;

        /// <summary>The page this bar belongs to — the only reliable source of its width.</summary>
        private readonly Page _page;

        public TillAppBar(Page page = null)
        {
            _help = IconButton("❓", "Help & support", OnHelp);
            _portal = TextButton("Switch to Portal", "Open the management portal", OnPortal);
            _portal.IsVisible = false;

            // ⚠⚠ FILL, OR THE STAR SPACER GETS NO WIDTH AND EVERYTHING PACKS LEFT — which is exactly
            // how this shipped in 1.117.0. A `Shell.TitleView` is sized to its content unless the view
            // asks for the width, so the spacer collapsed to zero and the clock, ❓ and 👥 sat hard
            // against the left edge while the web till has them on the right. Matt: *"Why does MAUI
            // still look different to the webtill?"*
            HorizontalOptions = LayoutOptions.Fill;
            ColumnSpacing = 0;

            // ⚠⚠ AND AN EXPLICIT WIDTH, BECAUSE `Fill` ALONE DID NOT WORK — 2026-08-23. That was the
            // 1.118.0 fix and Matt reported it again: *"On MAUI, the till name, switch to portal,
            // time all need to be on the far right."* A `Shell.TitleView` is measured with INFINITE
            // available width on WinUI, so a star column has nothing to divide and collapses to zero,
            // and every "right-hand" item packs against the brand. `HorizontalOptions` cannot fix
            // that — there is no constraint for it to fill.
            //
            // ⚠ So the bar is told how wide it is, from the Shell, and re-told whenever the window
            // changes. A till runs windowed, full-screen and on a small terminal; a width measured
            // once at start-up is right until somebody drags the edge.
            // ⚠⚠ THE PAGE, NOT `Shell.Current` — THIRD ATTEMPT, 2026-08-23. `Fill` did nothing
            // (1.118.0) because a TitleView is measured with infinite width, so there was no
            // constraint to fill. `Shell.Current.Width` did nothing either (1.119.0): at the moment a
            // tab is built the Shell has not laid out and reports -1, and its `SizeChanged` never
            // reached a bar that was already parented. The PAGE is handed in by `AppShell.Tab()`, it
            // is the thing that actually resizes with the window, and it is live from the start.
            _page = page;
            if (page is not null)
            {
                page.SizeChanged += (s, _) => SizeTo(s as VisualElement);
                SizeTo(page);
            }

            // ⚠ THE WEB TILL'S ORDER, EXACTLY — `App.tsx`'s `header.appbar`: the brand, then the tabs
            // (the Shell's own, drawn below this), then till name → Switch to Portal → clock → ❓ → 👥
            // hard right. Parity now includes look and feel, so an operator moving between the two
            // mid-shift finds the same things in the same corners.
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Plutus
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });   // spacer
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // till name
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // Switch to Portal
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // clock
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // help
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // users

            // ⚠ THE INK COMES FROM THE SCHEME. This sits on the Shell's navigation bar, whose
            // background the portal can restyle — a hard-coded colour here is the unreadable-label
            // fault of 1.74.0 waiting to happen on somebody's dark scheme.
            _clock.SetDynamicResource(Label.TextColorProperty, "ThemeInk");
            _tillName.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");

            // ⚠ The hover caption names the TIMEZONE, and that is the diagnostic half — `Europe/London`
            // on a till reading an hour out says immediately whether the fault is the PC or the data.
            // That question took a morning on 2026-08-21.
            // ⚠ The caption is set by `Paint`, which knows whether the shop has its own zone.

            this.Add(Brand(), 0, 0);
            this.Add(_tillName, 2, 0);
            this.Add(_portal, 3, 0);
            this.Add(_clock, 4, 0);
            this.Add(_help, 5, 0);
            this.Add(IconButton("👥", "Users", OnUsers), 6, 0);

            _onTick = (_, _) => Paint();
            Paint();

            // ⚠⚠ THE SUPPORT BADGE (WP-TICKETS, 2026-08-21). Matt: *"When I reply to a live ticket,
            // how is the user informed?"* They were not — the reply sat in a thread nobody had a
            // reason to open. The count rides the heartbeat (`TillCadence.UnreadSupportReplies`),
            // which is the only thing on this till that runs with nobody watching the screen.
            //
            // ⚠ IT CLEARS WHEN THE THREAD IS OPENED, not when this button is pressed: the badge is a
            // consequence of the state, and clearing it here would leave the till beside this one
            // still lit.
            _onUnread = n => MainThread.BeginInvokeOnMainThread(() => PaintBadge(n));
            PaintBadge(Services.Sync.TillCadence.UnreadSupportReplies);

            Loaded += (_, _) =>
            {
                Start();
                Tick += _onTick;
                Services.Sync.TillCadence.UnreadSupportChanged += _onUnread;
                Paint();
                PaintBadge(Services.Sync.TillCadence.UnreadSupportReplies);

                // ⚠ FIRE-AND-FORGET, from Loaded rather than the constructor: the bar must draw before
                // anything is asked of the network, and both answers are decoration.
                // ⚠ RE-APPLIED HERE TOO. A page can be sized before this bar is parented, in which
                // case its SizeChanged has already been and gone — so the constructor's one-shot read
                // was the only chance and it may have seen -1.
                SizeTo(_page);

                _ = FillIdentityAsync();
            };

            Unloaded += (_, _) =>
            {
                Tick -= _onTick;
                Services.Sync.TillCadence.UnreadSupportChanged -= _onUnread;
            };
        }

        /// <summary>
        /// Put the unread count on the ❓.
        ///
        /// ⚠ THE GLYPH CARRIES IT, rather than an overlaid pill. MAUI has no cheap absolute-position
        /// overlay inside a Grid cell the way CSS does, and a second column for a badge would move
        /// the 👥 every time support replied — the same jitter the clock's fixed-width digits avoid.
        ///
        /// ⚠ AND THE TOOLTIP SAYS WHAT IT MEANS. A number beside a question mark is not self-
        /// explanatory; "2 unread replies from Plutus support" is.
        /// </summary>
        private void PaintBadge(int unread)
        {
            _help.Text = unread > 0 ? $"❓{unread}" : "❓";

            ToolTipProperties.SetText(_help, unread > 0
                ? $"{unread} unread repl{(unread == 1 ? "y" : "ies")} from Plutus support"
                : "Help & support");
        }

        /// <summary>
        /// Paint the clock.
        ///
        /// ⚠⚠ ON THE SHOP'S CLOCK WHEN THE PORTAL HAS SET ONE (WP-TZ, 2026-08-22), otherwise on this
        /// PC's — which is the behaviour every till had before, and is right for a PC sitting in the
        /// shop it belongs to.
        ///
        /// ⚠⚠ AND IT SHOUTS WHEN THE TWO DISAGREE. `BusinessDay` is this PC's local wall clock, so a
        /// till on the wrong timezone files sales under the wrong trading day — silently, with no
        /// clue in the numbers, and the Z-read then balances against another day's takings. Nothing
        /// anywhere has ever checked this. The clock is the natural place to say so, because it is
        /// the one control on the screen already claiming to know what time it is.
        /// </summary>
        private void Paint()
        {
            var zone = Services.Sync.StoreZone.Current;
            var utcNow = DateTime.UtcNow;
            var shopNow = SharedKernel.StoreClock.InStore(utcNow, zone);

            _clock.Text = shopNow.ToString("HH:mm:ss");

            // ⚠ Recomputed every tick rather than cached: two zones can agree in January and differ
            // in July, and a till checked once at start-up is one that goes wrong at the clock change.
            var drift = SharedKernel.StoreClock.DeviceDisagrees(zone, utcNow);

            _clock.SetDynamicResource(Label.TextColorProperty, drift ? "ThemeDanger" : "ThemeInk");

            ToolTipProperties.SetText(_clock, drift
                ? "⚠ " + SharedKernel.StoreClock.DisagreementMessage(zone, utcNow)
                : zone is null
                    ? $"This PC's clock · {TimeZoneInfo.Local.Id}"
                    : $"The shop's clock · {zone.Id}");
        }

        private static void Start()
        {
            if (_started) return;
            _started = true;

            // ⚠ The DISPATCHER's timer, not a `System.Threading.Timer`: this writes to a `Label`, and a
            // control touched off the UI thread throws on WinUI — intermittently, which is worse.
            Application.Current?.Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                Tick?.Invoke(null, EventArgs.Empty);
                return true;   // ⚠ keep ticking; returning false stops it for good
            });
        }

        /// <summary>
        /// **Plutus**, top-left — the web till's `&lt;h1&gt;&lt;PlutusMark /&gt;Plutus&lt;/h1&gt;`.
        ///
        /// ⚠ MAUI HAD NO PRODUCT NAME ANYWHERE ON THE TILL SCREEN. Matt, 2026-08-22: *"It is missing
        /// the plutus name."* On the web till it is the first thing in the bar; here the navigation bar
        /// started with a clock, so the two did not read as the same application at all.
        ///
        /// ⚠ THE MARK IS THE LETTER, not an image asset. The web till draws an inline SVG; the till
        /// has no equivalent asset and adding a PNG would be one more thing to keep in step with a
        /// vector. A bold `P` in the accent colour is the same idea at the same size, and it cannot go
        /// missing from a build.
        /// </summary>


        /// <summary>
        /// Match the page's width, so the star spacer has something to divide.
        ///
        /// ⚠ A SMALL INSET, not zero. The navigation bar has its own chrome either side and a title
        /// view measured to the exact shell width pushes the 👥 under it. ⚠ And never a NEGATIVE
        /// width — a page reports -1 before its first layout pass, and `WidthRequest = -1` means
        /// "size to content", which is silently the bug this exists to fix.
        /// </summary>
        private void SizeTo(VisualElement host)
        {
            var width = host?.Width ?? 0;
            WidthRequest = width > 40 ? width - 24 : -1;
        }

        private static View Brand()
        {
            var row = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(4, 0, 14, 0),
            };

            var mark = new Label
            {
                Text = "P",
                FontSize = 19,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            };
            mark.SetDynamicResource(Label.TextColorProperty, "ThemeAccent");

            var name = new Label
            {
                Text = "Plutus",
                FontSize = 17,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            };
            name.SetDynamicResource(Label.TextColorProperty, "ThemeInk");

            row.Add(mark);
            row.Add(name);
            return row;
        }

        /// <summary>
        /// Ask the platform what this till is called, and whether this operator may open the portal.
        ///
        /// ⚠ NEVER THROWS AND NEVER BLOCKS. It runs fire-and-forget from `Loaded`; both answers are
        /// decoration, and a till that could not reach the server must still sell.
        ///
        /// ⚠ THE NAME IS NOT CACHED HERE. `TillId` is in the local store, so the label appears a beat
        /// after the bar draws — which is the same order the web till does it in (`fetchTillName` on
        /// mount) and why both show the bar first and the badge second.
        /// </summary>
        private async Task FillIdentityAsync()
        {
            try
            {
                // ⚠ The PORTAL BUTTON is gated on the permission, not on connectivity — the web till
                // shows it only for operators with a `portal.*` scope, and showing it to a cashier who
                // will be refused at the far end is worse than not showing it at all.
                var op = App.GetViewModel()?.SignedInOperator;
                _portal.IsVisible = op is not null && Services.Security.TillGate.MayOpenPortal(op);

                var tillId = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.GetGuidMetaAsync(Plutus.Client.Storage.MetaKeys.TillId)).ConfigureAwait(true);
                if (tillId is not Guid id || id == Guid.Empty) return;

                var api = await Services.Connectivity.PlutusApi.GetAsync().ConfigureAwait(true);
                if (api is null) return;

                var answer = await api.GetTillNameAsync(id).ConfigureAwait(true);
                var name = answer?.Name;
                if (string.IsNullOrWhiteSpace(name)) return;

                _tillName.Text = name;
                _tillName.IsVisible = true;
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillAppBar.FillIdentity", ex);
            }
        }

        /// <summary>
        /// ⚠ A `Button` WITH A GLYPH, not an `ImageButton` and not a tappable `Label`. A Grid with a
        /// `TapGestureRecognizer` is NOT focusable, and this till is used with a keyboard and a
        /// scanner — the same finding that made `SettingsSection`'s headers real buttons.
        /// </summary>
        private static Button IconButton(string glyph, string tip, EventHandler onClick)
        {
            var b = new Button
            {
                Text = glyph,
                FontSize = 15,
                Padding = new Thickness(8, 0),
                MinimumWidthRequest = 36,
                BackgroundColor = Colors.Transparent,
            };

            b.SetDynamicResource(Button.TextColorProperty, "ThemeInk");
            ToolTipProperties.SetText(b, tip);
            b.Clicked += (s, e) => onClick(s, e);
            return b;
        }

        /// <summary>The same button with words rather than a glyph — the web till's `.switch-app`.</summary>
        private static Button TextButton(string text, string tip, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                FontSize = 12,
                Padding = new Thickness(10, 2),
                BackgroundColor = Colors.Transparent,
                BorderWidth = 1,
                CornerRadius = 12,
                Margin = new Thickness(0, 0, 8, 0),
            };

            b.SetDynamicResource(Button.TextColorProperty, "ThemeInk");
            b.SetDynamicResource(Button.BorderColorProperty, "ThemeLine");
            ToolTipProperties.SetText(b, tip);
            b.Clicked += (s, e) => onClick(s, e);
            return b;
        }

        /// <summary>
        /// ⚠ `async void` ON A HANDLER, so it must not let anything escape — an unhandled exception
        /// from one goes to the dispatcher unhandled, which on MAUI closes the till.
        /// </summary>
        private static async void OnHelp(object sender, EventArgs e)
        {
            try
            {
                await Services.Support.SupportFlow.ShowAsync();
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillAppBar.Help", ex);
            }
        }

        private static async void OnUsers(object sender, EventArgs e)
        {
            try
            {
                await Services.People.StaffFlow.ShowAsync();
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillAppBar.Users", ex);
            }
        }

        /// <summary>
        /// Open the portal in the operator's browser.
        ///
        /// ⚠⚠ A BROWSER, NOT A VIEW INSIDE THE TILL. The web till NAVIGATES (`location.assign`)
        /// because it is already in a browser; MAUI is not, and embedding the portal would mean a
        /// WebView with a second session, a second auth implementation and a second place for a token
        /// to live. Matt asked for *"link to the portal"* and a link is what this is.
        ///
        /// ⚠ IT WARNS FIRST IF A BASKET WOULD BE ABANDONED — the same guard the web till applies. The
        /// till stays open behind the browser, so nothing is actually lost; the warning exists because
        /// walking away mid-sale is the mistake, not the navigation.
        /// </summary>
        private static async void OnPortal(object sender, EventArgs e)
        {
            try
            {
                var url = await Services.Connectivity.PortalLink.ResolveAsync().ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(url))
                {
                    // ⚠ `ShowAsync<T>` needs a Task<T>; the three-argument DisplayAlert returns a bare
                    // Task. Returning true is the value nobody reads — it exists to satisfy the gate.
                    await Services.UIHandeling.Modal.ShowAsync(async () =>
                    {
                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                            "This till hasn't been told where the portal is yet.", "OK".Translate());
                        return true;
                    }).ConfigureAwait(true);
                    return;
                }

                await Microsoft.Maui.ApplicationModel.Browser.OpenAsync(
                    url, Microsoft.Maui.ApplicationModel.BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("TillAppBar.Portal", ex);
            }
        }
    }
}
