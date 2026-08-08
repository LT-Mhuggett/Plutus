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
