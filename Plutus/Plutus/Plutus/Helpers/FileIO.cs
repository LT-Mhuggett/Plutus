using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if __ANDROID__
using Com.Cloudrail;
using Com.Cloudrail.SI.Types;
using Com.Cloudrail.SI.Services;
using Com.Cloudrail.SI;
using Com.Cloudrail.SI.Interfaces;
#elif __IOS__

#else
using Windows.Storage;
using Windows.Storage.Pickers;
#endif

namespace Plutus.Helpers
{
    /// <summary>
    /// This handels all File Input and Output for all devices using #if defs.
    /// </summary>
    public class FileIO
    {
        /// <summary>
        /// This handles File Saving
        /// This calls the GetLib() and then uses WriteAllLines() to save the file to the local system.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        /// <param name="fileC">This is the File's Content</param>
        public static void Save(string fileN, string[] fileC)
        {
            File.WriteAllLines(Path.Combine(GetLib(), fileN), fileC);
        }

        /// <summary>
        /// This handles File Saving
        /// This calls the GetLib() and then uses WriteAllLines() to save the file to the local system.
        /// </summary>
        /// <param name="fileN"></param>
        /// <param name="fileC"></param>
        public static void Save(string fileN, List<string> fileC)
        {
            File.WriteAllLines(Path.Combine(GetLib(), fileN), fileC);
        }

        /// <summary>
        /// This handles File Saving
        /// This calls the GetLib() and then uses WriteAllText() to save the file to the local system.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        /// <param name="fileC">This is the File's Content</param>
        public static void Save(string fileN, string fileC)
        {
            File.WriteAllText(Path.Combine(GetLib(), fileN), fileC);
        }

        /// <summary>
        /// This handles File Loading.
        /// This calls the GetLib() and then uses File.Open() to open the file and read it's contents to memory.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        public static void Load(string fileN)
        {
            //extra implementation needed
            File.Open(Path.Combine(GetLib(), fileN), FileMode.Open);
        }

        /// <summary>
        /// This handles File Existence.
        /// This calls the GetLib() and then uses File.Exists() to check if the file is in it's directory.
        /// </summary>
        /// <param name="fileN">This is the File's Name</param>
        /// <returns>Bool</returns>
        public static bool Exists(string fileN)
        {
            return File.Exists(Path.Combine(GetLib(), fileN));
        }

        /// <summary>
        /// This Handles all device specfic code to find the correct directory for files, per device specifiction.
        /// it uses #if def to only compile code that's required for that device.
        /// </summary>
        /// <returns>string libPath - this contains the correct file directory based on device</returns>
        public static string GetLib()
        {
#if __ANDROID__
                string libPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
#elif __IOS__
                string documentsPath =
 Environment.GetFolderPath (Environment.SpecialFolder.Personal); // Documents folder
                string libPath = Path.Combine (documentsPath, "..", "Library");
#else
            string libPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
#endif

            return libPath;
        }
#if __ANDROID__ || __IOS__

#else
        /// <summary>
        /// Get Windows SavePicker with fieltypeChoices and suggested filename
        /// </summary>
        /// <param name="listKeyValuePair"></param>
        /// <param name="suggestedFileName"></param>
        /// <returns></returns>
        public static FileSavePicker GetFileSavePicker(List<KeyValuePair<string, List<string>>> listKeyValuePair,
            string suggestedFileName)
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var kVP in listKeyValuePair)
            {
                savePicker.FileTypeChoices.Add(kVP.Key, kVP.Value);
            }

            savePicker.SuggestedFileName = suggestedFileName;
            return savePicker;
        }

        public static FileOpenPicker GetFileOpenPicker(List<string> listFileType)
        {
            var filePicker = new FileOpenPicker();
            filePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            foreach (var fileType in listFileType)
            {
                filePicker.FileTypeFilter.Add(fileType);
            }

            return filePicker;
        }
