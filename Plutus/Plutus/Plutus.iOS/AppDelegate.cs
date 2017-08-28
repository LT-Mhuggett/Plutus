using System;
using System.Collections.Generic;
using System.Linq;
using HockeyApp.iOS;
using Foundation;
using UIKit;

namespace Plutus.iOS
{
	// The UIApplicationDelegate for the application. This class is responsible for launching the 
	// User Interface of the application, as well as listening (and optionally responding) to 
	// application events from iOS.
	[Register("AppDelegate")]
	public partial class AppDelegate : global::Xamarin.Forms.Platform.iOS.FormsApplicationDelegate
	{
		//
		// This method is invoked when the application has loaded and is ready to run. In this 
		// method you should instantiate the window, load the UI into it and then make the window
		// visible.
		//
		// You have 17 seconds to return from this method, or iOS will terminate your application.
		//
		public override bool FinishedLaunching(UIApplication app, NSDictionary options)
		{
			global::Xamarin.Forms.Forms.Init ();
		    Xamarin.FormsMaps.Init();
		    ZXing.Net.Mobile.Forms.iOS.Platform.Init();
            var manager = BITHockeyManager.SharedHockeyManager;
            manager.Configure("3250123b05a54662b341241d21b1c1b1");
            manager.StartManager();
            manager.Authenticator.AuthenticateInstallation(); // This line is obsolete in crash only builds
            LoadApplication (new Plutus.App ());
            return base.FinishedLaunching (app, options);
		}
	}
}
