using NatApp.Plutus.Services.IOHandeling;
using NatApp.Plutus.UWP.Implementations.Services;
using NatApp.Plutus.Helpers.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Xamarin.Forms;

[assembly: Dependency(typeof(FileUWP))]
namespace NatApp.Plutus.UWP.Implementations.Services
{
    class FileUWP : IFile
    {
        public async Task<bool> Copy(string srcPath, IDictionary<string, IList<string>> fileTypeChoices, string suggestedName)
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

        public async Task<object> GetFile(IList<string> listFileTypes)
        {
            var filePicker = GetFileOpenPicker(listFileTypes);

            var file = await filePicker.PickSingleFileAsync();
            return file;
        }
        public async Task<byte[]> GetFileAsByteArray(IList<string> listFileTypes)
        {
            var filePicker = GetFileOpenPicker(listFileTypes);

            var file = await filePicker.PickSingleFileAsync();
            if (file == null)
                return default;
            IBuffer buffer = await FileIO.ReadBufferAsync(file);
            return buffer.ToArray();
        }

        public async Task<string> GetFilePath(IList<string> listFileTypes)
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

        public async Task<bool> SaveAndView(string fileName, string contentType, MemoryStream stream, IDictionary<string, IList<string>> listFileTypes)
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
                FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(outFile);
                if (status == FileUpdateStatus.Complete)
                {
                    await Windows.System.Launcher.LaunchFileAsync(outFile);
                    return true;
                }
                else
                    return false;
            }
            return false;
        }

        public async Task<IList<(string fileName, bool status)>> SaveFiles(IList<(string fileName, string contentType, MemoryStream stream, string extension)> fileData)
        {
            var folderPicker = new FolderPicker();
            fileData.ForEach(fD =>
            {
                if (!folderPicker.FileTypeFilter.Contains(fD.extension))
                    folderPicker.FileTypeFilter.Add(fD.extension);
            });

            var folderToSaveIn = await folderPicker.PickSingleFolderAsync();

            var results = new List<(string fileName, bool status)>();

            foreach (var fileDatum in fileData)
            {
                var outFile = await folderToSaveIn.CreateFileAsync(fileDatum.fileName);
                if (outFile != null)
                {
                    CachedFileManager.DeferUpdates(outFile);
                    var outFileStream = await outFile.OpenAsync(FileAccessMode.ReadWrite);
                    using (var outputStream = outFileStream.GetOutputStreamAt(0))
                    {
                        using (var dataWriter = new DataWriter(outputStream))
                        {
                            dataWriter.WriteBytes(fileDatum.stream.ToArray());
                            await dataWriter.StoreAsync();
                            await outputStream.FlushAsync();
                        }
                    }
                    FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(outFile);
                    if (status == FileUpdateStatus.Complete)
                        results.Add((fileDatum.fileName, true));
                    else
                        results.Add((fileDatum.fileName, false));
                    outFile = null;
                }
                else
                    results.Add((fileDatum.fileName, false));
            }
            await Windows.System.Launcher.LaunchFolderAsync(folderToSaveIn);
            folderToSaveIn = null;
            return results;
        }

        private FileOpenPicker GetFileOpenPicker(IList<string> listFileTypes)
        {
            var filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var fileType in listFileTypes)
                filePicker.FileTypeFilter.Add(fileType);
            return filePicker;
        }

        private FileSavePicker GetFileSavePicker(IDictionary<string, IList<string>> listFileTypes, string suggestedFileName)
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
