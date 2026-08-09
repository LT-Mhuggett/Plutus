using Mopups.Pages;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CustomViews
{
    public class AlertDialogBase<T> : PopupPage
    {

        private readonly bool _interuptable;

        public Task<T> PageClosedTask
        {
            get { return PageClosedTaskCompletionSource.Task; }
        }

        public TaskCompletionSource<T> PageClosedTaskCompletionSource { get; set; }

        public AlertDialogBase(View contentBody, bool interuptable)
        {
            Content = contentBody;
            _interuptable = interuptable;

            // ⚠ This scrim is what the operator sees when anything else here goes wrong: a 40%-black
            // sheet over the whole app. It is only ever acceptable while the dialog on top of it is
            // VISIBLE and ESCAPABLE — see the two guarantees below.
            this.BackgroundColor = new Color(0, 0, 0, .4f);

            PageClosedTaskCompletionSource = new TaskCompletionSource<T>();
        }

        /// <summary>
        /// ⚠ GUARANTEE 1 — THE CONTENT IS VISIBLE. Opacity is forced on appearing rather than only
        /// at the end of the platform's animation: if that animation never completed, the scrim
        /// showed and the dialog did not, which reads as the till freezing.
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (Content != null) Content.Opacity = 1;
        }

        protected override Task OnAppearingAnimationEndAsync()
        {
            return Content.FadeTo(1);
        }

        protected override Task OnDisappearingAnimationBeginAsync()
        {
            return Content.FadeTo(1);
        }

        /// <summary>
        /// ⚠ GUARANTEE 2 — THERE IS ALWAYS A WAY OUT. This returned <c>true</c> unconditionally,
        /// which swallows Escape and the hardware back button. Combined with a dialog built without
        /// a Cancel button and <c>interuptable: false</c> — which is exactly how the CASH PAYMENT
        /// dialog was raised — the operator had no exit at all: no button, no background click, no
        /// key. A till in a shop cannot have a screen you can only leave by killing the process.
        ///
        /// Now it CANCELS. `PageClosedTaskCompletionSource` completes with the default, and the
        /// caller treats that as "the operator backed out" — the same as pressing Cancel.
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            PageClosedTaskCompletionSource?.TrySetResult(default);
            return true;
        }

        protected override bool OnBackgroundClicked()
        {
            if (_interuptable) PageClosedTaskCompletionSource?.TrySetResult(default);
            return _interuptable;
        }
    }


}
