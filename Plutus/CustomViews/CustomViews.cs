using System;
using Rg.Plugins.Popup.Pages;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace CustomViews
{
    public class InputAlertDialogBase<T> : PopupPage
    {

        public Task<T> PageClosedTask
        {
            get { return PageClosedTaskCompletionSource.Task; }
        }

        public TaskCompletionSource<T> PageClosedTaskCompletionSource { get; set; }

        public InputAlertDialogBase(View contentBody)
        {
            Content = contentBody;

            this.BackgroundColor = new Color(0, 0, 0, .4);

            PageClosedTaskCompletionSource = new TaskCompletionSource<T>();
        }

        protected override Task OnAppearingAnimationEnd()
        {
            return Content.FadeTo(1);
        }

        protected override Task OnDisappearingAnimationBegin()
        {
            return Content.FadeTo(1);
        }

        protected override bool OnBackButtonPressed()
        {
            return true;
        }

        protected override bool OnBackgroundClicked()
        {
            return false;
        }
    }


}
