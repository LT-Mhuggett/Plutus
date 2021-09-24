using NatApp.Plutus.UWP.Implementations.Services;
using NatApp.Plutus.Services.IOHandeling.Picker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Windows.Storage.Pickers;
using NatApp.Plutus.Exceptions;
using Windows.Storage;

[assembly: Dependency(typeof(FolderPickerUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    class FolderPickerUWP : IFolderPicker
    {
        FolderPicker Picker;
        public void InitFolderPicker(params string[] types)
        {
            Picker = new FolderPicker();
            foreach (var type in types)
            {
                Picker.FileTypeFilter.Add(type);
            }
        }

        public async Task<object> PickFolderAsync()
        {
            if (Picker == null)
                throw new FolderPickerNotInitalizedException();

            var folder = await Picker.PickSingleFolderAsync();
            return folder;
        }
    }
}