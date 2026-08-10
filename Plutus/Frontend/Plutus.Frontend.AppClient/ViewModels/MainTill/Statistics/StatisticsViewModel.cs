using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Views.MainTill.Statistics;
using Microsoft.Maui.ApplicationModel;
using Plutus.SharedKernel;
using System;
using System.Threading.Tasks;
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

            // ⚠ TODAY'S REAL FIGURES, FROM THE PLATFORM (WP11 / cutover step 26). This is where the
            // red "these reports read the OLD local database" warning used to be — and the warning
            // was true: both legacy reports open `Helpers.Database.Database`, and since step 11
            // sales go to the v2 store and the platform instead, so they read ZERO for everything
            // sold since. A report saying "£0.00 takings" about a £2,000 day is worse than one that
            // will not open.
            //
            // ⚠ THE FIGURES COME FROM THE SERVER, and they have to. A till cannot know its own
            // takings: sales posted by the other device on the same till, sales pruned out of local
            // history, and refunds taken at another counter against sales rung up here all belong in
            // the number an operator counts a drawer against.
            rightStackColumn.Children.Add(_today);
            LoadToday();

            // ⚠ The legacy reports are still REACHABLE and still warned about, deliberately. A till
            // migrated from NatApp holds real pre-cutover history in that file and this is the only
            // way to see it — so the honest position is both facts at once, not a deletion.
            leftStackColumn.Children.Add(new Label
            {
                Text = "⚠ The two reports above read this till's OLD local database only — useful "
                     + "for pre-Plutus history, and NOT a record of anything sold since. Today's "
                     + "real figures are on the right.",
                FontSize = new Label().FontSize - 1,
                TextColor = Colors.OrangeRed,
                Margin = new Microsoft.Maui.Thickness(0, 12, 0, 0),
            });
        }

        /// <summary>Where today's platform figures are rendered.</summary>
        private readonly StackLayout _today = new() { Spacing = 2 };

        /// <summary>
        /// This till's takings for today, from the platform.
        ///
        /// ⚠ Off the UI thread and it CANNOT THROW: this runs from a constructor `AppShell` invokes
        /// while it is being built, and a figures panel that fails must never be able to stop
        /// somebody signing in. That exact shape — a viewmodel constructor throwing — is how a
        /// correct password came back as "something went wrong signing in" on 2026-08-09.
        /// </summary>
        private void LoadToday()
        {
            _ = Task.Run(async () =>
            {
                string line;
                Color colour;
                try
                {
                    var tillId = await Services.Storage.TillStoreAccess.UseAsync(
                        s => s.GetGuidMetaAsync(Plutus.Client.Storage.MetaKeys.TillId));

                    var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();

                    if (tillId is not Guid till || till == Guid.Empty)
                    {
                        line = "This till hasn't been enrolled yet, so it has no figures.";
                        colour = Colors.Gray;
                    }
                    else if (api is null)
                    {
                        // ⚠ Says WHICH of the two it is. "No figures" with no reason reads as
                        // "we took nothing today", which is the one thing it must never imply.
                        line = "Sign in and connect to Plutus to see today's takings.";
                        colour = Colors.Gray;
                    }
                    else
                    {
                        var day = SharedKernel.BusinessDay.Today();
                        var summary = await api.GetReportSummaryAsync("till", till.ToString("D"), day, day);

                        line = summary is null
                            ? "Plutus couldn't be reached, so today's takings aren't shown."
                            : $"Today — {summary.Totals.GrossPence / 100m:C} taken over "
                              + $"{summary.Totals.TxnCount} sale{(summary.Totals.TxnCount == 1 ? "" : "s")}"
                              + $"  ·  VAT {summary.Totals.VatPence / 100m:C}"
                              + $"  ·  average basket {summary.Totals.AvgBasketPence / 100m:C}";
                        colour = summary is null ? Colors.Gray : Colors.SeaGreen;
                    }
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("StatisticsViewModel.LoadToday", ex);
                    line = "Today's takings couldn't be loaded.";
                    colour = Colors.Gray;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        _today.Children.Clear();
                        _today.Children.Add(new Label
                        {
                            Text = "This till, from Plutus",
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.LightGray,
                        });
                        _today.Children.Add(new Label { Text = line, TextColor = colour });
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("StatisticsViewModel.DrawToday", ex);
                    }
                });
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
        // ⚠ NO LOADING OVERLAY AROUND A NAVIGATION — same fault, same fix as
        // `InventoryViewModel.ExecuteOpenViewAllItems`, whose header explains it: a modal push and a
        // navigation push issued against one window in the same instant produce a corrupted layout,
        // and nothing here ever lowered the overlay it raised.
        private async void ExecuteOpenSalesReports()
        {
            if (IsBusy)
                return;
            IsBusy = true;

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new SalesReportsView());
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("StatisticsViewModel.OpenSalesReports", ex);
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
            IsBusy = true;

            try
            {
                await App.Current.MainPage.Navigation.PushAsync(new StockOuttakeView());
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("StatisticsViewModel.OpenStockOuttakeReport", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }
        #endregion
    }
}
