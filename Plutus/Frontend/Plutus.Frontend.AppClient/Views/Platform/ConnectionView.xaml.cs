using System;
using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.Platform
{
    public partial class ConnectionView : ContentPage
    {
        /// <summary>
        /// True when this page was pushed onto the modal stack and therefore has somewhere to go back
        /// to. False on first run, where it IS the screen and closing it would leave nothing.
        /// </summary>
        private readonly bool _closeable;

        /// <param name="firstRun">True when shown as the first-run tab, where it also offers the
        /// route on to sign-in. Inside the shell (post-login) that button would be nonsense.</param>
        public ConnectionView(bool firstRun = false)
        {
            InitializeComponent();
            BindingContext = new ViewModels.Platform.ConnectionViewModel(firstRun);

            // ⚠⚠ THE EXIT, AND WHY IT IS CONDITIONAL — till-design D4, 2026-08-19. Matt: *"I cannot
            // exit this screen. There is no Close and esc doesnt work"*.
            //
            // This page spent its life as a TAB, where leaving it meant tapping another tab, so it
            // never needed a close control. §5c item 9 un-tabbed it and began pushing it MODALLY — and
            // a modal `ContentPage` gets no back affordance from anywhere, so the only way out was
            // killing the app. That is the trap D4 exists to prevent, on the one screen an operator
            // opens when the till is already misbehaving.
            //
            // ⚠ NOT shown on first run: the same page is the pre-sign-in enrolment screen and there is
            // nothing behind it. A Close there would either do nothing or strand the operator on a
            // blank shell, which is worse than no button.
            _closeable = !firstRun;
            CloseButton.IsVisible = _closeable;
        }

        /// <summary>
        /// ⚠ D4 RULE 2 — the back button and Escape cancel, not just the ✕/Close. `OnBackButtonPressed`
        /// is what Escape reaches on this platform (the same hook `AlertDialogBase` uses), so wiring
        /// only the button would leave the keyboard exit that D4 requires still missing.
        ///
        /// ⚠ Returns TRUE only when it has actually handled the press. On first run it must fall through
        /// to the default, or a back press there would be silently swallowed — which is how
        /// `AlertDialogBase` once ate both Escape and the back button.
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            if (!_closeable) return base.OnBackButtonPressed();

            CloseAsync();
            return true;
        }

        private void OnCloseClicked(object sender, EventArgs e) => CloseAsync();

        /// <summary>
        /// ⚠ NOT `async void`. An exception escaping an `async void` handler reaches the dispatcher
        /// unhandled and kills the till — the shape of every crash reported on 2026-08-18. This starts
        /// the pop and swallows a failure into the crash log, because a Close that throws must still
        /// leave the operator on a usable screen.
        /// </summary>
        private void CloseAsync()
        {
            _ = PopAsync();

            static async System.Threading.Tasks.Task PopAsync()
            {
                try
                {
                    var nav = Application.Current?.MainPage?.Navigation;

                    // ⚠ Only pop what is actually there. Popping an empty modal stack throws, and this
                    // handler can be reached twice — a fast double-tap on Close, or Close plus Escape.
                    if (nav is not null && nav.ModalStack.Count > 0)
                        await nav.PopModalAsync();
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("ConnectionView.Close", ex);
                }
            }
        }
    }
}
