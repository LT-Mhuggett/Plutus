using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.ViewModels;

namespace Plutus.Frontend.ClientUI.Pages
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage(LoginViewModel loginViewModel)
        {
            InitializeComponent();
            BindingContext = loginViewModel;
        }
    }
}