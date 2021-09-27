using Foundation;
using Syncfusion.ListView.XForms.iOS;
using Syncfusion.SfCalendar.XForms.iOS;
using Syncfusion.SfChart.XForms.iOS.Renderers;
using Syncfusion.SfNumericUpDown.XForms.iOS;
using Syncfusion.SfPicker.XForms.iOS;
using Syncfusion.SfSunburstChart.XForms.iOS;
using Syncfusion.XForms.iOS.PopupLayout;
using UIKit;

namespace NatApp.Plutus.iOS
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
            //Init Rg Plugins
            Rg.Plugins.Popup.Popup.Init();
            //Init Xamarin
            Xamarin.Forms.Forms.Init();
            //Init Synfusion Libaries
            SfNumericUpDownRenderer.Init();
            SfPickerRenderer.Init();
            SfListViewRenderer.Init();
            SfPopupLayoutRenderer.Init();
            SfChartRenderer.Init();
            SfCalendarRenderer.Init();
            SfSunburstChartRenderer.Init();

            //Load App
            LoadApplication(new App());

            return base.FinishedLaunching(app, options);
        }
    }
}
