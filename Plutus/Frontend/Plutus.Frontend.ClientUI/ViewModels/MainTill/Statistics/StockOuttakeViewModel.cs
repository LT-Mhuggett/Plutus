using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Statistics
{
    public partial class StockOuttakeViewModel : BaseViewModel
    {
        public StockOuttakeViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            Title = Strings.StockOuttakeReport;
            Icon = "\uf49e";
        }
    }
}
