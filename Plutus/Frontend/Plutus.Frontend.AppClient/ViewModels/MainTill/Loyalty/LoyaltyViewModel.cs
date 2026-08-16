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
        private readonly ContentView _tableHost;
        private readonly Label _status;
        private readonly Controls.TillTable<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto> _table;
        private string _search = string.Empty;

        public LoyaltyViewModel(ContentView tableHost, Label status)
        {
            _tableHost = tableHost;
            _status = status;

            // ⚠ NO SEARCH BOX ON THE TABLE. The page already has one, and that one re-queries the
            // SERVER — the list is capped server-side, so filtering only what is on screen would
            // quietly hide members who exist. Two boxes doing different things is worse than one.
            _table = new Controls.TillTable<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                Columns(), search: null, emptyText: "No members or store credit yet.");

            _tableHost.Content = _table;

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
                _table.SetRows(rows ?? Array.Empty<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>());
            });
        }

        /// <summary>
        /// The columns, per `table-standard.md`.
        ///
        /// ⚠ THE BALANCE IS `Numeric`, WHICH IS NOT COSMETIC. It right-aligns so the column can be
        /// read down, and — more importantly — it sorts as a NUMBER. Ordered as text, £100 comes
        /// before £9, and a manager looking for the biggest balances gets nonsense.
        ///
        /// ⚠ The tier column sorts on the TIER NAME rather than its rendered text, so "Gold" and
        /// "Gold (expired)" group together instead of splitting on a bracket.
        /// </summary>
        private static Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>[] Columns() => new[]
        {
            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Member",
                r => string.IsNullOrWhiteSpace(r.MemberNo)
                    ? (string.IsNullOrWhiteSpace(r.Name) ? "(no name)" : r.Name)
                    : $"{(string.IsNullOrWhiteSpace(r.Name) ? "(no name)" : r.Name)} · {r.MemberNo}"),

            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Tier",
                // ⚠ AN EXPIRED MEMBERSHIP SAYS SO. "Gold 10%" beside a lapsed member is an operator
                // promising a discount the till will not give. `Expired` is the SERVER's verdict,
                // never re-derived from a renewal date against this till's clock.
                r => string.IsNullOrWhiteSpace(r.Tier)
                    ? string.Empty
                    : r.Expired
                        ? $"{r.Tier} (expired)"
                        : MemberDiscount.Label(r.Tier, r.AutoDiscountRate ?? 0m),
                SortText: r => r.Tier ?? string.Empty),

            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Credit",
                r => r.CreditBalancePence == 0
                    ? string.Empty
                    : (r.CreditBalancePence / 100m).ToString("C2", CultureInfo.CurrentCulture),
                Numeric: true,
                SortNumber: r => r.CreditBalancePence),
        };
    }
}
