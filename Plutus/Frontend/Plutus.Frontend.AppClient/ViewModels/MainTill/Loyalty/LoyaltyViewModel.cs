using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.SharedKernel;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Loyalty
{
    /// <summary>
    /// The loyalty list — everyone who is a member or holds store credit (step 27, WP12).
    ///
    /// ⚠ A LOOKUP, NOT A CONFIGURATION SCREEN. Tiers are created in the **portal only** (binding
    /// default 20). This answers *"what does this customer have?"* away from a sale; assigning a
    /// tier is done on the Till tab against an attached member, where it is gated `customers.manage`.
    ///
    /// ⚠ ROWS ARE BUILT IN CODE, like Cash and Statistics — MAUI bindings fail silently, and a blank
    /// tier here tells a customer they have no discount while a blank balance makes credit look
    /// spent. Built in code, a typo is a compile error.
    /// </summary>
    public class LoyaltyViewModel : BaseViewModel
    {
        private readonly Layout _rows;
        private readonly Label _status;
        private string _search = string.Empty;

        public LoyaltyViewModel(Layout rows, Label status)
        {
            _rows = rows;
            _status = status;

            Title = "Loyalty";
            Icon = "card_membership";
        }

        public string Search
        {
            get => _search;
            set => SetProperty(ref _search, value);
        }

        private Command _refreshCommand;
        public Command RefreshCommand => _refreshCommand ??= new Command(() => Refresh());

        /// <summary>
        /// Reload the list.
        ///
        /// ⚠ ONLINE-ONLY, and it says so rather than showing an empty list. A till holds no customer
        /// cache; an empty screen would read as "this shop has no members", which is a far worse
        /// answer than "you are offline".
        /// </summary>
        public void Refresh() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            SetStatus("Loading…");

            try
            {
                var api = await Services.Storage.TillPlacement.TryCreateApiAsync().ConfigureAwait(false);
                if (api is null)
                {
                    Render(null, "Members can only be listed while the till is online.");
                    return;
                }

                var rows = await api.GetLoyaltyAsync(Search).ConfigureAwait(false);

                if (rows is null)
                {
                    // ⚠ NULL IS NOT EMPTY. A 403 or an unreadable answer must not render as "no
                    // members" — that is a wrong statement about the shop, made confidently.
                    Render(null, "The member list couldn't be read. You may not have permission to see it.");
                    return;
                }

                Render(rows, rows.Count == 0
                    ? (string.IsNullOrWhiteSpace(Search)
                        ? "No members or store credit yet."
                        : $"Nothing found for '{Search}'.")
                    : $"{rows.Count} shown");
            }
            catch (Exception ex)
            {
                CrashLog.Write("LoyaltyViewModel.RefreshAsync", ex);
                Render(null, "The member list couldn't be loaded.");
            }
        }

        private void SetStatus(string text) =>
            MainThread.BeginInvokeOnMainThread(() => _status.Text = text);

        /// <summary>
        /// ⚠ ON THE UI THREAD, ALWAYS. The load runs off it, and touching a `Layout`'s children from
        /// a pool thread is the same crash class as A4's dialog — WinUI refuses, from a place no
        /// `catch` on this path would see.
        /// </summary>
        private void Render(
            System.Collections.Generic.IReadOnlyList<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto> rows,
            string status)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _status.Text = status;
                _rows.Clear();

                if (rows is null) return;

                foreach (var r in rows)
                    _rows.Add(RowFor(r));
            });
        }

        /// <summary>One member's line. ⚠ Every figure is formatted from what the SERVER said —
        /// nothing is re-derived here, least of all whether a membership has expired.</summary>
        private static View RowFor(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto r)
        {
            var grid = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            var name = string.IsNullOrWhiteSpace(r.Name) ? "(no name)" : r.Name;
            var who = string.IsNullOrWhiteSpace(r.MemberNo) ? name : $"{name} · {r.MemberNo}";

            grid.Add(new Label { Text = who, VerticalOptions = LayoutOptions.Center }, 0);

            // ⚠ AN EXPIRED MEMBERSHIP SAYS SO. Showing "Gold 10%" beside a lapsed member is how an
            // operator promises a discount the till will not give — the shared rule decides, and
            // `Expired` is the SERVER's verdict, never re-derived from a renewal date here.
            var tier = string.IsNullOrWhiteSpace(r.Tier)
                ? string.Empty
                : r.Expired
                    ? $"{r.Tier} (expired)"
                    : MemberDiscount.Label(r.Tier, r.AutoDiscountRate ?? 0m);

            grid.Add(new Label { Text = tier, VerticalOptions = LayoutOptions.Center }, 1);

            grid.Add(new Label
            {
                Text = r.CreditBalancePence == 0
                    ? string.Empty
                    : (r.CreditBalancePence / 100m).ToString("C2", CultureInfo.CurrentCulture),
                VerticalOptions = LayoutOptions.Center,
            }, 2);

            return grid;
        }
    }
}
