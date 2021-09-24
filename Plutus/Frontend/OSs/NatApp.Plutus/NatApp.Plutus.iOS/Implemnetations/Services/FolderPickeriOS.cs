using System;
using System.Threading.Tasks;
using NatApp.Plutus.iOS.Implemnetations.Services;
using NatApp.Plutus.Services.IOHandeling.Picker;
using Xamarin.Forms;

[assembly: Dependency(typeof(FolderPickeriOS))]
namespace NatApp.Plutus.iOS.Implemnetations.Services
{
    class FolderPickeriOS : IFolderPicker
    {
        public void InitFolderPicker(params string[] types)
        {
            throw new PlatformNotSupportedException();
        }

        public async Task<object> PickFolderAsync()
        {
            throw new PlatformNotSupportedException();
        }
    }
}