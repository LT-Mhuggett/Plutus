using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Plugin.Media;
using Syncfusion.ListView.XForms.UWP;

namespace Plutus.UWP
{
    public sealed partial class MainPage
    {
        public MainPage()
        {
            this.InitializeComponent();

            ZXing.Net.Mobile.Forms.WindowsUniversal.ZXingScannerViewRenderer.Init();

            SfListViewRenderer.Init();

            LoadApplication(new Plutus.App());

            Windows.UI.Core.Preview.SystemNavigationManagerPreview.GetForCurrentView().CloseRequested +=
                async (sender, args) =>
                {
                    if (await Implementations.POSCommunicationImplementation.CloseServiceAsync($"{Plutus.App.Store.Id}-32134"))
                    {
                        args.Handled = false;
                    }
                    else
                    {
                        await Plutus.App.Current.MainPage.DisplayAlert("Wait!", "There is another till using printer, Please try again later.", "OK");
                        args.Handled = true;
                    }
                };
        }
    }
}
