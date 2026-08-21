using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
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

        public TillAppBar()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });   // spacer
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // clock
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // help
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // users

            // ⚠ THE INK COMES FROM THE SCHEME. This sits on the Shell's navigation bar, whose
            // background the portal can restyle — a hard-coded colour here is the unreadable-label
            // fault of 1.74.0 waiting to happen on somebody's dark scheme.
            _clock.SetDynamicResource(Label.TextColorProperty, "ThemeInk");

            // ⚠ The hover caption names the TIMEZONE, and that is the diagnostic half — `Europe/London`
            // on a till reading an hour out says immediately whether the fault is the PC or the data.
            // That question took a morning on 2026-08-21.
            ToolTipProperties.SetText(_clock, $"This PC's clock · {TimeZoneInfo.Local.Id}");

            this.Add(_clock, 1, 0);
            this.Add(IconButton("❓", "Help & support", OnHelp), 2, 0);
            this.Add(IconButton("👥", "Users", OnUsers), 3, 0);

            _onTick = (_, _) => Paint();
            Paint();

            Loaded += (_, _) => { Start(); Tick += _onTick; Paint(); };
            Unloaded += (_, _) => Tick -= _onTick;
        }

        /// <summary>⚠ SECONDS, deliberately — a clock showing only `HH:MM` is indistinguishable from a
        /// static label for up to a minute, and "is this live?" is exactly what somebody is asking when
        /// they look at it after a wrong time.</summary>
        private void Paint() => _clock.Text = DateTime.Now.ToString("HH:mm:ss");

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
    }
}
