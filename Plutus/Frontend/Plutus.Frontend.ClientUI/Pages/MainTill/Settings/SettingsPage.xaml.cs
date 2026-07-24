using Plutus.Frontend.ClientUI.ViewModels.MainTill.Settings;

namespace Plutus.Frontend.ClientUI.Pages.MainTill.Settings;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
