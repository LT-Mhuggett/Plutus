using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace NatApp.Plutus.Views.FirstTimeStartUp
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class FTSUMainView : TabbedPage
    {
        public FTSUMainView()
        {
            InitializeComponent();

            if (Device.Idiom == TargetIdiom.Desktop)
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