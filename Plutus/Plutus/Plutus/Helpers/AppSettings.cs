using Plugin.Settings;
using Plugin.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Plutus.Helpers
{
    class AppSettings
    {
        private ISettings Settings => CrossSettings.Current;

        public string PrinterLogicalName
        {
            get => Settings.GetValueOrDefault(nameof(PrinterLogicalName), null);
            set => Settings.AddOrUpdateValue(nameof(PrinterLogicalName), value);
        }
    }
}
