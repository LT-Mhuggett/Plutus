using Plutus.UWP;
using Plutus.UWP.Implementations;
using Xamarin.Forms;

[assembly: Dependency(typeof(CloseAppImplementation))]
namespace Plutus.UWP.Implementations
{
    class CloseAppImplementation : Plutus.Helpers.Interface.ICloseApp
    {
        public void CloseApp()
        {
            Windows.UI.Xaml.Application.Current.Exit();
        }
    }
}
