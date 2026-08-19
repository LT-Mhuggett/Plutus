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
        #region Properties
        /// <summary>
        /// ⚠ THE LEGACY LOCAL STORE, KEPT ONLY BECAUSE OTHER SCREENS STILL BIND IT. Nothing on THIS
        /// screen reads it any more (2026-08-17): cutover step 20 made the platform the source of
        /// truth for store details, and on a portal-provisioned till this model is empty — which is
        /// exactly what Matt's screenshot showed, a grey band with nothing in it above the same
        /// facts fetched properly.
        /// </summary>
        public StoreModel Store
        {
            get => App.GetViewModel().Store;
        }
        #endregion

        public StoreInformationViewModel(VerticalStackLayout body)
        {
            Title = "StoreInformation".Translate();
            Icon = "md-store";

            // ⚠ NULL-SAFE, AND IT MUST STAY THAT WAY. This runs inside a CONSTRUCTOR that AppShell
            // invokes while it is being built, so a failure here does not degrade one tab — it
            // throws out of `new AppShell()` and the operator is told "Something went wrong signing
            // in" after typing a correct password. A screen that cannot render its own data shows
            // nothing; it must never be able to stop somebody signing in.
            //
            // ⚠ THE LOGO IS DROPPED (binding default 18, cutover step 20). There is no logo field on
            // the store-info contract, so a logo here could only ever have been THIS machine's local
            // opinion — differing from every other till, and printed on receipts as the company's.
            _body = body;

            // ⚠ The heading and the read-only note are the web till's own words, verbatim
            // (`StoreInformationPage.tsx`). Parity in what the operator READS is the point — Matt,
            // 2026-08-17: "MAUI looks nothing like the webtill."
            _body.Children.Add(Heading("Store Information", FontSizes.Title));
            _body.Children.Add(Muted(
                "Read-only here — edit these details in the management portal under Company and Locations."));

            _body.Children.Add(_cards);
            _body.Children.Add(_thisTill);

            LoadStoreDetails();
        }

        /// <summary>The page body, filled in code — see the XAML's header for why.</summary>
        private readonly VerticalStackLayout _body;

        /// <summary>
        /// The three cards — Business / Store / Opening hours — wrapping like the web till's
        /// `.info-cards` grid.
        ///
        /// ⚠ `FlexLayout Wrap` rather than a Grid reflowed from `OnSizeAllocated`, which is what this
        /// screen used to do. The platform already knows how to wrap; a size callback that moves
        /// children between cells re-enters on every resize.
        /// </summary>
        private readonly FlexLayout _cards = new()
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start,
        };

        /// <summary>Facts about THIS machine, not about the shop — kept visually separate for that
        /// reason, exactly as the web till separates them below its cards.</summary>
        private readonly VerticalStackLayout _thisTill = new() { Spacing = 6 };


        /// <summary>
        /// Re-read the store's details — what `LiveScreen` calls on appearing and on every tick.
        ///
        /// ⚠⚠ THIS SCREEN HAD NO WAY TO BE RE-READ AT ALL until 2026-08-18. `LoadStoreDetails` ran
        /// only from the constructor, and `AppShell` builds every tab up front, so a value corrected
        /// in the portal could not reach this till until somebody signed out and back in — §5c item 7,
        /// and pitfall 17 in its purest form. ⚠ A till that cannot be shown a corrected value is
        /// indistinguishable from a portal that never saved it, which is exactly the confusion the
        /// opening-hours report came out of.
        ///
        /// ⚠ Safe to call repeatedly: the load is fire-and-forget, off the UI thread, and swallows
        /// its own failures — see `LoadStoreDetails`.
        /// </summary>
        public void Refresh() => LoadStoreDetails();

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
                Guid? deviceId = null;
                try
                {
                    info = await Services.Storage.StoreInfoCache.RefreshAsync();
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("StoreInformationViewModel.LoadStoreDetails", ex);
                }

                try
                {
                    // ⚠ The web till shows its till id on this screen and MAUI showed nothing at all.
                    // It is the first thing anybody is asked for when a till misbehaves, and reading
                    // it off a support call beats hunting for it in the Plutus tab.
                    deviceId = (await Services.Connectivity.SecureDeviceCredentialStore.LoadAsync())?.DeviceId;
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("StoreInformationViewModel.LoadDeviceId", ex);
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _cards.Children.Clear();
                    _thisTill.Children.Clear();

                    if (info is null)
                    {
                        // ⚠ Says WHY, and says it is recoverable. "Unavailable" on its own reads as
                        // broken; this till simply has not been told yet.
                        _cards.Children.Add(Card("Store", new[]
                        {
                            Muted("Unavailable — this till hasn't been told its store details yet. "
                                + "It fills in on the next connection."),
                        }));
                    }
                    else
                    {
                        // ⚠ THE SAME THREE CARDS, IN THE SAME ORDER, WITH THE SAME FIELD LABELS as
                        // the web till's Store Information page. Somebody moving between a browser
                        // till and this one should not have to re-learn where a VAT number lives.
                        _cards.Children.Add(Card("Business", new View[]
                        {
                            Detail("Name", info.BusinessName),
                            Detail("VAT number", Dashless(info.VatNumber)),
                        }));

                        _cards.Children.Add(Card("Store", new View[]
                        {
                            Detail("Store name", info.Name),
                            Detail("Address", Services.Storage.StoreInfoCache.AddressOf(info)),
                            Detail("Contact number", Dashless(info.ContactNumber)),
                        }));

                        _cards.Children.Add(Card("Opening hours", OpeningHoursViews(info.OpeningHoursJson)));
                    }

                    // ⚠ Below the cards, like the web till: these identify the MACHINE, not the shop.
                    _thisTill.Children.Add(Detail("Store id",
                        info is null ? null : info.StoreId.ToString(CultureInfo.InvariantCulture)));
                    _thisTill.Children.Add(Detail("Till id",
                        deviceId is Guid id ? id.ToString() : "Not enrolled"));

                    // ⚠⚠ THE BAG SETTING MOVED TO **SETTINGS → TILL** (2026-08-18, §5c item 8). Matt
                    // listed *"'Choose bag item' in Store Information"* as one of ten findings, and it
                    // was misfiled rather than mysterious: **the web till keeps the same setting in
                    // Settings** (`prefs.ts bagBarcode`), and it belongs there. This screen is
                    // READ-ONLY and about the SHOP; which carrier bag this machine sells is about this
                    // machine. `SettingsViewModel.ChooseBagItemCommand` is the same code, moved.
                });
            });
        }

        /// <summary>⚠ The legacy tables store "not set" as a literal <c>-</c> or <c>N/A</c>, and
        /// printing those verbatim makes a blank field look like real data. Same rule as the web
        /// till, which filters `-` out of its address and contact fields.</summary>
        private static string Dashless(string value) =>
            string.IsNullOrWhiteSpace(value) || value.Trim() is "-" or "N/A" ? null : value;

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
        private static IReadOnlyList<View> OpeningHoursViews(string openingHoursJson)
        {
            var reading = Plutus.Client.Core.OpeningHours.Parse(openingHoursJson);

            if (reading.State == Plutus.Client.Core.OpeningHoursState.Unset)
                return new View[] { Muted("Not set — add opening hours in the management portal.") };

            if (reading.State == Plutus.Client.Core.OpeningHoursState.Unreadable)
                return new View[]
                {
                    // ⚠ It names the FIELD and the FAULT. Sending somebody back to the portal to
                    // retype hours that are already there, into the box that is already wrong, is
                    // what the old single message did.
                    Error($"The portal has opening hours for this store, but this till can't read "
                        + $"them: {reading.Detail}."),
                    Muted("Fix them in Locations → this store → Opening hours. If the advanced JSON "
                        + "box was used, switching back to the simple editor and re-ticking the days "
                        + "will rewrite it cleanly."),
                };

            var views = new List<View>();
            foreach (var day in reading.Week)
                views.Add(Detail(day.Label, Plutus.Client.Core.OpeningHours.DayText(day)));

            // ⚠ Named rather than dropped: a key nobody reads is a setting somebody thinks is in
            // effect. The web till says the same thing in the same place.
            var leftovers = Plutus.Client.Core.OpeningHours.UnknownDayKeys(openingHoursJson);
            if (leftovers.Count > 0)
                views.Add(Muted($"Ignored (not a day): {string.Join(", ", leftovers)}."));

            return views;
        }

        // ── the look ──────────────────────────────────────────────────────────
        //
        // ⚠⚠ EVERY COLOUR IS A **DYNAMIC** RESOURCE, and that is not a style preference. A theme set
        // in the portal is applied at runtime by `Services/Theming/Theming.cs`, which swaps the values
        // of these keys in `Application.Current.Resources`. A `TextColor = Colors.X` assignment — which
        // is what this screen used to do — captures the colour once and never follows a theme change,
        // so a themed till would show a half-themed screen.
        //
        // ⚠⚠ AND `Colors.LightGray` WAS THE ACTUAL BUG IN MATT'S SCREENSHOT: every field LABEL on this
        // screen was light grey on a near-white surface. The information was all there and none of it
        // was legible. Labels are `ThemeInkMuted` now — muted is a contrast step, not an invisibility
        // setting.

        private static class FontSizes
        {
            internal const double Title = 20;
            internal const double CardHeading = 15;
            internal const double Body = 14;
            internal const double Small = 12;
        }

        private static Label Heading(string text, double size)
        {
            var label = new Label { Text = text, FontSize = size, FontAttributes = FontAttributes.Bold };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInk");
            return label;
        }

        private static Label Muted(string text)
        {
            var label = new Label { Text = text, FontSize = FontSizes.Small };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            return label;
        }

        private static Label Error(string text)
        {
            var label = new Label { Text = text, FontSize = FontSizes.Small };

            // ⚠⚠ `ThemeDanger`, NOT `"Error"` (WP-T1 T1.3, 2026-08-19). `Error` is a palette key and
            // **not one of the seven portal slots**, so `Theming.Apply` can never move it — the colour
            // was frozen at `#FF9494` whatever scheme a shop set, and measured **2.12:1** on the dark
            // surface. An error message nobody can read is worse than no error message: the operator
            // concludes the screen is simply blank.
            //
            // ⚠ A `SetDynamicResource` to a key that does not exist applies NOTHING, silently — which is
            // why this class of miss survives every build and every XAML test. T1.4's guard is what
            // catches the next one.
            label.SetDynamicResource(Label.TextColorProperty, "ThemeDanger");
            return label;
        }

        /// <summary>
        /// One of the web till's `.info-card`s: an accent heading over label/value pairs, on a
        /// surface with a hairline border.
        /// </summary>
        private static View Card(string heading, IReadOnlyList<View> rows)
        {
            var stack = new VerticalStackLayout { Spacing = 6 };
            var title = Heading(heading, FontSizes.CardHeading);
            title.SetDynamicResource(Label.TextColorProperty, "ThemeAccent");
            stack.Children.Add(title);
            foreach (var row in rows) stack.Children.Add(row);

            var card = new Border
            {
                Content = stack,
                Padding = 12,
                Margin = new Microsoft.Maui.Thickness(0, 0, 12, 12),
                // ⚠ A minimum rather than a fixed width: three cards fit a desktop till side by side
                // and wrap on a narrow one, which is what the web till's grid does. A fixed width
                // would clip a long address on the smallest screen it has to work on.
                MinimumWidthRequest = 260,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            };
            card.SetDynamicResource(Border.BackgroundColorProperty, "ThemeSurface");
            card.SetDynamicResource(Border.StrokeProperty, "ThemeLineBrush");
            return card;
        }

        /// <summary>A label pair. ⚠ An empty value reads "Not set" rather than rendering blank — a
        /// blank row is indistinguishable from a binding to a property that does not exist, which is
        /// the failure mode MAUI hands you for free.</summary>
        private static View Detail(string label, string value)
        {
            var stack = new VerticalStackLayout();
            stack.Children.Add(Muted(label));

            var text = new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "Not set" : value,
                FontSize = FontSizes.Body,
            };
            text.SetDynamicResource(Label.TextColorProperty, "ThemeInk");
            stack.Children.Add(text);
            return stack;
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

        #endregion

        // ⚠ THE REGION AND EMPLOYEE COMMANDS ARE GONE (2026-08-17). Four `Command` properties —
        // currency, date, add-employee, view-all-employees — each wrapping an EMPTY `Execute` method,
        // bound by nothing since the buttons were commented out. A command that does nothing is worse
        // than no command: the next person to want "edit the currency" finds a property that looks
        // like the wiring already exists. Currency and date come from the platform's own settings;
        // employees are the portal's (Users), and the till reads the roster.
        #endregion

        #region Execute Commands
        #region Store Details
        #endregion
        #endregion
    }
}
