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
    /// ⚠ THE MEMBER DESK: look somebody up, sign somebody up, correct their details, set their tier.
    /// Tiers themselves are created in the **portal only** (binding default 20).
    ///
    /// ⚠⚠ ITS COLUMNS AND ITS ACTIONS ARE THE WEB TILL'S — Matt, 2026-08-18: *"Ensure the webtill and
    /// maui are inline."* **Add member** for `pos.customers.add`, and **Edit** — which MAUI simply did
    /// not have — for `customers.manage`. ⚠ Edit is a **row tap** rather than a per-row button: the web
    /// till has room for one and a till screen does not. Parity in FUNCTIONALITY, not in how the
    /// function operates (Matt, 2026-08-17).
    ///
    /// ⚠⚠ EIGHT COLUMNS, EACH WITH A WIDTH — *"The MAUI till is all over the place!"* (2026-08-18, with
    /// a screenshot). Email is **its own column** rather than a second line under the name, **Created**
    /// was added, and every column now declares a weight: without one the text columns split the whole
    /// row and the numeric ones were crushed into what was left. Both tills carry the same eight.
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
                Columns(), search: null,
                // ⚠ The web till's own wording, verbatim - two tills that describe the same empty
                // state differently read as two different products.
                emptyText: "No members or credit holders yet.",
                // ⚠⚠ TAP A ROW TO OPEN THE CUSTOMER — the portal's dialog on a till: their details,
                // their credit, and their whole history, with **Edit details** and **Grant credit** on
                // it. A tap used to go straight to the edit box (WP-L1, §5d).
                onRowTap: ExecuteOpenCustomer);

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
        /// ⚠⚠ NO LONGER CREATE-ONLY — see `ExecuteEditMember`. This said *"there is no edit path on
        /// this till at all — changing a member's email quietly redirects their account"*, and Matt's
        /// 2026-08-18 ruling retired the reason: the identity is `Customer.Id`, **nothing resolves a
        /// customer by email**, so an edit redirects nothing — and the server now records `before` and
        /// `after` on each one. Edit stays `customers.manage`; adding is still the wider gate.
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
        /// Open a customer — **everything the portal shows**, from a row tap (WP-L1, §5d).
        ///
        /// ⚠⚠ MATT, 2026-08-18: *"I need to be able to see all the information you see in the portal
        /// on both MAUI and the webtill."* A tap used to go straight to the edit box; it now opens the
        /// customer, and **Edit details** is a button on it — exactly where the portal puts it.
        ///
        /// ⚠ THE FACTS COME FROM THE ROW ALREADY ON SCREEN, so the dialog opens instantly and shows
        /// something even when the history cannot be read. Only the history is fetched.
        ///
        /// ⚠ NOT `async void` at the gesture: `TillTable` calls this on the UI thread, and an escaping
        /// exception from an `async void` goes to the dispatcher unhandled — which kills the till.
        /// </summary>
        private void ExecuteOpenCustomer(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row)
        {
            if (row is null || IsBusy) return;
            _ = OpenCustomerAsync(row);
        }

        private async Task OpenCustomerAsync(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row)
        {
            // ⚠ What the operator asked for on the way out of the detail dialog, acted on AFTER the
            // `finally` below releases `IsBusy` — see the comment where it is set.
            var chosen = Helpers.CustomViews.CustomerDetailHelper.Outcome.Closed;

            IsBusy = true;
            try
            {
                Plutus.Client.Core.PlutusApiClient.CustomerHistoryPage history = null;

                // ⚠ THE OPERATOR'S CLIENT — the history is `perm:`-gated like everything else here, and
                // a device token's `NameIdentifier` is the device id, which holds no grants (item 6a).
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api != null) history = await api.GetCustomerHistoryAsync(row.Id);

                // ⚠ A NULL HISTORY IS PASSED THROUGH, NOT SUPPRESSED. The dialog says it could not be
                // read; rendering "no history" for a customer who has traded for years is a confident
                // wrong statement, and an operator would then grant credit believing none was ever given.
                var outcome = await Helpers.CustomViews.CustomerDetailHelper.ShowAsync(
                    row, history,
                    mayEdit: MayManageCustomers,
                    mayGrantCredit: MayManageCustomers,
                    // ⚠ NOT A PERMISSION — just whether this till has an agent to print through. A
                    // Cashier may print somebody their card; a till with no printer cannot.
                    mayPrintCard: Services.Printing.TillAgentPrinting.Paired);

                // ⚠⚠ THE FOLLOW-UP IS DISPATCHED **OUTSIDE** THIS `try`, AND THAT IS THE WHOLE FIX
                // (2026-08-19). Matt, on 1.100.0: *"in Loyalty, when I try to edit details or grant
                // credit, the screen just closes."*
                //
                // Both `ExecuteEditMember` and `ExecuteGrantCredit` open with
                // `if (row is null || IsBusy) return;` — and THIS method still held `IsBusy = true`
                // until its `finally`. So the detail dialog closed exactly as designed, the follow-up
                // was invoked, its own guard saw the flag its own caller was holding, and it returned
                // **silently**. Nothing opened, nothing threw, nothing logged.
                //
                // ⚠ The guards are right and stay: they are what stops a double-tap opening two
                // dialogs. What was wrong is dispatching a guarded action from inside the guard, so the
                // decision is recorded here and acted on after the flag has been released.
                chosen = outcome;
            }
            catch (Exception ex)
            {
                CrashLog.Write("LoyaltyViewModel.OpenCustomer", ex);
                try
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That customer couldn't be opened.", "OK".Translate());
                }
                catch (Exception inner) { CrashLog.Write("LoyaltyViewModel.OpenCustomer.alert", inner); }
            }
            finally
            {
                IsBusy = false;
            }

            // ⚠ The detail dialog has closed and `IsBusy` is clear, so each of these can take the flag
            // for itself. ⚠⚠ Two Mopups pages still cannot stack, which is why the dialog closes before
            // any of this rather than after — that part of the original design was correct.
            switch (chosen)
            {
                case Helpers.CustomViews.CustomerDetailHelper.Outcome.Edit:
                    ExecuteEditMember(row);
                    break;

                case Helpers.CustomViews.CustomerDetailHelper.Outcome.GrantCredit:
                    ExecuteGrantCredit(row);
                    break;

                // ⚠ Awaited rather than fire-and-forget: it is a `Task`, and an unobserved exception
                // from it would be lost. ⚠ It does NOT guard on `IsBusy`, which is why printing was the
                // one action of the three that still worked — worth knowing, because it means the
                // symptom pointed at the guard rather than at the dialog.
                case Helpers.CustomViews.CustomerDetailHelper.Outcome.PrintCard:
                    await PrintCardAsync(row);
                    break;
            }
        }


        /// <summary>
        /// Print a customer their membership card (WP-L1c, §5d).
        ///
        /// ⚠⚠ MATT, 2026-08-18: *"Need to be able to print the card from the till."* — then *"Build for
        /// both"*, so the web till prints one too.
        ///
        /// ⚠⚠ **WHAT COMES OUT IS NOT THE SAME OBJECT ON BOTH TILLS, AND THAT IS DELIBERATE.** The
        /// portal and the web till render a **CR80 card** (85.6 × 54 mm) and send it to an ordinary
        /// printer, because a browser can. **MAUI's printer is the thermal receipt printer on the
        /// counter** — so it prints a scannable membership **slip**. Same Code 39, same `C`-prefixed
        /// payload, so it scans as a MEMBER on any till. Pretending a thermal printer can produce a
        /// plastic card would be the lie; parity is in what the customer can do with it.
        ///
        /// ⚠ NO PERMISSION GATE. Handing somebody their own card is counter work — a Cashier is who is
        /// standing in front of them.
        /// </summary>
        private async Task PrintCardAsync(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.MemberNo)) return;

            var ok = await Services.Printing.MemberCardPrint.PrintAsync(
                row.Name, row.MemberNo, row.Tier, row.RenewalDay);

            // ⚠ SAY WHICH WAY IT WENT. A print that silently fails is a customer sent away without the
            // card they were promised, and the operator finds out only when they come back.
            if (ok)
            {
                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Member card printed",
                    new Dictionary<string, string> { { "CustomerId", row.Id.ToString() } });
                return;
            }

            await Application.Current.MainPage.DisplayAlert("Not printed",
                "That card couldn't be printed. Check the receipt printer is on and the till agent is paired "
                + "(Settings → Hardware).",
                "OK".Translate());
        }
        /// <summary>
        /// Put credit on a customer's account.
        ///
        /// ⚠⚠ SUPERVISOR AND ABOVE — Matt, 2026-08-18: *"Granting credit needs to be supervisor and
        /// above."* That was **already true** and is asserted rather than newly imposed:
        /// `POST /customers/{id}/credit/issue` is gated `customers.manage`, `RbacSeeder` gives
        /// Supervisor that code, and a Cashier holds only `pos.sell` + `pos.customers.add`. The gate
        /// here matches the server's so the refusal happens before the round trip, not after it.
        ///
        /// ⚠⚠ THE REASON IS MANDATORY AND IS NEVER SUBSTITUTED. The endpoint refuses a blank one
        /// (backend 1.17.4) because credit granted with a reason nobody typed shows a plausible word
        /// in the history that means nothing — worse than a blank, because it READS as an audit trail.
        /// Marked `*` here so the operator finds out before the round trip.
        ///
        /// ⚠ ONLINE-ONLY: the balance is the server's, and a till has no business inventing one.
        /// </summary>
        private async void ExecuteGrantCredit(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row)
        {
            if (row is null || IsBusy) return;

            var gate = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.CustomersManage);

            if (!gate.Allowed)
            {
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                return;
            }

            IsBusy = true;
            try
            {
                const NumberStyles money = NumberStyles.AllowCurrencySymbol | NumberStyles.AllowThousands
                    | NumberStyles.AllowDecimalPoint;

                Helpers.Validators.IValidator[] amount =
                {
                    new Helpers.Validators.RequiredValidator(),
                    new Helpers.Validators.CurrencyValueValidator(money),
                };

                Helpers.Validators.IValidator[] required = { new Helpers.Validators.RequiredValidator() };

                CustomViews.Structs.ViewElementData[] elements =
                {
                    new CustomViews.Structs.ViewElementData(
                        1, "Amount *", "", amount.AsEnumerable(), isPassword: false, isEnabled: true),
                    new CustomViews.Structs.ViewElementData(
                        2, "Reason *", "", required.AsEnumerable(), isPassword: false, isEnabled: true),
                };

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Grant".Translate(), true,
                    $"Grant credit to {(string.IsNullOrWhiteSpace(row.Name) ? "this customer" : row.Name)}",
                    "Cancel".Translate());

                // ⚠ An empty result is "they backed out" — all four exits agree since 2026-08-18.
                answers.TryGetValue(1, out var typedAmount);
                answers.TryGetValue(2, out var reason);

                if (string.IsNullOrWhiteSpace(typedAmount) || string.IsNullOrWhiteSpace(reason)) return;

                var pence = Plutus.SharedKernel.Pence.FromDecimal(
                    decimal.Parse(typedAmount, money, CultureInfo.CurrentCulture));

                // ⚠ NOT NEGATIVE, NOT ZERO. Taking credit AWAY is not a grant — it is a redemption or
                // an expiry, both of which have their own paths and their own reasons.
                if (pence <= 0)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Credit must be more than nothing. Use a refund to take money back.", "OK".Translate());
                    return;
                }

                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Credit can only be granted while the till is online and somebody is signed in. Nothing has been added.",
                        "OK".Translate());
                    return;
                }

                // ⚠ THE ENTRY ID IS MINTED ONCE, HERE — it is what makes the grant idempotent, so a
                // retry of the same attempt cannot credit the account twice.
                var (ok, problem) = await api.IssueCreditAsync(row.Id, pence, reason, Uuid7.New());

                if (!ok)
                {
                    await Application.Current.MainPage.DisplayAlert("Not granted",
                        problem ?? "That credit couldn't be added, so nothing has changed.", "OK".Translate());
                    return;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Credit granted",
                    new Dictionary<string, string>
                    { { "CustomerId", row.Id.ToString() }, { "AmountPence", pence.ToString() } });

                await Application.Current.MainPage.DisplayAlert("Credit added",
                    $"{(pence / 100m).ToString("C2", CultureInfo.CurrentCulture)} added to {row.Name}. "
                    + "The reason is on their history.",
                    "OK".Translate());

                Refresh();
            }
            catch (Exception ex)
            {
                CrashLog.Write("LoyaltyViewModel.ExecuteGrantCredit", ex);
                try
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That credit couldn't be added. Nothing has changed.", "OK".Translate());
                }
                catch (Exception inner) { CrashLog.Write("LoyaltyViewModel.ExecuteGrantCredit.alert", inner); }
            }
            finally
            {
                IsBusy = false;
            }
        }
        /// <summary>
        /// Change a member's details — **tap their row**.
        ///
        /// ⚠⚠ MAUI HAD NO EDIT AT ALL AND THE WEB TILL HAS HAD ONE SINCE IT WAS WRITTEN (its Loyalty
        /// page carries a per-row **Edit** button gated `customers.manage`). Matt, 2026-08-18: *"Ensure
        /// the webtill and maui are inline."*
        ///
        /// ⚠⚠ IT WAS ABSENT ON PURPOSE, AND THE RULING REMOVED THE REASON. `PlutusApiClient`'s own
        /// header said the client *"deliberately exposes no customer edit"* because changing an email
        /// was thought to redirect somebody's account. Matt, 2026-08-18: *"A customer needs to have a
        /// unique ID, because people can change emails over time. Audit please."* The id is
        /// `Customer.Id`, a UUIDv7, and **nothing resolves a customer by email** — so an edit cannot
        /// redirect anything, and the server records `before` and `after` on every one.
        ///
        /// ⚠ A ROW TAP RATHER THAN A BUTTON COLUMN — the web till has room for a per-row action; a
        /// till screen does not. `TillTable`'s tap hook is opt-in (added for the sale drill-down) and
        /// hand-run 1 confirmed a gesture really does fire inside a `ViewCell`, which is what makes
        /// this the cheap option rather than a gamble.
        ///
        /// ⚠ THE TIER IS NOT IN THIS DIALOG, unlike the web till's. `InputAlert` has no picker, and
        /// `SetTierCommand` already does that job with its own permission — the web till's own comment
        /// explains why create-then-tier must stay two separately-gated calls. Same capability, one
        /// more tap. **Functional parity, not identical interaction** (Matt, 2026-08-17).
        ///
        /// ⚠ CONTACT DETAILS ONLY. `PUT /api/v1/customers/{id}` carries name, email and phone; a
        /// membership number is minted centrally and is never editable anywhere.
        /// </summary>
        private async void ExecuteEditMember(Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto row)
        {
            if (row is null || IsBusy) return;

            // ⚠ SILENT WHEN NOT PERMITTED. The whole table is tappable, so a cashier brushing a row
            // must not be told off for a control they were never offered — the web till simply does
            // not render its Edit button for them.
            if (!MayManageCustomers) return;

            IsBusy = true;
            try
            {
                Helpers.Validators.IValidator[] required = { new Helpers.Validators.RequiredValidator() };

                // ⚠⚠ `prefillWithPlaceholder: true` IS WHAT PUTS THE CURRENT VALUES IN THE BOXES AS
                // REAL, EDITABLE TEXT. Without it they render as grey hint text, and `InputResults` is
                // seeded from `entry.Text` — so an operator who changed only the phone would submit an
                // EMPTY name and the save would be refused with no message. That is finding K, *"I can
                // ONLY change the tax"* (2026-08-10 and again 2026-08-11), and this is the first edit
                // form written since it was fixed.
                CustomViews.Structs.ViewElementData[] elements =
                {
                    new CustomViews.Structs.ViewElementData(
                        1, "Name *", row.Name ?? "", required.AsEnumerable(),
                        isPassword: false, isEnabled: true, prefillWithPlaceholder: true),
                    new CustomViews.Structs.ViewElementData(
                        2, "Email", row.Email ?? "", new List<Helpers.Validators.IValidator>(),
                        isPassword: false, isEnabled: true, prefillWithPlaceholder: true),
                    new CustomViews.Structs.ViewElementData(
                        3, "Phone", row.Phone ?? "", new List<Helpers.Validators.IValidator>(),
                        isPassword: false, isEnabled: true, prefillWithPlaceholder: true),
                };

                var answers = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    elements, "Save".Translate(), true,
                    string.IsNullOrWhiteSpace(row.Name) ? "Edit member" : $"Edit {row.Name}",
                    "Cancel".Translate());

                // ⚠ An empty result is "they backed out" — all four exits agree since 2026-08-18.
                answers.TryGetValue(1, out var name);
                if (string.IsNullOrWhiteSpace(name)) return;

                answers.TryGetValue(2, out var email);
                answers.TryGetValue(3, out var phone);

                // ⚠ THE OPERATOR'S CLIENT — `PUT /api/v1/customers/{id}` is `perm:`-gated, and a device
                // token's `NameIdentifier` is the device id, which holds no grants (item 6a).
                var api = await Services.Connectivity.PlutusApi.GetOperatorAsync();
                if (api is null)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "A member can only be changed while the till is online and somebody is signed in. Nothing has been changed.",
                        "OK".Translate());
                    return;
                }

                var (ok, problem) = await api.UpdateCustomerAsync(row.Id, name, email, phone);

                if (!ok)
                {
                    await Application.Current.MainPage.DisplayAlert("Not changed",
                        problem ?? "That change couldn't be saved, so nothing has changed.", "OK".Translate());
                    return;
                }

                Logger.LogEvent(AppLogLevel.Info, $"{GetType().Name}: Member edited",
                    new Dictionary<string, string> { { "CustomerId", row.Id.ToString() } });

                Refresh();
            }
            catch (Exception ex)
            {
                CrashLog.Write("LoyaltyViewModel.ExecuteEditMember", ex);
                try
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "That change couldn't be saved. Nothing has changed.", "OK".Translate());
                }
                catch (Exception inner) { CrashLog.Write("LoyaltyViewModel.ExecuteEditMember.alert", inner); }
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
                    Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
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
                    Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
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
        /// The columns.
        ///
        /// ⚠⚠ **EVERY COLUMN CARRIES A WIDTH, AND THAT IS THE FIX FOR "ALL OVER THE PLACE"** (Matt,
        /// 2026-08-18, with a screenshot). `TillTable`'s old rule was *numeric ⇒ size to content,
        /// everything else ⇒ share what is left*. Fine for three columns; at six the three text
        /// columns split the entire row between them and **Discount, Renews and Credit were crushed
        /// into the remainder** — the headers ran together and the dashes under them touched.
        ///
        /// ⚠ Weights, never pixels: a till runs windowed, full-screen and on a small terminal, and a
        /// column measured in pixels is right on exactly one of them.
        ///
        /// ⚠ **EMAIL IS ITS OWN COLUMN** — Matt asked for it, and he is right. It was a second line
        /// under the name (copied from the web till, which renders it that way), which made every row
        /// double height and left the name column looking oddly empty on rows with no email.
        ///
        /// ⚠ **CREATED** answers *"is this a regular, or did they sign up last week?"* — and it is the
        /// only column that distinguishes a member with no tier and no credit from any other.
        ///
        /// ⚠ Numeric columns right-align and sort as NUMBERS. Ordered as text, £100 comes before £9.
        ///
        /// ⚠ The dates are shown exactly as the server sent them (`yyyy-MM-dd`) rather than reformatted
        /// per culture — they sort correctly as text that way, and one clock decides what day a thing
        /// happened on.
        /// </summary>
        private static Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>[] Columns() => new[]
        {
            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Customer",
                r => string.IsNullOrWhiteSpace(r.Name) ? "(no name)" : r.Name,
                Width: 3),

            // ⚠ ITS OWN COLUMN NOW, not a second line under the name.
            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Email",
                r => string.IsNullOrWhiteSpace(r.Email) ? "—" : r.Email,
                Width: 4),

            // ⚠ "—" NOT BLANK, throughout. A blank cell reads as a screen that failed to load; a dash
            // is an answer. (The web till renders the same `<span className="muted">—</span>`.)
            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Member no.",
                r => string.IsNullOrWhiteSpace(r.MemberNo) ? "—" : r.MemberNo,
                Width: 2),

            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Tier",
                // ⚠ AN EXPIRED MEMBERSHIP SAYS SO. "Gold" beside a lapsed member is an operator
                // promising a discount the till will not give. `Expired` is the SERVER's verdict,
                // never re-derived from a renewal date against this till's clock.
                r => string.IsNullOrWhiteSpace(r.Tier)
                    ? "—"
                    : r.Expired ? $"{r.Tier} (expired)" : r.Tier,
                SortText: r => r.Tier ?? string.Empty,
                Width: 2),

            // ⚠ The TIER's current rate, not a snapshot from when it was assigned — re-rating "Gold"
            // in the portal moves every Gold member at once.
            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Discount",
                r => r.AutoDiscountRate is decimal rate && rate > 0m
                    ? $"{System.Math.Round(rate * 100m)}%"
                    : "—",
                Numeric: true,
                SortNumber: r => (long)System.Math.Round((r.AutoDiscountRate ?? 0m) * 10000m),
                Width: 2),

            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Renews",
                r => string.IsNullOrWhiteSpace(r.RenewalDay) ? "—" : r.RenewalDay,
                Width: 2),

            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Created",
                r => string.IsNullOrWhiteSpace(r.CreatedAtUtc) ? "—" : r.CreatedAtUtc,
                Width: 2),

            new Controls.TableColumn<Plutus.Client.Core.PlutusApiClient.LoyaltyRowDto>(
                "Credit",
                r => r.CreditBalancePence == 0
                    ? "—"
                    : (r.CreditBalancePence / 100m).ToString("C2", CultureInfo.CurrentCulture),
                Numeric: true,
                SortNumber: r => r.CreditBalancePence,
                Width: 2),
        };
    }
}
