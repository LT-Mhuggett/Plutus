
using Plutus;
using Plutus.Frontend;
using Plutus.Frontend.ClientUI;
using Plutus.Frontend.ClientUI.Core;
using Plutus.Frontend.ClientUI.Core.AppSettings;

namespace Plutus.Frontend.ClientUI.Core.AppSettings
{
    public class AppSettings
    {
        public string DBServiceURL { get; set; }
        public AzureAdB2CSettings AzureAdB2CSettings { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is null or not AppSettings)
                return false;
            else
                return DBServiceURL == ((AppSettings)obj).DBServiceURL && AzureAdB2CSettings == ((AppSettings)obj).AzureAdB2CSettings;
        }

        public override int GetHashCode() => DBServiceURL.GetHashCode() ^ AzureAdB2CSettings.GetHashCode();

        public override string ToString() => string.Format("AppSettings(DBServiceURL {0}, AzureAdB2CSettings {1})", DBServiceURL, AzureAdB2CSettings.ToString());
    }
}
