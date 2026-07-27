using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Views.MainTill.Statistics;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Statistics
{
    public class StatisticsViewModel : BaseViewModel
    {
        #region Private Fields

        #endregion

        #region Properties

        #endregion

        public StatisticsViewModel(StackLayout leftStackColumn, StackLayout rightStackColumn)
        {
            Title = "Statistics".Translate();
            Icon = "md-data-usage";

            var buttons = new List<Tuple<string, string>>
            {
                Tuple.Create("SalesReports".Translate(), "OpenSalesReportsCommand"),
                Tuple.Create("StockOuttakeReport".Translate(), "OpenStockOuttakeReportComamnd"),
            };

            for(int i = 0; i < buttons.Count; i++)
            {
                var button = new Button { Text = buttons[i].Item1 };
                button.SetBinding(Button.CommandProperty, buttons[i].Item2);
                if (i % 2 == 0)
                    leftStackColumn.Children.Add(button);
                else
                    rightStackColumn.Children.Add(button);
            }
        }

        #region Commands
        Command _openSalesReportsCommand;
        public Command OpenSalesReportsCommand
        {
            get => _openSalesReportsCommand ?? (_openSalesReportsCommand = new Command(ExecuteOpenSalesReports));
        }

        Command _openStockOuttakeReportCommand;

        public Command OpenStockOuttakeReportComamnd
        {
            get => _openStockOuttakeReportCommand ?? (_openStockOuttakeReportCommand = new Command(ExecuteOpenStockOuttakeReport));
        }
        #endregion

        #region Execute Commands
        private async void ExecuteOpenSalesReports()
        {
            if (IsBusy)
                return;
            App.SetLoading(IsBusy = true);

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new SalesReportsView());
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteOpenStockOuttakeReport()
        {
            if (IsBusy)
                return;
            App.SetLoading(IsBusy = true);

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new StockOuttakeView());
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
    }
}
