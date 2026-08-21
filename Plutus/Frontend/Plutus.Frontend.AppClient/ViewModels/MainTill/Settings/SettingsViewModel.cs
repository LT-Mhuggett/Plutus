using CommonPOSLibrary.Exceptions;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using CustomViews.Structs;
using Plutus.Client.Core;
using Plutus.Frontend.AppClient.Helpers.Compatibility;
using Plutus.Frontend.AppClient.Helpers.Validators;
using Plutus.Frontend.AppClient.Helpers.Extensions;
using Plutus.Frontend.AppClient.Helpers.Security;
using Plutus.Frontend.AppClient.Services.Analytics;
using Plutus.Frontend.AppClient.Services.IOHandeling;
using Plutus.Frontend.AppClient.Services.POSHandeling;
using Plutus.Client.Storage;
using Plutus.SharedKernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.ViewModels.MainTill.Settings
{
    public class SettingsViewModel : BaseViewModel
    {
        #region Private Fields
        private string _version;
        #endregion

        #region Properties
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }
        #endregion

        public SettingsViewModel(VerticalStackLayout column)
        {
            Title = "Settings".Translate();
            Icon = "md-settings";
            Version = $"Ver. {AppInfo.VersionString}";

            // ⚠ THE "DATABASE" SECTION IS GONE — Matt, 2026-08-10: *"all the archive legacy database
            // and restore. Its no longer needed."*
            //
            // "Archive legacy database" was the cutover on-ramp: it stamped `MetaKeys
            // .LegacyArchivedAtUtc`, which is what `EnrolmentFlow.BlockedReasonAsync` looks for
            // before letting a till with an un-archived legacy file enrol. That gate is passed
            // `null` today and does not run, so removing this button changes nothing that works
            // — but it does mean the gate can never be switched ON, because nothing else can
            // produce the stamp. ⚠ If a real shop is ever migrated off NatApp, its sales history
            // has no on-ramp until something replaces this. Recorded in `Build/To do/MAUI-retrofit.md` §10.
            //
            // "Restore database" was worse than unused: it ran the legacy `IsAuthorised` gate, so on
            // a portal-provisioned till it crashed the app rather than refusing (same fault as
            // "Change printer", below), and what it restored was a legacy file no screen reads any
            // more.
            // ⚠⚠ ONE COLUMN OF COLLAPSIBLE SECTIONS, as the web till has — see `BuildSections`.
            // This was ~90 lines of tuple list plus a loop that dealt sections left/right by
            // arrival order, so "Printer" could land beside "Till device" with no relationship
            // between them and the reading order changed with the window width.
            BuildSections(column);
        }

        /// <summary>
        /// Build the Settings page as the WEB TILL builds it — Matt, 2026-08-21: *"Can the settings
        /// screen in MAUI be made to look like the webtill please, so its consistent."*
        ///
        /// ⚠⚠ SAME SECTIONS, SAME ORDER, SAME ONE-LINE DESCRIPTIONS, ALL CLOSED. `SettingsPage.tsx`
        /// has **Till · Checkout · Printer · Hardware · Database · Till device · Environment**, each a
        /// `&lt;details&gt;` with a description in its `&lt;summary&gt;`, closed by default *"so the page reads as
        /// a table of contents"*. The section NAMES were aligned on 2026-08-18 and nothing else was:
        /// this screen was a flat, always-open, two-column list of bare bold headings.
        ///
        /// ⚠ **Help** is MAUI-only and last. The web till reaches support from a **❓ in the app bar**,
        /// which MAUI has no equivalent of — so it is a section here rather than a missing one there.
        ///
        /// ⚠⚠ THREE SECTIONS ARE NEW, AND THEY ARE NOT PADDING. Hardware, Database and Environment
        /// existed only on the web till, so an operator moving between the two met a screen that was
        /// missing a third of its answers — including *"is the agent paired"* and *"what version is
        /// this"*, which are the two questions somebody asks before ringing for help.
        ///
        /// ⚠ Where MAUI's honest answer DIFFERS from the web till's, it says so rather than copying the
        /// wording. **Database** is the clearest case: the web till says it has no device database
        /// because the server is the source of truth. MAUI has a real local SQLite store, and telling a
        /// shop otherwise would be a lie about where their unsynced sales are sitting.
        /// </summary>
        private void BuildSections(VerticalStackLayout column)
        {
            // ── Till ──────────────────────────────────────────────────────────────────────────────
            var till = new Controls.SettingsSection(
                "Till", "How the till screen behaves — the bag buttons it offers");

            // ⚠ READ-ONLY (ruling 2026-08-19). This was "Quick-sell bag item", a per-device barcode an
            // operator typed in — so a five-till shop set it five times and could offer only ONE bag
            // when a shop normally sells a single-use one AND a bag for life. Bags come from the portal
            // and are pushed to every till, exactly like the colour scheme.
            AddAction(till, "Carrier bags", nameof(CarrierBagsInfoCommand));

            // ── Checkout ──────────────────────────────────────────────────────────────────────────
            var checkout = new Controls.SettingsSection(
                "Checkout", "What happens after each sale — the receipt prompt");

            // ⚠⚠ SWITCHES, NOT BUTTONS — §5c item 9. These were `DisplayAlert("Hmm", …, "Yes", "No")`,
            // so the only way to learn whether "ask for receipt" was ON was to open a dialog that
            // offered to change it, and pressing the wrong button changed a checkout behaviour with no
            // undo.
            checkout.Add(BuildToggle("AskForReceiptOption".Translate(), nameof(AskForReceipt)));

            // ── Printer ───────────────────────────────────────────────────────────────────────────
            var printer = new Controls.SettingsSection(
                "Printer", "The receipt this till prints — and a test print on paper");

            AddAction(printer, "Receipt printer", nameof(ChangePrinterCommand));
            AddAction(printer, "PrintTestPage".Translate(), nameof(PrintTestPageCommand));

            // ── Hardware ──────────────────────────────────────────────────────────────────────────
            //
            // ⚠⚠ NEW ON MAUI. The web till has had this since the agent shipped; MAUI printed through
            // the same agent and never said so anywhere on screen. ⚠ The DRAWER toggle moves here from
            // Checkout, where MAUI had it — the web till groups the drawer with the hardware that
            // opens it, and "does this till have a cash drawer" is a fact about the PC, not about what
            // happens after a sale.
            var hardware = new Controls.SettingsSection(
                "Hardware", "Receipt printer & cash drawer — the Plutus Till Agent on this PC");

            hardware.Add(BuildToggle("ChangeCashDrawerExists".Translate(), nameof(TryCashDrawer)));

            // ⚠ ASKED, NOT ASSUMED, and asked ASYNCHRONOUSLY — same rule as the printer name below.
            // A confident "no agent" before anything has been asked is the 1.99.0 fault
            // ("The Printer says 'No printer chosen' yet it is selected and prints correctly").
            var agentLine = MutedLabel("Checking the hardware agent…");
            hardware.Add(agentLine);
            _ = FillAgentValueAsync(agentLine);

            // ── Database ──────────────────────────────────────────────────────────────────────────
            //
            // ⚠⚠ MAUI'S ANSWER IS THE OPPOSITE OF THE WEB TILL'S, AND COPYING ITS WORDING WOULD BE A
            // LIE. The web till says it has no device database because the server is the single source
            // of truth. This till has a real local SQLite store holding the catalogue, the roster and —
            // the part that matters — **sales that have not reached the server yet**. Telling a shop
            // there is no local data would be telling them their unsynced takings live somewhere they
            // do not.
            var database = new Controls.SettingsSection(
                "Database", "Where this till's data lives");

            database.Add(MutedLabel(
                "This till keeps its own database on this PC — the catalogue, the staff roster, and any "
                + "sales taken while offline. Sales are pushed to Plutus as soon as it can reach it; "
                + "until then this machine is the only copy."));
            database.Add(MutedLabel(
                "⚠ The queue of unsent sales is under Till device below. If it is not empty, do not "
                + "wipe or re-image this PC."));

            // ── Till device ───────────────────────────────────────────────────────────────────────
            //
            // ⚠⚠ THIS IS WHAT THE "PLUTUS" TAB WAS. Removed as a tab on 2026-08-18 (§5c item 9); the
            // web till calls it Till device and so does this. One button, opening the same screen —
            // flattening its five diagnostics into a button list would have lost the thing that makes
            // them useful, which is a failure pointing at ONE layer rather than at "the network".
            var device = new Controls.SettingsSection(
                "Till device", "This till's enrolment — identity, sync queue, diagnostics, un-enrol");

            AddAction(device, "Connection, enrolment & diagnostics", nameof(OpenTillDeviceCommand));

            // ── Environment ───────────────────────────────────────────────────────────────────────
            //
            // ⚠⚠ NEW ON MAUI, and it is the section somebody reads out down the phone. The version was
            // a caption in the corner of the page; who is signed in was nowhere at all.
            var environment = new Controls.SettingsSection(
                "Environment", "Who's signed in, and this app's build");

            var operatorName = App.GetViewModel()?.SignedInOperator?.DisplayName;
            environment.Add(Fact("Signed in as", string.IsNullOrWhiteSpace(operatorName) ? "—" : operatorName));

            // ⚠ `PlutusVersion.Current`, NOT `AppInfo.VersionString`. The latter reads the package
            // manifest, which is deliberately 0.0.0.0 so the build can substitute the real number —
            // so on a packaged build it answers 0.0.0.0 and on an unpackaged one it answers something
            // else again. This is the number the heartbeat sends, which is the one support needs.
            environment.Add(Fact("Till version", SharedKernel.PlutusVersion.Current));

            // ── Help ──────────────────────────────────────────────────────────────────────────────
            //
            // ⚠ OP4/WP6.3 — the till's way of asking Plutus for help and READING THE REPLY. MAUI-only
            // as a section: the web till puts it behind a ❓ in the app bar, which MAUI has no
            // equivalent of.
            var help = new Controls.SettingsSection(
                "Help", "Ask Plutus for help, and read the reply");

            AddAction(help, "Help and support", nameof(HelpAndSupportCommand));

            // ⚠ The web till's order exactly. ⚠ An EMPTY section is a heading that lies about having
            // content, so one is never added — today none can be, but a permission-gated action would
            // make that possible and the guard costs nothing.
            foreach (var section in new[] { till, checkout, printer, hardware, database, device, environment, help })
            {
                if (!section.IsEmpty) column.Children.Add(section);
            }
        }

        /// <summary>
        /// A button that runs a command, with the CURRENT VALUE under it where there is one.
        ///
        /// ⚠⚠ THE LIVE VALUE IS §5c ITEM 9'S OTHER HALF. "Quick-sell bag item" and "Receipt printer"
        /// both opened a picker and told you nothing about what was already chosen, so the only way to
        /// read a setting was to start changing it. The web till shows the value beside every control.
        /// </summary>
        private void AddAction(Controls.SettingsSection section, string text, string commandName)
        {
            var button = new Button { Text = text };
            button.SetBinding(Button.CommandProperty, commandName);
            section.Add(button);

            var live = LiveValueFor(commandName);
            if (live is not null) section.Add(live);
        }

        /// <summary>Label and value on one line — the web till's `&lt;dl class="env-info"&gt;`.</summary>
        private static View Fact(string label, string value)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });

            var name = new Label { Text = label, FontSize = 12 };
            name.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");

            var val = new Label { Text = value, FontSize = 12 };
            val.SetDynamicResource(Label.TextColorProperty, "ThemeInk");

            grid.Add(name, 0, 0);
            grid.Add(val, 1, 0);
            return grid;
        }

        /// <summary>
        /// Ask the agent whether it is there, and say so.
        ///
        /// ⚠ NEVER THROWS and never blocks the screen — this runs fire-and-forget from the constructor.
        /// ⚠ "Not paired" is a real answer and a useful one; "checking…" for ever is not, which is why
        /// every path below writes something.
        /// </summary>
        private static async Task FillAgentValueAsync(Label label)
        {
            try
            {
                var status = await Services.Printing.TillAgentPrinting.ResolveAsync();

                label.Text = status is null
                    ? "No hardware agent is paired with this PC — receipts print through Windows instead."
                    : $"Agent v{status.AgentVersion} is paired"
                      + (string.IsNullOrWhiteSpace(status.PrinterName)
                          ? ", with no printer chosen yet."
                          : $", printing to {status.PrinterName}.");
            }
            catch (Exception ex)
            {
                Services.Analytics.CrashLog.Write("SettingsViewModel.FillAgentValue", ex);
                label.Text = "Couldn't check the hardware agent.";
            }
        }


        /// <summary>
        /// A muted line showing what a setting is CURRENTLY set to, under the button that changes it.
        ///
        /// ⚠ Returns null for buttons that are actions rather than settings — "Print test page" has no
        /// value to show, and inventing "not set" for it would be noise.
        ///
        /// ⚠ Read once, at construction. These change only through the buttons on this screen, and
        /// `AppShell` rebuilds nothing — so a value edited here is re-read next time Settings is opened.
        /// Live-updating them would mean an observable wrapper around `Preferences` for two labels.
        /// </summary>
        private Label LiveValueFor(string command)
        {
            // ⚠⚠ THE PRINTER IS NOT A LOCAL SETTING, AND READING ONE WAS WRONG (fixed 2026-08-19).
            // Matt, on 1.99.0: *"The Printer says 'No printer chosen' yet it is selected and prints
            // correctly"*. `PrinterLogicalNameSetting` is the OPOS **logical name** — the old Windows
            // device path — and it is empty on every till that prints through the Plutus Till Agent,
            // which is the front door since 2026-08-10. The real answer comes from the AGENT
            // (`TillAgentPrinting.StatusAsync().PrinterName`), and asking it is asynchronous.
            //
            // ⚠ So the label starts NEUTRAL and is filled in when the agent answers. It must never
            // claim "no printer" before it has asked: a confident wrong answer about working hardware
            // is worse than a moment of "checking", and that is precisely the bug being fixed.
            if (command == nameof(ChangePrinterCommand))
            {
                var printerLabel = MutedLabel("Checking the printer…");
                _ = FillPrinterValueAsync(printerLabel);
                return printerLabel;
            }

            var text = command switch
            {
                // ⚠ It NAMES the bags rather than saying "set in the portal", because the question a
                // cashier actually has is "why is there no 20p button" — and the honest answer is either
                // "the shop has not added one" or "this till has never reached the server". Same wording
                // as the web till's Settings → Carrier bags line (2026-08-19 look-and-feel ruling).
                nameof(CarrierBagsInfoCommand) => Services.Sales.CarrierBags.Bags.Count == 0
                    ? "None set up, so the till shows no Bag button."
                    : "This till offers: " + string.Join(", ", Services.Sales.CarrierBags.Bags
                        .Select(b => $"{b.Name} ({(b.PricePence / 100m):C2})")),
                _ => null,
            };

            return text is null ? null : MutedLabel(text);
        }

        private static Label MutedLabel(string text)
        {
            var label = new Label { Text = text, FontSize = 12 };
            label.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            return label;
        }

        /// <summary>
        /// Ask the agent which printer it owns, then say so.
        ///
        /// ⚠ FOUR HONEST ANSWERS, because "no printer" was only ever true for one of them: the agent
        /// has a printer; the agent is running but has none set; there is no agent but an OPOS device is
        /// configured; or neither. Collapsing these is what told Matt his working printer did not exist.
        ///
        /// ⚠ Never throws. It runs detached from the constructor, so an escape would reach the
        /// dispatcher unhandled and kill the till.
        /// </summary>
        private async Task FillPrinterValueAsync(Label label)
        {
            string text;
            try
            {
                var status = await Services.Printing.TillAgentPrinting.StatusAsync();

                text = status switch
                {
                    { PrinterName: { Length: > 0 } name } =>
                        $"Currently: {name} (via the Plutus Till Agent)"
                        + (status.PrinterOnline ? "" : " — reported OFFLINE"),

                    not null => "The Plutus Till Agent is running but has no printer set.",

                    _ when !string.IsNullOrWhiteSpace(PrinterLogicalNameSetting) =>
                        $"Currently: {PrinterLogicalNameSetting} (OPOS device)",

                    _ => "No printer chosen — receipts print as PDF.",
                };
            }
            catch (Exception ex)
            {
                CrashLog.Write("SettingsViewModel.FillPrinterValue", ex);

                // ⚠ A FAILED CHECK IS NOT "NO PRINTER". Saying so would repeat the reported bug every
                // time the agent was momentarily busy.
                text = "Couldn't check the printer just now.";
            }

            await MainThread.InvokeOnMainThreadAsync(() => label.Text = text);
        }

        /// <summary>
        /// A setting that is a yes/no, shown as a switch with the sentence explaining it — the shape
        /// `SettingsPage.tsx` uses for every one of these (`setting-row`).
        ///
        /// ⚠⚠ GATED ON `pos.settings.manage`, WHICH IT WAS NOT BEFORE. Both of these were plain
        /// `DisplayAlert` yes/no prompts with **no permission check at all**, so any cashier could turn
        /// the receipt prompt off or tell the till it had no cash drawer. The web till has always
        /// disabled them without the permission (`canManageSettings`); MAUI simply asked. Found while
        /// converting them, and it is the reason this is more than a cosmetic change.
        ///
        /// ⚠ DISABLED, NOT REFUSED. A switch that cannot move, with the reason under it, tells a
        /// cashier where they stand before they touch it — a dialog that says no after the fact is how
        /// an operator learns to treat the screen as unpredictable. The three gated BUTTONS still
        /// refuse on press, because a button gives nothing away by being pressable.
        /// </summary>
        private View BuildToggle(string title, string settingPath)
        {
            // ⚠ The sentence lives beside the setting it explains, not in a resource file — these two
            // are the only ones, and a `.Translate()` key would hide the wording from anyone reading
            // this screen's code. ⚠⚠ It must not be `.Translate()`d on an English sentence anyway:
            // runbook pitfall 18 — that crashes a Debug build.
            var hint = settingPath switch
            {
                nameof(AskForReceipt) =>
                    "The NatApp behaviour — a yes/no prompt when the sale completes. Off: no prompt.",
                nameof(TryCashDrawer) =>
                    "On: the till tries to kick a connected cash drawer when a sale is paid in cash.",
                _ => string.Empty,
            };

            var allowed = Services.Security.TillGate.Check(
                App.GetViewModel().SignedInOperator, PermissionCatalogue.PosSettingsManage).Allowed;

            var row = new StackLayout { Margin = new Microsoft.Maui.Thickness(0, 6, 0, 0) };

            var heading = new Label { Text = title, FontAttributes = FontAttributes.Bold };
            row.Children.Add(heading);

            var explain = new Label
            {
                // ⚠ The hint says what the setting DOES; when it is locked, it also says why it cannot
                // be moved. Two sentences, because "you need a permission" without "and this is what it
                // would have done" leaves the operator none the wiser.
                Text = allowed ? hint : $"{hint}\nOnly a supervisor can change this ({PermissionCatalogue.PosSettingsManage}).",
                FontSize = 12,
            };
            explain.SetDynamicResource(Label.TextColorProperty, "ThemeInkMuted");
            row.Children.Add(explain);

            var toggle = new Microsoft.Maui.Controls.Switch { IsEnabled = allowed, HorizontalOptions = LayoutOptions.Start };
            // ⚠ TWO-WAY onto the `Settings` property, which reads and writes `Preferences` directly.
            // There is no `INotifyPropertyChanged` behind it and none is needed: the switch is the only
            // thing that changes it while this screen is up.
            toggle.SetBinding(Microsoft.Maui.Controls.Switch.IsToggledProperty, new Binding(settingPath, BindingMode.TwoWay));
            row.Children.Add(toggle);

            return row;
        }

        #region Commands
        #region Database
        // ⚠ `BackupDbCommand` is kept, WITHOUT a button (2026-08-10). `ExecuteBackupDb` is the only
        // thing that can stamp `MetaKeys.LegacyArchivedAtUtc`, which is the enrolment gate's input —
        // deleting the implementation would remove a platform capability, not just a control, and
        // the gate is a binding default. The button is gone because Matt does not need the on-ramp;
        // the code goes when `Build/To do/MAUI-retrofit.md` §10 item L1 is actioned.
        Command _backupDbCommand;
        public Command BackupDbCommand
        {
            get => _backupDbCommand ?? (_backupDbCommand = new Command(ExecuteBackupDb));
        }
        #endregion

        #region Help and support
        Command _helpAndSupportCommand;
        public Command HelpAndSupportCommand =>
            _helpAndSupportCommand ??= new Command(async () => await Services.Support.SupportFlow.ShowAsync());

        // ⚠⚠ THE FLOW MOVED TO `Services.Support.SupportFlow` ON 2026-08-21 and this is now a two-line
        // command that calls it. It has TWO entry points since MAUI got an app bar — Matt: *"The help
        // needs to be in the top corner of the MAUI till like the web till"* — and the web till has the
        // same pair: a ❓ in the app bar, and the Settings section.
        //
        // ⚠ ONE IMPLEMENTATION, TWO DOORS. Copying the flow into the app bar would have been two
        // copies of "how a till raises a ticket" in the same assembly, in the same language, still able
        // to disagree — the least defensible kind of C2 twin.
        #endregion

        #region Printer
        Command _changePrinterCommand;
        public Command ChangePrinterCommand
        {
            get => _changePrinterCommand ?? (_changePrinterCommand = new Command(ExecuteChangePrinter));
        }

        Command _printTestPageCommand;
        public Command PrintTestPageCommand
        {
            get => _printTestPageCommand ?? (_printTestPageCommand = new Command(ExecutePrintTestPage));
        }

        // ⚠⚠ `ChangeAskForReceiptOptionCommand` and `ChangeCashDrawerExistsCommand` ARE GONE (2026-08-19,
        // §5c item 9). Both settings are switches now — `BuildToggle` — so the commands, their backing
        // fields and their `Execute…` methods had no caller left.
        //
        // ⚠ Deleted rather than left in place with a note. An unreferenced `Command` on a viewmodel is
        // indistinguishable from one whose binding has a typo, which is exactly how
        // `StoreOptionsViewModel` ended up with `AddEmployeeCommmand` bound to `AddEmployeeCommand` and
        // two empty methods that looked live for months.
        //
        // ⚠ What they did is worth keeping: each raised `DisplayAlert(…, "Yes", "No")` and assigned the
        // answer, so the only way to READ either setting was to open a dialog offering to change it —
        // and neither checked a permission. Both facts are in the commit message and in `BuildToggle`.
        /*
        Command _changeBarcodeTypeCommand;
        public Command ChangeBarcodeTypeCommand
        {
            get => _changeBarcodeTypeCommand ?? (_changeBarcodeTypeCommand = new Command(ExecuteChangeBarcodeType));
        }*/
        #endregion
        #endregion


        Command _carrierBagsInfoCommand;

        /// <summary>
        /// Say where the carrier bags come from — ruling 2026-08-19.
        ///
        /// ⚠⚠ IT SETS NOTHING, AND THAT IS THE POINT. This row used to be **"Quick-sell bag item"**, a
        /// free-typed barcode stored per device, so a five-till shop configured it five times and could
        /// offer only ONE bag — when a shop normally sells a single-use bag AND a bag for life. Matt:
        /// *"That creates the 5p and 20p bags at the back and that pushes down to the tills… This would
        /// be cleaner than creating a bag at each till."*
        ///
        /// ⚠ It is still a ROW rather than a plain line because that is what this screen is made of, and
        /// tapping something that then explains itself is better than a caption nobody can act on. The
        /// subtitle already names the bags; this says who changes them.
        ///
        /// ⚠ No permission gate: it reveals nothing a cashier cannot see on the till's own buttons.
        /// </summary>
        public Command CarrierBagsInfoCommand =>
            _carrierBagsInfoCommand ??= new Command(ExecuteCarrierBagsInfo);

        private async void ExecuteCarrierBagsInfo()
        {
            try
            {
                var bags = Services.Sales.CarrierBags.Bags;

                var body = bags.Count == 0
                    ? "This shop has no carrier bags set up, so the till shows no Bag button.\n\n"
                      + "Add them in the management portal under Company → Carrier bags. Every till "
                      + "picks them up within a minute."
                    : "This till offers:\n\n"
                      + string.Join("\n", bags.Select(b => $"  • {b.Name} — {(b.PricePence / 100m):C2}"))
                      + "\n\nThey are set in the management portal under Company → Carrier bags, and "
                      + "every till gets the same list. There is nothing to configure here.";

                await App.Current.MainPage.DisplayAlert("Carrier bags", body, "OK".Translate());
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here goes to the dispatcher unhandled and takes the till.
                Services.Analytics.CrashLog.Write("SettingsViewModel.ExecuteCarrierBagsInfo", ex);
            }
        }

        /// <summary>
        /// Open the platform/diagnostics screen — what the **Plutus tab** used to be (5c item 9).
        ///
        /// ⚠⚠ THE TAB IS GONE AND THIS IS WHERE IT WENT. Matt: *"most of the MAUI Plutus tab
        /// would move into settings"*. The web till has no such tab either — its equivalents are
        /// Settings sections called **Till device** and **Environment**.
        ///
        /// ⚠ THE SCREEN ITSELF IS NOT REBUILT, and that is deliberate: it carries live connection
        /// state, the enrolment flow and five diagnostics that each exercise one layer of the retrofit.
        /// Flattening that into a button list would lose the thing that makes it useful — a failure
        /// pointing at a specific layer rather than at "the network". So it is the same page, reached
        /// from here instead of from a tab.
        ///
        /// ⚠ PUSHED MODALLY rather than through a Shell route: it is a `ContentPage` that was only
        /// ever hosted as a tab, and a modal push needs no route registration to get wrong.
        /// </summary>
        public Command OpenTillDeviceCommand =>
            _openTillDeviceCommand ??= new Command(async () =>
            {
                try
                {
                    await App.Current.MainPage.Navigation.PushModalAsync(
                        new Views.Platform.ConnectionView());
                }
                catch (Exception ex)
                {
                    Services.Analytics.CrashLog.Write("SettingsViewModel.OpenTillDevice", ex);
                }
            });

        Command _openTillDeviceCommand;
        #region Execute Commands
        #region Database
        /// <summary>
        /// Archive the legacy database — the cutover on-ramp (step 21, binding default 9.3).
        ///
        /// ⚠ THIS IS WHAT UNLOCKS ENROLMENT. `EnrolmentFlow.BlockedReasonAsync` refuses to enrol a
        /// till that still holds an un-archived legacy file, because that file is the shop's sales
        /// history and the migration's only input. Until now nothing could archive, so the gate was
        /// passed `null` and did not run at all; this is the capability that lets it be switched on.
        ///
        /// ⚠ It was "Backup database": it copied the file through a save dialog, gated on
        /// `IsAuthorised` (the legacy `AuthActions` table a portal till has no rows in) via
        /// `EmployeeId` (null for every roster operator), falling back to
        /// `RequestAuthorisedUserInput` (which never terminates). So on the tills that most need to
        /// archive, it could not run — and even when it did, nothing recorded that it had happened.
        ///
        /// ⚠ COPY, NEVER MOVE, and never overwrite an existing archive: a second cutover must not
        /// quietly replace the only copy of the first one's data.
        /// </summary>
        private async void ExecuteBackupDb()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var gate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosSettingsManage);

                if (!gate.Allowed)
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                var legacyPath = Path.Combine(FileSystem.AppDataDirectory, "Database.db");
                if (!File.Exists(legacyPath))
                {
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "There's no previous database on this till to archive.", "OK".Translate());
                    return;
                }

                var archiveDir = Path.Combine(FileSystem.AppDataDirectory, "legacy-archive");
                string archivedTo;
                try
                {
                    archivedTo = Plutus.Client.Storage.Cutover.ArchiveLegacyDatabase(
                        legacyPath, archiveDir, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    CrashLog.Write("SettingsViewModel.ExecuteBackupDb", ex);
                    await App.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Couldn't archive the previous database. Nothing has been changed or deleted.",
                        "OK".Translate());
                    return;
                }

                // ⚠ STAMPED ONLY AFTER THE COPY SUCCEEDED. The stamp is what opens the enrolment
                // gate, so writing it first would let a till enrol having archived nothing.
                await Services.Storage.TillStoreAccess.UseAsync(async s =>
                {
                    await s.SetMetaAsync(MetaKeys.LegacyArchivedAtUtc,
                        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    return true;
                });

                await App.Current.MainPage.DisplayAlert("Archived",
                    $"The previous database has been archived to:\n\n{archivedTo}\n\n" +
                    "The original is untouched. This till can now be enrolled.", "OK".Translate());
            }
            catch (Exception ex)
            {
                CrashLog.Write("SettingsViewModel.ExecuteBackupDb", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ⚠ "RESTORE DATABASE" IS GONE (2026-08-10, Matt: *"its no longer needed"*).
        //
        // It let someone pick a `.db` file and copy it over the legacy `Database.db`. Three reasons
        // it went rather than being fixed: no screen reads that file any more (the v2 store is
        // `till-v2.db`), it was gated on the legacy `IsAuthorised` and therefore CRASHED rather than
        // refused on a portal till, and "overwrite this till's database from a file on a USB stick"
        // is not a thing a shop assistant should be able to do from a settings menu.

        // ⚠ "DELETE DATABASE" IS GONE (cutover step 21), and not merely disabled.
        //
        // It deleted the legacy `Database.db` outright, behind a single "are you sure". That file
        // is the shop's entire sales history and the ONLY input the migration has — there is no
        // server copy of a pre-cutover till's data, and no undo. A button that can destroy a
        // business's records permanently does not belong on a settings screen next to the printer
        // picker, and it certainly does not belong there gated on an authorisation check that
        // cannot succeed on the tills that still have something to lose.
        //
        // "Archive legacy database" above is the operation that was actually wanted.
        #endregion

        #region Printer
        /// <summary>
        /// Pick which printer this till prints receipts on.
        ///
        /// ⚠ THIS CRASHED THE APP ON EVERY PORTAL-PROVISIONED TILL — reported 2026-08-10. It gated
        /// on `empId.IsAuthorised("Admin", …)`, which opens the LEGACY local database and does
        /// `emp.EmpAuths` on the result of `GetEmployee(...)` without a null check. A portal till has
        /// no legacy employee rows and `AppViewModel.EmployeeId` is null for every roster operator,
        /// so that dereference threw a NullReferenceException — out of an `async void` with no
        /// `catch`, which is an unhandled exception and takes the process down. Not a refusal: a
        /// crash, from a settings button, mid-shift.
        ///
        /// ⚠ And the fallback could not have rescued it. `RequestAuthorisedUserInput` never assigns
        /// the id it returns, so the `do/while` re-prompted for ever and only Cancel escaped — see
        /// `TillGate`'s header, which replaced this whole model at cutover step 12.
        ///
        /// Now gated on `pos.settings.manage` through the platform's own permissions, with the
        /// refusal shown rather than thrown.
        ///
        /// ⚠ AND IT NO LONGER OPENS THE WINDOWS DEVICE PICKER, because that picker could not find a
        /// printer the web till prints on every day. Matt, 2026-08-10: *"I still cannot see a
        /// printer, it says wifi is turned off, but I do not understand what this means? The webtill
        /// can see the receipt printer fine."* Both observations were correct and had one cause:
        ///
        ///   • The picker enumerated `Windows.Devices.PointOfService` devices — a specialist driver
        ///     profile almost no receipt printer ships. With nothing to show, Windows' generic
        ///     device chrome fills the empty list with its stock advice about Bluetooth and Wi-Fi
        ///     Direct radios. ⚠ "Wireless is turned off" was never about the printer.
        ///   • The WEB till never used that route at all. It POSTs to the Plutus Till Agent, which
        ///     prints through the ordinary Windows print queue — so every driver-installed printer
        ///     is visible to it.
        ///
        /// This screen now leads with the agent, which is the parity answer: one hardware route for
        /// both tills. The OPOS picker survives one level down, for a till that genuinely has a
        /// PointOfService device — but it is no longer what an operator meets first, and it no
        /// longer looks like a fault when it finds nothing.
        /// </summary>
        private async void ExecuteChangePrinter()
        {
            if (IsBusy)
                return;

            // ⚠ Set where the operator asks to retry, acted on after the `finally` releases `IsBusy` —
            // see the comment at that assignment for why it cannot be dispatched in place.
            var retry = false;

            IsBusy = true;
            try
            {
                var gate = Services.Security.TillGate.Check(
                    App.GetViewModel().SignedInOperator, PermissionCatalogue.PosSettingsManage);

                if (!gate.Allowed)
                {
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(), gate.Message, "OK".Translate());
                    return;
                }

                var status = await Services.Printing.TillAgentPrinting.StatusAsync();

                if (status is null)
                {
                    // ⚠ Say WHAT IS MISSING and WHERE IT COMES FROM. "No printer selected" told an
                    // operator nothing they could act on; this names the thing to install and the
                    // fact that the web till on this same PC would have the same problem.
                    var fallback = "Use a POS (OPOS) printer instead";
                    var choice = await Services.UIHandeling.Modal.ShowAsync(() =>
                        Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                            "No Plutus Till Agent is running on this PC. The agent is the tray app that " +
                            "owns the receipt printer and cash drawer — the web till prints through it too. " +
                            "Start it (or install it) and try again.",
                            "Cancel".Translate(), null, "Try again", fallback));

                    // ⚠⚠ RETRY IS RE-DISPATCHED AFTER THIS CALL FINISHES, NOT FROM INSIDE IT
                    // (2026-08-19). `ExecuteChangePrinter` opens with `if (IsBusy) return;` and this
                    // method still holds that flag until its `finally` — so "Try again" invoked itself,
                    // hit its own caller's guard and returned SILENTLY. The action sheet closed and
                    // nothing happened, which reads as a dead button.
                    //
                    // ⚠ And the flag was the smaller half: the inner call would have set `IsBusy = true`
                    // and then this method's `finally` would have cleared it underneath the work it had
                    // just started — so even had the guard let it through, the busy state would have
                    // been wrong for the rest of the flow.
                    //
                    // ⚠ Found by sweeping for the same shape after Matt hit it in Loyalty. The identical
                    // fault was already known and fixed once in `TillViewModel.ExecuteAlterTransaction`,
                    // whose comment names this exact mechanism — third occurrence of one bug.
                    retry = choice == "Try again";

                    if (choice == fallback) await PickOposPrinterAsync();
                    return;
                }

                var printer = string.IsNullOrWhiteSpace(status.PrinterName) ? "none chosen yet" : status.PrinterName;
                var pair = string.IsNullOrWhiteSpace(TillAgentTokenSetting) ? "Pair this till" : "Change the pairing code";
                const string test = "Print a test receipt";

                var picked = await Services.UIHandeling.Modal.ShowAsync(() =>
                    Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync(
                        $"Plutus Till Agent v{status.AgentVersion} — printer: {printer}" +
                        (status.PrinterOnline ? "" : " (offline)") +
                        (status.DrawerSupported ? ", cash drawer supported" : ", no cash drawer"),
                        "Cancel".Translate(), null, pair, test, "Use a POS (OPOS) printer instead"));

                if (picked == pair)
                {
                    // ⚠ The agent's own tray window shows this code. It is per till PC, never
                    // leaves the machine, and is NOT a Plutus login — the same design the web till
                    // uses in `hardware.ts`.
                    var typed = await Application.Current.MainPage.DisplayPromptAsync(
                        "Pair with the agent",
                        "Enter the pairing code from the Plutus Till Agent's tray window.",
                        "OK".Translate(), "Cancel".Translate(), initialValue: TillAgentTokenSetting);

                    if (typed is null) return;   // ⚠ null is Cancel; empty is "unpair me", which is allowed
                    TillAgentTokenSetting = typed;

                    var ok = await Services.Printing.TillAgentPrinting.TestPrintAsync();
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        ok ? "Paired. A test receipt should be coming out of the printer."
                           : "The agent didn't accept that code. Check it in the agent's tray window.",
                        "OK".Translate());
                }
                else if (picked == test)
                {
                    var ok = await Services.Printing.TillAgentPrinting.TestPrintAsync();
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        ok ? "Sent to the printer."
                           : "The agent refused. Pair this till first, or check the printer is on.",
                        "OK".Translate());
                }
                else if (picked == "Use a POS (OPOS) printer instead")
                {
                    await PickOposPrinterAsync();
                }
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — an escape here is an UNHANDLED exception, not a failed command.
                CrashLog.Write("SettingsViewModel.ExecuteChangePrinter", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "Couldn't open the printer settings. Nothing has been changed.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }

            // ⚠ `IsBusy` is clear now, so the retry can take it for itself. ⚠⚠ It is a re-entry into this
            // same method, which is only safe BECAUSE it happens out here: the operator started the agent
            // and asked us to look again, and looking again is the whole flow, not a step of it.
            if (retry) ExecuteChangePrinter();
        }

        /// <summary>
        /// The old route, kept for a till with a genuine OPOS device.
        ///
        /// ⚠ It now WARNS FIRST rather than presenting an empty list as a fault. Almost no receipt
        /// printer ships a Windows PointOfService driver profile, so on most till PCs this picker is
        /// correctly empty — and the empty state is what produced "Wireless is turned off".
        /// </summary>
        private async Task PickOposPrinterAsync()
        {
            using (var printerMgr = new PosPrinterManager())
            {
                var printerId = await printerMgr.SelectPrinterAndGetPrinterId();

                // ⚠ Only WRITE the setting when one was actually chosen. The old code assigned
                // the empty result in the else branch too, so cancelling the picker silently
                // UNSET the till's printer and the next receipt went nowhere.
                if (!string.IsNullOrEmpty(printerId))
                    PrinterLogicalNameSetting = printerId;
                else
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        "Windows found no POS (OPOS) printer on this PC — which is normal, as most " +
                        "receipt printers don't ship that driver profile. Use the Plutus Till Agent " +
                        "instead: it prints to any printer Windows already has.", "OK".Translate());
            }
        }

        private async void ExecutePrintTestPage()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                // ⚠ THE AGENT FIRST, because it is what will print the next real receipt. A test
                // page that exercises a route the till no longer uses proves nothing — and this
                // button silently did NOTHING at all on a till with no OPOS device: `InitPrinter`
                // returned false, the `if` fell through, and the operator got no paper and no
                // message, which is indistinguishable from a broken printer.
                if (Services.Printing.TillAgentPrinting.Paired)
                {
                    var ok = await Services.Printing.TillAgentPrinting.TestPrintAsync();
                    await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                        ok ? "Sent to the printer."
                           : "The Plutus Till Agent didn't print. Check it's running and this till is paired.",
                        "OK".Translate());
                    return;
                }

                if(DeviceInfo.Idiom == DeviceIdiom.Desktop)
                {
                    using (var printMgr = new PosPrinterManager())
                    {
                        if(await printMgr.InitPrinter())
                        {
                            printMgr.WriteText("Test Nomral text");
                            printMgr.WriteText("Test Bold On", bold: "true");
                            printMgr.WriteText("Test Underline On", underline: "true");
                            printMgr.WriteText("Test align Center", align: "cntr");
                            printMgr.WriteText("Test align Right", align: "rght");
                            printMgr.WriteText("test Bold + Underline", bold: "true", underline: "true");
                            printMgr.WriteText("test Bold + Algin Center", bold: "true", align: "cntr");
                            printMgr.WriteText("test Bold + Algin Right", bold: "true", align: "rght");
                            printMgr.WriteText("test Underline + Algin Center", underline: "true", align: "cntr");
                            printMgr.WriteText("test Underline + Algin Right", underline: "true", align: "rght");
                            printMgr.ScoreReceipt();
                            printMgr.WriteBarcode("123456789", App.GetViewModel().BarcodeSymbologySetting, 100, "cntr");
                            printMgr.ScoreReceipt();
                            printMgr.WriteText("Blank Line Test (3)");
                            printMgr.BlankLine();
                            printMgr.BlankLine();
                            printMgr.BlankLine();
                            printMgr.WriteText("End of Test Print!");
                            printMgr.CutPaper();
                            await printMgr.ExecuteOposOrPdfAsync();
                            await printMgr.CloseConnection();
                        }
                        else
                        {
                            // ⚠ THE SILENT PATH, now closed. `InitPrinter` returns false whenever
                            // no OPOS printer is set — which is every till PC without that driver
                            // profile — and the `if` simply fell through. No paper, no message, no
                            // log: identical to a printer that is broken, from a button whose whole
                            // job is telling you whether the printer works.
                            await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                                "This till has no printer set up. Set one up under Receipt printer — " +
                                "the Plutus Till Agent is the one the web till uses.", "OK".Translate());
                        }
                    }
                }
            }
            catch(POSObjectException pOSObjectException)
            {
                Debug.WriteLine(pOSObjectException.Message);
            }
            catch (Exception ex)
            {
                // ⚠ `async void` — anything that is not a POS exception was an UNHANDLED exception
                // and took the app down from a settings button.
                CrashLog.Write("SettingsViewModel.ExecutePrintTestPage", ex);
                await Application.Current.MainPage.DisplayAlert("Hmm".Translate(),
                    "The test print didn't work. Nothing has been changed.", "OK".Translate());
            }
            finally
            {
                IsBusy = false;
            }
        }

        /*
        private async void ExecuteChangeBarcodeType()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                var empId = App.GetViewModel().EmployeeId;
                bool escape = false;
                do
                {
                    Enum.TryParse(DatabaseProviderSetting, out Database.Enums.DatabaseProvider databaseProvider);
                    if (empId.IsAuthorised("Admin", Database.Enums.Permissions.Execute, databaseProvider))
                    {
                        bool tryAgain;
                        do
                        {
                            tryAgain = false;
                            using (var printerMgr = new PosPrinterManager())
                            {
                                var barcodeTypes = await printerMgr.GetBarcodeSymbols();
                                var selectedType = await Plutus.Frontend.AppClient.Helpers.CustomViews.ChoiceHelper.AskAsync("ChangeBarcodeType".Translate(), "Cancel".Translate(), null, barcodeTypes);
                                if (selectedType != "Cancel".Translate())
                                {
                                    var barcodeTestID = $"{DateTime.Now.Year}" +
                                        $"{DateTime.Now.Month}" +
                                        $"{DateTime.Now.Day}" +
                                        $"{DateTime.Now.Hour}" +
                                        $"{DateTime.Now.Minute}" +
                                        $"{DateTime.Now.Second}" +
                                        $"{DateTime.Now.Millisecond}";
                                    if (await printerMgr.InitPrinter())
                                    {
                                        printerMgr.WriteText("TestPrint".Translate(), "cntr", "true");
                                        printerMgr.BlankLine();
                                        printerMgr.WriteText("TestPrint".Translate(), "cntr", "true");
                                        printerMgr.WriteBarcode(barcodeTestID, selectedType, 100, "cntr");
                                        printerMgr.CutPaper();
                                        await printerMgr.SetupExecutePrintMultiLine();
                                        if (!await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "CheckReceiptCorrect".Translate(), "Correct".Translate(), "TryAgain".Translate()))
                                            tryAgain = true;
                                        else
                                            BarcodeSymbologySetting = selectedType;
                                    }
                                    else
                                        await App.Current.MainPage.DisplayAlert("Hmm".Translate(), "PrinterNotFound".Translate(), "OK".Translate());
                                }
                                escape = true;
                            }
                        } while (tryAgain);
                    }
                    if (!escape)
                    {
                        var empAuthoriser = await Authorisation.RequestAuthorisedUserInput(databaseProvider);
                        if (empAuthoriser == default)
                            escape = true;
                    }
                } while (!escape);
            }
            finally
            {
                IsBusy = false;
            }
        }
        */
        #endregion
        #endregion
    }
}
