using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.Platform
{
    public partial class ConnectionView : ContentPage
    {
        /// <param name="firstRun">True when shown as the first-run tab, where it also offers the
        /// route on to sign-in. Inside the shell (post-login) that button would be nonsense.</param>
        public ConnectionView(bool firstRun = false)
        {
            InitializeComponent();
            BindingContext = new ViewModels.Platform.ConnectionViewModel(firstRun);
        }
    }
}
