using Plutus.UWP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Xamarin.Forms;

[assembly: Dependency(typeof(CloseAppImplementation))]
namespace Plutus.UWP
{
    class CloseAppImplementation : Plutus.Helpers.Interface.ICloseApp
    {
        public void CloseApp()
        {
            Windows.UI.Xaml.Application.Current.Exit();
        }
    }
}
