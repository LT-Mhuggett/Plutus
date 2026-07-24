using CommunityToolkit.Mvvm.Input;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Helpers;
using Plutus.Frontend.ClientUI.Pages.MainTill.Statistics;
using Plutus.Frontend.ClientUI.Resources.I18N_L10N;
using Plutus.Frontend.ClientUI.Services.Analytics;
using Plutus.Frontend.ClientUI.Services.Loading;
using Plutus.Frontend.ClientUI.Services.Repository.Contracts;

namespace Plutus.Frontend.ClientUI.ViewModels.MainTill.Statistics
{
    public partial class StatisticsViewModel : BaseViewModel
    {
        public StatisticsViewModel(ILogger logger, IAppState appState, LoadingViewService loadingViewService, IRepositoryWrapper repositoryWrapper) : base(logger, appState, loadingViewService, repositoryWrapper)
        {
            Title = Strings.Statistics;
            Icon = "\uf201";
        }

        public void SetupNavigationButtons(StackLayout leftColumn, StackLayout rightColumn)
        {
            var buttons = new List<Tuple<string, string>>
            {
                Tuple.Create(Strings.SalesReports, "OpenSalesReportsCommand"),
                Tuple.Create(Strings.StockOuttakeReport, "OpenStockOuttakeCommand"),
            };

            for (int i = 0; i < buttons.Count; i++)
            {
                var button = new Button { Text = buttons[i].Item1 };
                button.SetBinding(Button.CommandProperty, buttons[i].Item2);
                if (i % 2 == 0)
                    leftColumn.Children.Add(button);
                else
                    rightColumn.Children.Add(button);
            }
        }

        [RelayCommand]
        private async void OpenSalesReports()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                await App.Current.MainPage.Navigation.PushModalAsync(ServiceHelper.GetService<SalesReportPage>());
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async void OpenStockOuttake()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                await App.Current.MainPage.Navigation.PushModalAsync(ServiceHelper.GetService<StockOuttakePage>());
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
