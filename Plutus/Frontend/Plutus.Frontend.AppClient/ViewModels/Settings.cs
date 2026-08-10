using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Plutus.Frontend.AppClient.ViewModels
{
    public class Settings
    {
        /// <summary>
        /// Printer logical name
        /// </summary>
        public string PrinterLogicalNameSetting
        {
            get => Preferences.Get(nameof(PrinterLogicalNameSetting), default(string));
            set => Preferences.Set(nameof(PrinterLogicalNameSetting), value);
        }

        /// <summary>
        /// The pairing code for the Plutus Till Agent on THIS PC — the tray app that owns the
        /// receipt printer and the cash drawer.
        ///
        /// ⚠ THE AGENT IS HOW THE WEB TILL HAS ALWAYS PRINTED, and teaching MAUI to use it is the
        /// fix for "I still cannot see a printer, it says wifi is turned off" (Matt, 2026-08-10).
        /// The old route asked Windows for a `PointOfService` device, a driver profile almost no
        /// receipt printer ships, and the picker's generic chrome then volunteered a complaint about
        /// RADIOS. The agent instead prints through the ordinary Windows print queue, so any printer
        /// this PC has a driver for is one both tills can use.
        ///
        /// ⚠ PER TILL PC and it NEVER LEAVES THE MACHINE — same as the web till's
        /// `localStorage["plutus.agentToken"]`. It authorises hardware on this counter, not a person
        /// or a tenant, so it must not travel with a Plutus login or ride in a device enrolment.
        /// ⚠ Upper-cased on the way in: the agent compares it exactly, and a code read off a tray
        /// window and typed in lower case is otherwise a 401 that reads as "the printer is broken".
        /// </summary>
        public string TillAgentTokenSetting
        {
            get => Preferences.Get(nameof(TillAgentTokenSetting), string.Empty);
            set => Preferences.Set(nameof(TillAgentTokenSetting), (value ?? string.Empty).Trim().ToUpperInvariant());
        }

        /// <summary>
        /// Item search matches each word separately — "batman one" finds *Batman Year One*. Off,
        /// the whole phrase must appear.
        ///
        /// ⚠ A per-DEVICE preference, not a platform decision, and the default must match the web
        /// till's (`prefs.ts DEFAULTS.matchAllWords: true`). Two tills with the same setting must
        /// agree about what a search finds; two tills that merely *default* differently would
        /// disagree out of the box, and the symptom is somebody finding an item on one counter and
        /// not the next and concluding the stock is wrong. The matching rule itself is shared —
        /// `SharedKernel.ItemSearch` — see till-design C1.
        /// </summary>
        public bool MatchAllWordsSetting
        {
            get => Preferences.Get(nameof(MatchAllWordsSetting), true);
            set => Preferences.Set(nameof(MatchAllWordsSetting), value);
        }

        /// <summary>
        /// User defined barcode symbology
        /// </summary>
        public string BarcodeSymbologySetting
        {
            get => Preferences.Get(nameof(BarcodeSymbologySetting), default(string));
            set => Preferences.Set(nameof(BarcodeSymbologySetting), value);
        }

        /// <summary>
        /// Current database provider
        /// </summary>
        public string DatabaseProviderSetting
        {
            get => Preferences.Get(nameof(DatabaseProviderSetting), null);
            set => Preferences.Set(nameof(DatabaseProviderSetting), value);
        }

        /// <summary>
        /// Where this till's Plutus backend lives.
        ///
        /// ⚠ MAUI needs this and the web till never did — a browser till is served from the same
        /// origin it talks to, so its address is implicit. An installed app has to be told.
        ///
        /// WP4 moves the authoritative copy into the local store's <c>Meta.serverUrl</c>, set during
        /// enrolment. Until then this preference is what the connection check dials, defaulting to
        /// the test environment so a fresh dev install shows something truthful rather than blank.
        /// </summary>
        public string ServerUrlSetting
        {
            get => Preferences.Get(nameof(ServerUrlSetting), "https://plutus.huggett.dscloud.me");
            set => Preferences.Set(nameof(ServerUrlSetting), value);
        }

        /// <summary>
        /// Cashback enabeld for card transactions
        /// </summary>
        public bool CashbackEnabled
        {
            get => Preferences.Get(nameof(CashbackEnabled), false);
            set => Preferences.Set(nameof(CashbackEnabled), value);
        }

        /// <summary>
        /// User defined culture infomration
        /// </summary>
        public string CustomCultureInfo
        {
            get => Preferences.Get(nameof(CustomCultureInfo), null);
            set => Preferences.Set(nameof(CustomCultureInfo), value);
        }

        /// <summary>
        /// User defined default BagId
        /// </summary>
        public string DefaultBagId
        {
            get => Preferences.Get(nameof(DefaultBagId), string.Empty);
            set => Preferences.Set(nameof(DefaultBagId), value);
        }

        /// <summary>
        /// Cash Drawer warning silenced status
        /// </summary>
        public bool CashDrawerWarningSilenced
        {
            get => Preferences.Get(nameof(CashDrawerWarningSilenced), false);
            set => Preferences.Set(nameof(CashDrawerWarningSilenced), value);
        }

        /// <summary>
        /// Try to use Cash Drawer if exists
        /// </summary>
        public bool TryCashDrawer
        {
            get => Preferences.Get(nameof(TryCashDrawer), false);
            set => Preferences.Set(nameof(TryCashDrawer), value);
        }

        /// <summary>
        /// Ask for receipt at checkout
        /// </summary>
        public bool AskForReceipt
        {
            get => Preferences.Get(nameof(AskForReceipt), false);
            set => Preferences.Set(nameof(AskForReceipt), value);
        }
    }
}
