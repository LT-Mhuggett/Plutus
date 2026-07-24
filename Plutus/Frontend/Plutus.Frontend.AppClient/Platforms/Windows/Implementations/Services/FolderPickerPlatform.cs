using Plutus.Frontend.AppClient.Platforms.Windows.Helpers;
using Plutus.Frontend.AppClient.Services.IOHandeling.Picker;
using System;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Plutus.Frontend.AppClient.Exceptions;

namespace Plutus.Frontend.AppClient.Platforms.Windows.Implementations.Services
{
    class FolderPickerPlatform : IFolderPicker
    {
        FolderPicker Picker;
        public void InitFolderPicker(params string[] types)
        {
            Picker = new FolderPicker();
            WindowInterop.InitializeWithCurrentWindow(Picker);
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