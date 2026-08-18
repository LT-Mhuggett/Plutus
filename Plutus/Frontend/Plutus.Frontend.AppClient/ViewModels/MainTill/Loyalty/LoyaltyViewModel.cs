using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
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
    /// ⚠ A LOOKUP AND THE SIGN-UP DESK, NOT A CONFIGURATION SCREEN. Tiers are created in the **portal only** (binding
    /// default 20). This answers *"what does this customer have?"* away from a sale; assigning a
    /// tier is done HERE, from the list, gated `customers.manage`.
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

        /// <summary>What "which member?" is asked from - the rows currently on screen.</summary>
        private System.Collections.Generic.IReadOnlyList<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto> _rows
            = Array.Empty<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>();

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

        /// <summary>Can this operator add a member? Drives the button's VISIBILITY as well as the
        /// gate, so a cashier without the grant is never shown a control that will refuse them.</summary>
        public bool MayAddCustomers =>
            Services.Security.TillGate.CheckAny(
                App.GetViewModel().SignedInOperator, null,
                PermissionCatalogue.PosCustomersAdd, PermissionCatalogue.CustomersManage).Allowed;

        /// <summary>Can this operator change a member's tier? Supervisor and up.</summary>
        public bool MayManageCustomers =>
            Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.CustomersManage).Allowed;

        private Command _addMemberCommand;
        public Command AddMemberCommand => _addMemberCommand ??= new Command(ExecuteAddMember);

        private Command _setTierCommand;
        public Command SetTierCommand => _setTierCommand ??= new Command(ExecuteSetTier);

        /// <summary>
        /// Sign a new member up — binding default 20, *"Till operator to add new loyalty members"*.
        ///
        /// ⚠⚠ **MOVED HERE FROM THE TILL SCREEN, 2026-08-18 (§5c items 4 and 6).** Matt: those buttons
        /// did not belong on the till screen — a member is attached by SCANNING THEIR CARD, and neither
        /// the web till nor NatApp has an add button there. But removing them left MAUI with **no way
        /// to add a member at all**, because this tab — the one place a member list exists — had none
        /// either. The web till puts add/edit on its LOYALTY page; so does this now.
        ///
        /// ⚠⚠ **NO TIER PICKER IN THIS DIALOG, AND THAT IS THE WEB TILL'S SCAR, NOT A SIMPLIFICATION.**
        /// Creating a member and setting their tier are two calls with two different permissions:
        /// `pos.customers.add` creates, `customers.manage` sets the tier. A dialog that offers both
        /// to a cashier gets **201 on the create and 403 on the tier** — an error message in front of
        /// a customer, for a member who HAS actually been added, whose natural retry creates a
        /// duplicate. So the tier is a separate, separately-gated action (`SetTierCommand`).
        ///
        /// ⚠ MANDATORY FIELDS ARE MARKED — Matt, 2026-08-18. Name carries a `RequiredValidator` AND a
        /// `*`; a required box that looks optional is a save that fails for a reason nobody can see.
        ///
        /// ⚠ ONLINE-ONLY, deliberately and permanently: the membership number comes from a
        /// tenant-wide counter, so two offline tills would mint the same one.
        ///
        /// ⚠ CREATE-ONLY. There is no edit path on this till at all — changing a member's email
        /// quietly redirects their account.
        /// </summary>
        private async void ExecuteAddMember()
        {
            if (IsBusy) return;

            var gate = Services.Security.TillGate.CheckAny(
                App.GetViewModel().SignedInOperator, null,
                PermissionCatalogue.PosCustomersAdd, PermissionCatalogue.CustomersManage);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return;
            }

            IsBusy = true;
            try
            {
                Helpers.Validators.IValidator[] required = { new Helpers.Validators.RequiredValidator() };

                CustomViews.Structs.ViewElementData[] elements =
                {
                    // ⚠ "Name *", not "Name" — see the header. The other two say "(optional)" for the
                    // same reason, from the other side.
                    //
                    // ⚠⚠ THE LAST ARGUMENT IS `IsEnabled`, AND EMAIL AND PHONE WERE PASSED `false`.
                    // Matt, on the first hand-run (2026-08-18): *"email and phone are not editable"* —
                    // they were **disabled boxes**, exactly as written. The value came straight from
                    // the till-screen original this was moved from, which nobody could ever reach
                    // because it answered 403 for every operator (item 6a): **a bug that had never
                    // been seen because the screen in front of it had never worked.**
                    //
                    // ⚠ `IsPassword` is the argument before it — that one is genuinely `false` here.
                    // The two booleans sit side by side and mean opposite kinds of thing; naming them
                    // at the call site is what stops the next person swapping them again.
                    new CustomViews.Structs.ViewElementData(1, "Name *", "", required.AsEnumerable(), isPassword: false, isEnabled: true),
                    new CustomViews.Structs.ViewElementData(2, "Email (optional)", "", new List<Helpers.Validators.IValidator>(), isPassword: false, isEnabled: true),
                    new CustomViews.Structs.ViewElementData(3, "Phone (optional)", "", new List<Helpers.Validators.IValidator>(), isPassword: false, isEnabled: true),
                };

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Add".Translate(), false, "New member", "Cancel".Translate());

                // ⚠ Backing out yields an EMPTY dictionary — all four exits agree on that since
                // 2026-08-18 (the ✕ used to blank fields to `""`, which the caller then parsed). So an
                // absent name means "changed their mind", not "typed nothing".
                answers.TryGetValue(1, out var name);
                if (string.IsNullOrWhiteSpace(name)) return;

                answers.TryGetValue(2, out var email);
                answers.TryGetValue(3, out var phone);

                // ⚠⚠ THE **OPERATOR'S** CLIENT, NOT THE TILL'S. `POST /api/v1/customers` is
                // `perm:`-gated, and `PermissionAuthorizationHandler` resolves RBAC by the token's
                // `NameIdentifier` — which on a DEVICE token is the device id, and a device holds no
                // grants. On the device client this answered 403 for every operator whatever their
                // role. Same fault as the Reports tab (1.75.0), in a second place.
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "A member can only be added while the till is online and somebody is signed in — their membership number comes from Plutus. Nothing has been saved.",
                        "OK".Translate());
                    return;
                }

                var (ok, id, memberNo, problem) = await api.CreateCustomerAsync(name, email, phone);

                if (!ok)
                {
                    // ⚠ SAY WHAT FAILED, and say nothing was saved. A create that reports nothing is
                    // indistinguishable from one that worked.
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "That member couldn't be added. Nothing has been saved.", "OK".Translate());
                    return;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Member added",
                    new Dictionary<string, string> { { "CustomerId", id.ToString() }, { "MemberNo", memberNo ?? "" } });

                await Application.Current.MainPage.DisplayAlert(
                    "Member".Translate(),
                    string.Format("{0} added. Membership number {1}.", name, memberNo ?? "(pending)")
                    + (MayManageCustomers ? "" : " " + "A supervisor can set their tier."),
                    "OK".Translate());

                // ⚠ Search for who was just added, rather than reloading the whole list. It proves the
                // member exists (this screen's only feedback that the write landed), and it leaves
                // them selected for Set tier — the operator's usual next action.
                Search = string.IsNullOrWhiteSpace(memberNo) ? name : memberNo;
                Refresh();
            }
            catch (Exception ex)
            {
                CrashLog.Write("LoyaltyViewModel.ExecuteAddMember", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That member couldn't be added. Nothing has been saved.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Set or change a member's tier — **Supervisor and up** (`customers.manage`).
        ///
        /// ⚠ ASKED AS TWO PICKERS (which member, then which tier) rather than by tapping a row.
        /// `TillTable` has no row-tap hook, and giving one to a control shared by Cash, Statistics,
        /// Inventory and this screen — in order to serve this screen — is a far bigger change than
        /// this needs. An action sheet is already the till's idiom for *"which one?"*.
        ///
        /// ⚠ THE TILL SENDS A TIER ID AND NOTHING ELSE. The tier owns its discount rate and renewal
        /// length, so re-rating "Gold" in the portal moves every Gold member at once instead of
        /// leaving a snapshot behind on whichever till assigned it. A till never types a rate.
        ///
        /// ⚠ INACTIVE TIERS ARE NOT OFFERED. A retired tier still exists so history reads correctly;
        /// putting somebody new on one would resurrect it.
        ///
        /// ⚠ The list is RE-READ afterwards rather than patched: the server decides the renewal date
        /// and the expiry verdict, and this screen must show the server's answer, not a guess at it.
        /// </summary>
        private async void ExecuteSetTier()
        {
            if (IsBusy) return;

            var gate = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.CustomersManage);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return;
            }

            // ⚠ Nothing on screen is a SEARCH instruction, not an error — the list is capped
            // server-side, so "no rows" usually means nobody has searched yet.
            if (_rows.Count == 0)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Search for a member first — this sets the tier of somebody already on the list.",
                    "OK".Translate());
                return;
            }

            IsBusy = true;
            try
            {
                // ⚠ Labelled with the membership number as well as the name. Two customers called
                // "J Smith" are ordinary, and putting the wrong one on Gold is money.
                var who = _rows
                    .Select(r => string.IsNullOrWhiteSpace(r.MemberNo)
                        ? (string.IsNullOrWhiteSpace(r.Name) ? "(no name)" : r.Name)
                        : $"{(string.IsNullOrWhiteSpace(r.Name) ? "(no name)" : r.Name)} · {r.MemberNo}")
                    .ToArray();

                var pickedWho = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Application.Current.MainPage.DisplayActionSheet(
                        "Whose tier?", "Cancel".Translate(), null, who));

                if (string.IsNullOrWhiteSpace(pickedWho) || pickedWho == "Cancel".Translate()) return;

                var whoIndex = Array.IndexOf(who, pickedWho);
                if (whoIndex < 0) return;

                var member = _rows[whoIndex];

                // ⚠⚠ THE OPERATOR'S CLIENT — `PUT /api/v1/customers/{id}` is gated on
                // `perm:customers.manage`, which a DEVICE token can never satisfy. See Add member.
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Tiers can only be changed while the till is online and somebody is signed in.",
                        "OK".Translate());
                    return;
                }

                var tiers = (await api.GetLoyaltyTiersAsync())?.Where(t => t.Active).ToList();

                // ⚠ Tiers are created in the PORTAL only (binding default 20). An empty list is a
                // configuration answer, not an error — and naming the portal stops an operator
                // hunting this device for a setting that does not exist on it.
                if (tiers is null || tiers.Count == 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "No membership tiers have been set up yet. They're created in the Plutus portal, under Loyalty — not on the till.",
                        "OK".Translate());
                    return;
                }

                var names = tiers
                    .Select(t => $"{t.Name} · {MemberDiscount.Label(t.Name, t.AutoDiscountRate)}")
                    .ToArray();

                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Application.Current.MainPage.DisplayActionSheet(
                        $"Tier for {(string.IsNullOrWhiteSpace(member.Name) ? "this member" : member.Name)}?",
                        "Cancel".Translate(), null, names));

                if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel".Translate()) return;

                var index = Array.IndexOf(names, picked);
                if (index < 0) return;

                var (ok, problem) = await api.SetMembershipAsync(member.Id, tiers[index].Id);

                if (!ok)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        problem ?? "That tier couldn't be set. Nothing has changed.", "OK".Translate());
                    return;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Tier set",
                    new Dictionary<string, string>
                    { { "CustomerId", member.Id.ToString() }, { "TierId", tiers[index].Id.ToString() } });

                // ⚠ Re-read, never patch locally — see the header.
                Refresh();
            }
            catch (Exception ex)
            {
                CrashLog.Write("LoyaltyViewModel.ExecuteSetTier", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "That tier couldn't be set.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

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
            // ⚠ SET BEFORE THE DISPATCH, always: this is what SetTierCommand asks "which member?"
            // from, so the picker can never offer somebody the screen has stopped showing.
            _rows = rows ?? Array.Empty<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _status.Text = status;
                OnPropertyChanged(nameof(MayAddCustomers));
                OnPropertyChanged(nameof(MayManageCustomers));
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
