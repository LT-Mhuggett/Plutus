using Rg.Plugins.Popup.Pages;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace CustomViews
{
    public class AlertDialogBase<T> : PopupPage
    {

        private bool _interuptable;

        public Task<T> PageClosedTask
        {
            get { return PageClosedTaskCompletionSource.Task; }
        }

        public TaskCompletionSource<T> PageClosedTaskCompletionSource { get; set; }

        public AlertDialogBase(View contentBody, bool interuptable)
        {
            Content = contentBody;
            _interuptable = interuptable;

            this.BackgroundColor = new Color(0, 0, 0, .4);

            PageClosedTaskCompletionSource = new TaskCompletionSource<T>();
        }

        protected override Task OnAppearingAnimationEndAsync()
        {
            return Content.FadeTo(1);
        }

        protected override Task OnDisappearingAnimationBeginAsync()
        {
            return Content.FadeTo(1);
        }

        protected override bool OnBackButtonPressed()
        {
            return true;
        }

        protected override bool OnBackgroundClicked()
        {
            return _interuptable;
        }
    }


}
