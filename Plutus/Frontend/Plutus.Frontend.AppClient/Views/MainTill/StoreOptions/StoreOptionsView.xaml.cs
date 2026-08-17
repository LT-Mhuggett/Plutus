using Microsoft.Maui.Controls;

namespace Plutus.Frontend.AppClient.Views.MainTill.StoreOptions
{
    public partial class StoreOptionsView : ContentPage
    {
        public StoreOptionsView()
        {
            InitializeComponent();

            BindingContext = new ViewModels.MainTill.StoreOptions.StoreInformationViewModel(Body);
        }

        // ⚠ `OnSizeAllocated` is GONE (2026-08-17). It moved a `RightColumn` between grid cells to
        // fake a responsive layout; the cards now wrap themselves (`FlexLayout Wrap="Wrap"`), which
        // is what the web till's CSS does and what the platform is for. Reflowing a layout from a
        // size callback also re-entered on every window resize, which is a good way to get a
        // half-applied layout that nobody can reproduce.
    }
}
