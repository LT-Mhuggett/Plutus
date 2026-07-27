using Foundation;
using UIKit;

namespace Plutus.Frontend.ClientUI
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
        public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
        {
            // Microsoft.Identity.Client.AuthenticationContinuationHelper only ships an iOS
            // implementation (Platforms/iOS in the MSAL source) - there never was a MacCatalyst
            // one, and newer MSAL versions no longer resolve it here at all for this TFM.
            return base.OpenUrl(application, url, options);
        }
    }
}