using NatApp.Plutus.Helpers.Extensions;
using Syncfusion.ListView.XForms.UWP;
using Syncfusion.SfPicker.XForms.UWP;

namespace NatApp.Plutus.UWP
{
    public sealed partial class MainPage
    {
        public MainPage()
        {
            this.InitializeComponent();

            SfPickerRenderer.Init();
            SfListViewRenderer.Init();

            LoadApplication(new Plutus.App());

            Windows.UI.Core.Preview.SystemNavigationManagerPreview.GetForCurrentView().CloseRequested += async (sender, args) =>
              {
                  var deferral = args.GetDeferral();
                  if (!await Implementations.Services.POSCommunicationUWP.CloseServiceAsync($"{Plutus.App.GetViewModel().SessionId.ToString()}"))
                  {
                      await Plutus.App.Current.MainPage.DisplayAlert("Wait!".Translate(), "POSDeviceStillActive".Translate(), "OK".Translate());
                      args.Handled = true;
                  }
                  deferral.Complete();
              };
        }
    }
}
