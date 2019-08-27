using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using NatApp.Plutus.Droid.Implementations.Services;
using NatApp.Plutus.Services.IOHandeling.Picker;
using Xamarin.Forms;

[assembly: Dependency(typeof(FolderPickerDroid))]
namespace NatApp.Plutus.Droid.Implementations.Services
{
    class FolderPickerDroid : IFolderPicker
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