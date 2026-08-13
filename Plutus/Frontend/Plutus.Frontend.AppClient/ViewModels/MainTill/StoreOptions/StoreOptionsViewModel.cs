using CustomViews.Structs;
using Microsoft.Maui.Devices;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Database.Models;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.SharedKernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.StoreOptions
{
    public class StoreInformationViewModel : BaseViewModel
    {
        #region Fields
        private bool _displayLogo;
        private string _currencyFormat;
        private string _positiveCurrencyFormat;
        private string _negativeCurrencyFormat;
        #endregion

        #region Properties
        public StoreModel Store
        {
            get => App.GetViewModel().Store;
        }
        public bool DisplayLogo
        {
            get => _displayLogo;
            set => SetProperty(ref _displayLogo, value);
        }
        public CultureInfo CultureInfo
        {
            get => CultureInfo.CurrentCulture;
        }
        public string PositiveCurrencyDisplay
        {
            get => _positiveCurrencyFormat.Replace("n", _currencyFormat);
        }
        public string NegativeCurrencyDisplay
        {
            get => _negativeCurrencyFormat.Replace("n", _currencyFormat);
        }
        public string CurrencyDisplay
        {
            get => $"{PositiveCurrencyDisplay}/{NegativeCurrencyDisplay}";
        }
        #endregion

        public StoreInformationViewModel(StackLayout leftColumn, StackLayout rightColumn)
        {
            Title = "StoreInformation".Translate();
            Icon = "md-store";

            // ⚠ NULL-SAFE ON PURPOSE. This ran inside a CONSTRUCTOR that AppShell invokes while it
            // is being built, so a null store did not degrade one tab — it threw
            // NullReferenceException out of `new AppShell()` and the operator was told
            // "Something went wrong signing in" after typing a correct password. A screen that
            // cannot render its own data should show nothing; it must never be able to stop
            // somebody signing in.
            // ⚠ THE LOGO IS DROPPED (binding default 18, cutover step 20). There is no logo field
            // on the store-info contract, so a logo on this screen could only ever have been THIS
            // machine's local opinion — set by an edit command that no longer exists, differing
            // from every other till, and printed on receipts as though it were the company's. The
            // XAML `Image` is left in place and simply never shown: hiding it is a one-line change
            // whose effect I can reason about, whereas deleting an element from a layout I cannot
            // run is how a screen quietly loses its spacing.
            DisplayLogo = false;

            SetCurrencyDisplays();

            var buttonsAndSubHeadings = new List<Tuple<string, string>>
            {
                Tuple.Create("Store".Translate(), ""),
                // ⚠ Name / address / logo / phone / VAT number are no longer BUTTONS — the portal
                // owns them (WP6.1) and they are rendered read-only below. Only the bag, which is a
                // per-till preference rather than a company fact, is still editable here.
                Tuple.Create("Bag".Translate(), "StoreDefaultBagChangeCommand"),
                /*Tuple.Create("Region".Translate(), ""),
                Tuple.Create("Currency", "CurrencySettingsChangeCommand"),
                Tuple.Create("Date", "DateSettingsChangeCommand"),
                Tuple.Create("Employee".Translate(), ""),
                Tuple.Create($"{"Add".Translate()} {"Employee".Translate()}", "AddEmployeeCommand"),
                Tuple.Create(string.Format("ViewAllArg".Translate(), "Employees".Translate()), "ViewAllEmployeesCommand"),*/
                Tuple.Create("","")
            };

            StackLayout stack = null;
            int? n = null;
            for (int i = 0; i < buttonsAndSubHeadings.Count; i++)
            {
                if (buttonsAndSubHeadings[i].Item2 == "")
                {
                    if (n == null)
                        n = 0;
                    else
                    {
                        if (n % 2 == 0)
                            leftColumn.Children.Add(stack);
                        else
                            rightColumn.Children.Add(stack);
                        n++;
                    }
                    stack = new StackLayout();
                    stack.Children.Add(new Label
                    {
                        Text = buttonsAndSubHeadings[i].Item1,
                        FontSize = new Label().FontSize,
                        TextColor = Colors.LightGray,
                        FontAttributes = FontAttributes.Bold
                    });
                }
                else
                {
                    var button = new Button { Text = buttonsAndSubHeadings[i].Item1 };
                    button.SetBinding(Button.CommandProperty, buttonsAndSubHeadings[i].Item2);
                    stack.Children.Add(button);
                }
            }

            // ⚠ The store's own details, READ-ONLY, from the portal (cutover step 20). Added in
            // code rather than XAML because this whole screen is built in code — and because the
            // labels are filled from an async fetch, which a XAML binding to a missing property
            // would render as a silent blank.
            leftColumn.Children.Add(_storeDetails);
            LoadStoreDetails();
        }

        /// <summary>Where the read-only store details are rendered.</summary>
        private readonly StackLayout _storeDetails = new();

        /// <summary>
        /// Show what the PORTAL says this store is.
        ///
        /// ⚠ Last-good when offline, "unavailable" when this till has never been told — never the
        /// legacy local record, which is the thing step 20 exists to stop being authoritative.
        /// ⚠ Off the UI thread, and it cannot throw: this runs from a constructor `AppShell`
        /// invokes, and a details screen that fails must not be able to stop somebody signing in.
        /// </summary>
        private void LoadStoreDetails()
        {
            _ = Task.Run(async () =>
            {
                Plutus.Contracts.Client.StoreInfoResult info = null;
                try
                {
                    info = await Services.Storage.StoreInfoCache.RefreshAsync();
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("StoreInformationViewModel.LoadStoreDetails", ex);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _storeDetails.Children.Clear();

                    if (info is null)
                    {
                        _storeDetails.Children.Add(Detail("Store".Translate(),
                            "Unavailable — this till hasn't been told its store details yet."));
                        return;
                    }

                    _storeDetails.Children.Add(Detail("Name".Translate(), info.Name));
                    _storeDetails.Children.Add(Detail("Business", info.BusinessName));
                    _storeDetails.Children.Add(Detail("Address".Translate(),
                        Services.Storage.StoreInfoCache.AddressOf(info)));
                    _storeDetails.Children.Add(Detail("ContactNumber".Translate(), info.ContactNumber));
                    _storeDetails.Children.Add(Detail("VatIN".Translate(), info.VatNumber));
                    _storeDetails.Children.Add(OpeningHours(info.OpeningHoursJson));
                });
            });
        }

        /// <summary>
        /// The week, as the portal set it — finding Z2, 2026-08-13.
        ///
        /// ⚠⚠ WP6's DoD REQUIRED THIS AND STEP 20 WAS TICKED WITHOUT IT: *"rendering the per-day
        /// `openingHoursJson` as a read-only weekly table"*. Matt found it by looking: *"Opening hours
        /// is not reflected on the webtill or Maui."* The portal sets them, the server serves them and
        /// the web till renders them — MAUI had **zero references to `openingHours` anywhere**. A ⬜
        /// wearing a ✅, which is the failure the Part B register exists to catch.
        ///
        /// ⚠ THE SAME PARSE AS THE WEB TILL (`StoreInformationPage.tsx`): a map of day key → spans of
        /// `{open, close}`, where a missing or empty day means CLOSED — not "unknown". Keys are the
        /// portal's `mon`…`sun`, so this is a C2-shaped agreement about a data format rather than about
        /// money; the format lives in the portal's editor and both tills read it.
        ///
        /// ⚠ Unparseable JSON reads as "not set" rather than throwing. A store screen must never be the
        /// thing that takes the till down, and a malformed field is the portal's problem to fix.
        /// </summary>
        private static View OpeningHours(string openingHoursJson)
        {
            var stack = new StackLayout
            {
                Children =
                {
                    new Label
                    {
                        Text = "Opening hours",
                        FontSize = new Label().FontSize,
                        TextColor = Colors.LightGray,
                        FontAttributes = FontAttributes.Bold,
                    },
                },
            };

            Dictionary<string, List<OpeningSpan>> week = null;
            if (!string.IsNullOrWhiteSpace(openingHoursJson))
            {
                try
                {
                    week = System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, List<OpeningSpan>>>(openingHoursJson);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    Services.Analytics.CrashLog.Write("StoreOptions.OpeningHours", ex);
                }
            }

            if (week is null || week.Count == 0)
            {
                stack.Children.Add(new Label
                {
                    Text = "Not set — add opening hours in the management portal.",
                    TextColor = Colors.Gray,
                });
                return stack;
            }

            foreach (var (key, label) in Days)
            {
                var spans = week.TryGetValue(key, out var found) ? found : null;
                var text = spans is null || spans.Count == 0
                    ? "Closed"
                    : string.Join(", ", spans.Select(s => $"{s.open}–{s.close}"));

                stack.Children.Add(new Label { Text = $"{label}   {text}" });
            }

            return stack;
        }

        /// <summary>⚠ The portal's own keys, in the portal's own order — not `DayOfWeek`, which starts
        /// on Sunday and would silently reorder a shop's week.</summary>
        private static readonly (string Key, string Label)[] Days =
        {
            ("mon", "Monday"), ("tue", "Tuesday"), ("wed", "Wednesday"), ("thu", "Thursday"),
            ("fri", "Friday"), ("sat", "Saturday"), ("sun", "Sunday"),
        };

        /// <summary>One open period. ⚠ Lower-case members: these are the JSON's own names, and the
        /// portal writes `{"open":"09:00","close":"17:30"}`.</summary>
        private sealed class OpeningSpan
        {
            public string open { get; set; }
            public string close { get; set; }
        }

        /// <summary>A label pair. ⚠ An empty value reads "Not set" rather than rendering blank —
        /// a blank row is indistinguishable from a broken binding, which is exactly the failure
        /// mode MAUI hands you for free.</summary>
        private static View Detail(string label, string value) => new StackLayout
        {
            Children =
            {
                new Label { Text = label, FontSize = new Label().FontSize, TextColor = Colors.LightGray, FontAttributes = FontAttributes.Bold },
                new Label { Text = string.IsNullOrWhiteSpace(value) ? "Not set" : value },
            },
        };

        private void SetCurrencyDisplays()
        {
            CultureInfo.NumberFormat.CurrencyGroupSizes.ForEach((group) =>
            {
                _currencyFormat += new string('#', group);
                _currencyFormat += CultureInfo.NumberFormat.CurrencyGroupSeparator;
            });
            _currencyFormat +=
                $"{new string('#', CultureInfo.NumberFormat.CurrencyGroupSizes[0])}" +
                $"{CultureInfo.NumberFormat.CurrencyDecimalSeparator}" +
                $"{new string('#', CultureInfo.NumberFormat.CurrencyDecimalDigits)}";

            _negativeCurrencyFormat = (-1).ToString("C0", CultureInfo.NumberFormat).Replace('1', 'n');
            _positiveCurrencyFormat = 1.ToString("C0", CultureInfo.NumberFormat).Replace('1', 'n');
        }

        #region Commands
        #region Store Details

        // ⚠ THE FIVE STORE-DETAIL EDIT COMMANDS ARE GONE (cutover step 20, WP6.1).
        //
        // The till used to edit its shop's NAME, ADDRESS, LOGO, PHONE and VAT NUMBER straight into
        // the legacy local database. Three things were wrong with that, in rising order:
        //
        //   1. The PORTAL is the source of truth for store details (binding default 9). A till
        //      writing them locally means the shop's own VAT number can differ on every till in the
        //      estate, and the one printed on a receipt is whichever machine happened to print it.
        //      Nobody finds that until an inspection.
        //   2. Each command gated on `empId.IsAuthorised(...)`, the legacy `AuthActions` lookup a
        //      portal-provisioned till has no table for, via `App.GetViewModel().EmployeeId`, which
        //      is null for every roster operator.
        //   3. Each fell back to `Authorisation.RequestAuthorisedUserInput`, which never assigns the
        //      id it returns and therefore re-prompts for ever (see `TillGate`'s header).
        //
        // So on a portal till they could not work, and where they could they wrote the wrong thing
        // to the wrong place. The screen is READ-ONLY now, off `StoreInfoCache`.
        //
        // The LOGO went with them (binding default 18): there is no logo field on the store-info
        // contract, so a locally-set one could only ever have been this machine's opinion.

        Command _storeDefaultBagChangeCommand;
        public Command StoreDefaultBagChangeCommand
        {
            get => _storeDefaultBagChangeCommand ?? (_storeDefaultBagChangeCommand = new Command(ExecuteStoreDefaultBagChange));
        }
        #endregion
        #region Region
        Command _currencySettingsChangeCommand;

        public Command CurrencySettingsChangeCommand
        {
            get => _currencySettingsChangeCommand ?? (_currencySettingsChangeCommand = new Command(ExecuteCurrencySettingsChange));
        }

        Command _dateSettingsChangeCommand;

        public Command DateSettingsChangeCommand
        {
            get => _dateSettingsChangeCommand ?? (_dateSettingsChangeCommand = new Command(ExecuteDateSettingsChange));
        }
        #endregion
        #region Employee
        Command _addEmployeeCommand;

        public Command AddEmployeeCommmand
        {
            get => _addEmployeeCommand ?? (_addEmployeeCommand = new Command(ExecuteAddEmployee));
        }

        Command _viewAllEmployees;

        public Command ViewAllEmployeeCommand
        {
            get => _viewAllEmployees ?? (_viewAllEmployees = new Command(ExecuteViewAllEmployees));
        }
        #endregion
        #endregion

        #region Execute Commands
        #region Store Details
        /// <summary>
        /// Which item the till's quick "Bag" button rings up. The web till has the same setting
        /// (`prefs.ts bagBarcode`), and like it this is a per-DEVICE preference: which carrier bag a
        /// shop sells is a shop-floor fact, not a platform one.
        ///
        /// ⚠ It was on the legacy gate and the legacy catalogue, so on a portal-provisioned till it
        /// could not work in three separate ways: `IsAuthorised` crashed the app rather than
        /// refusing, `RequestAuthorisedUserInput` re-prompted for ever if it hadn't, and the
        /// existence check ran against the legacy `Items` table — empty on a portal till, so a
        /// perfectly good barcode came back as "we can't find an item with that ID".
        /// </summary>
        private async void ExecuteStoreDefaultBagChange()
        {
            try
            {
                var gate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosSettingsManage);

                if (!gate.Allowed)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                var validators = new IValidator[] { new RequiredValidator() };

                var viewElements = new ViewElementData[]
                {
                    new ViewElementData(1, string.Format("IdArg".Translate(), "Bag".Translate()), DefaultBagId, validators.AsEnumerable(), false, true),
                };

                var data = await Helpers.CustomViews.InputAlertHelper.LaunchInputAlertAsync(
                    viewElements, "Confirm".Translate(), false, cancelText: "Cancel".Translate());

                if (data == null || data.Any(d => string.IsNullOrEmpty(d.Value))) return;

                _ = data.TryGetValue(1, out var bagIdText);
                if (string.IsNullOrWhiteSpace(bagIdText)) return;

                // ⚠ Checked against the V2 CATALOGUE — the same place the basket resolves a scan.
                // Validating against a different list than the one that sells is how a setting is
                // accepted here and fails at the counter.
                var exists = await Services.Storage.TillStoreAccess.TryUseAsync(
                    s => s.FindByBarcodeAsync(bagIdText.Trim()));

                if (exists == null)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "ItemNotFoundMesg".Translate(), "OK".Translate());
                    return;
                }

                DefaultBagId = bagIdText.Trim();
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — see Authorisation's header.
                Services.Analytics.CrashLog.Write("StoreOptionsViewModel.ExecuteStoreDefaultBagChange", ex);
            }
        }
        #endregion
        #region Region
        private void ExecuteCurrencySettingsChange()
        {

        }

        private void ExecuteDateSettingsChange()
        {

        }
        #endregion
        #region Employee
        private void ExecuteAddEmployee()
        {

        }
        private void ExecuteViewAllEmployees()
        {

        }
        #endregion
        #endregion

        #region Operation
        #endregion
    }
}
