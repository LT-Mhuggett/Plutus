using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Services
{
    /// <summary>
    /// **Make the OS's own drop-downs follow the shop's theme.**
    ///
    /// ⚠⚠ A MAUI STYLE CANNOT REACH A WINUI POPUP, WHICH IS WHY THE LISTS WERE WHITE. Matt,
    /// 2026-08-22: *"Can you check all of the drop down listrs, they are white and do not follow
    /// themeing."* `Styles.xaml` already styles `Picker` and `DatePicker` — and it works, on the
    /// CLOSED control. Open one and WinUI renders the list in a `Popup` with its own visual root: it
    /// never sees the MAUI style, and takes its brushes from the WinUI element tree's
    /// `RequestedTheme` instead. On a dark Plutus scheme that meant black text on the shop's dark
    /// surface, then a glaring white list the moment it opened.
    ///
    /// ⚠ SO THE FIX IS AT THE ROOT ELEMENT, not on each control. Setting `RequestedTheme` on the
    /// window's content makes every native surface — combo lists, date-picker flyouts, context menus,
    /// tooltips, scrollbars — inherit it. Chasing them one brush at a time would leave the next one
    /// white and nobody would know which.
    ///
    /// ⚠⚠ IT IS LIGHT-OR-DARK ONLY, AND THAT IS THE HONEST LIMIT. A Plutus scheme is arbitrary
    /// colours; WinUI's theme is a binary. So this matches the scheme's BASE MODE, which is the thing
    /// that makes a popup readable or not — a dark list on a dark till, a light one on a light till.
    /// It does not tint the popup in the shop's accent, and it should not pretend to.
    /// </summary>
    internal static class NativePopupTheme
    {
        /// <summary>
        /// Push the current app theme down to the WinUI root.
        ///
        /// ⚠ NEVER THROWS. It is called from `Theming.Apply`, which runs on a cadence and on boot;
        /// an exception there would take out theming for the whole session over a cosmetic concern.
        ///
        /// ⚠ AND IT IS CALLED AGAIN ON EVERY APPLY, not once at start-up. The portal can change a
        /// shop's scheme while the till is open — that is the whole point of `Theming.RefreshAsync` —
        /// and a root theme set once would leave the popups on the previous mode until a restart.
        /// </summary>
        public static void Apply()
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // ⚠ Fully qualified: `Microsoft.UI.Xaml` also has an `Application`, and this
                        // one is MAUI's.
                        var app = Microsoft.Maui.Controls.Application.Current;
                        if (app is null) return;

                        // ⚠ `Unspecified` means "follow the device", and WinUI's `Default` is exactly
                        // that — so it is passed through rather than guessed at. Inventing dark from a
                        // device setting the scheme never mentioned is the same mistake `Theming.Apply`
                        // refuses to make with its stock palette.
                        var theme = app.UserAppTheme switch
                        {
                            AppTheme.Light => ElementTheme.Light,
                            AppTheme.Dark => ElementTheme.Dark,
                            _ => ElementTheme.Default,
                        };

                        foreach (var window in app.Windows)
                        {
                            if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) continue;
                            if (native.Content is FrameworkElement root) root.RequestedTheme = theme;
                        }
                    }
                    catch (Exception inner)
                    {
                        global::Plutus.Frontend.AppClient.Services.Analytics.CrashLog.Write(
                            "NativePopupTheme.Apply(ui)", inner);
                    }
                });
            }
            catch (Exception ex)
            {
                global::Plutus.Frontend.AppClient.Services.Analytics.CrashLog.Write("NativePopupTheme.Apply", ex);
            }
        }
    }
}
