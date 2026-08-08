using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.Platform
{
    public partial class ConnectionView : ContentPage
    {
        public ConnectionView()
        {
            InitializeComponent();
            BindingContext = new ViewModels.Platform.ConnectionViewModel();
        }
    }
}
