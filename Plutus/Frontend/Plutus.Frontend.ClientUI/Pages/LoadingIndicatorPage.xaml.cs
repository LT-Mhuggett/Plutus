using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.ViewModels;

namespace Plutus.Frontend.ClientUI.Pages
{
    public partial class LoadingIndicatorPage : ContentView
    {
        public LoadingIndicatorPage(LoadingIndicatorViewModel loadingIndicatorViewModel)
        {
            InitializeComponent();

            BindingContext = loadingIndicatorViewModel;
        }
    }
}