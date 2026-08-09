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

            // ⚠ WARNED RATHER THAN HIDDEN, and the distinction matters. Both reports read the
            // LEGACY local database (`SalesReportsViewModel` and `StockOuttakeViewModel` open
            // `Helpers.Database.Database` throughout), and since cutover step 11 sales no longer go
            // there — they go to the v2 store and the platform. So on a portal-provisioned till
            // these read ZERO for everything sold since, and a report that says "£0.00 takings"
            // about a day the shop took £2,000 is far worse than one that refuses to open.
            //
            // Not hidden, because a till MIGRATED from a NatApp install still holds real history in
            // that file and this is the only way to see it. The honest position is both facts at
            // once. Platform-wide reporting is cutover step 26 (WP11) — see
            // `Build/legacy-removal.md` (L4).
            leftStackColumn.Children.Add(new Label
            {
                Text = "⚠ These reports read this till's OLD local database only. Sales made since "
                     + "this till joined Plutus are NOT included — for current figures use the "
                     + "Plutus portal.",
                FontSize = new Label().FontSize - 1,
                TextColor = Colors.OrangeRed,
                Margin = new Microsoft.Maui.Thickness(0, 12, 0, 0),
            });
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
