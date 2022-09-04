using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plutus.Frontend.ClientUI.Core
{
    public static class Settings
    {
        /// <summary>
        /// Printer logical name
        /// </summary>
        public static string PrinterLogicalNameSetting
        {
            get => Preferences.Get(nameof(PrinterLogicalNameSetting), default(string));
            set
            {
                if (EqualityComparer<string>.Default.Equals(PrinterLogicalNameSetting, value)) return;
                Preferences.Set(nameof(PrinterLogicalNameSetting), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// User defined barcode symbology
        /// </summary>
        public static string BarcodeSymbologySetting
        {
            get => Preferences.Get(nameof(BarcodeSymbologySetting), default(string));
            set
            {
                if (EqualityComparer<string>.Default.Equals(BarcodeSymbologySetting, value)) return;
                Preferences.Set(nameof(BarcodeSymbologySetting), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// Current database provider
        /// </summary>
        public static string DatabaseProviderSetting
        {
            get => Preferences.Get(nameof(DatabaseProviderSetting), null);
            set
            {
                if (EqualityComparer<string>.Default.Equals(DatabaseProviderSetting, value)) return;
                Preferences.Set(nameof(DatabaseProviderSetting), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// Cashback enabeld for card transactions
        /// </summary>
        public static bool CashbackEnabled
        {
            get => Preferences.Get(nameof(CashbackEnabled), false);
            set
            {
                if (EqualityComparer<bool>.Default.Equals(CashbackEnabled, value)) return;
                Preferences.Set(nameof(CashbackEnabled), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// User defined culture infomration
        /// </summary>
        public static string CustomCultureInfo
        {
            get => Preferences.Get(nameof(CustomCultureInfo), null);
            set
            {
                if (EqualityComparer<string>.Default.Equals(CustomCultureInfo, value)) return;
                Preferences.Set(nameof(CustomCultureInfo), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// User defined default BagId
        /// </summary>
        public static string DefaultBagId
        {
            get => Preferences.Get(nameof(DefaultBagId), string.Empty);
            set 
            {
                if (EqualityComparer<string>.Default.Equals(DefaultBagId, value)) return;
                Preferences.Set(nameof(DefaultBagId), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// Cash Drawer warning silenced status
        /// </summary>
        public static bool CashDrawerWarningSilenced
        {
            get => Preferences.Get(nameof(CashDrawerWarningSilenced), false);
            set
            {
                if (EqualityComparer<bool>.Default.Equals(CashDrawerWarningSilenced, value)) return;
                Preferences.Set(nameof(CashDrawerWarningSilenced), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// Try to use Cash Drawer if exists
        /// </summary>
        public static bool TryCashDrawer
        {
            get => Preferences.Get(nameof(TryCashDrawer), false);
            set
            {
                if (EqualityComparer<bool>.Default.Equals(TryCashDrawer, value)) return;
                Preferences.Set(nameof(TryCashDrawer), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// Ask for receipt at checkout
        /// </summary>
        public static bool AskForReceipt
        {
            get => Preferences.Get(nameof(AskForReceipt), false);
            set
            {
                if (EqualityComparer<bool>.Default.Equals(AskForReceipt, value)) return;
                Preferences.Set(nameof(AskForReceipt), value);
                StaticOnPropertyChanged();
            }
        }

        /// <summary>
        /// Should the order of the Till ListView be reversed
        /// </summary>
        public static bool TillListViewOrderReversed
        {
            get => Preferences.Get(nameof(TillListViewOrderReversed), true);
            set 
            {
                if(EqualityComparer<bool>.Default.Equals(TillListViewOrderReversed, value))
                Preferences.Set(nameof(TillListViewOrderReversed), value);
                StaticOnPropertyChanged();
            } 
        }

        #region INotifyPropertyChanged
        public static event PropertyChangedEventHandler StaticPropertyChanged;

        private static void StaticOnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
