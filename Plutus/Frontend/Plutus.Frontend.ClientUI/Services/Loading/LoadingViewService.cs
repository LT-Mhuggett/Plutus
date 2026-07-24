using Microsoft.Maui.Controls;
using Plutus.Frontend.ClientUI.Pages;

namespace Plutus.Frontend.ClientUI.Services.Loading
{
    public partial class LoadingViewService
    {
        protected LoadingIndicatorPage LoadingIndicatorPage;
        public LoadingViewService(LoadingIndicatorPage loadingIndicatorPage)
        {
            LoadingIndicatorPage = loadingIndicatorPage;
        }
        public partial void InitLoadingView();
        public partial void ShowLoadingView();
        public partial void HideLoadingView();
    }
}