#endif

        public static async Task<bool> BackUp()
        {
#if __ANDROID__
/*CloudRail.AppKey = "59bff56c3d70425997876e19";

ICloudStorage service;

Box box = new Box
    (
    Android.App.Application.Context,
    "yx4h29y5i1xxr6rawcjugsi49sbhurrv",
    "aOZBqB53lFu5RvmAzS3aEr7XsWNtKcxr"
    );

Com.Cloudrail.SI.Services.Dropbox dropbox = new Com.Cloudrail.SI.Services.Dropbox
    (
    Android.App.Application.Context,
    "t8en46ucho8bzx9",
    "cj5m2va847fgem0"
    );

dropbox.UseAdvancedAuthentication();

GoogleDrive googleDrive = new GoogleDrive
    (
    Android.App.Application.Context,
    "467494168983-jstrn11o4v7uchs2cqp8euv4riar0ta3.apps.googleusercontent.com ",
    "",
    "Plutus.Plutus:/oauth2redirect",
    "state"
    );

googleDrive.UseAdvancedAuthentication();

OneDrive oneDrive = new OneDrive
    (
    Android.App.Application.Context,
    "3d2ca2df-bc21-425c-9d5d-5f38bb2b228d",
    "Lob77ow5rkGZsCnxjRWe9U7"
    );



var selection = await App.Current.MainPage.DisplayActionSheet(App.Translate.ProvideValue("SelectCloudService"), App.Translate.ProvideValue("Cancel"), null, "box", "Dropbox", "Google Drive", "OneDrive");

switch (selection)
{
    case "box":
        service = box;
        break;
    case "Dropbox":
        service = dropbox;
        break;
    case "Google Drive":
        service = googleDrive;
        break;
    case "OneDrive":
        service = oneDrive;
        break;
    default:
        return false;
}

service.UserLogin

return true;*/
            return false;
#elif __IOS__
            return false;
#else
            var savePicker =
                GetFileSavePicker(
                    new List<KeyValuePair<string, List<string>>>
                    {
                        new KeyValuePair<string, List<string>>("SQL Database", new List<string> {".db"})
                    },
                    string.Format("{0}-Database-{1}", App.Store.StoreName,
                        DateTime.Now.ToString(CultureInfo.CurrentCulture)));

            var file = await savePicker.PickSaveFileAsync();

            var dbFile = await StorageFile.GetFileFromPathAsync(Path.Combine(GetLib(), "Database.db"));
            if (file == null) return false;
            try
            {
                await dbFile.CopyAndReplaceAsync(file);
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"File Transfer error: {e}");
                return false;
            }
#endif
        }

        public static async Task<bool> Restore()
        {
#if __ANDROID__
            return false;
#elif __IOS__
            return false;
#else
            var dbPicker = GetFileOpenPicker(new List<string>() {".db"});
            var dbFile = await dbPicker.PickSingleFileAsync();

            var file = await StorageFile.GetFileFromPathAsync(Path.Combine(GetLib(), "Database.db"));

            if (dbFile == null) return false;
            try
            {
                await dbFile.CopyAndReplaceAsync(file);
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"File Transfer error: {e}");
                return false;
            }
#endif
        }
#if __ANDROID__

#elif __IOS__

#else
        internal static async Task<StorageFolder> GetFolderAsync()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder;
        }

        internal static async Task<string[]> GetStringsFromCsvAsync(StorageFile file, char delmiter = ',')
        {
            var stream = await file.OpenStreamForReadAsync();
            using (var strReader = new StreamReader(stream))
            {
                var @string = await strReader.ReadToEndAsync();
                return @string.Split(delmiter);
            }
        }

        internal static async Task<List<KeyValuePair<string,string[]>>> GetAllLinesCsvAsync(StorageFile file, char delimiter = ',')
        {
            var stream = await file.OpenStreamForReadAsync();
            var lines = new List<string>();
            using (var strReader = new StreamReader(stream))
            {
                while (strReader.Peek() >= 0)
                    lines.Add(await strReader.ReadLineAsync());
            }

            var kVPList = new List<KeyValuePair<string, string[]>>();
            foreach (var line in lines)
            {
                var tempLine = line.Split(delimiter);
                kVPList.Add(new KeyValuePair<string, string[]>(tempLine[0], tempLine.Skip(1).ToArray()));
            }

            return kVPList;
        }

        internal static async Task<List<string>> GetFirstLineCsvAsync(StorageFile file, char delimiter = ',')
        {
            var stream = await file.OpenStreamForReadAsync();
            using (var strReader = new StreamReader(stream))
            {
                var @string = await strReader.ReadLineAsync();
                return @string.Split(delimiter).ToList();
            }
        }
#endif
    }
}