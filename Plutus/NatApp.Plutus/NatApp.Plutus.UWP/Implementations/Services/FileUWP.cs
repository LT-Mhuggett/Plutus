using NatApp.Plutus.Services.IOHandeling;
using NatApp.Plutus.UWP.Implementations.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Xamarin.Forms;

[assembly: Dependency(typeof(FileUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    class FileUWP : IFile
    {
        public async Task<bool> Copy(string srcPath, List<KeyValuePair<string, List<string>>> fileTypeChoices, string suggestedName)
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var fileTypeChoice in fileTypeChoices)
                savePicker.FileTypeChoices.Add(fileTypeChoice.Key, fileTypeChoice.Value);

            savePicker.SuggestedFileName = suggestedName;

            var dsetFile = await savePicker.PickSaveFileAsync();

            var srcFile = await StorageFile.GetFileFromPathAsync(srcPath);
            if (dsetFile == null)
                return false;
            try
            {
                await srcFile.CopyAndReplaceAsync(dsetFile);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"File Transfer error: {ex}");
                return false;
            }
        }

        public async Task<bool> Copy(object src, string destPath, string fileName)
        {
            if (src is StorageFile srcFile)
            {
                var destFile = await (await StorageFolder.GetFolderFromPathAsync(destPath)).CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                if (srcFile == null)
                    return false;
                try
                {
                    await srcFile.CopyAndReplaceAsync(destFile);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"File Transfer error: {ex}");
                    return false;
                }
            }
            throw new ArgumentException("File is not of type StorageFile");
        }

        public async Task<object> GetFile(List<string> listFileTypes)
        {
            var filePicker = GetFileOpenPicker(listFileTypes);

            var file = await filePicker.PickSingleFileAsync();
            return file;
        }

        public async Task<string> GetFilePath(List<string> listFileTypes)
        {
            var filePicker = GetFileOpenPicker(listFileTypes);

            var file = await filePicker.PickSingleFileAsync();
            if (file == null)
                return null;
            return file.Path;
        }

        public string GetFilePath(object file)
        {
            if (file is StorageFile storageFile)
                return storageFile.Path;
            throw new ArgumentException("File is not of type StorageFile");
        }

        public async Task<bool> DeleteFile(object file)
        {
            if (file is StorageFile storageFile)
            {
                await storageFile.DeleteAsync();
                return true;
            }
            throw new ArgumentException("File is not of type StorageFile");
        }

        public async Task<bool> DeleteFile(string filePath)
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            if (storageFile != null)
            {
                await storageFile.DeleteAsync();
                return true;
            }
            return false;
        }

        public async Task<bool> SaveAndView(string fileName, string contentType, MemoryStream stream, IDictionary<string, List<string>> listFileTypes)
        {
            StorageFile outFile = await GetFileSavePicker(listFileTypes, fileName).PickSaveFileAsync();
            if (outFile != null)
            {
                CachedFileManager.DeferUpdates(outFile);
                var outFileStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
                using (var outputStream = outFileStream.GetOutputStreamAt(0))
                {
                    using (var dataWriter = new DataWriter(outputStream))
                    {
                        dataWriter.WriteBytes(stream.ToArray());
                        await dataWriter.StoreAsync();
                        await outputStream.FlushAsync();
                    }
                }
                Windows.Storage.Provider.FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(outFile);
                if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                {
                    await Windows.System.Launcher.LaunchFileAsync(outFile);
                    return true;
                }
                else
                    return false;
            }
            return false;
        }

        private FileOpenPicker GetFileOpenPicker(List<string> listFileTypes)
        {
            var filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var fileType in listFileTypes)
                filePicker.FileTypeFilter.Add(fileType);
            return filePicker;
        }

        private FileSavePicker GetFileSavePicker(IDictionary<string, List<string>> listFileTypes, string suggestedFileName)
        {
            var filePicker = new FileSavePicker();
            filePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var fileType in listFileTypes)
                filePicker.FileTypeChoices.Add(fileType.Key, fileType.Value);
            filePicker.SuggestedFileName = suggestedFileName;
            return filePicker;
        }
    }
}
