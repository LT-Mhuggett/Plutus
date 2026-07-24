using System.IO;
using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Storage;

namespace Plutus.Frontend.ClientUI.Services.IOHandeling
{
    /// <inheritdoc cref="IFileService"/>
    public class FileService : IFileService
    {
        public async Task<bool> SaveCopyAsync(string sourcePath, string suggestedName)
        {
            if (!File.Exists(sourcePath))
                return false;

            using var stream = File.OpenRead(sourcePath);
            var result = await FileSaver.Default.SaveAsync(suggestedName, stream, CancellationToken.None);
            return result.IsSuccessful;
        }

        public async Task<string> PickFileAsync(params string[] allowedExtensions)
        {
            var options = new PickOptions { PickerTitle = "Select a file" };

            if (allowedExtensions is { Length: > 0 })
            {
                options.FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, allowedExtensions }
                });
            }

            var result = await FilePicker.Default.PickAsync(options);
            return result?.FullPath;
        }

        public bool DeleteFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            return !File.Exists(path);
        }
    }
}
