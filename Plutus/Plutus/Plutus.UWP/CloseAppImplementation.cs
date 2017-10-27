using Plutus.UWP;
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
