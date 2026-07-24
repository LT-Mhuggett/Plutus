using System;
using Microsoft.Maui.Devices;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Plutus.Frontend.AppClient.Views.FirstTimeStartUp
{
    public partial class FTSUMainView : TabbedPage
    {
        public FTSUMainView()
        {
            InitializeComponent();

            if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
            {
                Children.Add(new SetupView());
                Children.Add(new RecoveryView());
                Children.Add(new TransferThirdPartyView());
            }
            else
            {
                Children.Add(new RecoveryView());
            }
        }
    }
}