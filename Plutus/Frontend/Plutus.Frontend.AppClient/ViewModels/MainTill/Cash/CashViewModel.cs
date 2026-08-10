using CustomViews.Structs;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Plutus.Client.Storage;
using Plutus.Contracts.Client;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.SharedKernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Cash
{
    /// <summary>
    /// The drawer: open the float, record money in and out, count mid-shift, close the day (WP9,
    /// cutover step 23).
    ///
    /// ⚠ THIS IS THE ONE CAPABILITY A SHOP CANNOT TRADE WITHOUT. Everything else on the till has a
    /// workaround; a till that cannot declare a float or run a Z-read cannot be reconciled at close,
    /// so its takings cannot be banked. Until 2026-08-10 none of it existed on MAUI — no screen, no
    /// client method, and the five type names appeared nowhere in the app at all.
    ///
    /// ⚠ EVERY ACTION IS RECORDED LOCALLY AND SENT LATER. A shop opens before its broadband does,
    /// and the money moves whether or not the platform hears about it. `TillCadence` drains the
    /// queue on its 60s tick exactly like a sale.
    ///
    /// ⚠ THE EXPECTED FIGURE IS THE SERVER'S, never this screen's. Expected = float + cash takings
    /// + paid-ins − paid-outs, and only the platform can see the sales half — including sales
    /// another device on the same till posted. A till that computed its own would disagree with the
    /// banking report and nobody could say which was right, so this screen shows what it COUNTED and
    /// leaves the variance to the platform.
    /// </summary>
    public class CashViewModel : BaseViewModel
    {
        private readonly Label _summary;
        private readonly StackLayout _actions;
        private readonly StackLayout _history;

        public CashViewModel(Label summary, StackLayout actions, StackLayout history)
        {
            Title = "Cash";
            Icon = "md-account-balance-wallet";

            _summary = summary;
            _actions = actions;
            _history = history;

            Add("Open float", () => RecordAsync(CashEventTypes.OpenFloat, "How much is going in?"));
            Add("Paid in", () => RecordAsync(CashEventTypes.PaidIn, "How much is going in?"));
            Add("Paid out", () => RecordAsync(CashEventTypes.PaidOut, "How much is coming out?"));
            Add("X read", () => RecordAsync(CashEventTypes.XSnapshot, "What is in the drawer?"));
            Add("Z read — close the day", () => RecordAsync(CashEventTypes.ZClose, "What is in the drawer?"));

            Refresh();
        }

        private void Add(string text, Func<Task> action)
        {
            var button = new Button { Text = text };

            // ⚠ `async void` is unavoidable on a Clicked handler, so the body must never be able to
            // escape: an unhandled exception on a MAUI dispatcher closes the till. Nine buttons in
            // this app crashed the app instead of refusing before 2026-08-10.
            button.Clicked += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("CashViewModel." + text, ex);
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That didn't work. Nothing has been recorded.", "OK".Translate());
                }
            };

            _actions.Children.Add(button);
        }

        /// <summary>
        /// The business day this till is trading.
        ///
        /// ⚠ THE TRADING DAY, NOT THE CALENDAR DAY, and it must match what a SALE records or the
        /// drawer reconciles against the wrong takings. `BusinessDay.For` is the shared rule.
        /// </summary>
        private static string Today() =>
            SharedKernel.BusinessDay.Wire(SharedKernel.BusinessDay.Today());

        private async Task RecordAsync(string type, string prompt)
        {
            var gate = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PosNoSale);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return;
            }

            var day = Today();

            // ⚠ Told at the COUNTER, not discovered in tomorrow's report. The server refuses this
            // too, but only if it can be reached — and the shop that most needs to know is the one
            // whose line is down.
            if (await Services.Storage.TillStoreAccess.UseAsync(s => s.IsDayClosedAsync(day)))
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "This day has already been closed with a Z read. Nothing more can be recorded against it.",
                    "OK".Translate());
                return;
            }

            var needsReason = CashEventTypes.NeedsReason(type);

            const NumberStyles money = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands
                                       | NumberStyles.AllowDecimalPoint;

            var elements = new List<ViewElementData>
            {
                new ViewElementData(1, prompt, "",
                    new IValidator[] { new RequiredValidator(), new CurrencyValueValidator(money) }, false, true),
            };

            // ⚠ Money leaving or entering a drawer outside a sale is the one movement with no other
            // record of WHY. The server refuses it without a reason; so does this.
            if (needsReason)
                elements.Add(new ViewElementData(2, "Reason".Translate(), "",
                    new IValidator[] { new RequiredValidator() }, false, true));

            var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                elements, "Confirm".Translate(), true, type, "Cancel".Translate());

            if (answers.Count == 0) return;   // backed out

            _ = answers.TryGetValue(1, out var amountText);
            _ = answers.TryGetValue(2, out var reason);

            if (string.IsNullOrWhiteSpace(amountText)) return;
            if (needsReason && string.IsNullOrWhiteSpace(reason)) return;

            if (!decimal.TryParse(amountText, money, CultureInfo.CurrentCulture, out var amount))
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That amount didn't look like a number. Nothing has been recorded.", "OK".Translate());
                return;
            }

            // ⚠ NON-NEGATIVE. The TYPE carries the direction, not the sign — a negative paid-out is
            // a paid-in nobody meant, and the server 400s it (which, queued, means it never banks).
            var pence = Math.Abs(Pence.FromDecimal(amount));

            // ⚠ A Z-READ ENDS THE DAY. Confirmed explicitly because nothing can be recorded against
            // the day afterwards — not by this till, not by the other device on the same till.
            if (type == CashEventTypes.ZClose &&
                !await Application.Current.MainPage.DisplayAlert("Close the day?",
                    $"This closes {day} with {pence / 100m:C} counted. Nothing more can be recorded "
                    + "against today afterwards.", "Close the day", "Cancel".Translate()))
                return;

            var counted = CashEventTypes.NeedsCount(type) ? (long?)pence : null;
            var moved = CashEventTypes.NeedsCount(type) ? 0 : pence;

            var row = await Services.Storage.TillStoreAccess.UseAsync(s => s.RecordCashEventAsync(
                type, day, moved, counted, reason,
                App.GetViewModel().SignedInOperator?.UserId));

            if (row is null)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "This day has already been closed with a Z read.", "OK".Translate());
                return;
            }

            Refresh();

            // ⚠ Nudge the clock rather than posting inline. Recording is done and safe; sending is
            // the cadence's job, and an operator must never wait on the network to open a drawer.
            _ = Task.Run(() => Services.Sync.TillCadence.TickAsync());
        }

        /// <summary>Redraw the day's story from this till's own record — available offline.</summary>
        public void Refresh()
        {
            _ = Task.Run(async () =>
            {
                var day = Today();
                IReadOnlyList<LocalCashEvent> events;
                bool closed;
                try
                {
                    events = await Services.Storage.TillStoreAccess.UseAsync(s => s.CashEventsForDayAsync(day));
                    closed = await Services.Storage.TillStoreAccess.UseAsync(s => s.IsDayClosedAsync(day));
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("CashViewModel.Refresh", ex);
                    return;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        _summary.Text = closed
                            ? $"{day} — CLOSED. {events.Count} cash event(s)."
                            : $"{day} — open. {events.Count} cash event(s).";
                        _summary.TextColor = closed ? Colors.OrangeRed : Colors.Gray;

                        _history.Children.Clear();
                        foreach (var e in events.Reverse())
                        {
                            var money = CashEventTypes.NeedsCount(e.Type)
                                ? $"counted {(e.CountedPence ?? 0) / 100m:C}"
                                : $"{e.AmountPence / 100m:C}";

                            // ⚠ Says whether it has REACHED the platform. A float that never banked
                            // is the thing somebody has to chase, and a screen that hides the queue
                            // is a screen that lets them not notice.
                            var sent = e.Status switch
                            {
                                1 => "",                    // Pushed
                                2 => "  ⚠ REFUSED",         // Failed — terminal, see the Plutus tab
                                _ => "  (waiting to send)",
                            };

                            _history.Children.Add(new Label
                            {
                                Text = $"{e.OccurredAtUtc.ToLocalTime():HH:mm}  {e.Type}  {money}"
                                     + (string.IsNullOrWhiteSpace(e.Reason) ? "" : $"  — {e.Reason}")
                                     + sent,
                                FontSize = new Label().FontSize - 1,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Services.Analytics.CrashLog.Write("CashViewModel.Draw", ex);
                    }
                });
            });
        }
    }
}
