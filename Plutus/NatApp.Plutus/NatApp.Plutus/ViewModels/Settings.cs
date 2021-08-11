using Xamarin.Essentials;

namespace NatApp.Plutus.ViewModels
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
        /// Ask for receipt at checkout
        /// </summary>
        public bool AskForReceipt
        {
            get => Preferences.Get(nameof(AskForReceipt), false);
            set => Preferences.Set(nameof(AskForReceipt), value);
        }
    }
}
