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
        }
    }
}
