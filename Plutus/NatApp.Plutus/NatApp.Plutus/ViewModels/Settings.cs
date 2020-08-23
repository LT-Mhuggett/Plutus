using Plugin.Settings;
using Plugin.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NatApp.Plutus.ViewModels
{
    public class Settings
    {
        protected static ISettings AppSettings => CrossSettings.Current;

        public string PrinterLogicalNameSetting
        {
            get => AppSettings.GetValueOrDefault(nameof(PrinterLogicalNameSetting), null);
            set => AppSettings.AddOrUpdateValue(nameof(PrinterLogicalNameSetting), value);
        }

        public string BarcodeSymbologySetting
        {
            get => AppSettings.GetValueOrDefault(nameof(BarcodeSymbologySetting), default(string));
            set => AppSettings.AddOrUpdateValue(nameof(BarcodeSymbologySetting), value);
        }

        public string DatabaseProviderSetting
        {
            get => AppSettings.GetValueOrDefault(nameof(DatabaseProviderSetting), null);
            set => AppSettings.AddOrUpdateValue(nameof(DatabaseProviderSetting), value); 
        }

        public bool CashbackEnabled
        {
            get => AppSettings.GetValueOrDefault(nameof(CashbackEnabled), false);
            set => AppSettings.AddOrUpdateValue(nameof(CashbackEnabled), value);
        }

        public string CustomCultureInfo
        {
            get => AppSettings.GetValueOrDefault(nameof(CustomCultureInfo), null);
            set => AppSettings.AddOrUpdateValue(nameof(CustomCultureInfo), value);
        }

        public string DefaultBagId
        {
            get => AppSettings.GetValueOrDefault(nameof(DefaultBagId), string.Empty);
            set => AppSettings.AddOrUpdateValue(nameof(DefaultBagId), value);
        }
    }
}
