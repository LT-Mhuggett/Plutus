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

            // ⚠ THE TWO LEGACY REPORT BUTTONS ARE HIDDEN (2026-08-10). Matt hit the reason: opening
            // Sales Reports raises a SYNCFUSION LICENCE dialog and then **the screen cannot be left**
            // — a dead end on a shop floor, which is the worst failure this app can have.
            //
            // The licence IS registered (`App.xaml.cs`), but Syncfusion keys are VERSION-SPECIFIC and
            // the packages are on **34.1.32**; the registered key predates that, so `SfCartesianChart`
            // and `SfCalendar` refuse to render. ⚠ A new key can only come from Matt's Syncfusion
            // account (Downloads → Get License Key, for 34.x) — nothing in this repo can produce one.
            //
            // Hiding rather than fixing is the right call for three reasons, not one:
            //   • both reports read the LEGACY local database, so they show ZERO for everything sold
            //     since cutover step 11 — the figure they would show is wrong even when they render;
            //   • "what has this till taken" is now answered from the platform, below;
            //   • a screen you cannot leave beats every other consideration.
            //
            // ⚠ NOTHING IS DELETED. A till migrated from NatApp still holds real pre-cutover history
            // in that file, and these screens are how you read it — once the key is renewed. Recorded
            // in `Build/To do/MAUI-retrofit.md` §10 (L4).
            // ⚠ REPRINT LIVES HERE, on the "what has this till taken today" screen (cutover step
            // 26). It is the screen an operator is already on when somebody comes back to the
            // counter without their receipt, and it is the only screen in the app that lists past
            // sales for a reason other than refunding them.
            var buttons = new List<Tuple<string, string>>
            {
                Tuple.Create("Reprint a receipt", "ReprintReceiptCommand"),
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
                Text = "The old Sales and Stock reports are hidden: they read this till's pre-Plutus "
                     + "database (so they show nothing sold since it joined Plutus), and the charting "
                     + "licence needs renewing for the current version. Fuller reporting is in the "
                     + "Plutus portal.",
                FontSize = new Label().FontSize - 1,
                TextColor = Colors.Gray,
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
        ///
        /// ⚠ PUBLIC AND RE-ENTRANT, because it is called again on every cadence tick while this
        /// screen is up. It was called ONCE, from the constructor `AppShell` runs at sign-in — so
        /// the takings figure was frozen at whatever it read the moment the operator signed in, and
        /// a full day of selling never moved it. It redraws by clearing <see cref="_today"/> first,
        /// so repeat calls replace the panel rather than stacking copies of it.
        /// </summary>
        public void LoadToday()
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

        Command _reprintReceiptCommand;
        public Command ReprintReceiptCommand
        {
            get => _reprintReceiptCommand ?? (_reprintReceiptCommand = new Command(ExecuteReprintReceipt));
        }
        #endregion

        #region Execute Commands
        /// <summary>
        /// Print another copy of a receipt this till has already issued (cutover step 26).
        ///
        /// ⚠ `async void` on a Command — so it must not let anything escape. An unhandled exception
        /// here is not a failed button, it is a closed till.
        /// </summary>
        private async void ExecuteReprintReceipt()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await Services.Printing.ReceiptReprint.PickAndReprintAsync();
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("StatisticsViewModel.Reprint", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That didn't work. Nothing has been printed.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

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
